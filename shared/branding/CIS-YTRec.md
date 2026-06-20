# YT快錄 (YT Rec) — 視覺識別規範 (CIS)

> YouTube 直播搶錄 · macOS App
> 中文名 YT快錄｜英文字標 YT Rec
> 版本 1.1 · 2026-06-17（CIS 已接進程式，見 §3.5/§3.6 與 §8）
> 母檔與資產：`branding/`　產製：`scripts/make-icons.sh`

---

## 0. 這份文件怎麼用

這是品牌的**單一事實來源 (single source of truth)**。下一個對齊 UI 的 session：

1. 先讀 §3 色彩、§5 介面應用原則——這兩節決定 90% 的實作。
2. 色彩直接用 `BrandColor.swift` 的 token（§3.5 填色／指示燈用鮮明值、§3.6 小字用對比安全變體），**不要在各 View 散落硬寫 hex**。
3. 核心原則(§5.1):**強調，而非重漆**。chrome（背景／文字／邊框）走 macOS 系統語意色自動適應深淺色；品牌紅只用在「強調點」（主要操作、錄製狀態、進度、選取）。
4. 已移除、不要復活的：選單列狀態列（見 §2.3）——錄製指示與控制改由**監看小窗**承擔。

---

## 1. 品牌定位

| 項目 | 內容 |
|---|---|
| 一句話 | 直播一開，立刻框住、搶錄下來。 |
| 個性 | 快、果斷、可靠。工具感，不花俏。 |
| 三個情緒支點 | **LIVE**(直播紅) ·　**擷取**(取景框) ·　**快**(乾淨俐落) |
| 要避開 | 看起來像 YouTube 本尊（純紅＋裸播放三角）。我們是「錄 YouTube 的工具」，不是平台。 |

---

## 2. 標誌系統 Logo & Icon

### 2.1 主標記 (The Mark)

**取景框 (viewfinder) ＋ 圓角播放三角**。

- 四個角的取景括號＝「框住直播畫面、按下擷取」。這是和 YouTube 區隔的關鍵——三角永遠待在框裡，**不可單獨使用裸三角**。
- 中央圓角播放三角＝影片內容。圓角（非尖角）為定版，呼應括號的圓端點。
- 線語言：圓端點 (round cap)、圓轉角 (round join)、生成式的等寬筆畫。

### 2.2 App 圖示 (macOS)

| 規格 | 值 |
|---|---|
| 畫布 | 1024 × 1024 |
| 本體 squircle | 824 × 824 置中（四邊各留 100px） |
| 圓角半徑 | 185（≈ 本體 22.5%） |
| 底色 | 垂直漸層 `#FF3D52`(上) → `#D5002A`(下) |
| 光澤 | 頂部白 13% 柔光、底部深紅 16% 反光（皆裁切於本體內） |
| 標記 | 白色取景框（筆畫 37）＋ 白色圓角三角 |

母檔：[`branding/LiveClipFast-AppIcon-1024.svg`](LiveClipFast-AppIcon-1024.svg)
產物：`branding/AppIcon.icns`（16–1024 全尺寸含 @2x）

### 2.3 選單列圖示 (2026-06-16 已移除)

> 選單列狀態列已拿掉——在 owner 的機器上它被瀏海擠進溢出區看不到，而**監看小窗**本身就是更顯眼的錄製指示燈（紅「● 時長」徽章＋即時畫面），且小窗上現在直接有「停止／隱藏」鈕。錄製狀態與控制改由監看小窗承擔；主視窗在預覽被收起時顯示「顯示監看預覽」叫回來；關掉主視窗仍可點 Dock 圖示叫回。

- 單色 template 母檔 [`branding/LiveClipFast-MenuBar.svg`](LiveClipFast-MenuBar.svg) 仍保留為品牌資產，但**已不再打包進 App**（`make-icons.sh` 仍可產 `MenuBarIcon.pdf` 供日後其他用途）。
- 取景框＋圓角三角的單色版若未來要用在工具列／小尺寸場合，沿用此母檔即可。

### 2.4 安全空間與最小尺寸

- **安全空間**：標記四周至少留「框寬」的留白，勿貼字或貼圖。
- **最小尺寸**：App 圖示 ≥ 16px（iconset 已涵蓋）；選單列／工具列單色標記 ≥ 18px。

### 2.5 Do / Don't

✅ 維持紅色家族、圓角三角、取景框框住三角。
🚫 別把三角單獨拉出來用（變 YouTube）。
🚫 別改漸層方向、別加陰影／外光暈／傾斜。
🚫 別把品牌紅換成其他色相當底。
🚫 別把三角改回尖角（定版＝圓角）。

---

## 3. 色彩系統 Color

### 3.1 品牌紅（兩顆紅各司其職）

| 名稱 | Hex | 角色 |
|---|---|---|
| **直播紅 Signal Red** | `#FF2E45` | 主色｜主要按鈕｜**錄製中**｜進度｜選取強調 |
| 直播紅·亮 | `#FF3D52` | 漸層亮端｜hover 提亮 |
| **深緋 Deep Crimson** | `#D5002A` | 漸層深端｜按下態 (pressed)｜**停止／刪除等危險操作** |

