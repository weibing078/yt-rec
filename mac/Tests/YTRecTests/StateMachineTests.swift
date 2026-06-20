import XCTest
@testable import YTRec

/// 軌道狀態機（純邏輯）：把「哪些狀態算已定案 / 進行中」釘成迴歸網。
/// 這是 UI 是否顯示「進行中」、智慧切換是否該動作的判斷依據，改錯會連鎖出大包。
final class TrackAStatusTests: XCTestCase {
    func testSettledStates() {
        // 已定案（停止輪詢、可清理）：關閉 / 成功 / 永久失敗 / 馬拉松跳過 / 自動跳過直播 / 取消
        XCTAssertTrue(TrackAStatus.disabled.isSettled)
        XCTAssertTrue(TrackAStatus.succeeded(URL(fileURLWithPath: "/tmp/a.mp4")).isSettled)
        XCTAssertTrue(TrackAStatus.failedTerminal("私人影片").isSettled)
        XCTAssertTrue(TrackAStatus.marathonSkipped.isSettled)
        XCTAssertTrue(TrackAStatus.skippedAutoLive.isSettled)
        XCTAssertTrue(TrackAStatus.cancelled.isSettled)
    }

    func testUnsettledStates() {
        // 仍在進行（不可清理、UI 顯示忙碌）
        XCTAssertFalse(TrackAStatus.idle.isSettled)
        XCTAssertFalse(TrackAStatus.probing.isSettled)
        XCTAssertFalse(TrackAStatus.running("下載 10%").isSettled)
        XCTAssertFalse(TrackAStatus.waitingRetry(round: 3).isSettled)
    }
}

final class TrackBStatusTests: XCTestCase {
    func testActiveStates() {
        // 進行中（會收工、要保檔、磁碟/時長保護生效）
        XCTAssertTrue(TrackBStatus.preparing.isActive)
        XCTAssertTrue(TrackBStatus.previewing.isActive)      // 定位中也算進行中（SCK 在跑、監看開著）
        XCTAssertTrue(TrackBStatus.recording(mode: "1080p").isActive)
        XCTAssertTrue(TrackBStatus.finalizing.isActive)
    }

    func testInactiveStates() {
        XCTAssertFalse(TrackBStatus.idle.isActive)
        XCTAssertFalse(TrackBStatus.finished(URL(fileURLWithPath: "/tmp/b.mp4")).isActive)
        XCTAssertFalse(TrackBStatus.failed("沒權限").isActive)
        XCTAssertFalse(TrackBStatus.discarded.isActive)
    }

    func testIsRecordingToFile() {
        // 只有真的在寫檔才算「錄影中」——預覽/定位不算（選單列紅點、結束前保檔靠這個）
        XCTAssertTrue(TrackBStatus.recording(mode: "1080p").isRecordingToFile)
        XCTAssertFalse(TrackBStatus.previewing.isRecordingToFile)
        XCTAssertFalse(TrackBStatus.preparing.isRecordingToFile)
        XCTAssertFalse(TrackBStatus.finalizing.isRecordingToFile)
        XCTAssertFalse(TrackBStatus.idle.isRecordingToFile)
    }
}

/// JobViewModel.isActive = 「A 尚未定案 或 B 仍在錄」。任一條成立即算任務進行中。
final class JobActiveTests: XCTestCase {
    @MainActor
    private func makeJob() -> JobViewModel {
        let root = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("lcf-test-\(UUID().uuidString)", isDirectory: true)
        return JobViewModel(url: "https://www.youtube.com/watch?v=jNQXAC9IVRw", outputRoot: root)
    }

    @MainActor
    func testActiveWhenTrackARunning() {
        let j = makeJob()
        j.trackA = .running("下載中")
        j.trackB = .idle
        XCTAssertTrue(j.isActive)
    }

    @MainActor
    func testActiveWhenTrackBRecording() {
        let j = makeJob()
        j.trackA = .succeeded(URL(fileURLWithPath: "/tmp/a.mp4"))   // A 已定案
        j.trackB = .recording(mode: "靜音側錄")                      // 但 B 還在錄
        XCTAssertTrue(j.isActive)
    }

    @MainActor
    func testInactiveWhenBothDone() {
        let j = makeJob()
        j.trackA = .succeeded(URL(fileURLWithPath: "/tmp/a.mp4"))
        j.trackB = .discarded
        XCTAssertFalse(j.isActive)
    }

    @MainActor
    func testInactiveWhenTerminalAndFailed() {
        let j = makeJob()
        j.trackA = .failedTerminal("已下架")
        j.trackB = .failed("沒權限")
        XCTAssertFalse(j.isActive)
    }

    @MainActor
    func testPathConstruction() {
        let j = makeJob()
        // workDir 在 jobDir 底下的 .work；clipsDir 是「精華片段」
        XCTAssertEqual(j.workDir.lastPathComponent, ".work")
        XCTAssertEqual(j.workDir.deletingLastPathComponent(), j.jobDir)
        XCTAssertEqual(j.clipsDir.lastPathComponent, "精華片段")
        XCTAssertEqual(j.clipsDir.deletingLastPathComponent(), j.jobDir)
        // 建構即建出 .work 目錄（切片要立刻能寫）
        XCTAssertTrue(FileManager.default.fileExists(atPath: j.workDir.path))
        XCTAssertEqual(j.videoID, "jNQXAC9IVRw")
        try? FileManager.default.removeItem(at: j.jobDir)
    }
}
