import XCTest
@testable import YTRec

/// 跟 C# StreamEndTableTests 同一份情境。時間是單調時鐘，不是牆上時間。
final class StreamEndTableTests: XCTestCase {
    private func snap(ended: Bool, ad: Bool, content: Bool, id: String, age: TimeInterval, now: TimeInterval = 1000) -> PlayerSnapshot {
        PlayerSnapshot(ended: ended, ad: ad, content: content, id: id, receivedAt: now - age)
    }

    private func eval(phase: StreamPhase, anchor: String, snap: PlayerSnapshot?, candidateAge: TimeInterval?,
                      ext: Int, streak: Int, now: TimeInterval = 1000) -> StreamDecision {
        StreamEnd.evaluate(phase: phase, anchorId: anchor, snapshot: snap,
                           candidateStart: candidateAge.map { now - $0 },
                           extendedAdSeconds: ext, otherVideoStreak: streak, now: now)
    }

    func testPreviewContentReadyNeedsFreshNonAdContent() {
        let now: TimeInterval = 1000
        XCTAssertTrue(StreamEnd.previewContentReady(snapshot: snap(ended: false, ad: false, content: true, id: "aaa", age: 0, now: now), now: now))
        XCTAssertFalse(StreamEnd.previewContentReady(snapshot: snap(ended: false, ad: true, content: true, id: "aaa", age: 0, now: now), now: now))
        XCTAssertFalse(StreamEnd.previewContentReady(snapshot: snap(ended: false, ad: false, content: true, id: "aaa", age: 6, now: now), now: now))
        XCTAssertFalse(StreamEnd.previewContentReady(snapshot: nil, now: now))
    }

