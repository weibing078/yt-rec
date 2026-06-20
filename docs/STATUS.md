# Status & Resume Point

> Updated: 2026-06-21. Living doc — the place to pick up from. Per-feature detail lives
> in [shared/spec/parity-matrix.md](../shared/spec/parity-matrix.md).

## One line
The Windows port's **core technical risk is retired**: download, single-window capture,
and video encoding are all **runtime-verified on a real Windows machine**. Remaining work
is "make it a product", not "is it possible".

## Done & verified (real hardware unless noted)
- **Repo**: cross-platform monorepo, Apache-2.0, docs, ADRs. macOS app unchanged (`swift build` + 163 tests green).
- **Windows CI**: `windows-build` GitHub Action compiles `YtRec.Core`/`Cli`/`App`/`Capture` on every push; publishes a portable self-contained zip artifact. I drive it via `gh`.
- **`YtRec.Core`** (download engine): 62 tests green on Mac **and** real Windows.
- **`YtRec.Cli`**: real YouTube download verified on Mac **and** real Win10 (629 KB MP4).
- **`YtRec.App`** (WinUI 3): compiles on Windows CI (GUI not yet runtime-tested — needs a desktop session / your eyes).
- **`YtRec.Capture`** (Phase 2a/b/c): WGC single-window capture → ffmpeg → **H.264 MP4**, verified end-to-end on real Win10 (live TradingView window → 1938×1048 12fps playable video that decodes to real content).
- **Test loop**: macOS → SSH/Tailscale → Win10 box. Desktop/GUI captures driven via an **interactive scheduled task** (see windows-test-box memory / windows/README).
- **Security**: Tailscale ACL isolates the Win box from the Mac mini (verified blocked); Mac mini has no SSH open.

## Next session starts here (Phase 2, in order)
1. **Real-time continuous recorder** — replace the PNG-sequence proof with live raw-frame
   piping (D3D11 staging-texture readback → ffmpeg stdin `-f rawvideo`) → fragmented MP4
   (`-movflags +frag_keyframe+empty_moov`). The PoC (`CaptureFramesToDirAsync`) proves the
   concept; this is the product-grade path.
2. **WebView2** — play the real YouTube stream in an embedded WebView2 (anti-occlusion
   flags: `--disable-features=CalculateNativeWinOcclusion ...`, keep `IsVisible=true`
   off-screen), then WGC-capture that window. See PHASE2-CAPTURE.md §2d.
3. **Audio** — WASAPI process-loopback targeting the WebView2 browser process tree.
   **Requires Windows 11** to verify isolation (current test box is Win10 22H2 → falls back
   to system-audio). See ADR-0004.
4. **Monitor window** (visible/movable/always-on-top/shrink-to-background) + session-gate,
   disk/duration guards (reuse `YtRec.Core`), disaster recovery, notifications, i18n wiring.
5. Wire it all into `YtRec.App`; GUI runtime test on Win11.

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
[parity-matrix](../shared/spec/parity-matrix.md)
