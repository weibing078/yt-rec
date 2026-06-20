import SwiftUI
import AppKit

// MARK: - 品牌色彩 Token（CIS 單一事實來源，見 branding/CIS-YTRec.md §3）
//
// 原則（§5.1 強調而非重漆）：chrome（背景／文字／邊框／一般控制）一律走 macOS 系統語意色，
// 自動深淺色；品牌色只點在「強調點」——主要操作、錄製狀態、進度、選取、狀態回饋。
// 兩顆紅各司其職：lcSignal＝進行中／主動（live、錄製、主操作、進度、選取）；
// lcCrimson＝停止／破壞（停止錄製、刪除、失敗）。

extension Color {
    // 品牌紅
    static let lcSignal      = Color(red: 1.000, green: 0.180, blue: 0.271) // #FF2E45 主色/錄製/主要操作/進度/選取
    static let lcSignalLight = Color(red: 1.000, green: 0.239, blue: 0.322) // #FF3D52 漸層亮端/hover
    static let lcCrimson     = Color(red: 0.835, green: 0.000, blue: 0.165) // #D5002A 按下/停止/刪除/失敗
    // 中性（參考值；大面積請優先用系統語意色）
    static let lcInk         = Color(red: 0.082, green: 0.090, blue: 0.110) // #15171C
    static let lcIdle        = Color(red: 0.541, green: 0.561, blue: 0.596) // #8A8F98
    static let lcCloud       = Color(red: 0.961, green: 0.965, blue: 0.973) // #F5F6F8
    // 狀態（鮮明值：給填色／指示燈／大圖示，達 3:1 非文字門檻）
    static let lcSuccess     = Color(red: 0.114, green: 0.620, blue: 0.459) // #1D9E75 已存檔/成功
    static let lcWarning     = Color(red: 0.961, green: 0.651, blue: 0.137) // #F5A623 可繼續的提醒

    // 「當文字用」的狀態色：光/暗自適應，兩種外觀都過 WCAG AA 4.5:1。
    // 鮮明的品牌橘／綠／緋當小字會對比不足（橘最嚴重）；故文字走加深(淺色)/提亮(深色)的變體，
    // 鮮明值仍保留給填色與指示燈。對應 CIS §5.1「強調而非重漆」。
    static let lcWarningText = lcDynamic(light: (0.541, 0.353, 0.000),  // 淺#8A5A00 / 深#F5A623
                                         dark:  (0.961, 0.651, 0.137))
    static let lcSuccessText = lcDynamic(light: (0.059, 0.431, 0.337),  // 淺#0F6E56 / 深#34C88E
                                         dark:  (0.204, 0.784, 0.557))
    static let lcDangerText  = lcDynamic(light: (0.835, 0.000, 0.165),  // 淺#D5002A / 深#FF6373
                                         dark:  (1.000, 0.388, 0.451))

    /// 依淺/深色外觀切換的動態色（不需 asset catalog，純程式定義）。
    private static func lcDynamic(light: (Double, Double, Double),
                                  dark: (Double, Double, Double)) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let isDark = appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
            let c = isDark ? dark : light
            return NSColor(srgbRed: c.0, green: c.1, blue: c.2, alpha: 1)
        })
    }
}

// 讓 token 在 ShapeStyle 情境（.foregroundStyle / .fill / .background(_:in:)）也能用 leading-dot，
// 與系統色（.red 等）一致。Color 情境（.tint）走上面的 Color 擴充；兩者不衝突。
extension ShapeStyle where Self == Color {
    static var lcSignal: Color      { .lcSignal }
    static var lcSignalLight: Color { .lcSignalLight }
    static var lcCrimson: Color     { .lcCrimson }
    static var lcInk: Color         { .lcInk }
    static var lcIdle: Color        { .lcIdle }
    static var lcCloud: Color       { .lcCloud }
    static var lcSuccess: Color     { .lcSuccess }
    static var lcWarning: Color     { .lcWarning }
    static var lcWarningText: Color { .lcWarningText }
    static var lcSuccessText: Color { .lcSuccessText }
    static var lcDangerText: Color  { .lcDangerText }
}

extension NSColor {
    // AppKit 端（監看小窗徽章／停止鈕）用，對應上面同名 token。
    static let lcSignal  = NSColor(srgbRed: 1.000, green: 0.180, blue: 0.271, alpha: 1) // #FF2E45 錄製指示
    static let lcCrimson = NSColor(srgbRed: 0.835, green: 0.000, blue: 0.165, alpha: 1) // #D5002A 停止
}
