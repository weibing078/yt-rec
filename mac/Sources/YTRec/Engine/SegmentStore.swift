import Foundation

/// HLS 播放清單產生（純邏輯，可測試）
enum M3U8Builder {
    struct Segment: Equatable {
        let name: String
        let duration: Double
    }

    static func playlist(segments: [Segment], ended: Bool, targetDuration: Int = 6) -> String {
        var lines = [
            "#EXTM3U",
            "#EXT-X-VERSION:7",
            "#EXT-X-TARGETDURATION:\(targetDuration)",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXT-X-PLAYLIST-TYPE:EVENT",
            "#EXT-X-INDEPENDENT-SEGMENTS",
            "#EXT-X-MAP:URI=\"seg_init.mp4\"",
        ]
        for seg in segments {
            lines.append(String(format: "#EXTINF:%.3f,", max(seg.duration, 0.001)))
            lines.append(seg.name)
        }
        if ended { lines.append("#EXT-X-ENDLIST") }
        return lines.joined(separator: "\n") + "\n"
    }
}

/// fMP4 切片倉庫：收 AVAssetWriter 吐出的切片、維護 live.m3u8、收工時拼回完整 MP4
final class SegmentStore {
    let dir: URL
    private let queue = DispatchQueue(label: "lcf.segmentstore")
    private var segments: [M3U8Builder.Segment] = []
    private var ended = false

    init(dir: URL) {
        self.dir = dir
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    var playlistURL: URL { dir.appendingPathComponent("live.m3u8") }

    var segmentDurations: [Double] {
        queue.sync { segments.map(\.duration) }
    }

    var totalDuration: Double {
        queue.sync { segments.reduce(0) { $0 + $1.duration } }
    }

    func appendInitSegment(_ data: Data) {
        queue.async { [self] in
            try? data.write(to: dir.appendingPathComponent("seg_init.mp4"))
            writePlaylist()
        }
    }

    func appendMediaSegment(_ data: Data, duration: Double) {
        queue.async { [self] in
            let name = String(format: "seg_%05d.m4s", segments.count)
            try? data.write(to: dir.appendingPathComponent(name))
            segments.append(.init(name: name, duration: duration))
            writePlaylist()
        }
    }

    func markEnded() {
        queue.async { [self] in
            ended = true
            writePlaylist()
        }
    }

    private func writePlaylist() {
        let text = M3U8Builder.playlist(segments: segments, ended: ended)
        try? text.data(using: .utf8)?.write(to: playlistURL, options: .atomic)
    }

    /// 把 init + 全部切片串成單一 fMP4 檔（之後可再用 ffmpeg remux 成標準 MP4）
    func assembleCombinedFile(to output: URL) throws {
        let names = queue.sync { ["seg_init.mp4"] + segments.map(\.name) }
        try Self.binaryConcat(dir: dir, names: names, output: output)
    }

    /// 純檔案層級拼接（也供災難復原使用：掃資料夾現有切片直接拼）
    static func binaryConcat(dir: URL, names: [String], output: URL) throws {
        let fm = FileManager.default
        try? fm.removeItem(at: output)
        fm.createFile(atPath: output.path, contents: nil)
        let out = try FileHandle(forWritingTo: output)
        defer { try? out.close() }
        for name in names {
            let f = dir.appendingPathComponent(name)
            guard fm.fileExists(atPath: f.path) else { continue }
            let input = try FileHandle(forReadingFrom: f)
            defer { try? input.close() }
            while let chunk = try input.read(upToCount: 8 << 20), !chunk.isEmpty {
                try out.write(contentsOf: chunk)
            }
        }
    }

    /// 災難復原是否該動手（純決策）：需 init＋≥2 片（count>2）、尚未修復過、且 ffmpeg 可用。
    static func shouldRecover(names: [String], recoveredExists: Bool, ffmpegAvailable: Bool) -> Bool {
        names.count > 2 && !recoveredExists && ffmpegAvailable
    }

    /// 災難復原：列出資料夾中現存切片（依檔名排序）
    static func existingSegmentNames(in dir: URL) -> [String] {
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(atPath: dir.path) else { return [] }
        let segs = items.filter { $0.hasPrefix("seg_") && $0.hasSuffix(".m4s") }.sorted()
        guard items.contains("seg_init.mp4"), !segs.isEmpty else { return [] }
        return ["seg_init.mp4"] + segs
    }
}
