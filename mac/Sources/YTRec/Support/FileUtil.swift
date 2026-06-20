import Foundation
import AppKit

enum FileUtil {
    /// 檔名消毒：去掉 macOS 不允許或容易出事的字元，限制長度
    static func sanitize(_ name: String, maxLength: Int = 60) -> String {
        var s = name
        for ch in ["/", ":", "\\", "\0", "\n", "\r", "\t"] {
            s = s.replacingOccurrences(of: ch, with: " ")
        }
        s = s.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.isEmpty { s = "未命名" }
        if s.count > maxLength { s = String(s.prefix(maxLength)) }
        return s
    }

    static func formatBytes(_ bytes: Int64) -> String {
        let fmt = ByteCountFormatter()
        fmt.countStyle = .file
        return fmt.string(fromByteCount: bytes)
    }

    static func fileSize(_ url: URL) -> Int64 {
        let attrs = try? FileManager.default.attributesOfItem(atPath: url.path)
        return (attrs?[.size] as? Int64) ?? 0
    }

    static func formatDuration(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds >= 0 else { return "00:00" }
        let s = Int(seconds)
        if s >= 3600 {
            return String(format: "%d:%02d:%02d", s / 3600, (s % 3600) / 60, s % 60)
        }
        return String(format: "%02d:%02d", s / 60, s % 60)
    }

    static func revealInFinder(_ url: URL) {
        NSWorkspace.shared.activateFileViewerSelecting([url])
    }

    static func trash(_ url: URL) {
        guard FileManager.default.fileExists(atPath: url.path) else { return }
        NSWorkspace.shared.recycle([url]) { _, error in
            if let error { Log.error("file", "丟垃圾桶失敗 \(url.lastPathComponent): \(error.localizedDescription)") }
        }
    }
}
