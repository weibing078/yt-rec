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
            _ = RunAutoRecordAsync(cmd[i + 1], secs, cmd[i + 3]);
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

        _window = new MainWindow();
        _window.Activate();
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

    private async Task RunAutoRecordAsync(string url, int seconds, string outDir)
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

            Log($"autorecord url={url} seconds={seconds}");
            await ctrl.StartAsync(url, jobDir, "autorecord");
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
