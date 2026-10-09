using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using YtRec.Core;

namespace YtRec.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // Crash diagnostics: the WinUI GUI had never been runtime-tested, so capture any unhandled failure
        // (incl. native XAML/COM activation errors) to a log the test loop can read.
        var crashLog = Path.Combine(Path.GetTempPath(), "ytrec-crash.log");
        void Dump(string src, Exception? ex)
        {
            try { File.AppendAllText(crashLog, $"[{src}]\n{ex}\nHRESULT=0x{ex?.HResult:X8}\nINNER={ex?.InnerException}\n\n"); } catch { }
        }
        UnhandledException += (_, e) => Dump("App.UnhandledException: " + e.Message, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Dump("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Dump("TaskScheduler", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Headless verification entry point (not user-facing):
        //   YtRec.App.exe --autorecord <youtube-url> <seconds> <outDir>
        // Runs the real CaptureController (WebView2 → RecordingSession → audio → finalize) and exits.
        var cmd = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(cmd, "--autorecord");
        if (i >= 0 && cmd.Length >= i + 4 && int.TryParse(cmd[i + 2], out var secs))
        {
            var quality = 1080;
            if (cmd.Length >= i + 5 && int.TryParse(cmd[i + 4], out var q) && (q == 720 || q == 1080))
                quality = q;
            _ = RunAutoRecordAsync(cmd[i + 1], secs, cmd[i + 3], quality);
            return;
        }

        // Headless verification of the live preview + rewind feature (not user-facing):
        //   YtRec.App.exe --previewseek <live-url> <behindSec> <seconds> <outDir>
        // Prepares the live preview (no file), polls the DVR position, rewinds <behindSec> behind live, confirms
        // the player moved there, then records <seconds> FROM that point. Logs assertable values.
        var j = Array.IndexOf(cmd, "--previewseek");
        if (j >= 0 && cmd.Length >= j + 5 && double.TryParse(cmd[j + 2], out var behind) && int.TryParse(cmd[j + 3], out var psecs))
        {
            _ = RunPreviewSeekAsync(cmd[j + 1], behind, psecs, cmd[j + 4]);
            return;
        }

        // Headless check (not user-facing): prefs round-trip, then the download decision for one URL.
        //   YtRec.App.exe --check <youtube-url> <outDir>
        var c = Array.IndexOf(cmd, "--check");
        if (c >= 0 && cmd.Length >= c + 3)
        {
            _ = RunCheckAsync(cmd[c + 1], cmd[c + 2]);
            return;
        }

        // Headless recovery (not user-facing): scan one output root for an unfinished side-record.
        //   YtRec.App.exe --recover <outputRoot> <logDir>
        var rz = Array.IndexOf(cmd, "--resize-check");
        if (rz >= 0 && cmd.Length >= rz + 4 && int.TryParse(cmd[rz + 2], out var resizeSeconds))
        {
            _ = RunResizeCheckAsync(cmd[rz + 1], resizeSeconds, cmd[rz + 3]);
            return;
        }

        var rec = Array.IndexOf(cmd, "--recover");
        if (rec >= 0 && cmd.Length >= rec + 3)
        {
            _ = RunRecoverAsync(cmd[rec + 1], cmd[rec + 2]);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    private async Task RunResizeCheckAsync(string url, int seconds, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = Path.Combine(outDir, "resize.log");
        void Log(string m) { try { File.AppendAllText(log, m + "\n"); } catch { } }
        try
        {
            var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg) ?? throw new Exception("ffmpeg not found");
            var jobDir = Path.Combine(outDir, "job");
            Directory.CreateDirectory(jobDir);
            var ctrl = new CaptureController(ffmpeg);
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ctrl.Status += s => Log("status: " + s);
            ctrl.Failed += m => { Log("FAILED: " + m); done.TrySetResult("fail:" + m); };
            ctrl.Finished += p => { Log("FINISHED: " + p); done.TrySetResult("ok:" + p); };
            Log("resize-check url=" + url);
            await ctrl.StartAsync(url, jobDir, "resize-check");
            Log("recording=" + ctrl.IsRecording);
            await Task.Delay(Math.Max(2, seconds) * 1000);
            ctrl.ChangeCaptureSizeForCheck();
            Log("resized");
            var finished = await Task.WhenAny(done.Task, Task.Delay(25000));
            if (finished != done.Task)
            {
                await ctrl.StopAsync();
                await Task.WhenAny(done.Task, Task.Delay(20000));
            }
            Log("result: " + (done.Task.IsCompleted ? done.Task.Result : "timeout"));
        }
        catch (Exception e) { Log("EXCEPTION: " + e.Message); }
        finally
        {
            try { File.WriteAllText(Path.Combine(outDir, "resize.done"), "done\n"); } catch { }
            Exit();
        }
    }

    private async Task RunRecoverAsync(string root, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var logPath = Path.Combine(outDir, "recover.log");
        void Log(string m) { try { File.AppendAllText(logPath, m + "\n"); } catch { } }
        try
        {
            var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg) ?? throw new Exception("ffmpeg not found");
            var results = await SegmentReassembler.RecoverAllAsync(
                root, activeJobDir: null, ffmpeg, new ProcessRunner(), OutputPaths.RecoveryOutput);
            Log("count=" + results.Count);
            foreach (var (dir, ok, err) in results)
                Log($"ok={ok} dir={dir} err={err}");
        }
        catch (Exception e) { Log("EXCEPTION: " + e.Message); }
        finally
        {
            try { File.WriteAllText(Path.Combine(outDir, "recover.done"), "done\n"); } catch { }
            Exit();
        }
    }

    private async Task RunCheckAsync(string url, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var logPath = Path.Combine(outDir, "check.log");
        void Log(string m) { try { File.AppendAllText(logPath, m + "\n"); } catch { } }
        var prefsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YT-Rec");
        var settingsPath = Path.Combine(prefsDir, "settings.json");
        var historyPath = Path.Combine(prefsDir, "history.json");
        byte[]? oldSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        byte[]? oldHistory = File.Exists(historyPath) ? File.ReadAllBytes(historyPath) : null;
        var keep = Path.Combine(outDir, "keep.mp4");
        var gone = Path.Combine(outDir, "gone.mp4");
        try
        {
            File.WriteAllBytes(keep, new byte[] { 1, 2, 3 });
            PrefsStore.SaveSettings(new SavedSettings(DurationCap.TwelveHours, 720, outDir, new[] { @"C:\old\YT-Rec" }));
            var loaded = PrefsStore.LoadSettings();
            Log($"prefs duration={loaded.Duration} quality={loaded.Quality} output={loaded.OutputDir} earlier={loaded.EarlierOutputDirs.Count}");
            PrefsStore.SaveRecent(new[]
            {
                new StoredRecent(keep, "Sidecar"),
                new StoredRecent(gone, "Native"),
            });
            var recent = PrefsStore.LoadRecent();
            Log("recent=" + string.Join(",", recent.Select(r => Path.GetFileName(r.Path))));
            var ads = new AdIntervalLog();
            ads.Observe(true, 62);
            ads.Observe(false, 100);
            var sideText = ads.Render() ?? "";
            var side = AdIntervalLog.SidecarPath(keep);
            File.WriteAllText(side, sideText);
            Log("sidecar=" + Path.GetFileName(side));
            Log("sidecarText=" + sideText.Replace("\n", " / "));

            var ytdlp = BinaryLocator.Resolve(BinaryLocator.Tool.YtDlp) ?? throw new Exception("yt-dlp not found");
            var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg);
            var ffmpegDir = ffmpeg is null ? null : Path.GetDirectoryName(ffmpeg);
            var engine = new YtDlpEngine(ytdlp, ffmpegDir, () => new ProcessRunner());
            var outcome = await engine.StartAsync(url, outDir, 1080, autoMode: true);
            Log("outcome=" + outcome.GetType().Name);
            if (outcome is DownloadOutcome.Marathon or DownloadOutcome.SkippedAutoLive)
                Log("switch=" + LocalPrefs.SwitchedToSideRecord);
        }
        catch (Exception e) { Log("EXCEPTION: " + e.Message); }
        finally
        {
            try
            {
                if (oldSettings is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, oldSettings);
                if (oldHistory is null) File.Delete(historyPath); else File.WriteAllBytes(historyPath, oldHistory);
            }
            catch { }
            try { File.WriteAllText(Path.Combine(outDir, "check.done"), "done\n"); } catch { }
            Exit();
        }
    }

    private async Task RunPreviewSeekAsync(string url, double behindSec, int seconds, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = Path.Combine(outDir, "previewseek.log");
        void Log(string m) { try { File.AppendAllText(log, m + "\n"); } catch { } }
        try
        {
            var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg) ?? throw new Exception("ffmpeg not found");
            var jobDir = Path.Combine(outDir, "job");
            Directory.CreateDirectory(jobDir);

            var ctrl = new CaptureController(ffmpeg);
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ctrl.Status += s => Log("status: " + s);
            ctrl.Finished += p => { Log("FINISHED: " + p); done.TrySetResult("ok:" + p); };
            ctrl.Failed += m => { Log("FAILED: " + m); done.TrySetResult("fail:" + m); };

            Log($"previewseek url={url} behind={behindSec} seconds={seconds}");
            await ctrl.PrepareAsync(url, jobDir, "previewseek");                 // PREVIEW — no file written yet
            Log("PREVIEW up; IsPreviewing=" + ctrl.IsPreviewing + " IsRecording=" + ctrl.IsRecording);

            for (int k = 0; k < 4; k++)                                         // observe the live DVR position
            {
                await Task.Delay(1000);
                var p = await ctrl.ProgressAsync();
                Log($"poll[{k}] dvrWindow={p?.DvrWindowSec:F1} behindLive={p?.BehindLiveSec:F1}");
            }

            await ctrl.SeekToBehindAsync(behindSec);                            // rewind
            Log($"seeked to behind={behindSec}");
            await Task.Delay(2500);
            var after = await ctrl.ProgressAsync();
            Log($"after-seek behindLive={after?.BehindLiveSec:F1} (target {behindSec})");

            ctrl.BeginRecording();                                             // record FROM the rewound point
            Log("BeginRecording; IsPreviewing=" + ctrl.IsPreviewing + " IsRecording=" + ctrl.IsRecording);
            await Task.Delay(seconds * 1000);
            await ctrl.StopAsync();
            await Task.WhenAny(done.Task, Task.Delay(30000));
            Log("result: " + (done.Task.IsCompleted ? done.Task.Result : "timeout"));
        }
        catch (Exception e) { Log("EXCEPTION: " + e); }
        finally
        {
            try { File.WriteAllText(Path.Combine(outDir, "previewseek.done"), "done\n"); } catch { }
            Exit();
        }
    }

    private async Task RunAutoRecordAsync(string url, int seconds, string outDir, int quality = 1080)
    {
        Directory.CreateDirectory(outDir);
        var log = Path.Combine(outDir, "autorecord.log");
        void Log(string m) { try { File.AppendAllText(log, m + "\n"); } catch { } }
        try
        {
            var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg) ?? throw new Exception("ffmpeg not found");
            var jobDir = Path.Combine(outDir, "job");
            Directory.CreateDirectory(jobDir);

            var ctrl = new CaptureController(ffmpeg);
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ctrl.Status += s => Log("status: " + s);
            ctrl.Finished += p => { Log("FINISHED: " + p); done.TrySetResult("ok:" + p); };
            ctrl.Failed += m => { Log("FAILED: " + m); done.TrySetResult("fail:" + m); };

            Log($"autorecord url={url} seconds={seconds} quality={quality}");
            await ctrl.StartAsync(url, jobDir, "autorecord", quality: quality);
            await Task.Delay(seconds * 1000);
            await ctrl.StopAsync();
            await Task.WhenAny(done.Task, Task.Delay(30000));
            Log("result: " + (done.Task.IsCompleted ? done.Task.Result : "timeout"));
        }
        catch (Exception e) { Log("EXCEPTION: " + e); }
        finally
        {
            try { File.WriteAllText(Path.Combine(outDir, "autorecord.done"), "done\n"); } catch { }
            Exit();
        }
    }
}
