# 交接

更新日期：2026-10-09 13:40（UTC+8）
交棒者：本 Cursor 對話
掌棒：仍是本 Cursor 對話。使用者尚未把掌棒交給另一位 AI。本檔不授予修改權。

## 位置

- 分支：`fix/2026-10-improvements`
- HEAD：`5c60c0fb0ea6057f4f42141c4dd8d46ad73880a5`（`5c60c0f`，`fix: debounce a video change and adopt an anchor when the URL has none`）
- 遠端：`origin/fix/2026-10-improvements` 與此 HEAD 相同
- PR：沒有
- 保存：已推上 `origin/fix/2026-10-improvements`。修復是 `d1a80a9`，版本是 `26389d0`（v1.1.3）。GitHub Release：https://github.com/weibing078/yt-rec/releases/tag/v1.1.3 。Mac `YT-Rec.dmg` 已簽名、公證、裝訂。官網 `latest.json` 已是 1.1.3。v1.1.3 還沒有 `YT-Rec-Setup.exe`（建置機沒有 Inno Setup，下載頁回的是 HTML），所以上面的 Windows 下載網址目前是 404。

## 現況

第一波（播放器每秒快照）已提交。其後的修復都在工作樹，沒有還原。

已完成且有證據：

- Mac 正片閘門與 45 秒逾時放行。簽名過的 `mac/dist/YT Rec.app` 錄過 Big Buck Bunny 8.24 秒（1920×1080、AAC）與 Al Jazeera 約 60 秒。lofi 逾時後仍寫出幾乎沒有畫面的檔。這次打包有 Developer ID 簽名，沒有送公證。
- Windows 裁切連續兩次相同；0 次畫面範圍則停，不錄整頁。lofi 日誌是「預覽沒有新畫面，先不要開錄」。
- Windows 時長、720/1080、輸出資料夾、最近 5 筆會記住。進行中或尚未開播的下載會改走側錄並說明（`--check` 寫出那句話）。舊資料夾未收工錄影有修回一支 12 秒檔。
- 擷取尺寸一變會停並留下部分檔（120 幀）。中間有一次新 dll 被程式碼完整性原則擋住（`0x800711C7`），後來的重編可以載入。不要關掉該原則。
- Windows x264：crf 23；720p 的 SEI 是 `vbv_maxrate=6000`、`vbv_bufsize=12000`；1080p 是 12000／24000。沒有加 `-g 60`。
- 更新連結網域限制有兩邊單元測試。按鈕沒有在畫面上點過。
- 官網 `web/index.html`、`llms.txt`、`sitemap.xml` 已對照產品事實。本機 `http://127.0.0.1:8011/` 看過。沒有部署。沒有 `web/og.png`。
- 測試：Mac `swift test` 195 通過、1 略過、0 失敗（2026-10-09 12:32）。Windows Core `dotnet test` 252 通過、0 失敗（同日 12:32，機器 `weibi@100.79.2.61`）。

還沒證明：

- 從正在播的廣告寫出 `.廣告時段.txt`。格式檔 `build/logs/goal-win-sidecar.txt` 的時間是檢查程式餵的。瀏覽器與實錄的正片 `getAdState()` 都是 -1。
- Windows「下載」與「下載更新」沒有在畫面上點過。
- 這次 Mac 打包沒有公證。官網沒有部署。工作樹沒有 commit。

## 完成目標

目標：這條分支上的修復可以被保存、被核對，而且文件講的是 2026-10-09 的工作樹，不是六月快照。

完成要同時為真：

1. 工作樹的行為與 `shared/spec/behavior-spec.md`、`parity-matrix.md`、`docs/PRD.md` 現況段、`docs/STATUS.md` 現況段一致。
2. Mac `swift test` 與 Windows Core `dotnet test` 維持全綠。Windows App 能編譯。
3. 已有的實錄與本機官網畫面證據還在，不把沒做過的講成做過。
4. 下面三件待拍板有答案之後，才做對應的那一步。沒有答案就不做。

## 步驟

已做，不要重跑來湊證據：

1. 第一波快照評估（已在 HEAD）。
2. 第二波與第三波程式、測試、實錄、官網文字。

待拍板（問過使用者，2026-10-09 12:52 還沒有答案）：

1. 要不要把工作樹 commit。沒有明確說要，就不 commit、不 push、不開 PR。
2. 「從正在播的廣告寫出時段檔」要不要列為完成門檻。這台現在播片不會進廣告。若不列，現有格式檔與「沒廣告不寫檔」就算這項的證據。
3. 要不要公證這次的 Mac App，以及要不要部署官網。兩件都要另一次批准。部署與發版、改版本號仍然分開。

不需拍板、文件已寫明、先不做的：

- 不加 `-g 60`。
- 不關掉 Windows 程式碼完整性原則。
- 不改回 Windows 全螢幕填滿。
- 不把「同一支 id 從頭重播不收工」「沒有快照就不因結束收工」「收尾競態」「Windows App 沒有 log」當成這輪必須修完的項目。

## 已知問題

- `docs/STATUS.md` 文首現況是 2026-10-09。下文六月段落是舊快照。
- Windows 若完全沒有播放器快照，不會因「結束」自動收工。同一支影片 id 從頭重播不會收工。收尾完成瞬間再按停止有競態。Windows App 沒有自己的 log。
- 這台 Windows 的 VS Build Tools 沒有 PRI 工作 DLL。App 用 `EnableMsixTooling=true` 與 `EnableDefaultPriItems=false` 編成。CI 仍走 VS `msbuild`，不加這兩個屬性。

## 工作樹

全部未提交修改都屬於本 Cursor 對話，疊在 HEAD 之上。`build/` 只放驗證紀錄，不提交。

## 驗證

| 指令 | exit | 證據 |
|---|---|---|
| `mac/` 裡 `swift test` | 0 | 2026-10-09 12:32：195 通過、1 略過、0 失敗 |
| `dotnet test windows/YtRec.Core.Tests/YtRec.Core.Tests.csproj` | 0 | 2026-10-09 12:32，`weibi@100.79.2.61`：252 通過、0 失敗 |
| Windows App Release x64，`EnableMsixTooling=true`、`EnableDefaultPriItems=false` | 0 | 同日稍後重編後，`--recover` 寫出 count=0，程式有載入 |

## 已拍板與不要重走

- 中插廣告照錄，只寫 `.廣告時段.txt`。不做黑畫面、自動剪掉、暫停寫檔。可略過的廣告仍按略過並靜音。
- 進行中或尚未開播按下載：改走側錄並說明。Mac 的 always 下載模式維持 Mac 才有。只下載已結束的影片。
- 播放器結束用每秒快照。不要改回單次 ended 事件。
- Windows 全螢幕填滿會錄到黑畫面。`PlayerWindow` 置頂不能拿掉。
- 這台 Windows 用使用者目錄的 .NET 8 SDK。遠端預設殼層是 PowerShell。本機 zsh 會展開 `$`。
