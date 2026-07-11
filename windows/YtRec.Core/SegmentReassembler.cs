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

    // Reassembly writes to these temp names, then atomically renames the finished file onto the real output.
    // They must NEVER count as a finalized output (Inspect), or a finalize interrupted by kill-9 / power loss
    // (where the finally cleanup can't run) would leave a stray file that permanently blocks disaster-recovery.
    // Chosen so neither ends in ".mp4" → the "*.mp4" finalized-output glob can't see them (mac §7).
    public const string ConcatTempSuffix = ".frag"; // binary concat of init+segments, fed to ffmpeg -i
    public const string OutputTempSuffix = ".part"; // ffmpeg output; File.Move onto the real path on exit 0

    /// <summary>The segments dir for a job dir.</summary>
    public static string SegmentsDir(string jobDir) => Path.Combine(jobDir, WorkSubdir, SegmentsSubdir);

    /// <summary>The loopback-audio PCM file for a job dir (sibling of the segments dir).</summary>
    public static string AudioPath(string jobDir) => Path.Combine(jobDir, WorkSubdir, AudioName);

    /// <summary>Gather the filesystem facts for one job dir into a <see cref="RecoveryCandidate"/>.
    /// A job is "finalized" only if a real (non-temp, non-empty) .mp4 sits in the job dir root.</summary>
    public static RecoveryCandidate Inspect(string jobDir, bool isRecording)
    {
        var segs = SegmentsDir(jobDir);
        var hasInit = File.Exists(Path.Combine(segs, InitName));
        var count = Directory.Exists(segs) ? Directory.GetFiles(segs, SegmentGlob).Length : 0;
        var hasFinal = HasFinalizedOutput(jobDir);
        return new RecoveryCandidate(jobDir, hasInit, count, hasFinal, isRecording);
    }

    /// <summary>True only if the job dir holds a genuinely finalized output — a non-empty .mp4 that is not one
    /// of the reassembly temp files. Excluding the temps is what stops an interrupted finalize from looking
    /// "already done" and permanently blocking recovery of the intact segments underneath.</summary>
    private static bool HasFinalizedOutput(string jobDir)
    {
        if (!Directory.Exists(jobDir)) return false;
        foreach (var path in Directory.EnumerateFiles(jobDir, "*.mp4"))
        {
            // The "*.mp4" glob still matches the legacy "<output>.mp4.frag.mp4" concat temp older builds wrote,
            // and can over-match on 8.3 short-name quirks — so require an exact ".mp4" tail and skip exactly that
            // legacy shape. The current temps (".frag"/".part") don't end in ".mp4" and never reach here. Keep the
            // exclusion this narrow: a title may legitimately END in ".part"/".frag" (側錄_Teaser.part.mp4) and
            // must still count as finalized, or recovery would re-run every launch and emit phantom duplicates.
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileName(path);
            if (name.EndsWith(".mp4" + ConcatTempSuffix + ".mp4", StringComparison.OrdinalIgnoreCase)) continue;
            try { if (new FileInfo(path).Length > 0) return true; } catch { /* unreadable → not a valid output */ }
        }
        return false;
    }

    /// <summary>Reassemble one segments dir to <paramref name="outputPath"/>. Caller decides whether it
    /// should run (via <see cref="RecoveryPlan"/>); this just does the work. If <paramref name="audioPcmPath"/>
    /// points at a non-empty 48k/2ch/s16le PCM file, it is muxed in (AAC) — video and audio both started at
    /// the session-gate anchor, so they line up. Otherwise the output is video-only.
    /// <paramref name="keepFullVideo"/>: when the audio stream is known to have died early (Win10 default-device
    /// switch mid-record), pass true so -shortest doesn't truncate the whole video to the audio length — the
    /// output keeps the full video and the sound simply ends at the point the device was lost.</summary>
    public static async Task<(bool Ok, string? Error)> ReassembleAsync(
        string segmentsPath, string outputPath, string ffmpegPath, IProcessRunner runner,
        string? audioPcmPath = null, bool keepFullVideo = false, CancellationToken ct = default)
    {
        var init = Path.Combine(segmentsPath, InitName);
        if (!File.Exists(init)) return (false, "no init segment");

        // seg_%05d.m4s is zero-padded → ordinal sort is chronological within the 6 h cap.
        var segs = Directory.GetFiles(segmentsPath, SegmentGlob);
        Array.Sort(segs, StringComparer.Ordinal);
        if (segs.Length < 2) return (false, "too few segments");

        var hasAudio = audioPcmPath is not null && File.Exists(audioPcmPath) && new FileInfo(audioPcmPath).Length > 0;
        // Write to temp names that do NOT match the "*.mp4" finalized-output glob, then rename atomically onto
        // outputPath only after ffmpeg exits 0. If the process is killed mid-finalize (kill-9 / power loss — the
        // finally below never runs), only these temps remain; Inspect ignores them, so recovery re-runs on the
        // next launch instead of seeing a stray .mp4 and skipping forever. File.Create also truncates any temp
        // left by a previous interrupted attempt, so a relaunch simply starts the reassembly over.
        var fragPath = outputPath + ConcatTempSuffix;   // ".frag" — binary concat, fed to ffmpeg -i
        var tmpOut = outputPath + OutputTempSuffix;      // ".part" — ffmpeg output, promoted on success
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
            // Audio (if any): the raw loopback PCM, encoded to AAC; -shortest trims to the common length —
            // EXCEPT when the caller knows audio died early (keepFullVideo): then -shortest would silently
            // truncate hours of good video to the audio length, so we keep the full video instead.
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", fragPath };
            if (hasAudio)
            {
                args.AddRange(new[] { "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", audioPcmPath!,
                    "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "160k" });
                if (!keepFullVideo) args.Add("-shortest");
            }
            else
                args.AddRange(new[] { "-c", "copy" });
            // Force the mp4 muxer explicitly: the ".part" extension isn't one ffmpeg recognizes, so without -f
            // it can't infer the output container.
            args.AddRange(new[] { "-f", "mp4", tmpOut });

            var r = await runner.RunAsync(ffmpegPath, args, ct: ct);
            if (r.ExitCode != 0) return (false, $"ffmpeg finalize exit {r.ExitCode}: {r.Output}");

            // Atomic promote (same-volume rename): a reader ever sees either no output or the complete file,
            // never a half-written .mp4 that would wrongly count as finalized.
            File.Move(tmpOut, outputPath, overwrite: true);
            return (true, null);
        }
        finally
        {
            try { File.Delete(fragPath); } catch { /* best effort */ }
            try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch { /* best effort */ }
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
            var (ok, err) = await ReassembleAsync(SegmentsDir(jobDir), outputPathFor(jobDir), ffmpegPath, runner, AudioPath(jobDir), ct: ct);
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
