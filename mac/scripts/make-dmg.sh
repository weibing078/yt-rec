#!/bin/bash
# 把已打包好的 dist/YT Rec.app 做成可分發的 DMG（簽名＋公證＋裝訂）。
# 前置：先跑過 scripts/package-app.sh（產出已公證的 dist/YT Rec.app）。
#
#   export DEV_ID_APP="Developer ID Application: Resona Frame CO., LTD. (4YY9BVXM88)"
#   export KEYCHAIN_PROFILE="AutoSyncNotary"   # 設了才送 DMG 公證（建議）
#   ./scripts/make-dmg.sh
set -euo pipefail
cd "$(dirname "$0")/.."

APP="dist/YT Rec.app"
DMG="dist/YT Rec.dmg"
VOL="YT Rec"

[[ -d "$APP" ]] || { echo "✗ 找不到 $APP，請先跑 scripts/package-app.sh"; exit 1; }

echo "▶ 準備 DMG 內容（App ＋ 拖曳到 Applications 的捷徑）"
STAGE="$(mktemp -d)"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

echo "▶ 建立壓縮 DMG"
rm -f "$DMG"
hdiutil create -volname "$VOL" -srcfolder "$STAGE" -ov -format UDZO "$DMG" 1>&2
rm -rf "$STAGE"

# 自動採用 Developer ID（與 package-app.sh 一致）
if [[ -z "${DEV_ID_APP:-}" ]]; then
    DEV_ID_APP="$(security find-identity -v -p codesigning 2>/dev/null \
        | grep -o '"Developer ID Application:[^"]*"' | head -1 | tr -d '"' || true)"
fi

if [[ -n "${DEV_ID_APP:-}" ]]; then
    echo "▶ 簽名 DMG：$DEV_ID_APP"
    codesign --force --sign "$DEV_ID_APP" --timestamp "$DMG"
else
    echo "▶ 無 Developer ID → DMG 不簽名（內部 App 已簽，但建議補簽 DMG）"
fi

# 公證 DMG（需 Developer ID + notarytool 設定檔）
if [[ -n "${DEV_ID_APP:-}" && -n "${KEYCHAIN_PROFILE:-}" ]]; then
    echo "▶ 送出 DMG 公證（--wait，可能數分鐘）"
    xcrun notarytool submit "$DMG" --keychain-profile "$KEYCHAIN_PROFILE" --wait
    echo "▶ stapler staple"
    xcrun stapler staple "$DMG"
    xcrun stapler validate "$DMG" 1>&2
    echo "✅ 已簽名＋公證＋裝訂，可分發：$DMG"
else
    echo "✅ DMG 已建立（未公證；內含 App 已公證可開）：$DMG"
    echo "   要公證 DMG 請設 KEYCHAIN_PROFILE 後重跑。"
fi

du -sh "$DMG" | sed 's/^/   大小：/'
