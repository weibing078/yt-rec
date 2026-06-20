import XCTest
import AVFoundation
import CoreMedia
@testable import YTRec

/// L3 整合層：用真 binary／真網路端對端，但缺依賴就 XCTSkip（CI/無網路不會紅）。
/// 真網路測試額外要 LCF_RUN_NETWORK_TESTS=1 才跑。

/// 讓 BinaryLocator 在測試情境也找得到內建 ffmpeg/yt-dlp（從本檔位置推回 repo 的 Vendor/bin）。
enum TestBins {
    static func ensure() {
        let repoRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // YTRecTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // repo root
        let bin = repoRoot.appendingPathComponent("Vendor/bin")
        setenv("LCF_BIN_DIR", bin.path, 1)
    }
}

// MARK: - 時間軸平移 retime（側錄 P0 修法的純函式核心）

final class RetimeTests: XCTestCase {
    private func makeSampleBuffer(pts: CMTime, duration: CMTime) -> CMSampleBuffer? {
        var blockBuffer: CMBlockBuffer?
        let length = 16
        let s1 = CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault, memoryBlock: nil, blockLength: length,
            blockAllocator: kCFAllocatorDefault, customBlockSource: nil,
            offsetToData: 0, dataLength: length, flags: kCMBlockBufferAssureMemoryNowFlag,
            blockBufferOut: &blockBuffer)
        guard s1 == kCMBlockBufferNoErr, let blockBuffer else { return nil }
        var timing = CMSampleTimingInfo(duration: duration, presentationTimeStamp: pts, decodeTimeStamp: .invalid)
        var sizeArray = [length]
        var sampleBuffer: CMSampleBuffer?
        let s2 = CMSampleBufferCreateReady(
            allocator: kCFAllocatorDefault, dataBuffer: blockBuffer, formatDescription: nil,
            sampleCount: 1, sampleTimingEntryCount: 1, sampleTimingArray: &timing,
            sampleSizeEntryCount: 1, sampleSizeArray: &sizeArray, sampleBufferOut: &sampleBuffer)
        guard s2 == noErr else { return nil }
        return sampleBuffer
    }

    func testRetimeShiftsToZero() throws {
        // host-time 起點 100s 的畫格，offset=100s → 平移後從 0 起算
        guard let sb = makeSampleBuffer(pts: CMTime(value: 100, timescale: 1),
                                        duration: CMTime(value: 1, timescale: 30)) else {
            throw XCTSkip("無法合成 CMSampleBuffer")
        }
        let retimed = try XCTUnwrap(RecorderEngine.retime(sb, offset: CMTime(value: 100, timescale: 1)))
        XCTAssertEqual(CMSampleBufferGetPresentationTimeStamp(retimed).seconds, 0, accuracy: 0.0001)
    }

    func testRetimePreservesDuration() throws {
        guard let sb = makeSampleBuffer(pts: CMTime(value: 250, timescale: 2),   // 125s
                                        duration: CMTime(value: 1, timescale: 30)) else {
            throw XCTSkip("無法合成 CMSampleBuffer")
        }
        let retimed = try XCTUnwrap(RecorderEngine.retime(sb, offset: CMTime(value: 100, timescale: 1)))
        // 平移只改起點不改長度；PTS 125-100=25
        XCTAssertEqual(CMSampleBufferGetPresentationTimeStamp(retimed).seconds, 25, accuracy: 0.0001)
        XCTAssertEqual(CMSampleBufferGetDuration(retimed).seconds, 1.0 / 30, accuracy: 0.0001)
    }
}

// MARK: - 軌道 A 真實下載（gated：重現 HANDOFF T1 PASS）

final class TrackADownloadTests: XCTestCase {
    func testRealDownloadPublishedVideo() async throws {
        guard ProcessInfo.processInfo.environment["LCF_RUN_NETWORK_TESTS"] == "1" else {
            throw XCTSkip("設 LCF_RUN_NETWORK_TESTS=1 才跑真實網路下載")
        }
        TestBins.ensure()
        guard BinaryLocator.url(for: .ytdlp) != nil else { throw XCTSkip("找不到 yt-dlp") }

        let outDir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("lcf-dl-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: outDir) }

        let outcome = await YtDlpEngine().start(
            url: "https://www.youtube.com/watch?v=jNQXAC9IVRw", outputDir: outDir, maxHeight: 1080)
        guard case .success(let file) = outcome else {
            XCTFail("預期 .success，實際 \(outcome)"); return
        }
        let dur = try await AVURLAsset(url: file).load(.duration)
        XCTAssertGreaterThan(dur.seconds, 0, "下載成品必須可解碼且有時長")
    }
}
