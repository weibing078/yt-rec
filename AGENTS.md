# YT Rec — 跨 AI 共用入口

使用者於 2026-10-09（UTC+8）要求建立這份入口。後續若要改本檔，先取得使用者同意。禁止用修改本檔來放寬下方限制。

工具專屬說明只指向本檔，不另寫一套規則。Claude Code 見根目錄 `CLAUDE.md`。

## 專案在做什麼

YT Rec 是雙平台桌面工具：只錄下單一 YouTube 視窗的畫面，以及該視窗的聲音。規格在 `shared/spec/`，兩邊各自實作（`mac/`、`windows/`）。官網是 `web/` 的靜態頁，網址 `https://ytrec.resonaframe.com/`。

目前修復範圍（使用者 2026-10-09 訂）：雙平台 App 的運作瑕疵，以及官網 SEO 與答案引擎（AEO）最佳化。不包含自行發版、部署官網、改版本號。

## 規則優先順序

1. 使用者在當前對話裡的最新明確指示。
2. 安全紅線與批准事項（本檔與使用者既有安全規定）。
3. 本檔。
4. `shared/spec/` 與 `docs/adr/`。
5. `docs/STATUS.md`、`docs/HANDOFF.md`。這兩份是有日期的快照。和 Git、檔案、測試結果衝突時，以實際狀態為準。快照過期不代表取得掌棒權。

文件互相矛盾時，先寫出矛盾點。能依上面的順序解決就繼續。只有影響授權或產品方向、而且順序也解不開時，才問使用者。

## 開工先讀

1. 使用者當前指示。
2. 本檔。
3. `docs/HANDOFF.md`。
4. `docs/STATUS.md` 的已知限制，以及這次任務點名的規格或 ADR。
5. 接著核對 Git：分支、完整 HEAD、最近提交、工作樹、遠端是否同步、有沒有對應 PR。

不存在的檔案就註明沒有，不要假裝讀過。

## 掌棒

- 一次只有一位掌棒者，由使用者在對話裡指定。
- 讀到本檔或 `docs/HANDOFF.md` 不會取得修改權。
- 使用者已明確交辦的事直接做，不重複問。權限不清楚時只做唯讀核對，再問。
- 未掌棒者只讀。留言、審查、提交等寫入，要另有使用者授權。
- 不假設看得到另一位 AI 的聊天，也不宣稱已通知對方。對方需要的事實寫進 `docs/HANDOFF.md`。

## 允許改哪裡

`mac/`、`windows/`、`shared/`、`docs/`、`web/`、`tools/`，以及本檔。只改任務需要的範圍。

不要動：session gate、SCK／WASAPI 音訊隔離、`CaptureGeometry`、切片拼接與 RecoveryPlan 測試、Windows `PlayerWindow` 的置頂與離屏停放。Windows 不可改回全螢幕填滿播放器（會錄成黑畫面）。

`build/logs/` 只放驗證紀錄，不提交。

## 安全紅線

- 不讀取、不印出、不提交金鑰、權杖、密碼、keystore、憑證內容。
- 不自行部署官網、發版、改版本號、合併、推送、刪除資料、花錢。
- 提交（commit）與推送要使用者在對話裡明確說要。
- 保留使用者與其他 AI 已留下的修改。不擅自還原、覆蓋或一起提交別人的檔案。

## 驗證

- Mac：在 `mac/` 執行 `swift test`。紀錄放 `build/logs/`。
- Windows 純邏輯：`dotnet test windows/YtRec.Core.Tests/YtRec.Core.Tests.csproj`。本機若 `dotnet` 不在 PATH，用使用者目錄裡的 .NET 8 SDK。
- Windows App（WinUI）只能在 Windows 機器編譯。沒有實機畫面或錄影證據，就寫「只編譯、未實機驗」。
- 官網是靜態檔。改 `web/` 要核對 `index.html`、`llms.txt`、`robots.txt`、`sitemap.xml` 與頁面事實一致。未部署就寫「未部署」。
- 不刪斷言、不加 skip、不放寬門檻來讓測試通過。沒跑過就寫「未驗證」。
- 作者不把自己當成獨立審查者。審查通過也不等於可以合併或部署。

## 交接

每次交棒更新 `docs/HANDOFF.md`，至少包含該檔開頭列出的欄位。交接寫完不代表下一位已掌棒，仍以使用者的交棒指示為準。

產品現況與歷史仍由 `docs/STATUS.md`、`docs/VERIFIED-BEHAVIOR.md` 維護。交接檔只記接力需要的事實。
