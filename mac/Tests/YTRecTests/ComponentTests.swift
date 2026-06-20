import XCTest
@testable import YTRec

/// L2 元件層：用暫存目錄驗「會落盤的邏輯」，不碰網路/權限/真 binary。
/// SegmentStore 是 P0 損毀 bug 的現場（HANDOFF §3.1），這層把它的切片命名/拼檔/復原列舉釘死。

final class SegmentStoreTests: XCTestCase {
    var dir: URL!
    var store: SegmentStore!

    override func setUp() {
        super.setUp()
        dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("lcf-seg-\(UUID().uuidString)", isDirectory: true)
        store = SegmentStore(dir: dir)
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: dir)
        super.tearDown()
    }

    /// 讀任一 queue.sync 屬性會把先前的 async 寫檔排空（serial queue），用來等檔案落盤。
    private func flush() { _ = store.totalDuration }

    func testInitSegmentWritten() {
        let data = Data([0xDE, 0xAD, 0xBE, 0xEF])
        store.appendInitSegment(data)
        flush()
        let initURL = dir.appendingPathComponent("seg_init.mp4")
        XCTAssertEqual(try? Data(contentsOf: initURL), data)
    }

    func testMediaSegmentNamingAndDurations() {
        store.appendInitSegment(Data([0x00]))
        store.appendMediaSegment(Data(count: 10), duration: 2.0)
        store.appendMediaSegment(Data(count: 10), duration: 1.96)
        store.appendMediaSegment(Data(count: 10), duration: 2.0)
        flush()
        // 命名 seg_%05d.m4s 從 0 遞增
        for i in 0..<3 {
            let name = String(format: "seg_%05d.m4s", i)
            XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent(name).path), "缺 \(name)")
        }
        XCTAssertEqual(store.segmentDurations, [2.0, 1.96, 2.0])
        XCTAssertEqual(store.totalDuration, 5.96, accuracy: 0.0001)
    }

    func testPlaylistEndList() {
        store.appendInitSegment(Data([0x00]))
        store.appendMediaSegment(Data(count: 4), duration: 2.0)
        store.markEnded()
        flush()
        let m3u8 = try? String(contentsOf: store.playlistURL, encoding: .utf8)
        XCTAssertNotNil(m3u8)
        XCTAssertTrue(m3u8!.contains("#EXT-X-ENDLIST"))
        XCTAssertTrue(m3u8!.contains("seg_00000.m4s"))
    }

    func testExistingSegmentNamesSortedAndFiltered() {
        // 模擬上次崩潰留下的切片（含一個雜檔、亂序建立）
        write("seg_init.mp4", 8)
        write("seg_00002.m4s", 8)
        write("seg_00000.m4s", 8)
        write("live.m3u8", 8)            // 非切片，應被忽略
        write("seg_garbage.txt", 8)      // 非切片，應被忽略
        let names = SegmentStore.existingSegmentNames(in: dir)
        XCTAssertEqual(names, ["seg_init.mp4", "seg_00000.m4s", "seg_00002.m4s"])
    }

    func testExistingSegmentNamesNoInitReturnsEmpty() {
        write("seg_00000.m4s", 8)        // 有切片但沒有 init → 無法復原
        XCTAssertEqual(SegmentStore.existingSegmentNames(in: dir), [])
    }

    func testExistingSegmentNamesEmptyDir() {
        XCTAssertEqual(SegmentStore.existingSegmentNames(in: dir), [])
    }

    func testBinaryConcatLengthAndSkipsMissing() throws {
        write("seg_init.mp4", 3)
        write("seg_00000.m4s", 4)
        write("seg_00001.m4s", 5)
        let out = dir.appendingPathComponent("combined.mp4")
        // 故意夾一個不存在的檔名 → 應被跳過，不丟例外
        try SegmentStore.binaryConcat(
            dir: dir,
            names: ["seg_init.mp4", "seg_00000.m4s", "MISSING.m4s", "seg_00001.m4s"],
            output: out)
        XCTAssertEqual(FileUtil.fileSize(out), 12)   // 3+4+5，缺檔跳過
    }

    func testAssembleCombinedFile() throws {
        store.appendInitSegment(Data(count: 6))
        store.appendMediaSegment(Data(count: 10), duration: 2.0)
        store.appendMediaSegment(Data(count: 10), duration: 2.0)
        flush()
        let out = dir.appendingPathComponent("assembled.mp4")
        try store.assembleCombinedFile(to: out)
        XCTAssertEqual(FileUtil.fileSize(out), 26)   // init 6 + 兩片各 10
    }

    private func write(_ name: String, _ bytes: Int) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try? Data(count: bytes).write(to: dir.appendingPathComponent(name))
    }
}

/// 軌道 A 成品挑檔：yt-dlp 跑完後「掃資料夾最新影片檔」的規則（PRD §6.3-8）。
final class NewestVideoFileTests: XCTestCase {
    var dir: URL!

