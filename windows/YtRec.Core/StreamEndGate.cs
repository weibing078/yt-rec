namespace YtRec.Core;

/// <summary>What to do with a player "ended" / "content ready" signal. Ended during preview is ignored
/// (don't tear the monitor down). Ended while recording schedules one stop; a repeat doesn't reschedule.
/// Content becoming ready again cancels that pending stop.</summary>
public enum StreamEndAction
{
    Ignore,
    ScheduleStop,
    CancelScheduledStop,
}

public static class StreamEndGate
{
    /// <summary>20 秒候選：只有「正在寫檔、且尚未排程」才排程。預覽中的 ended 不拆監看。</summary>
    public static StreamEndAction OnEnded(bool isRecording, bool alreadyScheduled)
    {
        if (!isRecording || alreadyScheduled) return StreamEndAction.Ignore;
        return StreamEndAction.ScheduleStop;
    }

    /// <summary>正片恢復（ready）就取消尚未執行的收工。沒在等，或其實還不是正片，就不動。</summary>
    public static StreamEndAction OnContentReady(bool contentReady, bool alreadyScheduled)
        => contentReady && alreadyScheduled ? StreamEndAction.CancelScheduledStop : StreamEndAction.Ignore;

    /// <summary>底層還有預覽工作才算開始錄影成功。沒有工作時呼叫端要提示，不能假裝已在錄。</summary>
    public static bool BeginRecordingSucceeds(bool hasSession, bool isPreviewing, bool isRecording)
        => hasSession && isPreviewing && !isRecording;

    /// <summary>要不要真的跑收尾。已經在收尾就不要再跑一次。</summary>
    public static bool StopHasWork(bool hasSession, bool alreadyStopping)
        => hasSession && !alreadyStopping;

    /// <summary>回給主畫面：沒有這一場才算失敗。已經在收尾不算失敗，避免把進行中的存檔清成「沒在錄」。</summary>
    public static bool StopReportsFailure(bool hasSession) => !hasSession;
}
