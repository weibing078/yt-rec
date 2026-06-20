import XCTest
import CoreMedia
@testable import YTRec

/// L1：把錄製引擎裡「誰先起 session」「健康檢查門檻」兩個 P0 敏感決策從即時管線抽出後釘住，
/// 並補 retime 的 DTS／負 PTS／多時序路徑（既有測試只有單一視訊樣本）。

final class SessionGateTests: XCTestCase {
    func testAudioStartsSession() {
        // 第一筆音訊起 session（核心不變量）
        XCTAssertEqual(RecorderEngine.sessionGate(kind: .audio, sessionStarted: false, audioDisabled: false), .startSession)
        XCTAssertEqual(RecorderEngine.sessionGate(kind: .audio, sessionStarted: true, audioDisabled: false), .appendOnly)
        XCTAssertEqual(RecorderEngine.sessionGate(kind: .audio, sessionStarted: false, audioDisabled: true), .dropAudioDisabled)
    }
    func testVideoWaitsForAudioUnlessDisabled() {
        // session 未起、音訊未停用 → 視訊先丟，等音訊
        XCTAssertEqual(RecorderEngine.sessionGate(kind: .video, sessionStarted: false, audioDisabled: false), .dropWaitingForAudio)
        // 逾時無音訊 → 視訊起 session 做純影像保命
        XCTAssertEqual(RecorderEngine.sessionGate(kind: .video, sessionStarted: false, audioDisabled: true), .startSession)
        // session 已起 → 照常 append
        XCTAssertEqual(RecorderEngine.sessionGate(kind: .video, sessionStarted: true, audioDisabled: false), .appendOnly)
    }
}

final class HealthDecisionTests: XCTestCase {
    func testNoFramesWarning() {
        XCTAssertFalse(RecorderEngine.healthDecision(ticks: 3, framesReceived: 0, sessionStarted: false, audioDisabled: false, alreadyWarned: false).warnNoFrames)
        XCTAssertTrue(RecorderEngine.healthDecision(ticks: 4, framesReceived: 0, sessionStarted: false, audioDisabled: false, alreadyWarned: false).warnNoFrames)
        XCTAssertFalse(RecorderEngine.healthDecision(ticks: 4, framesReceived: 0, sessionStarted: false, audioDisabled: false, alreadyWarned: true).warnNoFrames) // 已警告不重複
    }
    func testDisableAudioThreshold() {
        XCTAssertFalse(RecorderEngine.healthDecision(ticks: 5, framesReceived: 9, sessionStarted: false, audioDisabled: false, alreadyWarned: false).disableAudio) // 才 5 tick
        XCTAssertTrue(RecorderEngine.healthDecision(ticks: 6, framesReceived: 9, sessionStarted: false, audioDisabled: false, alreadyWarned: false).disableAudio)  // ~12s 有畫面無音訊
        XCTAssertFalse(RecorderEngine.healthDecision(ticks: 6, framesReceived: 9, sessionStarted: true, audioDisabled: false, alreadyWarned: false).disableAudio)  // 已起 session 不轉
        XCTAssertFalse(RecorderEngine.healthDecision(ticks: 6, framesReceived: 9, sessionStarted: false, audioDisabled: true, alreadyWarned: false).disableAudio)  // 已停用不重複
        XCTAssertFalse(RecorderEngine.healthDecision(ticks: 6, framesReceived: 0, sessionStarted: false, audioDisabled: false, alreadyWarned: false).disableAudio) // 連畫面都沒有
    }
}

