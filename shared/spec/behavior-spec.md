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
