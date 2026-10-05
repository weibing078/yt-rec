import XCTest
@testable import YTRec

/// 跟 C# StreamEndTableTests 同一份情境。時間都從固定的 t0 起算。
final class StreamEndTableTests: XCTestCase {
    private let t0 = Date(timeIntervalSince1970: 1_767_225_600)

    private func snap(ended: Bool, ad: Bool, content: Bool, id: String, age: TimeInterval) -> PlayerSnapshot {
        PlayerSnapshot(ended: ended, ad: ad, content: content, id: id, receivedAt: t0.addingTimeInterval(-age))
    }

    private func eval(phase: StreamPhase, anchor: String, snap: PlayerSnapshot?, candidateAge: TimeInterval?, ext: Int) -> StreamDecision {
        StreamEnd.evaluate(phase: phase, anchorId: anchor, snapshot: snap,
                           candidateStart: candidateAge.map { t0.addingTimeInterval(-$0) },
                           extendedAdSeconds: ext, now: t0)
    }

    func testSameVideoResumesAfterBriefEnd_DoesNotStop() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 0), candidateAge: 5, ext: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
        XCTAssertNil(d.countdownSeconds)
    }

    func testStillEndedAfter20Seconds_Stops() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: false, content: false, id: "aaa", age: 0), candidateAge: 20, ext: 0)
        XCTAssertEqual(d.stopReason, StreamEnd.videoEndedReason)
    }

    func testAutoplayNextVideoWithoutEnded_StopsNow() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "bbb", age: 0), candidateAge: nil, ext: 0)
        XCTAssertEqual(d.stopReason, StreamEnd.otherVideoReason)
    }

    func testDifferentIdDuringAd_DoesNotStop() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: true, content: false, id: "bbb", age: 0), candidateAge: nil, ext: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
    }

    func testAdAtDeadline_Extends() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: true, content: false, id: "aaa", age: 0), candidateAge: 20, ext: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertEqual(d.extendedAdSeconds, 20)
        XCTAssertNotNil(d.candidateStart)
    }

    func testAdExtensionsReach120Seconds_ThenStops() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: true, content: false, id: "aaa", age: 0), candidateAge: 140, ext: 120)
        XCTAssertEqual(d.stopReason, StreamEnd.videoEndedReason)
    }

    func testEmptyIdWhileContentPlays_DoesNotStop() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "", age: 0), candidateAge: 5, ext: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
    }

    func testStaleSnapshotPastDeadline_Stops() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 6), candidateAge: 20, ext: 0)
        XCTAssertEqual(d.stopReason, StreamEnd.videoEndedReason)
    }

    func testFinalizingIgnoresEnded() {
        let start = t0.addingTimeInterval(-5)
        let d = StreamEnd.evaluate(phase: .finalizing, anchorId: "aaa",
                                   snapshot: snap(ended: true, ad: false, content: false, id: "aaa", age: 0),
                                   candidateStart: start, extendedAdSeconds: 0, now: t0)
        XCTAssertNil(d.stopReason)
        XCTAssertTrue(d.leaveUiAlone)
        XCTAssertEqual(d.candidateStart, start)
        XCTAssertFalse(d.previewShowsEnded)
    }

    func testPreviewEndedShowsNoticeAndClearsWhenContentReturns() {
        let ended = eval(phase: .preview, anchor: "aaa", snap: snap(ended: true, ad: false, content: false, id: "aaa", age: 0), candidateAge: nil, ext: 0)
        XCTAssertNil(ended.stopReason)
        XCTAssertTrue(ended.previewShowsEnded)
        XCTAssertFalse(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: true))

        let back = eval(phase: .preview, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 0), candidateAge: nil, ext: 0)
        XCTAssertFalse(back.previewShowsEnded)
        XCTAssertTrue(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: false))
    }

    func testContentWithoutCandidate_DoesNothing() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 0), candidateAge: nil, ext: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
        XCTAssertNil(d.countdownSeconds)
    }
}
