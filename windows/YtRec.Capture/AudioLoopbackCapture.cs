using System.Runtime.InteropServices;
using static YtRec.Capture.WasapiConstants;
using static YtRec.Capture.WasapiNative;

namespace YtRec.Capture;

/// <summary>PCM format the capturer emits (uniform across both paths via AUTOCONVERTPCM): 48 kHz, stereo,
/// 16-bit signed LE — drops straight into ffmpeg as <c>-f s16le -ar 48000 -ac 2</c>.</summary>
public sealed record AudioPcmFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
    public static readonly AudioPcmFormat Default = new(48000, 2, 16);

    public string[] FfmpegInputArgs() => new[]
    {
        "-f", BitsPerSample == 16 ? "s16le" : "f32le",
        "-ar", SampleRate.ToString(), "-ac", Channels.ToString(),
    };
}

/// <summary>Loopback audio capture (mirrors Microsoft's ApplicationLoopback sample). Acquire an IAudioClient
/// either for the default render endpoint (system loopback — Win10, records everything) or for one process
/// tree (process loopback — Win11, the WebView2 browser PID + children, zero bleed), then run an
/// event-driven capture loop. PCM is delivered via the Start callback.
///
/// IMPORTANT: the IAudioClient/IAudioCaptureClient are not apartment-agile — acquiring them on the WinUI UI
/// thread (STA) and calling across to a worker fails with E_NOINTERFACE. So ALL COM work (acquire, init,
/// capture, release) happens on one dedicated MTA thread; nothing crosses an apartment boundary.</summary>
public sealed class AudioLoopbackCapture : IDisposable
{
    public enum Source { SystemLoopback, ProcessLoopback }

    public AudioPcmFormat Format { get; } = AudioPcmFormat.Default;

    private readonly Source _source;
    private readonly uint _pid;

    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private IntPtr _event;
    private IntPtr _formatPtr;
    private Thread? _thread;
    private volatile bool _running;
    private object? _handlerRoot; // keep the activation completion handler rooted until it fires

    public AudioLoopbackCapture(Source source, uint pid = 0)
    {
        _source = source;
        _pid = pid;
    }

    /// <summary>Begin capturing. Spins up the dedicated MTA thread, blocks until acquire+init finishes (so
    /// activation errors surface here), then the thread runs the capture loop. <paramref name="onPcm"/>
    /// (buffer, byteCount, qpcTicks) fires per packet on that thread; the buffer is reused — copy/write now.</summary>
    public void Start(Action<byte[], int, long> onPcm)
    {
        using var ready = new ManualResetEventSlim(false);
        Exception? initError = null;
        _running = true;

        _thread = new Thread(() =>
        {
            try { AcquireAndInit(); }
            catch (Exception e) { initError = e; ready.Set(); return; }
            ready.Set();
            CaptureLoop(onPcm);
        })
        { IsBackground = true, Name = "ytrec-audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();

        ready.Wait();
        if (initError is not null) { _running = false; throw initError; }
    }

    private void AcquireAndInit()
    {
        var client = _source == Source.SystemLoopback ? AcquireSystem() : AcquireProcess(_pid);
        _client = client;
        _formatPtr = AllocFormat(Format);
        _event = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);

        const long bufferHns = 200 * 10000; // 200 ms
        var flags = AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM;
        Check(client.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, bufferHns, 0, _formatPtr, IntPtr.Zero), "IAudioClient.Initialize");
        Check(client.SetEventHandle(_event), "SetEventHandle");

        var iidCap = IID_IAudioCaptureClient;
        Check(client.GetService(ref iidCap, out var capObj), "GetService(IAudioCaptureClient)");
        _capture = (IAudioCaptureClient)capObj;

