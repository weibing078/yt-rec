# 交接

更新日期：2026-10-09 18:40（UTC+8）
交棒者：本 Cursor 對話（使用者說額度到了，結束工作）
掌棒：沒有人。下一位要等使用者在對話裡指定。本檔不授予修改權。

## 位置

- 分支：`fix/2026-10-improvements`
- HEAD：本交接提交之前是 `40e4fa72e4c030862675c2aa6a6107308814b1ca`（`docs: record the v1.1.3 release and the missing Windows installer`）
- 遠端：已推上 `origin/fix/2026-10-improvements`，與本機相同
- PR：沒有。沒有合併到 `main`（使用者沒要求）
- 提交：`d1a80a9` 修復、`26389d0` 發版 v1.1.3、`40e4fa7` 文件、本檔一筆交接
- 工作樹：只有未追蹤的 `build/logs/`（驗證紀錄，不提交）

## 發佈現況

使用者 2026-10-09 授權：commit、push、發版、部署官網、推送更新。

- GitHub Release：https://github.com/weibing078/yt-rec/releases/tag/v1.1.3 （target 是 `fix/2026-10-improvements`）
- Mac：`YT-Rec.dmg` 已上傳，Developer ID 簽名、公證、裝訂（公證設定檔名 `AutoSyncNotary`）
- 官網：已用 wrangler 部署到 `ytrec` 專案 main。`https://ytrec.resonaframe.com/latest.json` 是 1.1.3
- **Windows：沒完成。** `latest.json` 的 Windows 網址 `releases/latest/download/YT-Rec-Setup.exe` 目前回 404。舊版 Windows 使用者會被提示更新卻下載失敗，這是最優先要補的

## 唯一未完成：把 Windows 安裝檔掛上 v1.1.3

安裝檔已經在建置機做好，只差搬上 GitHub。

- 建置機：`weibi@100.79.2.61`，金鑰 `~/.ssh/wei_win_ed25519`，遠端殼層是 PowerShell
- 安裝檔：`C:\Users\weibi\yt-rec-113\windows\dist\YT-Rec-Setup.exe`，110275288 bytes。Inno Setup 6.7.3 已裝（`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`），繁中語系檔缺，略過
- 已切片：`C:\Users\weibi\ytrec-setup-1m\p000` 到 `p105`，每片 1 MiB，最後一片 `p105` 174808 bytes
- Mac 已收到：`/tmp/ytrec-1m` 45／106 片（`p000`–`p043` 等）。`/tmp` 重開機會清掉
- 大檔 scp 從建置機拉回時常被重置或逾時，所以才改切片

三種補法，擇一：

1. 繼續逐片 scp 缺的片到 `/tmp/ytrec-1m`，每片核對大小。全齊後依序 `cat p000 … p105 > /tmp/YT-Rec-Setup.exe`，確認 110275288 bytes，再 `gh release upload v1.1.3 /tmp/YT-Rec-Setup.exe --clobber`。
2. 請使用者在 Windows 機上用瀏覽器登入 GitHub，把安裝檔手動上傳到 v1.1.3。
3. 在 Windows 機上裝 gh，由使用者自己互動登入後上傳。

**不要**把本機的 GitHub token 經 SSH 傳給建置機（自動審查已擋過一次，資安紅線）。

上傳後核對：`curl -sI https://github.com/weibing078/yt-rec/releases/latest/download/YT-Rec-Setup.exe` 應轉址到檔案而不是 404。安裝檔本身沒有在乾淨 Windows 上安裝試跑過。

## 已完成且有證據

- Mac 正片閘門與 45 秒逾時放行；簽名的 App 實錄 Big Buck Bunny（1920×1080、AAC）與 Al Jazeera 約 60 秒。
- 廣告偵測加 `getAdState() === 1` 與 `isLifaAdPlaying()`，兩平台都有單元測試。
- Windows 裁切連續兩次相同才用；0 次則停並顯示「預覽沒有新畫面，先不要開錄」。
- Windows 時長、720／1080、輸出資料夾、最近 5 筆會記住；進行中直播按下載改走側錄並說明；舊資料夾未收工錄影能修回。
- 擷取尺寸變化會停並留下部分檔。
- Windows x264：crf 23；720p 6M／12M，1080p 12M／24M。沒加 `-g 60`。
- 更新連結網域限制：兩邊單元測試。
- 兩平台都有無頭 `--autorecord` 參數供實錄驗證。
- 測試：Mac `swift test` 195 通過、1 略過、0 失敗；Windows Core `dotnet test` 252 通過（建置機）。Windows App 能編譯、能載入。

## 還沒證明

- 從真的在播的廣告寫出 `.廣告時段.txt`（這幾台播片都沒進廣告）。使用者說不列為門檻。
- Windows「下載」「下載更新」按鈕沒有在畫面上點過。
- Windows 安裝檔沒有實機安裝試跑。
- 沒有 `web/og.png`。

## 已知問題

- Windows 完全沒有播放器快照時，不會因「結束」自動收工。同一支 id 從頭重播不收工。收尾瞬間再按停止有競態。Windows App 沒有自己的 log。
- 建置機的 Smart App Control／程式碼完整性偶爾擋新編的未簽名 DLL，重編通常能載入。不要關掉該原則。
- 建置機 VS Build Tools 的 MSBuild 解不到 `Microsoft.NET.Sdk`。用使用者目錄 .NET 8 SDK（`C:\Users\weibi\.dotnet`，要設 `DOTNET_ROOT`）的 `dotnet msbuild`，App 加 `EnableMsixTooling=true`、`EnableDefaultPriItems=false`。CI 不加這兩個屬性。
- 本機 zsh 會展開 `$`，遠端 PowerShell 指令請先寫成 `.ps1` 再 scp 過去執行。
- 要開 GUI 的遠端測試用互動式排程工作（名稱 `YtRecQA`）。

## 已拍板，不要重走

- 中插廣告照錄，只寫 `.廣告時段.txt`。不黑畫面、不自動剪、不暫停寫檔。
- 進行中或未開播按下載改走側錄並說明。只下載已結束的影片。
- 播放器結束用每秒快照，不改回單次 ended 事件。
- Windows 不改回全螢幕填滿；`PlayerWindow` 置頂不能拿掉。

## 驗證

| 指令 | exit | 證據 |
|---|---|---|
| `mac/` 裡 `swift test` | 0 | 2026-10-09：195 通過、1 略過、0 失敗 |
| `dotnet test windows/YtRec.Core.Tests/YtRec.Core.Tests.csproj` | 0 | 2026-10-09，`weibi@100.79.2.61`：252 通過 |
| Windows 安裝檔 ISCC 編譯 | 0 | 110275288 bytes，未上傳、未安裝試跑 |
| `curl -sI …/v1.1.3/YT-Rec-Setup.exe` | — | 2026-10-09 18:35：HTTP 404 |
