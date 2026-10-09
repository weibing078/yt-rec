import Foundation

/// 一份解析後的 latest.json（給 app 內更新檢查用）。
struct UpdateManifest {
    let version: String
    let notes: String?
    let url: String?
    let page: String
}

/// App 內更新檢查的純邏輯（對齊 Windows `AppUpdate`；見 shared/spec「App update check」）。
/// 只做版本比較與清單解析；抓取與提示由 app 處理。
enum AppUpdate {
    static let homepage = "https://ytrec.resonaframe.com/"

    /// 下載鈕要開的網址。url 必須落在這個 repo 的 releases 路徑；page 必須是官網。都不合就回官網首頁。
    static func safeOpenURL(downloadURL: String?, page: String?) -> String {
        if isAllowedRelease(downloadURL) { return downloadURL!.trimmingCharacters(in: .whitespacesAndNewlines) }
        if isAllowedPage(page) { return page!.trimmingCharacters(in: .whitespacesAndNewlines) }
        return homepage
    }

    /// 按下時再查一次。已經選好的連結若被換掉，改開官網首頁。
    static func openable(_ candidate: String?) -> String {
        if isAllowedRelease(candidate) || isAllowedPage(candidate) {
            return candidate!.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return homepage
    }

    static func isAllowedRelease(_ raw: String?) -> Bool {
        guard let url = httpsURL(raw), url.host?.lowercased() == "github.com" else { return false }
        let path = url.path
        return path == "/weibing078/yt-rec/releases" || path.hasPrefix("/weibing078/yt-rec/releases/")
    }

    static func isAllowedPage(_ raw: String?) -> Bool {
        guard let url = httpsURL(raw) else { return false }
        return url.host?.lowercased() == "ytrec.resonaframe.com"
    }

    private static func httpsURL(_ raw: String?) -> URL? {
        guard let raw = raw?.trimmingCharacters(in: .whitespacesAndNewlines),
              let url = URL(string: raw),
              url.scheme?.lowercased() == "https",
              url.user == nil,
              url.host != nil else { return nil }
        return url
    }

    /// `latest` 是否比 `current` 嚴格更新：點分數字比較（1.10 > 1.9）、缺位補 0、去掉前導 v 與
    /// `-pre`/`+meta` 後綴；無法解析 → 不算更新（壞清單不會亂跳；current 未知則偏向提示）。
    static func isNewer(current: String?, latest: String?) -> Bool {
        let c = nums(current), l = nums(latest)
        for i in 0..<max(c.count, l.count) {
            let cv = i < c.count ? c[i] : 0
            let lv = i < l.count ? l[i] : 0
            if lv != cv { return lv > cv }
        }
        return false
    }

    private static func nums(_ v: String?) -> [Int] {
        let t = (v ?? "").trimmingCharacters(in: .whitespaces)
        let noV = (t.hasPrefix("v") || t.hasPrefix("V")) ? String(t.dropFirst()) : t
        let core = noV.split(whereSeparator: { $0 == "-" || $0 == "+" }).first.map(String.init) ?? ""
        return core.split(separator: ".").map { Int($0) ?? 0 }
    }

    /// 解析 latest.json（platform = "mac"/"win"，`lang` 找不到時退回 `en`）。失敗回 nil。
    static func parseManifest(_ json: String?, platform: String, lang: String = "zh-Hant") -> UpdateManifest? {
        guard let data = json?.data(using: .utf8),
              let obj = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let version = obj["version"] as? String else { return nil }
        var notes: String?
        if let n = obj["notes"] as? String { notes = n }
        else if let nd = obj["notes"] as? [String: Any] { notes = (nd[lang] as? String) ?? (nd["en"] as? String) }
        let url = (obj[platform] as? [String: Any])?["url"] as? String
        let page = obj["page"] as? String ?? ""
        return UpdateManifest(version: version, notes: notes, url: url, page: page)
    }
}