final class RetimeMoreTests: XCTestCase {
    private func makeBuffer(pts: CMTime, dts: CMTime = .invalid,
                            duration: CMTime = CMTime(value: 1, timescale: 30)) -> CMSampleBuffer? {
        var bb: CMBlockBuffer?
        let len = 16
        guard CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault, memoryBlock: nil, blockLength: len,
            blockAllocator: kCFAllocatorDefault, customBlockSource: nil, offsetToData: 0,
            dataLength: len, flags: kCMBlockBufferAssureMemoryNowFlag, blockBufferOut: &bb) == kCMBlockBufferNoErr,
              let bb else { return nil }
        var timing = CMSampleTimingInfo(duration: duration, presentationTimeStamp: pts, decodeTimeStamp: dts)
        var size = [len]
        var sb: CMSampleBuffer?
        guard CMSampleBufferCreateReady(
            allocator: kCFAllocatorDefault, dataBuffer: bb, formatDescription: nil,
            sampleCount: 1, sampleTimingEntryCount: 1, sampleTimingArray: &timing,
            sampleSizeEntryCount: 1, sampleSizeArray: &size, sampleBufferOut: &sb) == noErr else { return nil }
        return sb
    }

    private func makeMultiTiming(pts0: CMTime, pts1: CMTime) -> CMSampleBuffer? {
        let len = 16, n = 2
        var bb: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault, memoryBlock: nil, blockLength: len * n,
            blockAllocator: kCFAllocatorDefault, customBlockSource: nil, offsetToData: 0,
            dataLength: len * n, flags: kCMBlockBufferAssureMemoryNowFlag, blockBufferOut: &bb) == kCMBlockBufferNoErr,
              let bb else { return nil }
        let dur = CMTime(value: 1, timescale: 48_000)
        var timings = [CMSampleTimingInfo(duration: dur, presentationTimeStamp: pts0, decodeTimeStamp: .invalid),
                       CMSampleTimingInfo(duration: dur, presentationTimeStamp: pts1, decodeTimeStamp: .invalid)]
        var sizes = [len, len]
        var sb: CMSampleBuffer?
        guard CMSampleBufferCreateReady(
            allocator: kCFAllocatorDefault, dataBuffer: bb, formatDescription: nil,
            sampleCount: n, sampleTimingEntryCount: n, sampleTimingArray: &timings,
            sampleSizeEntryCount: n, sampleSizeArray: &sizes, sampleBufferOut: &sb) == noErr else { return nil }
        return sb
    }

    func testRetimeShiftsDTSWhenValid() throws {
        let sb = try XCTUnwrap(makeBuffer(pts: CMTime(value: 100, timescale: 1), dts: CMTime(value: 99, timescale: 1)))
        let r = try XCTUnwrap(RecorderEngine.retime(sb, offset: CMTime(value: 100, timescale: 1)))
        XCTAssertEqual(CMSampleBufferGetPresentationTimeStamp(r).seconds, 0, accuracy: 0.0001)
        XCTAssertEqual(CMSampleBufferGetDecodeTimeStamp(r).seconds, -1, accuracy: 0.0001)   // DTS 也平移
    }
    func testRetimeKeepsInvalidDTS() throws {
        let sb = try XCTUnwrap(makeBuffer(pts: CMTime(value: 100, timescale: 1), dts: .invalid))
        let r = try XCTUnwrap(RecorderEngine.retime(sb, offset: CMTime(value: 100, timescale: 1)))
        XCTAssertFalse(CMSampleBufferGetDecodeTimeStamp(r).isValid)   // invalid 維持 invalid
    }
    func testRetimeCanGoNegativeBelowBaseline() throws {
        // 早於 baseline 的樣本 retime 後為負（handleVideo 的 >=0 丟棄門檻靠這個）
        let sb = try XCTUnwrap(makeBuffer(pts: CMTime(value: 99, timescale: 1)))
        let r = try XCTUnwrap(RecorderEngine.retime(sb, offset: CMTime(value: 100, timescale: 1)))
        XCTAssertLessThan(CMSampleBufferGetPresentationTimeStamp(r).seconds, 0)
    }
    func testRetimeShiftsAllTimingEntries() throws {
        // 音訊 buffer 常含多筆 timing；每一筆都要被同一 offset 平移（非只取第一筆）
        let sb = try XCTUnwrap(makeMultiTiming(pts0: CMTime(value: 100, timescale: 1),
                                               pts1: CMTime(value: 101, timescale: 1)))
        let r = try XCTUnwrap(RecorderEngine.retime(sb, offset: CMTime(value: 100, timescale: 1)))
        var count: CMItemCount = 0
        CMSampleBufferGetSampleTimingInfoArray(r, entryCount: 0, arrayToFill: nil, entriesNeededOut: &count)
        var infos = [CMSampleTimingInfo](repeating: CMSampleTimingInfo(), count: count)
        CMSampleBufferGetSampleTimingInfoArray(r, entryCount: count, arrayToFill: &infos, entriesNeededOut: &count)
        XCTAssertEqual(count, 2)
        XCTAssertEqual(infos[0].presentationTimeStamp.seconds, 0, accuracy: 0.0001)
        XCTAssertEqual(infos[1].presentationTimeStamp.seconds, 1, accuracy: 0.0001)
    }
}
