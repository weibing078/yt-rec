#!/usr/bin/env bash
# Cut a release: bump the version everywhere + the landing-page manifest, then print the manual
# publish checklist. Does NOT build, tag, push, or deploy — distribution stays your call (ADR-0005).
#
#   tools/release.sh <version> "<zh-Hant notes>" ["<en notes>"]
#   e.g.  tools/release.sh 1.1.0 "倒帶預覽、自動略過廣告、啟動不閃" "Live rewind, ad-skip, no-flash"
set -euo pipefail
cd "$(dirname "$0")/.."

ver="${1:?usage: release.sh <version> \"<zh notes>\" [\"<en notes>\"]}"
zh="${2:?need zh-Hant release notes}"
en="${3:-$zh}"
date_iso="$(date +%Y-%m-%d)"
[[ "$ver" =~ ^[0-9]+\.[0-9]+(\.[0-9]+)?$ ]] || { echo "version must be dotted-numeric (e.g. 1.1.0)"; exit 1; }

# 1) Landing-page manifest — the single source of truth the in-app updater AND the page read.
cat > web/latest.json <<JSON
{
  "version": "$ver",
  "pubDate": "$date_iso",
  "notes": {
    "zh-Hant": "$zh",
    "en": "$en"
  },
  "mac": {
    "url": "https://github.com/weibing078/yt-rec/releases/latest/download/YT-Rec.dmg",
    "minOS": "14.4"
  },
  "win": {
    "url": "https://github.com/weibing078/yt-rec/releases/latest/download/YT-Rec-win64.zip",
    "minOS": "10.0.20348"
  },
  "page": "https://ytrec.resonaframe.com/#download"
}
JSON
echo "✓ web/latest.json → $ver ($date_iso)"

# 2) App version strings — so the running app knows its own version for the IsNewer compare.
# PlistBuddy, not sed — the key/string sit on separate lines, which a line-based sed can't span.
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $ver" mac/scripts/Info.plist
echo "✓ mac Info.plist CFBundleShortVersionString → $(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' mac/scripts/Info.plist)"
if grep -q '<Version>' windows/YtRec.App/YtRec.App.csproj; then
  sed -i '' -E "s#<Version>[^<]*</Version>#<Version>$ver</Version>#" windows/YtRec.App/YtRec.App.csproj
  echo "✓ windows YtRec.App.csproj <Version> → $ver"
fi

cat <<NEXT

Next (manual — distribution stays your call, ADR-0005):
  1. Commit the version bump:  git commit -am "release: v$ver"
  2. Build:    mac → mac/scripts/make-dmg.sh   ·   win → CI 'windows-build' artifact
  3. GitHub:   draft a Release tagged v$ver, attach YT-Rec.dmg (+ YT-Rec-win64.zip)
  4. Deploy:   publish web/ to Cloudflare Pages (latest.json must go live)
  5. Verify:   curl -s https://ytrec.resonaframe.com/latest.json | grep '"version": "$ver"'
  Installed users see the in-app "new version" notice within ~24 h.
NEXT
