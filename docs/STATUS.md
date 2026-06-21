# Status & Resume Point

> Updated: 2026-06-21. Living doc — the place to pick up from. Per-feature detail lives
> in [shared/spec/parity-matrix.md](../shared/spec/parity-matrix.md).

## One line
**The Windows app is a complete, usable, no-install app — the whole GUI flow is verified on real
Win11.** Driving the actual MainWindow via UI Automation: paste URL → click **側錄** → records clean
**1920×1080** (or **1080×1920** vertical, full-frame) real video + isolated audio, the live page is
**hidden behind the lid**, the **viewfinder** shows a live preview, click **停止** → the file lands in
the **recent list**; the **下載** button downloads a VOD too. Mica window; binaries bundled (runs from a
fresh extract). Both platforms now record content-driven 1080p; Mac verified via SCK. Signing skipped per owner.

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
