# Runtime QA — Capture geometry, vertical & window-hiding

Acceptance tests for the "content-driven 1080p + vertical + hide window" work (commit
`9503fec`). Everything that does **not** need live hardware is already green:

| Layer | Status |
|---|---|
| BDD spec (`behavior-spec` "Capture geometry" / "Window hiding") | ✅ |
| TDD unit — Swift (`CaptureGeometry`, `parseDims`, JS smoke) | ✅ 167 green (`swift test`) |
| TDD unit — C# (`CaptureGeometry` 20, `FitWindow`, `PlayerAssets` fill) | ✅ 113 green (`dotnet test`) |
| Compile — macOS app | ✅ `swift build` |
| Compile — full Windows incl. **WinUI App** + portable artifact | ✅ CI `windows-build` on a real Windows runner |
| Runtime — ffmpeg `ScalePadFilter` output dims (5 cases, incl. portrait & 4:3 pillarbox) | ✅ exact target, SAR 1:1 |

What's left needs **live capture hardware** (the physical Win11 box must be powered on;
the Mac needs a logged-in GUI session with Screen-Recording permission).

---

## A. Windows (real Win11 box) — push, build, record, inspect

```sh
# 1. Push the tree to the box (no git needed) — from macOS repo root:
COPYFILE_DISABLE=1 tar czf - --exclude='windows/*/bin' --exclude='windows/*/obj' \
  global.json windows | ssh -i ~/.ssh/ytrec_win_ed25519 weibi@100.85.186.95 'tar -xzf - -C yt-rec'

# 2. Build on the box (App needs VS Build Tools MSBuild; engine with dotnet):
#    dotnet build windows/YtRec.CaptureProbe -c Release -p:Platform=x64
#    msbuild   windows/YtRec.App/YtRec.App.csproj /restore /p:Configuration=Release /p:Platform=x64

# 3. Full-path runtime via the App's autorecord (exercises CaptureController + Win32PlayerHost
#    + RecordingSession scale-pad + fill + dims + lid + border). Run under a desktop session.
#    YtRec.App.exe --autorecord "<youtube url>" --seconds 12 --out C:\ytrec-qa\out.mp4
```

**Pass criteria** (ffprobe each output: `ffprobe -v error -select_streams v:0 -show_entries stream=width,height,sample_aspect_ratio -of csv=p=0 out.mp4`):

1. **Landscape 1080p source** → output **1920×1080**, SAR 1:1, real 1080p detail (not an upscaled 720p crop). *This is the headline fix vs the old ~1280×720.*
2. **Vertical 9:16 (Shorts/portrait) source** → output **1080×1920**, SAR 1:1, full-frame portrait (no black-square crop). *The old broken case.*
3. **No yellow WGC border** anywhere on screen **and** none baked into the frame edges.
4. **Lid hides the player**: with nothing else open on a single monitor, the live YouTube page is **not** visible (only the opaque cover + the small viewfinder). The cover must **not** appear in the recording.
5. **Fill ≠ blank**: confirm the video actually composites into the captured surface (the disable-overlay flags work) — frames are the real video, not black.
6. **Small / hi-DPI screens** — repeat (1) at **1366×768 @100%**, **1920×1080 @150%**, **2560×1600 @200%**: output dims identical, capture returns a full frame (no overhang clip / partial / black).

## B. macOS — record a vertical video

```sh
swift run   # launch the app in a GUI session; grant Screen Recording if prompted
# Record a known VERTICAL (9:16) YouTube video for ~10 s, then stop.
```

**Pass criteria**: the saved `側錄_*.mp4` is **1080×1920** (not a letterboxed 16:9 1920×1080).
Landscape recordings must be unchanged (still exactly 1920×1080 / 1280×720). Verify the
`updateOutputSize` switch logged `輸出尺寸更新…（直式偵測）` and did **not** fire for landscape.

---

Once A + B pass, set the matrix rows in
[`shared/spec/parity-matrix.md`](../shared/spec/parity-matrix.md) (Clean 1080p / Vertical /
Hide window / No border) from 🟡 → ✅ and update [`STATUS.md`](STATUS.md).
