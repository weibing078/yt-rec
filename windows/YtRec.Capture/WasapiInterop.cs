using System.Runtime.InteropServices;

namespace YtRec.Capture;

// Minimal WASAPI COM interop for loopback capture. Mirrors Microsoft's ApplicationLoopback sample.
// Two acquisition paths share IAudioClient/IAudioCaptureClient:
//   • system loopback  — IMMDeviceEnumerator → default render endpoint (Win10+, records all audio)
//   • process loopback — ActivateAudioInterfaceAsync(VAD\Process_Loopback) (Win11, records one tree)
internal static class WasapiConstants
{
    public const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    // HRESULTs / format
    public const int S_OK = 0;
    public const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;
    public const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;

    // IAudioClient stream flags
    public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    public const uint AUDCLNT_SHAREMODE_SHARED = 0;

    // IAudioCaptureClient buffer flags
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

    // PROPVARIANT
    public const ushort VT_BLOB = 65;

    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
}

internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

internal enum AudioClientActivationType { Default = 0, ProcessLoopback = 1 }
internal enum ProcessLoopbackMode { IncludeTargetProcessTree = 0, ExcludeTargetProcessTree = 1 }

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProcessLoopbackParams
{
    public uint TargetProcessId;
    public ProcessLoopbackMode ProcessLoopbackMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParams
{
    public AudioClientActivationType ActivationType;
    public AudioClientProcessLoopbackParams ProcessLoopbackParams;
}

// VT_BLOB PROPVARIANT (x64 layout): vt at 0, BLOB { ULONG cbSize @8; void* pBlobData @16 }. Size 24.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariantBlob
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public uint cbSize;
    [FieldOffset(16)] public IntPtr pBlobData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    // remaining methods unused
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    // remaining methods unused
}

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    int Initialize(uint shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity,
        IntPtr pFormat, IntPtr audioSessionGuid);
    int GetBufferSize(out uint pNumBufferFrames);
    int GetStreamLatency(out long phnsLatency);
    int GetCurrentPadding(out uint pNumPaddingFrames);
    int IsFormatSupported(uint shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    int GetMixFormat(out IntPtr ppDeviceFormat);
    int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    int Start();
    int Stop();
    int Reset();
    int SetEventHandle(IntPtr eventHandle);
    int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags,
        out ulong pu64DevicePosition, out ulong pu64QPCPosition);
    int ReleaseBuffer(uint numFramesRead);
    int GetNextPacketSize(out uint pNumFramesInNextPacket);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

// Marker so native code treats our managed handler as free-threaded (avoids cross-apartment marshalling).
[ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject { }

internal static class WasapiNative
{
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    public static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid, IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint clsContext, ref Guid iid, out IntPtr ppv);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr attrs, bool manualReset, bool initialState, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint ms);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    public const uint CLSCTX_ALL = 23;
    public const uint WAIT_OBJECT_0 = 0;
}
