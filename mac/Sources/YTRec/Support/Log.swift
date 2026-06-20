import Foundation
import os

/// 同時寫入 os_log 與 ~/Library/Logs/<AppInfo.folderName>/<...>.log，
/// 方便無技術背景的使用者直接把 log 檔丟回來排查。
enum Log {
    private static let logger = Logger(subsystem: "tw.weibing.ytrec", category: "app")
    private static let queue = DispatchQueue(label: "lcf.log")
    private static let fileURL: URL = {
        let dir = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Logs/\(AppInfo.folderName)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("\(AppInfo.folderName).log")
    }()
    private static let stamp: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        return f
    }()

    static func info(_ area: String, _ msg: String) { write("INFO", area, msg); logger.info("[\(area)] \(msg)") }
    static func error(_ area: String, _ msg: String) { write("ERR ", area, msg); logger.error("[\(area)] \(msg)") }

    private static func write(_ level: String, _ area: String, _ msg: String) {
        queue.async {
            let line = "\(stamp.string(from: Date())) \(level) [\(area)] \(msg)\n"
            if let data = line.data(using: .utf8) {
                if let h = try? FileHandle(forWritingTo: fileURL) {
                    defer { try? h.close() }
                    if (try? h.seekToEnd()).map({ $0 > 5_000_000 }) == true {
                        try? h.close()
                        let old = fileURL.deletingPathExtension().appendingPathExtension("old.log")
                        try? FileManager.default.removeItem(at: old)
                        try? FileManager.default.moveItem(at: fileURL, to: old)
                        try? data.write(to: fileURL)
                        return
                    }
                    try? h.write(contentsOf: data)
                } else {
                    try? data.write(to: fileURL)
                }
            }
        }
    }
}