> 設計原則：**亮紅＝進行中／主動**（live、錄製、主操作）；**深紅＝停止／破壞**（停止錄製、刪除片段）。介面中只出現這兩種紅相，語意自然分流。

### 3.2 中性色

| 名稱 | Hex | 角色 |
|---|---|---|
| 墨黑 Ink | `#15171C` | 主文字｜單色標記 |
| 待命灰 Idle Gray | `#8A8F98` | 待命狀態｜次要圖示｜停用 |
| 雲白 Cloud | `#F5F6F8` | 淺色表面（參考值，實作優先用系統語意色，見 §5.1） |

### 3.3 狀態色

| 狀態 | 色 | Hex | 用途 |
|---|---|---|---|
| 待命 idle | 待命灰 | `#8A8F98` | 未錄製的指示燈、次要文字 |
| 錄製中 recording | 直播紅 | `#FF2E45` | 錄製指示燈（建議呼吸閃爍）、計時 |
| 已存檔 saved | 翠綠 Emerald | `#1D9E75` | 完成、成功 toast |
| 警告 warning | 琥珀 Amber | `#F5A623` | 磁碟空間不足等可繼續的提醒 |
| 危險／停止 | 深緋 | `#D5002A` | 全部停止、刪除片段 |

### 3.4 深色模式

- 品牌紅在深色模式維持不變（必要時 hover 用 `#FF3D52` 提亮一階）。
- 中性（背景／文字／邊框）**不要硬寫**——交給系統語意色自動切換（見 §5.1）。

### 3.5 SwiftUI Token（實作直接複製）

> 放一個 `Sources/LiveClipFast/Support/BrandColor.swift`，全 App 引用，禁止散落 hex。

```swift
import SwiftUI

extension Color {
    // 品牌紅
    static let lcSignal      = Color(red: 1.000, green: 0.180, blue: 0.271) // #FF2E45 主色/錄製/主要操作
    static let lcSignalLight = Color(red: 1.000, green: 0.239, blue: 0.322) // #FF3D52 漸層亮端/hover
    static let lcCrimson     = Color(red: 0.835, green: 0.000, blue: 0.165) // #D5002A 按下/停止/刪除
    // 中性（參考值；大面積請優先用 §5.1 系統語意色）
    static let lcInk         = Color(red: 0.082, green: 0.090, blue: 0.110) // #15171C
    static let lcIdle        = Color(red: 0.541, green: 0.561, blue: 0.596) // #8A8F98
    static let lcCloud       = Color(red: 0.961, green: 0.965, blue: 0.973) // #F5F6F8
    // 狀態
    static let lcSuccess     = Color(red: 0.114, green: 0.620, blue: 0.459) // #1D9E75
    static let lcWarning     = Color(red: 0.961, green: 0.651, blue: 0.137) // #F5A623
}
```

> ✅ 已實作：`Sources/LiveClipFast/Support/BrandColor.swift`（含 `Color`／`ShapeStyle`／`NSColor` 三種情境）。

### 3.6 文字用變體（對比安全，2026-06-17 補）

§3.5 的鮮明值是給**填色／指示燈／大圖示**（達 WCAG 3:1 非文字門檻）。當狀態色要**當小字**用（膠囊文字、清單列狀態、提示文字），鮮明的橘／綠／緋對比會不足 4.5:1——琥珀最嚴重（淺色僅 ~1.7:1）。故 `BrandColor.swift` 另備三個**光/暗自適應文字變體**，鮮明值仍只留給填色：

| Token | 淺色 | 深色 | 用在哪 |
|---|---|---|---|
| `lcWarningText` | `#8A5A00` | `#F5A623` | 警告文字、未授權說明 |
| `lcSuccessText` | `#0F6E56` | `#34C88E` | 已存檔／已到手／已抓出文字 |
| `lcDangerText`  | `#D5002A` | `#FF6373` | 失敗文字（緋紅在深色太暗→提亮） |

- **膠囊 (pill)**：文字／圖示用 `*Text` 變體、底色用鮮明色的淡 wash（`tint.opacity(0.14)`），兩者分流，避免「同色文字疊同色底」對比塌掉。
- **計時／大圖示／指示燈**：維持鮮明 `lcSignal` 等（已驗 3:1）。鮮明品牌紅**不要當小字**（淺色僅 3.1:1）——改用文字承載語意、紅點承載強調。

---

## 4. 字體系統 Typography

| 角色 | 字體 | 用在哪 |
|---|---|---|
| 英文標準字 | **Space Grotesk** Medium | 僅標誌字標、行銷主視覺。**不用在 App 介面內。** |
| 介面（中） | **PingFang TC**（系統） | App 內所有中文 UI |
| 介面（英／數） | **SF Pro**（系統 `.system`） | App 內英文與一般數字 |
| 計時／檔案大小等等寬數字 | **SF Mono** / `.monospacedDigit()` | `00:42`、`12.4 GB`、`1080p60` |

