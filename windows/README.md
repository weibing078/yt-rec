# YT Rec — Windows

Native Windows build: **C# / .NET 8 + WinUI 3** (see
[ADR-0002](../docs/adr/0002-windows-native-stack.md)).

## Status

| Project | What | State |
|---|---|---|
| `YtRec.Core` | Cross-platform logic (download engine, parsing, disk/marathon, locator) | ✅ 62 tests green (runs on any OS) |
| `YtRec.Core.Tests` | xUnit suite mirroring the macOS cases | ✅ |
| `YtRec.App` | WinUI 3 main window (Phase 1 = download track UI) | ✅ compiles on Windows CI · GUI/runtime test pending |

> `YtRec.App` (WinUI 3) **cannot be built on macOS/Linux** — but the
> [`windows-build`](../.github/workflows/windows-build.yml) GitHub Actions workflow
> compiles it on a real `windows-latest` runner on every push to `windows/**`. It is
> currently **green** (compiles clean). What remains is **runtime/GUI testing** — running
> the app and a real download — which needs a real Windows screen (your Win11 box or a
> Windows VM). Build/run locally with the steps below; paste any runtime issues back.

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

## No-install (portable) distribution

The app is **unpackaged** (`WindowsPackageType=None` — no MSIX/installer) and the
release build is **self-contained** (bundles the .NET 8 runtime + Windows App SDK).
Result: unzip → run `YtRec.exe`. No installer, no .NET install, no admin.

The [`windows-build`](../.github/workflows/windows-build.yml) CI publishes this on
every push and uploads it as the **`YtRec-win-x64-portable`** artifact (~61 MB; add
`yt-dlp.exe` + `ffmpeg.exe` for the full ~150 MB release bundle). Produce it locally
on Windows with:

```powershell
msbuild windows\YtRec.App\YtRec.App.csproj /t:Publish /p:Configuration=Release ^
  /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:WindowsAppSDKSelfContained=true
# output: windows\YtRec.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

> WebView2 Runtime (Phase 2, for playing YouTube) is preinstalled on Win11 and most
> Win10; bundle the fixed-version runtime only if targeting a bare machine.

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
