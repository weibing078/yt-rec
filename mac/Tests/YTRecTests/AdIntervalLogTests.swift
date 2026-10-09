import XCTest
@testable import YTRec

final class AdIntervalLogTests: XCTestCase {
    func testZeroSpansRenderNothing() {
        let log = AdIntervalLog()
        log.observe(ad: false, fileSec: 10)
        log.closeOpen(fileSec: 20)
        XCTAssertNil(log.render())
    }

    func testOneSpanUsesFileClockAndHeader() {
        let log = AdIntervalLog()
        log.observe(ad: false, fileSec: 0)
        log.observe(ad: true, fileSec: 62)
        log.observe(ad: true, fileSec: 63)
        log.observe(ad: false, fileSec: 100)
        XCTAssertEqual(log.render(), "每秒偵測，誤差約 ±1-2 秒\n00:01:02–00:01:40\n")
    }

    func testStillOpenAtStopClosesOnTheLastSecond() {
        let log = AdIntervalLog()
        log.observe(ad: true, fileSec: 5)
        log.closeOpen(fileSec: 8)
        XCTAssertEqual(log.render(), "每秒偵測，誤差約 ±1-2 秒\n00:00:05–00:00:08\n")
    }

    func testSidecarDropsMp4Extension() {
        let mp4 = URL(fileURLWithPath: "/tmp/out/側錄_標題.mp4")
        XCTAssertEqual(AdIntervalLog.sidecarURL(beside: mp4).lastPathComponent, "側錄_標題.廣告時段.txt")
    }
}