**原則**：App 介面一律走系統字（SF Pro / PingFang TC）以保原生質感與字級可達性；標準字 Space Grotesk 只活在圖示外的品牌物料。計時碼務必用等寬數字避免跳動。

層級（介面內，搭 Dynamic Type）：

| 層級 | 字重 | 參考級數 |
|---|---|---|
| 標題 Title | Semibold | 17–20pt |
| 內文 Body | Regular | 13–15pt |
| 次要 Caption | Regular | 11–12pt（次要灰 `#8A8F98`）|

---

## 5. 介面應用原則 UI Application（對齊 UI 的核心）

### 5.1 第一原則：強調，而非重漆

native macOS app 的高級感來自「用對系統材質 + 少量品牌強調」，不是把整個介面塗成紅色。

- **Chrome**（視窗背景、卡片、文字、分隔線、一般控制項）→ 用系統語意色，自動深淺色：
  - 背景：`Color(nsColor: .windowBackgroundColor)` / `.controlBackgroundColor`
  - 文字：`.primary` / `.secondary`（或 `Color(nsColor: .labelColor)`）
  - 分隔線：`Color(nsColor: .separatorColor)`
- **品牌紅只出現在強調點**：主要操作按鈕、錄製指示、進度條、選取／焦點、關鍵數值。

### 5.2 各元件對應

| 元件 | 規範 |
|---|---|
| 主要操作按鈕（開始側錄／搶錄） | 填滿 `lcSignal`，白字；hover `lcSignalLight`；pressed `lcCrimson`。圓角 `md`(10)。 |
| 次要按鈕 | 系統 bordered 樣式，不上品牌色。 |
| 破壞性按鈕（全部停止／刪除片段） | 文字／邊框用 `lcCrimson`，或 SwiftUI `.destructive` role。 |
| 錄製指示燈 | `lcSignal` 圓點，建議 1.2s 呼吸閃爍；旁附等寬計時。 |
| 待命指示 | `lcIdle` 圓點。 |
| 進度／緩衝 | 進度填色 `lcSignal`，軌道用系統 `.quaternary`。 |
| 選取／焦點 | `lcSignal`（可降透明度當選取底）。 |
| 成功 toast（已存檔） | `lcSuccess` 圖示＋系統背景。 |
| 警告（空間不足等） | `lcWarning`。 |
| 選單列 | **已移除**（見 §2.3）；錄製狀態／控制改由監看小窗（紅「● 時長」徽章＋停止／隱藏鈕）承擔。 |

### 5.3 形狀與間距語言（呼應標記的圓潤幾何）

| Token | 值 | 用途 |
|---|---|---|
| radius-sm | 6 | 小控制項、tag |
| radius-md | 10 | 按鈕、卡片 |
| radius-lg | 16 | 視窗、sheet、預覽容器 |
| squircle | — | 僅 App 圖示本身 |
| 間距節奏 | 4 / 8 / 12 / 16 / 24 | 沿用 4 的倍數 |

線性圖示：等寬筆畫（24px 網格約 1.5–2px）、**圓端點圓轉角**，與標記同語言；優先用 SF Symbols（weight 對齊內文字重）。

---

## 6. 語氣 Voice

- 中文、簡潔、動詞開頭、給結果不囉嗦。對齊現有字串：「全部停止」「顯示主視窗」「側錄中」「待命中」。
- 狀態先講人話再講數字：「側錄中 00:42」優於「Recording 42s」。
- 提醒類訊息要明確指引下一步（例：關窗仍在背景錄的提醒）。

---

## 7. 資產清單 Asset Inventory

| 檔案 | 用途 |
|---|---|
| `branding/LiveClipFast-AppIcon-1024.svg` | App 圖示母檔（改這個） |
| `branding/LiveClipFast-MenuBar.svg` | 選單列單色標記母檔 |
| `branding/AppIcon.icns` | 打包用，已接進 Info.plist (`CFBundleIconFile`) |
| `branding/MenuBarIcon.pdf` | 選單列單色 template 產物；**已不再打包進 App**（選單列已移除，見 §2.3），保留作品牌資產 |
| `branding/MenuBarIcon.png` / `@2x.png` | 參考用 |
| `branding/png/LiveClipFast-{1024,512,256,128}.png` | 透明底，文件／App Store／README |
| `scripts/make-icons.sh` | 改母檔後一鍵重產全部 |
| `scripts/package-app.sh` | 打包，會把上述資產拷進 `.app` |

---

## 8. 變更紀錄

| 日期 | 版本 | 內容 |
|---|---|---|
| 2026-06-16 | 1.0 | 首版。取景框＋圓角播放三角定案；色彩／字體／介面原則／資產建立。 |
| 2026-06-17 | 1.1 | **CIS 正式接進程式**：新增 `BrandColor.swift` 單一事實來源、全 UI 通用色換成品牌 token、紅色依語意分流（直播紅／深緋）。對抗審查抓出「品牌色當小字對比不足」→ 補 §3.6 文字用光/暗自適應變體（WCAG AA 已驗）。標題列 placeholder 閃電→取景框；待命指示燈接 `lcIdle`。Premiere in/out 把手刻意留原色。 |