    func testEndedStartsCountdown() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: false, content: false, id: "aaa", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNotNil(d.candidateStart)
        XCTAssertEqual(d.countdownSeconds, 20)
    }

    func testAdEndedDoesNotStartCountdown() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: true, content: false, id: "aaa", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
    }

    func testPreviewAdEndedDoesNotShowEnded() {
        let d = eval(phase: .preview, anchor: "aaa", snap: snap(ended: true, ad: true, content: false, id: "aaa", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertFalse(d.previewShowsEnded)
        XCTAssertTrue(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: d.previewShowsEnded, previewShowsOtherVideo: d.previewShowsOtherVideo))
    }

    func testSameVideoResumesAfterBriefEnd_DoesNotStop() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 0), candidateAge: 5, ext: 40, streak: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
        XCTAssertEqual(d.extendedAdSeconds, 0)
    }

    func testCountdownSecondsMatchTimeLeft() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: false, content: false, id: "aaa", age: 0), candidateAge: 5, ext: 0, streak: 0)
        XCTAssertEqual(d.countdownSeconds, 15)
        XCTAssertNil(d.stopReason)
    }

    func testStillEndedAfter20Seconds_Stops() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: false, content: false, id: "aaa", age: 0), candidateAge: 20, ext: 0, streak: 0)
        XCTAssertEqual(d.stopReason, StreamEnd.videoEndedReason)
    }

    func testOtherVideoNeedsThreeFreshSnapshots() {
        let other = snap(ended: false, ad: false, content: true, id: "bbb", age: 0)
        let d1 = StreamEnd.evaluate(phase: .recording, anchorId: "aaa", snapshot: other, candidateStart: nil, extendedAdSeconds: 0, otherVideoStreak: 0, now: 1000)
        XCTAssertNil(d1.stopReason)
        XCTAssertEqual(d1.otherVideoStreak, 1)
        let d2 = StreamEnd.evaluate(phase: .recording, anchorId: d1.anchorId, snapshot: other, candidateStart: nil, extendedAdSeconds: 0, otherVideoStreak: d1.otherVideoStreak, now: 1000)
        XCTAssertNil(d2.stopReason)
        XCTAssertEqual(d2.otherVideoStreak, 2)
        let same = snap(ended: false, ad: false, content: true, id: "aaa", age: 0)
        let reset = StreamEnd.evaluate(phase: .recording, anchorId: "aaa", snapshot: same, candidateStart: nil, extendedAdSeconds: 0, otherVideoStreak: 2, now: 1000)
        XCTAssertNil(reset.stopReason)
        XCTAssertEqual(reset.otherVideoStreak, 0)
        let third = StreamEnd.evaluate(phase: .recording, anchorId: "aaa", snapshot: other, candidateStart: nil, extendedAdSeconds: 0, otherVideoStreak: 2, now: 1000)
        XCTAssertEqual(third.stopReason, StreamEnd.otherVideoReason)
    }

    func testDifferentIdDuringAd_DoesNotStop() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: true, content: false, id: "bbb", age: 0), candidateAge: nil, ext: 0, streak: 2)
        XCTAssertNil(d.stopReason)
        XCTAssertEqual(d.otherVideoStreak, 0)
    }

    func testAdAtDeadline_Extends() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: true, content: false, id: "aaa", age: 0), candidateAge: 20, ext: 0, streak: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertEqual(d.extendedAdSeconds, 20)
        XCTAssertNotNil(d.candidateStart)
    }

    func testAdExtensionsReach120Seconds_ThenStops() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: true, ad: true, content: false, id: "aaa", age: 0), candidateAge: 140, ext: 120, streak: 0)
        XCTAssertEqual(d.stopReason, StreamEnd.videoEndedReason)
    }

    func testEmptyIdWhileContentPlays_DoesNotStop() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "", age: 0), candidateAge: 5, ext: 0, streak: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
    }

    func testStaleSnapshotClearsIdAndAd() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: true, content: false, id: "bbb", age: 6), candidateAge: nil, ext: 0, streak: 2)
        XCTAssertNil(d.stopReason)
        XCTAssertEqual(d.otherVideoStreak, 0)
    }

    func testStaleSnapshotPastDeadline_Stops() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 6), candidateAge: 20, ext: 0, streak: 0)
        XCTAssertEqual(d.stopReason, StreamEnd.videoEndedReason)
    }

    func testEmptyAnchorAdoptsFirstContentId_AndDoesNotStopBeforeThat() {
        let idle = eval(phase: .preview, anchor: "", snap: snap(ended: false, ad: false, content: false, id: "zzz", age: 0), candidateAge: nil, ext: 0, streak: 0)
        let idle3 = eval(phase: .preview, anchor: "", snap: snap(ended: false, ad: false, content: false, id: "zzz", age: 0), candidateAge: nil, ext: 0, streak: 2)
        XCTAssertEqual(idle.anchorId, "")
        XCTAssertEqual(idle3.otherVideoStreak, 0)
        XCTAssertNil(idle3.stopReason)
        XCTAssertFalse(idle3.previewShowsOtherVideo)

        let adopted = eval(phase: .preview, anchor: "", snap: snap(ended: false, ad: false, content: true, id: "xyz", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertEqual(adopted.anchorId, "xyz")
        XCTAssertNil(adopted.stopReason)
    }

    func testPreviewOtherVideoBlocksBeginUntilItReturns() {
        let other = snap(ended: false, ad: false, content: true, id: "bbb", age: 0)
        let d2 = StreamEnd.evaluate(phase: .preview, anchorId: "aaa", snapshot: other, candidateStart: nil, extendedAdSeconds: 0, otherVideoStreak: 2, now: 1000)
        XCTAssertTrue(d2.previewShowsOtherVideo)
        XCTAssertNil(d2.stopReason)
        XCTAssertFalse(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: d2.previewShowsEnded, previewShowsOtherVideo: d2.previewShowsOtherVideo))

        let back = snap(ended: false, ad: false, content: true, id: "aaa", age: 0)
        let restored = StreamEnd.evaluate(phase: .preview, anchorId: "aaa", snapshot: back, candidateStart: nil, extendedAdSeconds: 0, otherVideoStreak: d2.otherVideoStreak, now: 1000)
        XCTAssertFalse(restored.previewShowsOtherVideo)
        XCTAssertTrue(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: restored.previewShowsEnded, previewShowsOtherVideo: restored.previewShowsOtherVideo))
    }

    func testPreviewEndedShowsNoticeAndClearsWhenContentReturns() {
        let ended = eval(phase: .preview, anchor: "aaa", snap: snap(ended: true, ad: false, content: false, id: "aaa", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertNil(ended.stopReason)
        XCTAssertTrue(ended.previewShowsEnded)
        XCTAssertFalse(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: ended.previewShowsEnded, previewShowsOtherVideo: ended.previewShowsOtherVideo))

        let back = eval(phase: .preview, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertFalse(back.previewShowsEnded)
        XCTAssertTrue(StreamEnd.canBeginFromPreview(previewReady: true, previewShowsEnded: back.previewShowsEnded, previewShowsOtherVideo: back.previewShowsOtherVideo))
    }

    func testContentWithoutCandidate_DoesNothing() {
        let d = eval(phase: .recording, anchor: "aaa", snap: snap(ended: false, ad: false, content: true, id: "aaa", age: 0), candidateAge: nil, ext: 0, streak: 0)
        XCTAssertNil(d.stopReason)
        XCTAssertNil(d.candidateStart)
        XCTAssertNil(d.countdownSeconds)
    }
}
