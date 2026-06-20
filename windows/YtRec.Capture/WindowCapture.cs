using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace YtRec.Capture;

public sealed class CaptureResult
{
    public bool Supported { get; init; }
    public bool ItemCreated { get; init; }
    public int Frames { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>Phase 2a PoC: capture a single window via WGC and confirm frames flow at the right size.
/// (Pixel/PNG verification and the encode pipeline come in later stages.)</summary>
public static class WindowCapture
{
    public static async Task<CaptureResult> CountFramesAsync(IntPtr hwnd, int seconds)
    {
        if (!GraphicsCaptureSession.IsSupported())
            return new CaptureResult { Supported = false };

        var item = CaptureInterop.CreateForWindow(hwnd);
        var (_, device) = Direct3D11Helper.CreateDevice();
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        var session = pool.CreateCaptureSession(item);

        var result = new CaptureResult { Supported = true, ItemCreated = true };
        var frames = 0;
        pool.FrameArrived += (sender, _) =>
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null) return;
            Interlocked.Increment(ref frames);
            result.Width = frame.ContentSize.Width;
            result.Height = frame.ContentSize.Height;
        };

        session.StartCapture();
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        session.Dispose();
        pool.Dispose();
        result.Frames = frames;
        return result;
    }

    /// <summary>Capture the first frame of a window and save it as PNG. Returns (ok, w, h, error).</summary>
    public static async Task<(bool Ok, int Width, int Height, string? Error)> CaptureFrameToPngAsync(
        IntPtr hwnd, string pngPath, int timeoutMs = 6000)
    {
        if (!GraphicsCaptureSession.IsSupported()) return (false, 0, 0, "WGC not supported");

        GraphicsCaptureItem item;
        try { item = CaptureInterop.CreateForWindow(hwnd); }
        catch (Exception e) { return (false, 0, 0, "item: " + e.Message); }

        var (_, device) = Direct3D11Helper.CreateDevice();
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        var session = pool.CreateCaptureSession(item);

        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame?>();
        pool.FrameArrived += (sender, _) =>
        {
            var f = sender.TryGetNextFrame();
            if (f != null) tcs.TrySetResult(f);
        };
        session.StartCapture();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        if (completed != tcs.Task)
        {
            session.Dispose(); pool.Dispose();
            return (false, 0, 0, "timeout waiting for a frame");
        }

        var frame = tcs.Task.Result!;
        int w = 0, h = 0;
        try
        {
            using (frame)
            using (var bmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied))
            {
                w = bmp.PixelWidth; h = bmp.PixelHeight;
                using var ras = new InMemoryRandomAccessStream();
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
                enc.SetSoftwareBitmap(bmp);
                await enc.FlushAsync();
                var bytes = new byte[checked((int)ras.Size)];
                using var reader = new DataReader(ras.GetInputStreamAt(0));
                await reader.LoadAsync((uint)ras.Size);
                reader.ReadBytes(bytes);
                await File.WriteAllBytesAsync(pngPath, bytes);
            }
        }
        catch (Exception e)
        {
            session.Dispose(); pool.Dispose();
            return (false, w, h, "encode: " + e.Message);
        }

        session.Dispose(); pool.Dispose();
        return (true, w, h, null);
    }
}
