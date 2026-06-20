# YT Rec — Windows

Native Windows build: **C# / .NET 8 + WinUI 3** (see
[ADR-0002](../docs/adr/0002-windows-native-stack.md)).

## Status

| Project | What | State |
|---|---|---|
| `YtRec.Core` | Cross-platform logic (download engine, parsing, disk/marathon, locator) | ✅ 62 tests green (runs on any OS) |
| `YtRec.Core.Tests` | xUnit suite mirroring the macOS cases | ✅ |
| `YtRec.App` | WinUI 3 main window (Phase 1 = download track UI) | 🚧 written; **needs a Win11 build pass** |

> ⚠️ `YtRec.App` (WinUI 3) **cannot be built or tested on macOS/Linux** — it needs a
> real Windows 11 environment. It was authored on a Mac, so expect the first
> `dotnet build` on Windows to surface minor fix-ups (glyph codes, package
> versions, x:Uid resource keys). Build it, paste any errors back, and we iterate.

## Layout (implemented)

```
windows/
├── YtRec.sln
├── YtRec.Core/            net8.0 logic library (buildable & unit-tested anywhere)
├── YtRec.Core.Tests/      xUnit
├── YtRec.App/             WinUI 3, unpackaged + self-contained (portable, unsigned-friendly)
│   ├── App.xaml(.cs)      app entry + merged resources
│   ├── MainWindow.xaml(.cs)  control-center window (custom Fluent title bar)
│   ├── MainViewModel.cs   MVVM; wires YtDlpEngine + BinaryLocator (download flow)
│   ├── Themes/Brand.xaml  CIS brand tokens (signal red / crimson) light+dark
│   ├── XamlHelpers.cs     x:Bind visibility helpers
│   └── Strings/{en-US,zh-Hant-TW}/Resources.resw   locale-based i18n
└── vendor/bin/            yt-dlp.exe + ffmpeg.exe (tools\setup-binaries.ps1; not committed)
```

## Build & run (on Windows 11)

```powershell
tools\setup-binaries.ps1          # fetch yt-dlp.exe + ffmpeg.exe
dotnet test windows\YtRec.Core.Tests\YtRec.Core.Tests.csproj   # logic suite (also works on Mac)
# App: open windows\YtRec.sln in Visual Studio 2022 (Windows App SDK / .NET desktop workload),
# or: dotnet build windows\YtRec.App\YtRec.App.csproj -c Debug
```

On macOS, develop/test `YtRec.Core` only:
```bash
dotnet test windows/YtRec.Core.Tests/YtRec.Core.Tests.csproj
```

## Phase 1 vs Phase 2

- **Phase 1 (this UI):** download track — paste a URL → probe → download (incl.
  `--download-sections` for a clip range) → recent-outputs list with play / reveal.
- **Phase 2:** screen-capture / monitor window (Windows.Graphics.Capture + WASAPI
  process-loopback audio → fragmented-MP4), the floating always-on-top monitor,
  rewind recording, permissions panel, settings. The main-window layout already
  reserves space for these (recording card, monitor button).

See [../docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md),
[../docs/VERIFIED-BEHAVIOR.md](../docs/VERIFIED-BEHAVIOR.md), and
[../docs/TEST-PLAN.md](../docs/TEST-PLAN.md) before writing capture code.
