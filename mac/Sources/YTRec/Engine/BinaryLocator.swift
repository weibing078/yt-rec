import Foundation

/// 找 yt-dlp / ffmpeg 執行檔：優先用 App 內建，其次開發目錄與 Homebrew
enum BinaryLocator {
    enum Tool: String {
        case ytdlp = "yt-dlp"
        case ffmpeg = "ffmpeg"
    }

    static func url(for tool: Tool) -> URL? {
        var candidates: [URL] = []
        if let res = Bundle.main.resourceURL {
            candidates.append(res.appendingPathComponent("bin/\(tool.rawValue)"))
        }
        if let dev = ProcessInfo.processInfo.environment["LCF_BIN_DIR"] {
            candidates.append(URL(fileURLWithPath: dev).appendingPathComponent(tool.rawValue))
        }
        // swift run 開發情境：從執行檔往上找 Vendor/bin
        let exe = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
        var dir = exe.deletingLastPathComponent()
        for _ in 0..<6 {
            candidates.append(dir.appendingPathComponent("Vendor/bin/\(tool.rawValue)"))
            dir.deleteLastPathComponent()
        }
        candidates.append(URL(fileURLWithPath: "/opt/homebrew/bin/\(tool.rawValue)"))
        candidates.append(URL(fileURLWithPath: "/usr/local/bin/\(tool.rawValue)"))

        return firstExecutable(candidates, isExecutable: FileManager.default.isExecutableFile(atPath:))
    }

    /// 從候選清單挑「第一個可執行」（純函式，可注入 isExecutable 以擺脫真檔系統）。
    /// 候選順序＝內嵌 bin → 開發環境變數 → Vendor/bin → Homebrew，故「內嵌優先、找不到回 nil」由此保證。
    static func firstExecutable(_ candidates: [URL], isExecutable: (String) -> Bool) -> URL? {
        candidates.first { isExecutable($0.path) }
    }

    static var missingTools: [String] {
        var missing: [String] = []
        if url(for: .ytdlp) == nil { missing.append("yt-dlp") }
        if url(for: .ffmpeg) == nil { missing.append("ffmpeg") }
        return missing
    }
}
