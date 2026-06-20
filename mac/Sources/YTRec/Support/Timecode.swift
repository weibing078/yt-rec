import Foundation

/// 時間碼解析與格式化（純邏輯，可測試）。用於「只抓 VOD 的某一段」。
enum Timecode {

    /// 解析使用者輸入的時間碼 → 秒。支援「90」「5:30」「1:05:30」「1:30.5」等；無效回 nil。
    static func parse(_ raw: String) -> Double? {
        let s = raw.trimmingCharacters(in: .whitespaces)
        guard !s.isEmpty else { return nil }
        let parts = s.split(separator: ":", omittingEmptySubsequences: false).map(String.init)
        guard (1...3).contains(parts.count) else { return nil }
        var values: [Double] = []
        for p in parts {
            guard let v = Double(p), v >= 0 else { return nil }
            values.append(v)
        }
        switch values.count {
        case 1: return values[0]                                   // 秒
        case 2: return values[0] * 60 + values[1]                  // MM:SS
        default: return values[0] * 3600 + values[1] * 60 + values[2]  // HH:MM:SS
        }
    }

    /// 秒 → "HH:MM:SS"（給 yt-dlp 用）
    static func format(_ seconds: Double) -> String {
        let total = max(0, Int(seconds.rounded()))
        return String(format: "%02d:%02d:%02d", total / 3600, (total % 3600) / 60, total % 60)
    }

    /// 秒 → 顯示用短格式（< 1 小時顯示 M:SS）
    static func formatShort(_ seconds: Double) -> String {
        let total = max(0, Int(seconds.rounded()))
        let h = total / 3600, m = (total % 3600) / 60, s = total % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, s) : String(format: "%d:%02d", m, s)
    }

    /// yt-dlp `--download-sections` 參數 "*START-END"；需 0 ≤ start < end，否則回 nil。
    static func downloadSectionArg(startSec: Double, endSec: Double) -> String? {
        guard startSec >= 0, endSec > startSec else { return nil }
        return "*\(format(startSec))-\(format(endSec))"
    }

    /// 從兩個使用者輸入字串直接算出 section 參數＋人話標籤（兩者都要有效且 start < end）。
    static func section(from startRaw: String, to endRaw: String) -> (arg: String, label: String)? {
        guard let a = parse(startRaw), let b = parse(endRaw),
              let arg = downloadSectionArg(startSec: a, endSec: b) else { return nil }
        return (arg, "\(formatShort(a))–\(formatShort(b))")
    }
}
