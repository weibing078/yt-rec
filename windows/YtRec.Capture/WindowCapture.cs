using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

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
}
