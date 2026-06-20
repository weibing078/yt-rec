import Foundation

enum TrackAStatus: Equatable {
    case idle
    case disabled                   // 下載軌關閉（D2 預設：v2 只螢幕側錄）
    case probing
    case running(String)            // 進度說明文字
    case waitingRetry(round: Int)   // 30 秒輪詢中
    case succeeded(URL)
    case failedTerminal(String)
    case marathonSkipped            // 馬拉松直播：不啟動下載軌，僅側錄
    case skippedAutoLive            // D2 自動模式：進行中直播不下載，僅側錄
    case cancelled

    var isSettled: Bool {
        switch self {
        case .disabled, .succeeded, .failedTerminal, .marathonSkipped, .skippedAutoLive, .cancelled: return true
        default: return false
        }
    }
}

enum TrackBStatus: Equatable {
    case idle
    case preparing
    case previewing                 // SCK 擷取中、可倒帶定位，但還沒寫檔（倒帶錄影的定位階段）
    case recording(mode: String)    // mode: 畫質標籤（如「1080p」「720p」）
    case finalizing
    case finished(URL)
    case failed(String)
    case discarded                  // A 成功後側錄檔已丟垃圾桶

    var isActive: Bool {
        switch self {
        case .preparing, .previewing, .recording, .finalizing: return true
        default: return false
        }
    }
    /// 真正在寫檔（錄到檔案）——預覽/定位不算。
    var isRecordingToFile: Bool {
        if case .recording = self { return true }
        return false
    }
}

enum FileKind: String, Codable {
    case native = "原生下載"
    case sidecar = "背景側錄"
    case clip = "精華片段"
}

struct CompletedFile: Identifiable, Codable, Equatable {
    var id = UUID()
    let url: URL
    let kind: FileKind
    let jobTitle: String
    let date: Date

    var exists: Bool { FileManager.default.fileExists(atPath: url.path) }
}

/// 一場搶錄任務的可觀察狀態（UI 直接綁定）
@MainActor
final class JobViewModel: ObservableObject, Identifiable {
    let id = UUID()
    let url: String
    let videoID: String?
    let startedAt = Date()

    @Published var title: String
    @Published var trackA: TrackAStatus = .idle
    @Published var trackB: TrackBStatus = .idle
    @Published var recordSeconds: Double = 0
    @Published var infoMessage: String?
    @Published var behindLiveSec: Double = 0     // 定位/倒帶時：目前落後直播多少秒
    @Published var dvrWindowSec: Double = 0      // 可倒帶窗多長

    /// 非 nil＝這是「只抓某段」的 VOD 片段下載任務（不螢幕側錄），值為人話標籤如「5:30–7:45」。
    let sectionLabel: String?
    var isSectionDownload: Bool { sectionLabel != nil }

    /// 任務專屬資料夾（成品）與工作資料夾（暫存切片）
    let jobDir: URL
    var workDir: URL { jobDir.appendingPathComponent(".work", isDirectory: true) }
    var clipsDir: URL { jobDir.appendingPathComponent("精華片段", isDirectory: true) }

    var isActive: Bool { !trackA.isSettled || trackB.isActive }

    init(url: String, outputRoot: URL, sectionLabel: String? = nil) {
        self.url = url
        self.sectionLabel = sectionLabel
        self.videoID = YtURL.videoID(url)
        let stamp = Self.stampFormatter.string(from: Date())
        let name = "\(stamp) \(videoID ?? "live")"
        self.title = videoID ?? url
        self.jobDir = outputRoot.appendingPathComponent(name, isDirectory: true)
        try? FileManager.default.createDirectory(at: workDir, withIntermediateDirectories: true)
    }

    private static let stampFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd-HHmm"
        return f
    }()
}