        Check(client.Start(), "IAudioClient.Start");
    }

    private static IAudioClient AcquireSystem()
    {
        var clsid = CLSID_MMDeviceEnumerator;
        var iidEnum = IID_IMMDeviceEnumerator;
        Check(CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iidEnum, out var pEnum), "CoCreateInstance(MMDeviceEnumerator)");
        var enumr = (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pEnum);
        Marshal.Release(pEnum);

        Check(enumr.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var dev), "GetDefaultAudioEndpoint");
        var iidAc = IID_IAudioClient;
        Check(dev.Activate(ref iidAc, CLSCTX_ALL, IntPtr.Zero, out var ac), "IMMDevice.Activate");
        return (IAudioClient)ac;
    }

    private IAudioClient AcquireProcess(uint targetProcessId)
    {
        var activation = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = targetProcessId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree,
            },
        };

        var paramSize = Marshal.SizeOf<AudioClientActivationParams>();
        var pParams = Marshal.AllocCoTaskMem(paramSize);
        var pPropVar = Marshal.AllocCoTaskMem(Marshal.SizeOf<PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(activation, pParams, false);
            Marshal.StructureToPtr(new PropVariantBlob { vt = VT_BLOB, cbSize = (uint)paramSize, pBlobData = pParams }, pPropVar, false);

            var handler = new ActivateHandler();
            _handlerRoot = handler;
            var iid = IID_IAudioClient;
            Check(ActivateAudioInterfaceAsync(VirtualAudioDeviceProcessLoopback, ref iid, pPropVar, handler, out _),
                "ActivateAudioInterfaceAsync");

            if (!handler.Done.Wait(5000)) throw new InvalidOperationException("process-loopback activation timed out");
            if (handler.Error is not null) throw handler.Error;
            return handler.Result ?? throw new InvalidOperationException("process-loopback returned no client");
        }
        finally
        {
            Marshal.FreeCoTaskMem(pParams);
            Marshal.FreeCoTaskMem(pPropVar);
        }
    }

    private void CaptureLoop(Action<byte[], int, long> onPcm)
    {
        var block = Format.BlockAlign;
        var buf = Array.Empty<byte>();
        try
        {
            while (_running)
            {
                if (WaitForSingleObject(_event, 200) != WAIT_OBJECT_0) continue;
                while (_capture!.GetNextPacketSize(out var frames) == S_OK && frames > 0)
                {
                    if (_capture.GetBuffer(out var pData, out var numFrames, out var dwFlags, out _, out var qpc) != S_OK) break;
                    var bytes = (int)numFrames * block;
                    if (buf.Length < bytes) buf = new byte[bytes];
                    if ((dwFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(buf, 0, bytes);
                    else Marshal.Copy(pData, buf, 0, bytes);
                    onPcm(buf, bytes, (long)qpc);
                    _capture.ReleaseBuffer(numFrames);
                }
            }
        }
        finally
        {
            // Release COM on the same (MTA) thread that created it.
            try { _client?.Stop(); } catch { /* already gone */ }
            if (_capture is not null) { try { Marshal.ReleaseComObject(_capture); } catch { } _capture = null; }
            if (_client is not null) { try { Marshal.ReleaseComObject(_client); } catch { } _client = null; }
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _thread?.Join(1500);
    }

    public void Dispose()
    {
        Stop();
        if (_event != IntPtr.Zero) { CloseHandle(_event); _event = IntPtr.Zero; }
        if (_formatPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_formatPtr); _formatPtr = IntPtr.Zero; }
        GC.KeepAlive(_handlerRoot);
    }

    private static IntPtr AllocFormat(AudioPcmFormat f)
    {
        var wf = new WaveFormatEx
        {
            wFormatTag = 1, // WAVE_FORMAT_PCM
            nChannels = (ushort)f.Channels,
            nSamplesPerSec = (uint)f.SampleRate,
            wBitsPerSample = (ushort)f.BitsPerSample,
            nBlockAlign = (ushort)f.BlockAlign,
            nAvgBytesPerSec = (uint)(f.SampleRate * f.BlockAlign),
            cbSize = 0,
        };
        var p = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(wf, p, false);
        return p;
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what} failed: 0x{hr:X8}");
    }

    /// <summary>Activation completion handler — kept rooted by the capturer until it fires.</summary>
    private sealed class ActivateHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public IAudioClient? Result;
        public Exception? Error;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op)
        {
            try
            {
                var hr = op.GetActivateResult(out var activateResult, out var iface);
                if (hr < 0) Error = new InvalidOperationException($"GetActivateResult: 0x{hr:X8}");
                else if (activateResult < 0) Error = new InvalidOperationException($"activate: 0x{activateResult:X8}");
                else Result = (IAudioClient)iface;
            }
            catch (Exception e) { Error = e; }
            finally { Done.Set(); }
            return S_OK;
        }
    }
}
