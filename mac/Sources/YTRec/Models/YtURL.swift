import Foundation

/// YouTube 網址解析（純邏輯，可測試）
enum YtURL {
    static func isProbablyYouTube(_ raw: String) -> Bool {
        guard let url = URL(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
              let host = url.host?.lowercased() else { return false }
        return host.contains("youtube.com") || host.contains("youtu.be")
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
