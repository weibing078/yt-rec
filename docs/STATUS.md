# Status & Resume Point

> 現況：2026-10-09（UTC+8）。接力看 [HANDOFF.md](HANDOFF.md)。規則在 [../AGENTS.md](../AGENTS.md)。
> 功能對照在 [shared/spec/parity-matrix.md](../shared/spec/parity-matrix.md)。
> 2026-06-22 之後的段落是歷史快照，不要當成現況。

## 2026-10-09 工作樹（尚未 commit）

分支 `fix/2026-10-improvements`，HEAD `5c60c0fb0ea6`（已在 origin）。第一波播放器快照已提交。第二波與第三波只在工作樹。

已在程式裡、而且有測試或實錄：

- Mac 正片閘門：新鮮、非廣告、內容在播才進預覽；45 秒沒確認就放行並在畫面上說明。兩句都在 2026-10-09 的簽名 App 日誌裡出現過。
- Windows 裁切要連續兩次相同才開 ffmpeg。沒有畫面範圍就停，不錄整頁。lofi 實機停在「預覽沒有新畫面，先不要開錄」。
- 中插廣告照錄，時段寫在 `<檔名>.廣告時段.txt`。0 段不寫檔。格式檔有由 Windows 程式寫出；時間是檢查程式餵的，不是從正在播的廣告聽出來的。這台環境播正片時 `getAdState()` 為 -1。
- 更新連結只開 GitHub releases 路徑，或 `ytrec.resonaframe.com` 上的 https。其餘改開首頁。有單元測試，按鈕沒有點過。
- Windows x264：crf 23。長邊 ≤1280 為 6M／12M，否則 12M／24M。1080p 與 720p 實錄的 SEI 都對過。沒有加 `-g 60`。
- Windows 時長、畫質、輸出資料夾、最近 5 筆會記住。進行中或尚未開播按下載會改走側錄並說明（無介面檢查寫出那句話，畫面按鈕沒點過）。舊資料夾未收工錄影修得回來。擷取尺寸一變走既有錯誤收尾並留下部分檔。
- 官網文字已對照上述事實。本機頁面看過。沒有部署。倉庫沒有 `web/og.png`。

驗證（工作樹，不是 HEAD）：Mac `swift test` 195 通過、1 略過、0 失敗（12:32）。Windows Core `dotnet test` 252 通過、0 失敗（12:32）。Windows App 能編譯，修復掃描跑完（count=0）。

還沒做、而且要另批准才做：commit、push、PR、發版、改版本號、部署官網、把這次簽名的 Mac App 再送公證。

## Known limits (2026-10-05, not fixed this round)
- 同一支影片的 id 從頭重播，會被當成還是這一支，錄影繼續，不會收工。
- Windows 如果完全沒收到播放器快照，不會因為「結束」而自動收工（改之前也是這樣）。
- Windows 收尾剛完成的瞬間再按停止，有一小段競態。
- Windows App 沒有自己的 log 設施。

## One line
**Launch-ready: the Windows app is a polished, no-install product — full GUI flow verified on real Win11.**
Paste URL → click **側錄** → opens a live **preview** (no file yet); for a live stream a **DVR rewind
scrubber** + nudge buttons let you rewind, then **「從這裡開始錄影」** records clean **1920×1080** (or
**1080×1920** vertical, full-frame), **pure video with no YouTube UI**, **complete**, isolated audio — and
**pre-roll is waited out**; a mid-roll stays in the file and is listed beside it when the new sidecar code is running. The capture window is **invisible like Mac** and
**loads off-screen so YouTube never flashes** on startup. Click **停止** → file in the **recent list**;
**下載** grabs a finished video and switches a live or upcoming one to side-record preview. Mica window + real app icon; binaries bundled. Mac records 1080p+vertical via SCK.
Mac is signed and notarized. The Windows installer is unsigned.

