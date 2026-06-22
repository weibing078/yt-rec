# YT Rec 落地頁 — 設計系統

這頁的設計規範與無障礙基準。改動 `index.html` 時照這份走，數值有依據、不要憑感覺。
品牌母規範見 [`../shared/branding/CIS-YTRec.md`](../shared/branding/CIS-YTRec.md)；這份是「落地頁（深色版）」的落地細節。

## 風格定位
深色「精準工具」風（參考 Linear / Raycast / CleanShot）。紅色只當**強調點**，不滿版。
概念錨點：**「一個幫你省時間的小工具」**。不要像 YouTube（不用裸播放三角、不用純紅滿版）。

## 顏色 Token（CSS `:root`）
| Token | 值 | 角色 |
|---|---|---|
| `--red` | `#FF2E45` | 品牌主色 Signal Red：主按鈕、強調、LIVE |
| `--red-bright` | `#FF3D52` | hover、漸層亮端、focus 外框 |
| `--crimson` | `#D5002A` | 按下態、危險操作、漸層深端 |
| `--bg` | `#0d0e12` | 頁面底 |
| `--bg-1` | `#14161d` | 卡片／升起表面 |
| `--bg-2` | `#1b1e27` | 更高一層表面 |
| `--line` / `--line-2` | `rgba(255,255,255,.09 / .16)` | 細線、邊框 |
| `--text` | `#f4f5f7` | 主文字 |
| `--dim` | `#9aa1ad` | 次要文字 |
| `--dim-2` | `#868c97` | 第三級／小字（**已校正對比**，勿改回 #6b717c） |
| `--cloud` / `--ink` | `#F5F6F8` / `#15171C` | 淺色卡片（贊助區）用 |

> 一律用 token，不要在各處散寫 hex。

## 字體
- 主字體：`Inter`（拉丁字／數字，Google Fonts，`display:swap`）＋系統中文（PingFang TC / Noto Sans TC / Microsoft JhengHei）
- 等寬：`ui-monospace` 系列（計時、`git clone` 區塊）
- 級數：h1 `clamp(33–54px)`、h2 `clamp(27–40px)`、內文 15–16px、小字 12.5–14px
- 字重：標題 800、按鈕／標籤 700、內文 400

## 間距與版面
- 內容最大寬 `--maxw:1140px`，左右留白 24px
- 區塊上下 padding：96px（手機 64px）
- 圓角：卡片 `--radius:16px`、按鈕 12px
- 斷點：900px、520px（並以 375px 實測無水平捲動）

## 無障礙基準（必守，2026-06-21 實測）
| 項目 | 標準 | 現況 |
|---|---|---|
| 主文字對比 | ≥ 4.5:1 | `--text` 17.7、`--dim` 7.4、`--dim-2` 5.7（卡片 5.3）✅ |
| 鍵盤焦點 | 可見 focus | `:focus-visible` 2px `--red-bright` 外框、offset 3px ✅ |
| 觸控目標 | 主要 ≥44px、最低 24px | 漢堡 44×44、語言鍵 37×36、footer 連結高 32 ✅ |
| 動效 | 尊重偏好 | `prefers-reduced-motion` 全停 ✅ |
| 圖片 | 防 CLS | `<img>` 有 width/height ✅ |
| 圖示 | SVG，不用 emoji | 全 SVG（Lucide 風）✅ |

**已知品牌取捨**：主按鈕白字配 `--red`＝3.67:1（低於 4.5，但粗體大字過 3:1）。為保品牌紅，維持不動。

## 慣例
- **雙語**：中文（zh-Hant）寫在 HTML 當預設，英文放在 `<script>` 的 `EN` 字典，`.lang-toggle` 切換。
- **改文案＝連摘要一起改**：動到定位／文案，務必同步 JSON-LD `description`／`featureList`、`<meta>`／OG／Twitter description、`llms.txt`（見記憶 keep-summaries-in-sync）。
- **SEO/AEO**：`<head>` 內 JSON-LD `@graph`（WebSite＋SoftwareApplication＋HowTo＋FAQPage）；`robots.txt`／`sitemap.xml`／`llms.txt` 在 `web/` 根。
- **部署**（Cloudflare Pages，Direct Upload）：
  ```bash
  wrangler pages deploy web --project-name ytrec --branch main --commit-dirty=true
  ```
