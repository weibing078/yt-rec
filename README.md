<!-- Language: English first, 繁體中文在後 -->

# YT Rec

> Record one YouTube live stream — its video **and only its audio** — while you
> keep working, without capturing your other apps' sound.
>
> 把單一 YouTube 直播視窗「單獨」錄下來(畫面＋**只有該視窗的聲音**),
> 看得到、聽得到,但**不佔用你的工作畫面、也不會錄到電腦其他聲音**。

YT Rec is a desktop tool for news / current-affairs video editors who need to
grab long or in-progress YouTube live streams (press conferences, parliament,
24-hour channels) and pull highlight clips afterwards in their NLE (e.g.
Premiere).

---

## Why it's different (vs. plain screen recording)

| | Plain screen recording | YT Rec |
|---|---|---|
| What it records | The whole screen | **Only that one stream window** |
| Work while recording | No (screen is occupied) | **Yes** — other windows on top aren't captured |
| Other apps' audio | Mixed in | **Excluded** — only the stream window's audio |
| See what's recording | Yes, but it occupies the screen | **Yes** — a small, movable, pin-on-top monitor window |
| Very long streams | File bloats, fragile | fMP4 crash-resistant segments + disk/duration guards |

---

## Platforms

| Platform | Status | Stack |
|---|---|---|
| **macOS** (14.4+) | ✅ Shipping (v1.0) | Swift / SwiftUI · ScreenCaptureKit · AVFoundation |
| **Windows** (11; 10 best-effort) | 🚧 In development | C# / .NET 8 · WinUI 3 · Windows.Graphics.Capture · WASAPI |

The two apps are **native on each platform** (no shared-UI framework). What is
kept in sync is the *behavior spec* under [`shared/spec/`](shared/spec/) and the
acceptance tests in [`docs/TEST-PLAN.md`](docs/TEST-PLAN.md) — both
implementations must pass the same criteria. See
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the rationale.

---

## Repository layout

```
yt-rec/
├── docs/         Product & engineering docs (PRD, architecture, verified
│   └── adr/      behavior, test plan) + Architecture Decision Records
├── shared/
│   ├── spec/     Language-neutral behavior spec (the source of cross-platform parity)
│   └── branding/ Brand source assets (SVG, icons)
├── mac/          macOS app — Swift Package (Package.swift, Sources/, Tests/, scripts/)
├── windows/      Windows app — .NET 8 solution (Core lib + WinUI 3 app)
└── tools/        setup-binaries.{sh,ps1}, make-icons.sh
```

---

## Build

External tools (`yt-dlp`, `ffmpeg`) are **not** committed; fetch them first.

### macOS

```bash
tools/setup-binaries.sh          # downloads yt-dlp + ffmpeg into mac/Vendor/bin
cd mac
swift build                      # debug build
swift test                       # run the unit suite
./scripts/run.sh --rebuild       # package + launch the .app
```

### Windows

```powershell
tools\setup-binaries.ps1         # downloads yt-dlp.exe + ffmpeg.exe into windows\vendor\bin
cd windows
dotnet test                      # the cross-platform Core library is testable anywhere
# Build/run the WinUI app from Visual Studio 2022 (Windows 11) or `dotnet build`
```

> Note: the WinUI app and the native capture engine require a **real Windows
> environment** to build and test. The `YtRec.Core` logic library is plain .NET
> and can be developed and unit-tested on any OS.

---

## License

[Apache-2.0](LICENSE). The "YT Rec" name and icon are trademarks — see
[NOTICE](NOTICE). Use only to record content you are entitled to.

---

## 繁體中文摘要

YT Rec 是給新聞／政經時事剪輯師的桌面工具,用來搶錄超長或進行中的 YouTube
直播(記者會、立院、24hr 台),事後再進 Premiere 剪精華。核心價值:**只錄那一個
直播視窗的畫面與聲音,邊錄邊能正常工作,而且不會把電腦其他聲音錄進去。**

- **macOS**:已上線(v1.0),原生 Swift/SwiftUI。
- **Windows**:開發中,原生 C#/.NET 8 + WinUI 3。

兩個平台各自原生實作,靠 [`shared/spec/`](shared/spec/) 的行為規格與
[`docs/TEST-PLAN.md`](docs/TEST-PLAN.md) 的驗收標準保持同步。詳見
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。