    override func setUp() {
        super.setUp()
        dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("lcf-newest-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: dir)
        super.tearDown()
    }

    @discardableResult
    private func make(_ name: String, sizeKB: Int, ageSeconds: TimeInterval) -> URL {
        let url = dir.appendingPathComponent(name)
        try? Data(count: sizeKB * 1000).write(to: url)
        let mtime = Date().addingTimeInterval(-ageSeconds)
        try? FileManager.default.setAttributes([.modificationDate: mtime], ofItemAtPath: url.path)
        return url
    }

    func testPicksNewest() {
        make("old.mp4", sizeKB: 200, ageSeconds: 100)
        make("new.mp4", sizeKB: 200, ageSeconds: 1)
        let pick = YtDlpEngine.newestVideoFile(in: dir, newerThan: .distantPast)
        // 比檔名（暫存目錄 /var↔/private/var symlink 差異，非邏輯問題）
        XCTAssertEqual(pick?.lastPathComponent, "new.mp4")
    }

    func testExcludesPartFiles() {
        make("done.mp4", sizeKB: 200, ageSeconds: 100)
        make("downloading.mp4.part", sizeKB: 999, ageSeconds: 1)   // 較新但是 .part
        let pick = YtDlpEngine.newestVideoFile(in: dir, newerThan: .distantPast)
        XCTAssertEqual(pick?.lastPathComponent, "done.mp4")
    }

    func testExcludesTooSmall() {
        make("stub.mp4", sizeKB: 50, ageSeconds: 1)                 // <100KB 視為非成品
        XCTAssertNil(YtDlpEngine.newestVideoFile(in: dir, newerThan: .distantPast))
    }

    func testExcludesOlderThanThreshold() {
        make("stale.mp4", sizeKB: 200, ageSeconds: 100)
        // 門檻設在 50 秒前 → 100 秒前的檔被排除
        XCTAssertNil(YtDlpEngine.newestVideoFile(in: dir, newerThan: Date().addingTimeInterval(-50)))
    }

    func testExtensionWhitelist() {
        make("note.txt", sizeKB: 500, ageSeconds: 1)               // 非影片副檔名
        make("clip.mkv", sizeKB: 200, ageSeconds: 2)               // mkv 在白名單
        let pick = YtDlpEngine.newestVideoFile(in: dir, newerThan: .distantPast)
        XCTAssertEqual(pick?.lastPathComponent, "clip.mkv")
    }

    func testEmptyDirReturnsNil() {
        XCTAssertNil(YtDlpEngine.newestVideoFile(in: dir, newerThan: .distantPast))
    }
}

/// 災難復原決策（R4 的純函式核心）＋binaryConcat 跨 8MB chunk 邊界。
final class RecoveryTests: XCTestCase {
    func testShouldRecoverCountBoundary() {
        // existingSegmentNames 回傳含 init，count>2 ＝ init＋≥2 片
        XCTAssertFalse(SegmentStore.shouldRecover(names: ["seg_init.mp4", "seg_00000.m4s"], recoveredExists: false, ffmpegAvailable: true))   // 只 1 片
        XCTAssertTrue(SegmentStore.shouldRecover(names: ["seg_init.mp4", "seg_00000.m4s", "seg_00001.m4s"], recoveredExists: false, ffmpegAvailable: true))   // 2 片
    }
    func testShouldRecoverGuards() {
        let ok = ["seg_init.mp4", "seg_00000.m4s", "seg_00001.m4s"]
        XCTAssertFalse(SegmentStore.shouldRecover(names: ok, recoveredExists: true, ffmpegAvailable: true))   // 已修復過
        XCTAssertFalse(SegmentStore.shouldRecover(names: ok, recoveredExists: false, ffmpegAvailable: false)) // 沒 ffmpeg
        XCTAssertFalse(SegmentStore.shouldRecover(names: [], recoveredExists: false, ffmpegAvailable: true))  // 缺 init→空
    }

    func testBinaryConcatCrossesChunkBoundary() throws {
        let dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("lcf-concat-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }

        let chunk = 8 << 20   // binaryConcat 的 read(upToCount:) 大小
        let a = Data(repeating: 0xAA, count: 10)
        // b 跨越一個 chunk 邊界：前 8MB 是 0xBB，最後 1 byte 0xCC（落在第二圈）
        var b = Data(repeating: 0xBB, count: chunk)
        b.append(0xCC)
        try a.write(to: dir.appendingPathComponent("a"))
        try b.write(to: dir.appendingPathComponent("b"))
        let out = dir.appendingPathComponent("out.bin")
        try SegmentStore.binaryConcat(dir: dir, names: ["a", "b"], output: out)

        let result = try Data(contentsOf: out)
        XCTAssertEqual(result.count, 10 + chunk + 1)              // 長度＝各檔加總
        XCTAssertEqual(result, a + b)                             // 逐位元組正確、邊界不錯位
        XCTAssertEqual(result[10 + chunk], 0xCC)                  // 第二圈那個 byte 在對的位置
    }

    func testBinaryConcatEmptyAndAllMissing() throws {
        let dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("lcf-concat2-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let out = dir.appendingPathComponent("out.bin")
        // 全缺檔 → 產出 0 位元組檔、不丟例外（契約：上層須自行驗 fileSize）
        try SegmentStore.binaryConcat(dir: dir, names: ["nope1", "nope2"], output: out)
        XCTAssertEqual(FileUtil.fileSize(out), 0)
    }
}

/// 磁碟保護：查不到空間的 fallback ＋ 三門檻一致性。
final class DiskGuardResolverTests: XCTestCase {
    func testUnreadableReturnsMaxAndPassesPreCheck() {
        XCTAssertEqual(DiskGuard.freeBytes { nil }, .max)                 // 查不到→.max（不誤擋）
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: .max), .ok)
    }
    func testResolverPassesThrough() {
        XCTAssertEqual(DiskGuard.freeBytes { 5_000_000_000 }, 5_000_000_000)
    }
    func testStopThresholdGreaterThanRefuse() {
        // 9GB：開錄前不拒（warn）但錄製中會收工 → 鎖住 stopGB(10) > refuseGB(8) 的設計意圖
        let nineGB: Int64 = 9_000_000_000
        if case .refuse = DiskGuard.preCheck(freeBytes: nineGB) { XCTFail("9GB 不該拒錄") }
        XCTAssertTrue(DiskGuard.shouldStopRecording(freeBytes: nineGB))
    }
}
