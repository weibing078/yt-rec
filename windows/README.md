# YT Rec — Windows (in development)

Native Windows build: **C# / .NET 8 + WinUI 3** (see
[ADR-0002](../docs/adr/0002-windows-native-stack.md)).

## Planned layout

```
windows/
├── YtRec.sln
├── YtRec.Core/         Cross-platform .NET logic library — buildable & unit-testable
│                       on any OS. Ports the pure logic in shared/spec/behavior-spec.md
│                       (URL parse, strategy order, disk/duration/marathon, session-gate,
│                       probe parse, notifications, disaster-recovery decisions).
├── YtRec.Core.Tests/   xUnit suite mirroring the macOS L1/L2 cases.
├── YtRec.App/          WinUI 3 app — UI + the capture engine:
│                         • Windows.Graphics.Capture (single-window video)
│                         • WASAPI process-loopback (per-window audio; Win11/20348+)
│                         • WebView2 (embedded stream player)
│                         • ffmpeg/yt-dlp invoked as subprocesses
└── vendor/bin/         yt-dlp.exe + ffmpeg.exe (fetched by tools/setup-binaries.ps1,
                        not committed)
```

## Build order (roadmap Phase 1 → 2)

1. **Phase 1 — Download track.** Implement & unit-test `YtRec.Core` (no Windows
   needed for the logic), then a minimal WinUI shell to drive a real download on
   Windows 11.
2. **Phase 2 — Capture engine.** WGC video + process-loopback audio →
   fragmented-MP4 segments; the floating monitor window; graceful audio
   degradation on older Win10.

See [../docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md),
[../docs/VERIFIED-BEHAVIOR.md](../docs/VERIFIED-BEHAVIOR.md), and
[../docs/TEST-PLAN.md](../docs/TEST-PLAN.md) before writing capture code — the
hard-won macOS lessons (audio-timeline corruption, renderer-in-child-process,
hidden-player audio throttling) apply directly.

## Build prerequisites (on a real Windows 11 machine)

- Visual Studio 2022 with **.NET 8 SDK** + **Windows App SDK / WinUI 3** workload
- `tools\setup-binaries.ps1` to fetch yt-dlp.exe + ffmpeg.exe