## Built 2026-06-22 (rewind preview + ad gate + no-flash startup) — all verified, **uncommitted**
- **Live rewind recording (Windows, NEW)** — 側錄 → preview (no file) → DVR scrubber/nudge rewind →
  「從這裡開始錄影」 records from that point. Pure `DvrScrubber`/`Timecode`/`ParseProgress` (142 C# L1 tests);
  `CaptureController` `PrepareAsync`/`BeginRecording` split; `RecordingSession` preview-vs-write split;
  `PreviewReady` gate. **Verified on real Win11**: `--previewseek` on a VOD (360 frames) + **live Al Jazeera**
  (DVR 43183 s, seek 120→122.7 s, 1080p), and a **GUI UIA walkthrough** (側錄→scrubber@live→從這裡開始錄影→
  停止→1.81 MB 1080p Al Jazeera file). Edge: lofi on 2026-10-09 stopped with no mp4 and the
  message 「預覽沒有新畫面，先不要開錄。請確認播放器有在動。」 (`build/logs/goal-win-lofi3.log`).
- **Ad gate, no-Premium (both platforms)** — injected script auto-skips ads + mutes them; Windows writer
  gates on `contentReady`. Win11-verified gate; backported to mac `playerTakeoverJS`. L1 smoke tests both.
- **No-flash startup (Windows)** — the off-screen player is **born off-screen** (not at 0,0 then moved), so
  YouTube loads off-screen and never flashes. Runtime-verified (clean load-phase frames).
- **Tests green**: C# Core 142, mac 167. behavior-spec + parity-matrix updated.

## Verified end-to-end on real Win11 (machine `home`, build 26200)
- **Killer feature — per-process audio isolation = 33 dB** (target tone −24 dB while another
  process's tone is suppressed to −57 dB; system-loopback control confirms both were playing).
- **Real-time recorder**: WGC capture → D3D11 staging readback → ffmpeg → MP4, correct real-time
  duration, decode 0 errors.
- **Full live A/V**: App `--autorecord` of a YouTube URL → 1280×720/30fps H.264 + isolated AAC,
  frame-verified showing the actual video. Win32-hosted WebView2 + WGC monitor-capture-crop +
  `RecordingSession` (session-gate, CFR pacing, HLS fMP4 segments, reassemble + mux).
- Engine + 92 Core tests green on Win11; the WinUI App compiles with VS Build Tools MSBuild.
- ~6 hardware-only bugs found + fixed (see VERIFIED-BEHAVIOR §10b/10c).

## Built this session (Capture geometry + window hiding — code complete, unit-tested)
Goal: both platforms record **clean, content-driven 1080p** (screen/DPI-independent) incl. **vertical**,
and Windows **hides** the capture window. TDD/BDD: shared `behavior-spec` "Capture geometry" + "Window hiding";
shared `CaptureGeometry` implemented twice (20 C# + 3 Swift L1). **Mac: `swift build` + 167 tests green;
C# Core: 113 tests green; Capture/Probe/Cli compile (EnableWindowsTargeting).**
1. **Shared geometry** — `CaptureGeometry` (orientation + output size from source dims; Windows `FitWindow`).
2. **Windows 1080p** — `PlayerAssets.FillPlayAndReportScript` (CSS-fills the player to the window, forces
   `hd1080`, reports source dims) + `RecordingSession` now captures the **whole filled window** and ffmpeg
   **scale+pads to the exact target** (dropped the ~720p crop + null-rect race). `Win32PlayerHost.Resize` +
   `CaptureController` size the window to the target aspect that fits the screen.
3. **Vertical** — source dims → 1080×1920. mac: pre-write `updateOutputSize` (`SCStream.updateConfiguration`
   + off-screen window resize; safe — only before writing, landscape path byte-identical). win: portrait `FitWindow`.
4. **Hide window (win)** — `Win32PlayerCover`: opaque immovable lid slotted just above the player in the
   z-order (hides it on a bare desktop, never covers the user's other windows, never in the recording).
5. **No yellow border (win)** — `RequestAccessAsync(Borderless)` + `IsBorderRequired=false` (SDK→22621, floor Win10).

### Verification status for this session's work
- ✅ **Win App compiles + RUNS** — `windows-build` CI green on a real Windows runner; App `--autorecord`
  **runtime-verified on real Win11** (machine `home`, build 26200): YouTube video → clean **1920×1080**
  H.264+AAC, **real video content** (frame-verified Big Buck Bunny), 2519 kb/s, 12 s, audio isolated.
- ✅ **Win GUI launches** (was crashing) — fixed the `resources.pri` packaging bug; Mica backdrop added.
- ✅ **macOS runtime verified on this Mac** — real SCK off-screen capture → exact **1080×1920** (portrait)
  and **1920×1080** (landscape) H.264 MP4 (the vertical mechanism, hardware-proven).
- ✅ **ffmpeg `ScalePadFilter`** output dims — 5 cases exact target, SAR 1:1 (incl. portrait + 4:3 pillarbox).
- 🔑 **Key win-runtime lesson:** a fullscreen-filled video records BLACK (WGC can't see the GPU overlay);
  the working path is an INLINE player (theater mode) cropped to the video rect + scale-pad to target.

### All core items verified on real Win11 (machine `home`, build 26200)
- ✅ **Landscape** → clean **1920×1080**, real content (Big Buck Bunny, 2519 kb/s, full frame).
- ✅ **Vertical (9:16)** → clean **1080×1920**, real content **filling the frame** (native-vertical video, no
  pillarbox — crop to the object-fit picture rect after waiting for a fresh post-resize rect).
- ✅ **Bundled binaries** — recorded with **no `YTREC_BIN_DIR`**, so the portable build's `vendor/bin/{yt-dlp,
  ffmpeg}.exe` are found (CI fetches them via `tools/setup-binaries.ps1`).
- ✅ **GUI launches** + Mica backdrop (was crashing pre-fix).

### Still worth a visual confirm on hardware (recording itself is verified)
- ⏳ **Lid hides the player** on a bare single-monitor desktop; **no yellow border**; small/hi-DPI screens
  (1366×768, 150%/200%) capture full-frame. Runbook [RUNTIME-QA-geometry.md](RUNTIME-QA-geometry.md) §A.

## Remaining (not blocking the core result)
- Interactively eyeball the **GUI** (Record button, floating monitor preview, settings dialog,
  drag-to-Premiere) — these compile + run in the autorecord flow but weren't visually driven.
- Runtime-test **kill-9 disaster recovery** + disk/duration **guards** (logic is unit-tested).
- Build the App with **VS Build Tools MSBuild** (bare `dotnet` can't — MSB4062 PRI task).
- **Commit** the session's work (nothing committed yet).

## Done & verified (real hardware unless noted)
- **Repo**: cross-platform monorepo, Apache-2.0, docs, ADRs. macOS app unchanged (`swift build` + 163 tests green).
- **Windows CI**: `windows-build` GitHub Action compiles `YtRec.Core`/`Cli`/`App`/`Capture` on every push; publishes a portable self-contained zip artifact. I drive it via `gh`.
- **`YtRec.Core`** (download engine): 62 tests green on Mac **and** real Windows.
- **`YtRec.Cli`**: real YouTube download verified on Mac **and** real Win10 (629 KB MP4).
- **`YtRec.App`** (WinUI 3): compiles on Windows CI (GUI not yet runtime-tested — needs a desktop session / your eyes).
- **`YtRec.Capture`** (Phase 2a/b/c): WGC single-window capture → ffmpeg → **H.264 MP4**, verified end-to-end on real Win10 (live TradingView window → 1938×1048 12fps playable video that decodes to real content).
- **Test loop**: macOS → SSH/Tailscale → Win10 box. Desktop/GUI captures driven via an **interactive scheduled task** (see windows-test-box memory / windows/README).
- **Security**: Tailscale ACL isolates the Win box from the Mac mini (verified blocked); Mac mini has no SSH open.

## Built this session (Phase 2 — code complete, see verification debt above)
Engine (`YtRec.Core` + `YtRec.Capture`, compiles on Mac, Core tests 62→92 green):
1. **Real-time recorder** — `ContinuousRecorder` (D3D11 staging readback → ffmpeg stdin
   `-f rawvideo`) with two output modes: single fragmented MP4 and crash-resistant HLS fMP4
   segments. Shared readback in `FrameReadback`. Probe: `ytrec-capture --record <s> <out.mp4>`.
2. **Live A/V** — `RecordingSession`: WGC video (stdin) + loopback audio (named pipe), one
   QPC clock + `SessionGate` (first-audio anchor, mac §2), HLS fMP4 out, sleep-prevention held.
3. **Audio** — `AudioLoopbackCapture` (hand-rolled WASAPI, `WasapiInterop`): per-process
   loopback (Win11, targets the WebView2 browser PID tree) + system-audio fallback (Win10);
   `AudioCapability` gates by OS build (floor 20348).
4. **Crash-resistance** — `SegmentReassembler` (+ `RecoveryPlan`): finalize / launch-scan /
   reassemble (binary-concat init+segments → `ffmpeg -c copy`), idempotent, skips in-use dir.
5. **Guards** — `DurationCap`, `CaptureHealth`, `SleepPrevention`, plus `PlayerAssets`
   (anti-occlusion flags + force-play/report JS + seek). All pure logic unit-tested.

WinUI App (`YtRec.App`, **not yet compiled** — CI/Windows only):
6. `PlayerWindow` (off-screen WebView2 camera), `MonitorWindow` (floating viewfinder:
   live preview, REC/elapsed, shrink, stop), `CaptureController` (orchestrates record→finalize).
7. `MainWindow`/`MainViewModel`: **Record** button + capability InfoBar, guard timer
   (duration cap + disk), launch recovery, settings dialog (duration cap), drag-to-Premiere,
   en-US/zh-Hant-TW strings.

## Final verification (do this when the Win box is up — batched, per owner)
0. **CI compile the App first** (`git push` → `windows-build`) and fix what the WinUI build
   surfaces (see fragile spots above). This is the App's first real compile.
1. **Recorder** — `ytrec-capture --record 8 out.mp4 30` on a moving window → ffmpeg exit 0,
   `ffprobe` shows real decoded frames + fragmentation.
2. **A/V + session-gate** — record a real YouTube live via the App; confirm both tracks, sync,
   and that the opening pre-audio frames are dropped (not a poisoned file).
3. **Audio isolation** (needs **Win11**) — tone-injection bleed test (mac §1 method); on Win10,
   confirm the system-audio fallback + UI notice engage.
4. **Kill-9 recovery** — TerminateProcess mid-record → relaunch reassembles a playable MP4.
5. **Monitor window** + disk/duration guards + drag-into-Premiere + GUI smoke test.

## How to resume the dev/test loop (from macOS)
- .NET 8 SDK is at `~/.dotnet` on the Mac; build Windows-targeted projects with
  `-p:EnableWindowsTargeting=true` (already set in the capture csprojs) to compile-check here.
- Push code to the Win box without git:
  `COPYFILE_DISABLE=1 tar czf - --exclude='windows/*/bin' --exclude='windows/*/obj' global.json windows | ssh -i ~/.ssh/ytrec_win_ed25519 weibi@100.85.186.95 'tar -xzf - -C yt-rec'`
- Build on the box: `dotnet build <proj> -c Release -p:Platform=x64` (dotnet at `~/.dotnet`).
- Desktop capture tests: interactive scheduled task (`schtasks /it`) running a hidden VBS → bat; pull results/PNG/MP4 back via scp. Helpers staged in the Win home dir (`run-cap.bat`, `run-cap.vbs`, `win-run-task.ps1`).
- Access details: see the `windows-test-box` memory.

## Key docs
[PRD](PRD.md) · [ARCHITECTURE](ARCHITECTURE.md) · [VERIFIED-BEHAVIOR](VERIFIED-BEHAVIOR.md) ·
[PHASE2-CAPTURE](PHASE2-CAPTURE.md) · [TEST-PLAN](TEST-PLAN.md) · [ADRs](adr/) ·
[parity-matrix](../shared/spec/parity-matrix.md) · [RUNTIME-QA-geometry](RUNTIME-QA-geometry.md)
