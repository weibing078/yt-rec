import Foundation

struct AdSpan: Equatable {
    var startSec: Int
    var endSec: Int
}

/// 跟 C# `AdIntervalLog` 同一張規則。0 段回 nil，呼叫端不寫檔。崩潰復原不要呼叫。
final class AdIntervalLog {
    private var openStart: Int?
    private var closed: [AdSpan] = []

    func observe(ad: Bool, fileSec: Int) {
        let sec = max(0, fileSec)
        if ad {
            if openStart == nil { openStart = sec }
            return
        }
        if let start = openStart { close(start: start, end: sec) }
    }

    func closeOpen(fileSec: Int) {
        let sec = max(0, fileSec)
        if let start = openStart { close(start: start, end: sec) }
    }

    var spans: [AdSpan] { closed }

    func render() -> String? { Self.render(closed) }

    static func render(_ spans: [AdSpan]) -> String? {
        if spans.isEmpty { return nil }
        var lines = ["每秒偵測，誤差約 ±1-2 秒"]
        for span in spans {
            lines.append("\(clock(span.startSec))–\(clock(span.endSec))")
        }
        return lines.joined(separator: "\n") + "\n"
    }

    static func clock(_ sec: Int) -> String {
        let s = max(0, sec)
        return String(format: "%02d:%02d:%02d", s / 3600, (s % 3600) / 60, s % 60)
    }

    static func sidecarURL(beside mp4: URL) -> URL {
        mp4.deletingPathExtension().appendingPathExtension("廣告時段.txt")
    }

    private func close(start: Int, end: Int) {
        openStart = nil
        if end > start { closed.append(AdSpan(startSec: start, endSec: end)) }
    }
}
