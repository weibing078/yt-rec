using System.Text.Json;

namespace YtRec.Core;

public enum StreamPhase { Preview, Recording }

/// <summary>One second's view of the player. Times are a monotonic clock (not wall clock).
/// <see cref="Id"/> is empty when the page couldn't read it.</summary>
public readonly record struct PlayerSnapshot(bool Ended, bool Ad, bool Content, string Id, TimeSpan ReceivedAt);

public readonly record struct ParsedSnap(bool Ended, bool Ad, bool Content, string Id)
{
    public static readonly ParsedSnap Unknown = new(false, false, false, "");
}

public readonly record struct StreamDecision(
    string? StopReason,
    string AnchorId,
    TimeSpan? CandidateStart,
    int ExtendedAdSeconds,
    int OtherVideoStreak,
    int? CountdownSeconds,
    bool PreviewShowsEnded,
    bool PreviewShowsOtherVideo);

/// <summary>Once-a-second decision. Same table as macOS <c>StreamEnd.evaluate</c>.
/// The app stops the timer before finalize, so there is no Finalizing branch.</summary>
public static class StreamEndGate
{
    public const int StaleSeconds = 5;
    public const int BaseSeconds = 20;
    public const int ExtendStepSeconds = 20;
    public const int MaxExtendSeconds = 120;
    public const int OtherVideoStreakNeeded = 3;

    public const string OtherVideoReason = "播放器換成別支影片";
    public const string VideoEndedReason = "影片已結束";

    public static StreamDecision Evaluate(
        StreamPhase phase,
        string anchorId,
        PlayerSnapshot? snapshot,
        TimeSpan? candidateStart,
        int extendedAdSeconds,
        int otherVideoStreak,
        TimeSpan now)
    {
        var ended = false;
        var ad = false;
        var content = false;
        var id = "";
        // R0 — a snapshot older than 5s is "don't know": flags and id are all empty.
        if (snapshot is { } snap && (now - snap.ReceivedAt) <= TimeSpan.FromSeconds(StaleSeconds))
        {
            ended = snap.Ended;
            ad = snap.Ad;
            content = snap.Content;
            id = snap.Id ?? "";
        }

        var anchor = anchorId ?? "";
        // A URL with no id adopts the first fresh, non-ad, playing snapshot during preview.
        if (phase == StreamPhase.Preview && anchor.Length == 0 && !ad && content && id.Length > 0)
            anchor = id;

        // R1 debounce — three fresh mismatches in a row. Anything else resets. Empty anchor never counts.
        var mismatch = anchor.Length > 0 && !ad && id.Length > 0 && id != anchor;
        var streak = mismatch ? otherVideoStreak + 1 : 0;
        var switched = streak >= OtherVideoStreakNeeded;
        var showsEnded = ended && !ad;

        if (phase == StreamPhase.Preview)
        {
            return new StreamDecision(null, anchor, null, 0, streak, null, showsEnded, switched);
        }

        if (switched)
            return new StreamDecision(OtherVideoReason, anchor, null, 0, streak, null, false, false);

        var candidate = candidateStart;
        var extended = extendedAdSeconds;

        if (candidate != null && content && (id == anchor || id.Length == 0))
            return new StreamDecision(null, anchor, null, 0, streak, null, false, false);

        if (candidate == null && ended && !ad)
            candidate = now;

        if (candidate == null)
            return new StreamDecision(null, anchor, null, extended, streak, null, false, false);

        var limit = TimeSpan.FromSeconds(BaseSeconds + extended);
        if (now - candidate.Value >= limit)
        {
            if (ad && extended < MaxExtendSeconds)
            {
                extended += ExtendStepSeconds;
                return new StreamDecision(null, anchor, candidate, extended, streak, Countdown(candidate.Value, extended, now), false, false);
            }
            return new StreamDecision(VideoEndedReason, anchor, null, 0, streak, null, false, false);
        }

        return new StreamDecision(null, anchor, candidate, extended, streak, Countdown(candidate.Value, extended, now), false, false);
    }

    /// <summary>「從這裡開始錄影」可按：既有的正片就緒，而且當下沒有「影片已結束」或「換成別支」。</summary>
    public static bool CanBeginFromPreview(bool previewReady, bool previewShowsEnded, bool previewShowsOtherVideo)
        => previewReady && !previewShowsEnded && !previewShowsOtherVideo;

    /// <summary>Parse one snapshot object. Missing fields, wrong types, or a broken document
    /// become ended=false, ad=false, content=false, id="" — never "content" and never "another video".</summary>
    public static ParsedSnap ParseSnapJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ParsedSnap.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseSnap(doc.RootElement);
        }
        catch
        {
            return ParsedSnap.Unknown;
        }
    }

    public static ParsedSnap ParseSnap(JsonElement snap)
    {
        if (snap.ValueKind != JsonValueKind.Object) return ParsedSnap.Unknown;
        return new ParsedSnap(Flag(snap, "ended"), Flag(snap, "ad"), Flag(snap, "content"), Text(snap, "id"));
    }

    static bool Flag(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static string Text(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static int? Countdown(TimeSpan start, int extended, TimeSpan now)
    {
        var remain = start + TimeSpan.FromSeconds(BaseSeconds + extended) - now;
        if (remain <= TimeSpan.Zero) return null;
        return (int)Math.Ceiling(remain.TotalSeconds);
    }

    public static bool BeginRecordingSucceeds(bool hasSession, bool isPreviewing, bool isRecording)
        => hasSession && isPreviewing && !isRecording;

    public static bool StopHasWork(bool hasSession, bool alreadyStopping)
        => hasSession && !alreadyStopping;

    public static bool StopReportsFailure(bool hasSession) => !hasSession;
}
