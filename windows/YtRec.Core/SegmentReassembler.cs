namespace YtRec.Core;

/// <summary>Finalize + disaster-recovery for the crash-resistant HLS fMP4 segments the recorder writes
/// (mac §7). Layout: <c>&lt;jobDir&gt;/.work/segments/{seg_init.mp4, seg_00000.m4s, …}</c>. Reassembly =
/// binary-concat init + segments into a fragmented MP4, then <c>ffmpeg -c copy</c> to a single-moov,
/// Premiere-friendly file. On launch we scan every job dir and rebuild any orphan (idempotent; never
/// touches the in-use dir). The decision is delegated to <see cref="RecoveryPlan"/> for unit coverage.</summary>
public static class SegmentReassembler
{
    public const string WorkSubdir = ".work";
    public const string SegmentsSubdir = "segments";
    public const string InitName = "seg_init.mp4";
    public const string SegmentPattern = "seg_%05d.m4s"; // ffmpeg -hls_segment_filename
    public const string SegmentGlob = "seg_*.m4s";        // launch scan
    public const string AudioName = "audio.pcm";          // raw 48k/2ch/s16le loopback PCM

    /// <summary>The segments dir for a job dir.</summary>
    public static string SegmentsDir(string jobDir) => Path.Combine(jobDir, WorkSubdir, SegmentsSubdir);

    /// <summary>The loopback-audio PCM file for a job dir (sibling of the segments dir).</summary>
    public static string AudioPath(string jobDir) => Path.Combine(jobDir, WorkSubdir, AudioName);

    /// <summary>Gather the filesystem facts for one job dir into a <see cref="RecoveryCandidate"/>.
    /// A job is "finalized" if any .mp4 sits in the job dir root (the reassembled output).</summary>
    public static RecoveryCandidate Inspect(string jobDir, bool isRecording)
    {
        var segs = SegmentsDir(jobDir);
        var hasInit = File.Exists(Path.Combine(segs, InitName));
        var count = Directory.Exists(segs) ? Directory.GetFiles(segs, SegmentGlob).Length : 0;
        var hasFinal = Directory.Exists(jobDir) && Directory.GetFiles(jobDir, "*.mp4").Length > 0;
        return new RecoveryCandidate(jobDir, hasInit, count, hasFinal, isRecording);
    }

    /// <summary>Reassemble one segments dir to <paramref name="outputPath"/>. Caller decides whether it
    /// should run (via <see cref="RecoveryPlan"/>); this just does the work. If <paramref name="audioPcmPath"/>
    /// points at a non-empty 48k/2ch/s16le PCM file, it is muxed in (AAC) — video and audio both started at
    /// the session-gate anchor, so they line up. Otherwise the output is video-only.</summary>
    public static async Task<(bool Ok, string? Error)> ReassembleAsync(
        string segmentsPath, string outputPath, string ffmpegPath, IProcessRunner runner,
        string? audioPcmPath = null, CancellationToken ct = default)
    {
        var init = Path.Combine(segmentsPath, InitName);
        if (!File.Exists(init)) return (false, "no init segment");

        // seg_%05d.m4s is zero-padded → ordinal sort is chronological within the 6 h cap.
        var segs = Directory.GetFiles(segmentsPath, SegmentGlob);
        Array.Sort(segs, StringComparer.Ordinal);
        if (segs.Length < 2) return (false, "too few segments");

        var hasAudio = audioPcmPath is not null && File.Exists(audioPcmPath) && new FileInfo(audioPcmPath).Length > 0;
        var fragPath = outputPath + ".frag.mp4";
        try
        {
            await using (var outFs = File.Create(fragPath))
            {
                foreach (var part in Prepend(init, segs))
                {
                    await using var inFs = File.OpenRead(part);
                    await inFs.CopyToAsync(outFs, ct);
                }
            }

            // Video: -c copy (no re-encode, single moov, no +faststart for local files, mac §7).
            // Audio (if any): the raw loopback PCM, encoded to AAC; -shortest trims to the common length.
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", fragPath };
            if (hasAudio)
                args.AddRange(new[] { "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", audioPcmPath!,
                    "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "160k", "-shortest" });
            else
                args.AddRange(new[] { "-c", "copy" });
            args.Add(outputPath);

            var r = await runner.RunAsync(ffmpegPath, args, ct: ct);
            if (r.ExitCode != 0) return (false, $"ffmpeg finalize exit {r.ExitCode}: {r.Output}");
            return (true, null);
        }
        finally
        {
            try { File.Delete(fragPath); } catch { /* best effort */ }
        }
    }

    /// <summary>Scan every job dir under <paramref name="baseDir"/> and reassemble the orphans. The
    /// currently-recording dir (<paramref name="activeJobDir"/>) is left untouched. Idempotent: a job that
    /// already has a final .mp4 is skipped. Returns per-dir outcomes.</summary>
    public static async Task<IReadOnlyList<(string Dir, bool Ok, string? Error)>> RecoverAllAsync(
        string baseDir, string? activeJobDir, string ffmpegPath, IProcessRunner runner,
        Func<string, string> outputPathFor, CancellationToken ct = default)
    {
        var results = new List<(string, bool, string?)>();
        if (!Directory.Exists(baseDir)) return results;

        foreach (var jobDir in Directory.GetDirectories(baseDir))
        {
            var isRecording = activeJobDir is not null && SamePath(jobDir, activeJobDir);
            if (RecoveryPlan.Decide(Inspect(jobDir, isRecording)) != RecoveryAction.Reassemble) continue;
            var (ok, err) = await ReassembleAsync(SegmentsDir(jobDir), outputPathFor(jobDir), ffmpegPath, runner, AudioPath(jobDir), ct);
            results.Add((jobDir, ok, err));
        }
        return results;
    }

    private static IEnumerable<string> Prepend(string head, string[] rest)
    {
        yield return head;
        foreach (var r in rest) yield return r;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);
}
