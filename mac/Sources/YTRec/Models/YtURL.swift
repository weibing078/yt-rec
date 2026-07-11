import Foundation

/// YouTube 網址解析（純邏輯，可測試）
enum YtURL {
    static func isProbablyYouTube(_ raw: String) -> Bool {
        guard let url = URL(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
              let host = url.host?.lowercased() else { return false }
        return isYouTubeHost(host)
    }

    /// 精確網域比對（防仿冒）：只認 youtube.com / youtu.be 本體與其子網域（www./m./music. 等）。
    /// 子字串比對會把 youtube.com.evil.com、notyoutube.com、youtu.be.evil.com 誤判為 YouTube，
    /// 這些仿冒 host 一律拒絕（避免攻擊者網頁被載入 App 內 WebView、或原始網址被送進 yt-dlp）。
    static func isYouTubeHost(_ host: String) -> Bool {
        host == "youtube.com" || host.hasSuffix(".youtube.com")
            || host == "youtu.be" || host.hasSuffix(".youtu.be")
    }

    /// 從各種 YouTube 網址型態抽出 11 碼影片 ID
    static func videoID(_ raw: String) -> String? {
        let s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let url = URL(string: s) else { return nil }
        let idPattern = "^[A-Za-z0-9_-]{11}$"
        func valid(_ c: String?) -> String? {
            guard let c, c.range(of: idPattern, options: .regularExpression) != nil else { return nil }
            return c
        }
        if let host = url.host?.lowercased(), host.contains("youtu.be") {
            return valid(url.pathComponents.dropFirst().first)
        }
        if let comps = URLComponents(url: url, resolvingAgainstBaseURL: false),
           let v = comps.queryItems?.first(where: { $0.name == "v" })?.value,
           let id = valid(v) {
            return id
        }
        let parts = url.pathComponents
        for (i, p) in parts.enumerated() where ["live", "shorts", "embed", "v"].contains(p) {
            if i + 1 < parts.count, let id = valid(parts[i + 1]) { return id }
        }
        return nil
    }
}
