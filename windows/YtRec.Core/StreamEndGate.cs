namespace YtRec.Core;

public enum StreamPhase { Preview, Recording, Finalizing }

/// <summary>One second's view of the player. <see cref="Id"/> is empty when the page couldn't read it.
/// <see cref="ReceivedAt"/> is when the native side stored it.</summary>
public readonly record struct PlayerSnapshot(bool Ended, bool Ad, bool Content, string Id, DateTime ReceivedAt);

public readonly record struct StreamDecision(
    string? StopReason,
    DateTime? CandidateStart,
    int ExtendedAdSeconds,
    int? CountdownSeconds,
    bool PreviewShowsEnded,
    bool LeaveUiAlone);

/// <summary>Once-a-second decision for "should this recording stop". Same table on macOS
/// (<c>StreamEnd.evaluate</c>). Rules R0–R6 are the whole policy; callers must not keep a sticky ended flag.</summary>
public static class StreamEndGate
{
    public const int StaleSeconds = 5;
    public const int BaseSeconds = 20;
    public const int ExtendStepSeconds = 20;
    public const int MaxExtendSeconds = 120;

    public const string OtherVideoReason = "播放器換成別支影片";
    public const string VideoEndedReason = "影片已結束";

    public static StreamDecision Evaluate(
        StreamPhase phase,
        string anchorId,
        PlayerSnapshot? snapshot,
        DateTime? candidateStart,
        int extendedAdSeconds,
        DateTime now)
    {
        if (phase == StreamPhase.Finalizing)
            return new StreamDecision(null, candidateStart, extendedAdSeconds, null, false, true);

        var ended = false;
        var ad = false;
        var content = false;
        var id = snapshot?.Id ?? "";
        if (snapshot is { } snap && (now - snap.ReceivedAt) <= TimeSpan.FromSeconds(StaleSeconds))
        {
            ended = snap.Ended;
            ad = snap.Ad;
            content = snap.Content;
        }

        if (phase == StreamPhase.Preview)
        {
            return new StreamDecision(null, null, 0, null, ended && !ad, false);
        }

        // R1 — another video, and it isn't an ad.
        if (!ad && id.Length > 0 && id != anchorId)
            return new StreamDecision(OtherVideoReason, null, 0, null, false, false);

        var candidate = candidateStart;
        var extended = extendedAdSeconds;

        // R3 — same video (or an unreadable id) is playing real content again.
        if (candidate != null && content && (id == anchorId || id.Length == 0))
        {
            return new StreamDecision(null, null, 0, null, false, false);
        }

        // R2 — first ended sighting starts the 20s candidate.
        if (candidate == null && ended && !ad)
            candidate = now;

        if (candidate == null)
            return new StreamDecision(null, null, extended, null, false, false);

        var limit = TimeSpan.FromSeconds(BaseSeconds + extended);
        if (now - candidate.Value >= limit)
        {
            // R4 — an ad at the deadline buys another 20s, up to 120s of extensions.
            if (ad && extended < MaxExtendSeconds)
            {
                extended += ExtendStepSeconds;
                return new StreamDecision(null, candidate, extended, Countdown(candidate.Value, extended, now), false, false);
            }
            return new StreamDecision(VideoEndedReason, null, 0, null, false, false);
        }

        return new StreamDecision(null, candidate, extended, Countdown(candidate.Value, extended, now), false, false);
    }

    /// <summary>「從這裡開始錄影」可按：既有的正片就緒，而且當下沒在顯示「影片已結束」。</summary>
    public static bool CanBeginFromPreview(bool previewReady, bool previewShowsEnded)
        => previewReady && !previewShowsEnded;

    static int? Countdown(DateTime start, int extended, DateTime now)
    {
        var remain = start.AddSeconds(BaseSeconds + extended) - now;
        if (remain <= TimeSpan.Zero) return null;
        return (int)Math.Ceiling(remain.TotalSeconds);
    }

    /// <summary>底層還有預覽工作才算開始錄影成功。沒有工作時呼叫端要提示，不能假裝已在錄。</summary>
    public static bool BeginRecordingSucceeds(bool hasSession, bool isPreviewing, bool isRecording)
        => hasSession && isPreviewing && !isRecording;

    /// <summary>要不要真的跑收尾。已經在收尾就不要再跑一次。</summary>
    public static bool StopHasWork(bool hasSession, bool alreadyStopping)
        => hasSession && !alreadyStopping;

    /// <summary>回給主畫面：沒有這一場才算失敗。已經在收尾不算失敗，避免把進行中的存檔清成「沒在錄」。</summary>
    public static bool StopReportsFailure(bool hasSession) => !hasSession;
}
