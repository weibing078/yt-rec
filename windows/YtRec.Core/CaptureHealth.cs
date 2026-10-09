namespace YtRec.Core;

/// <summary>Preview must keep producing frames, and a mid-recording size change stops through the
/// existing error path instead of silently dropping frames.</summary>
public static class CaptureHealth
{
    public const int StallMilliseconds = 8000;
    public const string SizeChangedMessage = "擷取尺寸變了，已停止並保留已錄的部分";
    public const string PreviewStalledMessage = "預覽沒有新畫面，先不要開錄。請確認播放器有在動。";

    public static bool SizeChanged(int capturedWidth, int capturedHeight, int width, int height)
        => width != capturedWidth || height != capturedHeight;

    /// <summary>The capture pool's texture stays at the original size. A real window resize shows up on the
    /// frame's content size instead. Ignore a few pixels of noise; a clear change stops the take.</summary>
    public static bool ContentSizeChanged(int lockedWidth, int lockedHeight, int width, int height)
        => lockedWidth > 0 && lockedHeight > 0 && width > 0 && height > 0
           && (Math.Abs(width - lockedWidth) > 8 || Math.Abs(height - lockedHeight) > 8);

    /// <summary>When the file was saved after a capture fault, the fault is the notice unless a stop reason
    /// was already chosen. A clean save with no fault returns null.</summary>
    public static string? NoticeAfterSave(string? autoStopReason, string? fault)
    {
        if (!string.IsNullOrEmpty(autoStopReason)) return autoStopReason;
        return string.IsNullOrEmpty(fault) ? null : fault;
    }

    /// <summary>No picture rect at all means there was never a frame to record. Unstable but present rects
    /// are a different failure: do not fall back to the whole page.</summary>
    public static string CropGiveUpMessage(int samples, int withRect)
        => withRect == 0
            ? PreviewStalledMessage
            : $"裁切範圍沒有穩定，已停止，避免錄進整頁。（取樣 {samples}，有範圍 {withRect}）";

    public static bool PreviewIsStalled(int framesThen, int framesNow, long elapsedMilliseconds)
        => elapsedMilliseconds >= StallMilliseconds && framesNow <= framesThen;
}
