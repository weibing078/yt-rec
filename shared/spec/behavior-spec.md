# Behavior Spec (normative, language-neutral)

The canonical contracts for the pure logic that **both** the macOS (Swift) and
Windows (C#) apps implement. When code and this spec disagree, fix the side that
is wrong — but they must converge. Every contract here should have L1 unit tests
on both platforms ([TEST-PLAN.md](../../docs/TEST-PLAN.md)).

## URL parsing → video ID
- Accept `watch?v=`, `youtu.be/`, `youtube.com/live/`, `shorts/`, `embed/`, `/v/`,
  and `youtube-nocookie.com/embed/`. Extract the **11-char** ID.
- ID charset includes `-` and `_`. Length ≠ 11 → reject (10 and 12 are invalid).
- Trim surrounding whitespace; ignore extra query params (`&t=120s`, `?si=…`).
- Non-YouTube hosts, channel pages, and playlist-only links (`list=` with no
  `v=`) → reject with a friendly message; never pass garbage to yt-dlp.

## Download strategy order (keyed on `live_status`)
- `is_live` / `is_upcoming` → `[from-start, normal, degrade720]`
- `post_live` / `was_live` / `not_live` → `[normal, from-start, degrade720]`
- `from-start` adds `--live-from-start` (+ `--wait-for-video 60` if `is_upcoming`).
- `degrade720` uses `-S res:720,vcodec:h264`.
- A full failed round → wait **30 s**, retry. Give up after **6 h**.

## Download decision (auto / off / always)
- **off** (default) → never download; side-record only.
- **auto** → download only ended VODs: `post_live`/`was_live`/`not_live` → yes;
  `is_live`/`is_upcoming`/probe-failure/NA → no (side-record only).
- **always** → attempt regardless, **except** marathon intercept applies.

## Marathon detection
- `is_live` AND `(now − release_timestamp) > 4 h` → marathon → side-record only.
- Boundary: exactly 4 h → marathon; 3 h 59 m → not. No timestamp / ended → not.

## Terminal vs. retryable failure (yt-dlp output match)
- **Terminal** (stop polling): private video, video unavailable, removed by
  uploader, members-only / join this channel, age-restricted / "sign in to
  confirm your age", account terminated, geo-blocked.
- **Retryable**: HTTP 404/5xx, "this live event has ended", "no video formats",
  network errors.
- Any terminal failure MUST produce a clear localized reason — never the generic
  "download failed" fallback.

## Probe parsing
- Probe print format: `%(id)s\t%(title)s\t%(live_status)s\t%(release_timestamp)s`.
- Title may contain tabs → take `live_status` and `release_timestamp` from the
  **last two** fields, join the middle as the title. `id` must be 11 chars.
- `release_timestamp == "NA"` → null. 3-field (no timestamp) form accepted.

## Capture geometry (resolution / orientation) — screen-independent
The recorded file's pixel size is a function of the **source video** and the user's
quality setting **only** — never the host screen or its DPI. Both platforms compute
it from the same rule (`CaptureGeometry`).
- **Orientation**: `videoHeight > videoWidth` → **portrait**; square and landscape →
  **landscape**. (`videoWidth == 0` / unknown → landscape.)
- **Output size** (quality = long-edge target, `1080` default or `720`):
  - landscape → `1920×1080` (or `1280×720`)
  - portrait  → `1080×1920` (or `720×1280`)
  - always even dimensions.
- **The player fills the capture surface**: the embedded YouTube player container
  chain is CSS-stretched to `100vw/100vh` and quality is pinned (`hd1080`), so the
  captured surface is the video edge-to-edge (a non-matching source aspect — e.g. a
  4:3 clip — is `object-fit:contain` letterboxed inside the target frame, never
  distorted). No page chrome is ever recorded.
- **macOS** renders an off-screen window sized **exactly** to the output and lets
  ScreenCaptureKit scale to it → output is deterministic on any screen.
- **Windows** must keep the window on-screen and WGC captures native pixels, so it
  sizes the on-screen window to the **largest box of the target aspect that fits the
  screen** (`FitWindow`, capped at the target, even dims) and ffmpeg **scales+pads to
  the exact target**. Result is the same deterministic output; sub-target screens
  trade a little sharpness (upscale) for never overhanging the screen edge.

## Window hiding (capture source must stay unseen)
- **macOS**: the capture window lives fully **off-screen** (beyond all monitors);
  SCK still captures it. The user never sees it.
- **Windows**: WGC yields **no frames** for a fully off-screen window, so the player
  stays on-screen, composited, and is **hidden by other means**:
  - default: parked at `(0,0)`, no-activate, tool-window (no taskbar/Alt-Tab),
    pushed to the bottom of the z-order (hidden whenever any window is in front);
  - worst case (bare single-monitor desktop): covered by an **opaque, immovable,
    full-player-size app lid** so the live page is never visible. WGC captures the
    player's **own** surface, so the lid is never in the recording.
  - the **WGC yellow capture border is disabled** (`IsBorderRequired = false`) so it
    appears neither on screen nor in the output.

## Live rewind scrubber (preview → record-from-here)
For a live stream with a DVR window the user **previews first**, rewinds to the moment
to start, then records from there. The scrubber math is pure (`DvrScrubber`), L1-tested
on both platforms (macOS implements it inline in the monitor UI). Fraction `0` = oldest
rewindable point, `1` = live edge.
- `window = max(1, dvrWindowSec)`. The scrubber is shown **only when
  `dvrWindowSec > 90`** (a window of ≤ 90 s is too short to position in).
- `liveFrac = clamp(1 − behindLiveSec / window, 0, 1)`.
- Knob position (`shownFrac`): the live **drag** value while dragging; else the held
  release **settle** target (`1 − settleTargetBehind / window`); else `liveFrac`.
- `shownBehind = max(0, (1 − shownFrac) · window)`; a fraction → seek target is
  `behindForFrac(f) = max(0, (1 − f) · window)`.
- Position readout: `< 3 s` behind → `● 直播即時`; else `落後直播 <M:SS>`.
- Drag-release **hold**: after release the knob is held at the target until a fresh
  polled position lands within `tolerance = max(2, window · 0.02)` of it (stops the knob
  snapping back to a stale pre-seek poll); a 2.5 s fallback releases it.
- Tick marks: none for `window ≤ 120 s`; else spacing = 1 h (`window ≥ 2 h`) / 10 min
  (`≥ 10 min`) / else 1 min, at fractions measured from the live edge.
- **Record-from-here** begins writing from the current player position; **cancel
  preview** tears down with **no file produced** (mirrors the §Stop "positioning" rule).

## Ad gate (no-Premium: never record an ad)
Side-record only — the download (yt-dlp) path is ad-free by construction.
- The injected player script auto-clicks any **skip** control the instant it appears,
  keeps an ad **muted**, and never reports an ad's geometry as the capture size.
- Recording start is **gated on real content**: `contentReady` = a non-ad video is
  actually playing (`!ad && !paused && currentTime > 0 && readyState ≥ 3 &&
  videoWidth > 0`). The writer waits for `contentReady` (cap **45 s**, then proceed so a
  detection miss can't hang), then the normal audio-settle + session gate apply.

## App update check (notify, never auto-install)
A hosted manifest `latest.json` (on the landing page) is the single source of truth for the
newest release; pure logic is `AppUpdate` (L1-tested), the app does the fetch + the notice.
- Manifest fields: `version` (dotted-numeric), `pubDate`, `notes` (`{lang: text}` or a string),
  `mac`/`win` `{ url, minOS }`, `page`.
- `IsNewer(current, latest)`: dotted-numeric (so `1.10` > `1.9`), missing parts = 0, a leading
  `v` and any `-pre`/`+meta` suffix ignored; **unparseable → never newer** (a bad manifest or
  unknown latest can't nag; an unknown *current* errs toward offering).
- On launch (throttled to once per ~24 h, **fail-silent** with no network), fetch the manifest,
  compare to the app's own version; if newer show a **non-blocking** notice
  (`有新版 vX · <notes> · 下載更新`) that opens the download URL/`page`.
- **Never auto-download or auto-install** — the app is unsigned. Download URLs are GitHub
  `releases/latest/download/<asset>` (evergreen → always newest); a release only bumps
  `version`/`pubDate`/`notes` in the manifest (`tools/release.sh`).

## Session gate (the corruption-prevention rule)
- The **first audio sample** sets the session start (PTS baseline).
- Audio: if `audioDisabled` → drop; else if not started → start session; else
  append.
- Video: if started → append; else if `audioDisabled` → start session (video-only
  survival); else **drop** (waiting for first audio).
- `retime`: shift all timing by the baseline so PTS starts at 0; never emit
  negative PTS; shift valid DTS, keep invalid.

## Health check (per ~2 s tick)
- `ticks ≥ 4` and 0 complete frames → warn "window minimized?".
- `ticks ≥ 6`, frames received, session not started, audio not disabled →
  set `audioDisabled` (video-only survival).

## Disk guard
- Pre-start: `<8 GB` refuse side-record; `8–15 GB` warn (confirm); `>15 GB` ok.
- Lookup failure → treat free space as max (don't block external/network drives).
- During record: check every **60 s**; `<10 GB` → auto-finalize + save + notify.
- Invariant: stop threshold (10) > refuse threshold (8).

## Duration cap
- 3 h / **6 h default** / 12 h / unlimited. At the cap → auto-finalize + save +
  notify. `0` (unlimited) → never auto-stop on duration.

## Stop / outcome notifications (distinct messages)
- `durationLimit`, `lowDisk`, `playerEnded`, `userStopped` → each a distinct
  localized message.
- Track A `nativeSucceeded` while B writing → stop B, "side-record auto-finished".
- `nativeSucceeded` while B **not** running → filename only (must not falsely
  claim auto-finish).
- `nativeSucceeded` while B **positioning** (not yet writing) → do NOT stop B, do
  NOT close the monitor.
- Empty result → Track B failed "no output produced".

## Start-job guard precedence
1. empty input → ignore
2. **already-active-job → block** (this beats the next check)
3. non-YouTube → reject
4. valid → start

## Output naming
- Task folder: `<output>/YYYYMMDD-HHMM <id-or-title>/`.
- Side-record: `側錄_<title>.mp4`. Download: yt-dlp template
  `%(title).70B [%(id)s].%(ext)s`. Segments: `.work/segments/seg_init`,
  `seg_%05d`.
- Disaster-recovery output: `側錄_上次未收工自動修復.mp4`.
