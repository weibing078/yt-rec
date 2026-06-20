#!/bin/bash
# 打包 YT Rec.app（release 編譯 + 內嵌 yt-dlp/ffmpeg + 簽名 [+公證]）
#
# 直接執行：機器上有 Developer ID Application 憑證 → 自動採用（TCC 權限黏得住）；
#          沒憑證才退回 ad-hoc（可在本機跑，但無法分發、且重打包會掉螢幕錄製授權）。
#   ./scripts/package-app.sh
#
# 公證分發 / 指定憑證：先設環境變數再執行。
#   export DEV_ID_APP="Developer ID Application: 你的名字 (TEAMID)"   # 選填，覆蓋自動偵測
#   export KEYCHAIN_PROFILE="lcf-notary"                              # 選填，設了才送公證
#   ./scripts/package-app.sh
#
# notarytool 設定檔一次性建立（之後 KEYCHAIN_PROFILE 用同一個名字）：
#   xcrun notarytool store-credentials "lcf-notary" \
#     --apple-id <你的AppleID> --team-id <TEAMID> --password <App 專用密碼>
# 可用簽名身分查詢：security find-identity -v -p codesigning
set -euo pipefail
cd "$(dirname "$0")/.."

echo "▶ swift build -c release"
swift build -c release 1>&2

APP="dist/YT Rec.app"
rm -rf dist
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin"

cp .build/release/YTRec "$APP/Contents/MacOS/YTRec"
cp scripts/Info.plist "$APP/Contents/Info.plist"
cp ../shared/branding/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cp Vendor/bin/yt-dlp "$APP/Contents/Resources/bin/yt-dlp"
cp Vendor/bin/ffmpeg "$APP/Contents/Resources/bin/ffmpeg"
chmod +x "$APP/Contents/Resources/bin/"*

# 去除可能的隔離屬性
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

ENTITLEMENTS="scripts/ytdlp.entitlements"

# 未手動指定 → 自動採用機器上的 Developer ID Application 憑證（有就用，沒有才退回 ad-hoc）。
# 理由：adhoc 重打包會讓 TCC 螢幕錄製授權對不上簽章身分→「設定顯示開、App 內仍未授權」。
if [[ -z "${DEV_ID_APP:-}" ]]; then
    DEV_ID_APP="$(security find-identity -v -p codesigning 2>/dev/null \
        | grep -o '"Developer ID Application:[^"]*"' | head -1 | tr -d '"' || true)"
    [[ -n "$DEV_ID_APP" ]] && echo "▶ 自動採用 Developer ID 憑證：$DEV_ID_APP"
fi

if [[ -n "${DEV_ID_APP:-}" ]]; then
    echo "▶ Developer ID 簽名：$DEV_ID_APP"
    SIGN=( --force --options runtime --timestamp --sign "$DEV_ID_APP" )
    # 先簽內嵌 binary（yt-dlp 是 PyInstaller，需要 entitlements），再簽整個 .app
    codesign "${SIGN[@]}" --entitlements "$ENTITLEMENTS" "$APP/Contents/Resources/bin/yt-dlp"
    codesign "${SIGN[@]}" --entitlements "$ENTITLEMENTS" "$APP/Contents/Resources/bin/ffmpeg"
    codesign "${SIGN[@]}" "$APP"
else
    echo "▶ 未設 DEV_ID_APP → ad-hoc 簽名（僅供本機測試，無法分發／公證）"
    codesign --force --sign - "$APP/Contents/Resources/bin/yt-dlp"
    codesign --force --sign - "$APP/Contents/Resources/bin/ffmpeg"
    codesign --force --sign - "$APP"
fi

echo "▶ 驗證簽名"
codesign --verify --deep --strict --verbose=2 "$APP" 1>&2 || true

# 公證（需 Developer ID + notarytool 設定檔）
if [[ -n "${DEV_ID_APP:-}" && -n "${KEYCHAIN_PROFILE:-}" ]]; then
    ZIP="dist/YTRec.zip"
    echo "▶ 壓縮並送出公證（--wait，可能數分鐘）"
    /usr/bin/ditto -c -k --keepParent "$APP" "$ZIP"
    xcrun notarytool submit "$ZIP" --keychain-profile "$KEYCHAIN_PROFILE" --wait
    echo "▶ stapler staple"
    xcrun stapler staple "$APP"
    xcrun stapler validate "$APP" 1>&2
    rm -f "$ZIP"
    echo "✅ 已簽名＋公證＋裝訂，可分發：$APP"
elif [[ -n "${DEV_ID_APP:-}" ]]; then
    echo "✅ 已 Developer ID 簽名（未設 KEYCHAIN_PROFILE，略過公證）：$APP"
    echo "   要公證分發請設 KEYCHAIN_PROFILE 後重跑。"
else
    echo "✅ 打包完成（ad-hoc，本機測試用）：$APP"
fi
