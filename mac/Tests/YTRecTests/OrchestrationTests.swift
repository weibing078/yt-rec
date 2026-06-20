import XCTest
@testable import YTRec

/// L1：把編排決策從 AppState 的 @MainActor 副作用裡抽出來後，逐條釘住。
/// 對應測試計畫 R1（A 成功 vs 定位）、R2（保護閘只在寫檔中跑）、R3（守衛順序）、R6（環境文案）。

final class StartJobGuardTests: XCTestCase {
    func testEmptyIgnored() {
        XCTAssertEqual(AppState.startJobGuard(trimmedURL: "", hasActiveJob: false), .ignore)
    }
    func testNonYouTube() {
        XCTAssertEqual(AppState.startJobGuard(trimmedURL: "https://vimeo.com/123", hasActiveJob: false), .notYouTube)
    }
    func testValidStarts() {
        XCTAssertEqual(AppState.startJobGuard(trimmedURL: "https://www.youtube.com/watch?v=dQw4w9WgXcQ", hasActiveJob: false), .start)
    }
    func testActiveJobBlocksEvenValid() {
        // 有進行中任務 → 一律 ignore，不覆蓋進行中的 job
        XCTAssertEqual(AppState.startJobGuard(trimmedURL: "https://www.youtube.com/watch?v=dQw4w9WgXcQ", hasActiveJob: true), .ignore)
    }
    func testActiveJobBlocksJunkWithoutPollutingState() {
        // R3：錄製中貼垃圾字串 → ignore（不是 notYouTube），避免污染頂部警告
        XCTAssertEqual(AppState.startJobGuard(trimmedURL: "亂貼的字串", hasActiveJob: true), .ignore)
    }
}

final class SidecarEffectTests: XCTestCase {
    func testRecordingStopsAndFinalizes() {
        XCTAssertEqual(AppState.sidecarEffectOnNativeSuccess(recordingToFile: true, positioning: false), .stopAndFinalize)
    }
    func testPositioningKeptOpen() {
        // R1：A 搶先成功時 B 還在定位（未寫檔）→ 不停、不關監看
        XCTAssertEqual(AppState.sidecarEffectOnNativeSuccess(recordingToFile: false, positioning: true), .keepPositioning)
    }
    func testNeitherLeavesAsIs() {
        // 例如片段下載：B=idle，A 成功不需動 B
        XCTAssertEqual(AppState.sidecarEffectOnNativeSuccess(recordingToFile: false, positioning: false), .leaveAsIs)
    }
}

final class DurationLimitTests: XCTestCase {
    func testExactSixHoursTriggers() {
        XCTAssertFalse(AppState.shouldStopForDuration(elapsedSec: 21599.9, maxHours: 6))
        XCTAssertTrue(AppState.shouldStopForDuration(elapsedSec: 21600.0, maxHours: 6))   // 剛好 6h00m00s
        XCTAssertTrue(AppState.shouldStopForDuration(elapsedSec: 21600.001, maxHours: 6))
    }
    func testThreeHours() {
        XCTAssertFalse(AppState.shouldStopForDuration(elapsedSec: 10799.9, maxHours: 3))
        XCTAssertTrue(AppState.shouldStopForDuration(elapsedSec: 10800.0, maxHours: 3))
    }
    func testUnlimitedNeverFires() {
        XCTAssertFalse(AppState.shouldStopForDuration(elapsedSec: 0, maxHours: 0))
        XCTAssertFalse(AppState.shouldStopForDuration(elapsedSec: 43200, maxHours: 0))      // 12h
        XCTAssertFalse(AppState.shouldStopForDuration(elapsedSec: 1_000_000, maxHours: 0))
    }
}

final class DiskCheckThrottleTests: XCTestCase {
    func testThrottleBoundary() {
        XCTAssertFalse(AppState.shouldRunDiskCheck(elapsedSec: 59.9, lastCheckSec: 0))
        XCTAssertTrue(AppState.shouldRunDiskCheck(elapsedSec: 60.0, lastCheckSec: 0))       // 剛好 60s
        XCTAssertTrue(AppState.shouldRunDiskCheck(elapsedSec: 61, lastCheckSec: 0))
    }
    func testThrottleAfterUpdate() {
        XCTAssertFalse(AppState.shouldRunDiskCheck(elapsedSec: 119, lastCheckSec: 60))
        XCTAssertTrue(AppState.shouldRunDiskCheck(elapsedSec: 120, lastCheckSec: 60))
    }
    func testEarlyRecordingNoCheck() {
        // 剛開錄前 60 秒不查（lastCheck=0）
        XCTAssertFalse(AppState.shouldRunDiskCheck(elapsedSec: 0, lastCheckSec: 0))
        XCTAssertFalse(AppState.shouldRunDiskCheck(elapsedSec: 30, lastCheckSec: 0))
    }
}

final class EndedStopScheduleTests: XCTestCase {
    func testSchedulesWhenRecordingAndFresh() {
        XCTAssertTrue(AppState.shouldScheduleEndedStop(event: "ended", recordingToFile: true, alreadyScheduled: false))
    }
    func testNotWhilePositioning() {
        // 預覽/定位階段倒帶到 DVR 邊界的假 ended 不收工
        XCTAssertFalse(AppState.shouldScheduleEndedStop(event: "ended", recordingToFile: false, alreadyScheduled: false))
    }
    func testNotIfAlreadyScheduled() {
        XCTAssertFalse(AppState.shouldScheduleEndedStop(event: "ended", recordingToFile: true, alreadyScheduled: true))
    }
    func testIgnoresOtherEvents() {
        XCTAssertFalse(AppState.shouldScheduleEndedStop(event: "playing", recordingToFile: true, alreadyScheduled: false))
    }
}

final class StopNotificationTests: XCTestCase {
    func testEachReasonHasDistinctText() {
        XCTAssertEqual(AppState.stopNotification(reason: .durationLimit, fileName: "a.mp4")?.title, "已達側錄時長上限，自動保存收工")
        XCTAssertEqual(AppState.stopNotification(reason: .lowDisk, fileName: "a.mp4")?.title, "磁碟空間不足，側錄已自動保存收工")
        XCTAssertEqual(AppState.stopNotification(reason: .playerEnded, fileName: "a.mp4")?.title, "螢幕側錄已保存")
        XCTAssertEqual(AppState.stopNotification(reason: .userStopped, fileName: "a.mp4")?.title, "螢幕側錄已保存")
        XCTAssertEqual(AppState.stopNotification(reason: .durationLimit, fileName: "a.mp4")?.body, "a.mp4")
    }
    func testNativeSucceededHasNoStopNotification() {
        XCTAssertNil(AppState.stopNotification(reason: .nativeSucceeded, fileName: "a.mp4"))
    }
}

final class TerminationActionTests: XCTestCase {
    func testRecordingFinalizes() {
        XCTAssertEqual(AppState.terminationAction(isRecording: true, isPositioning: false), .finalizeAndSave)
    }
    func testPositioningCancels() {
        XCTAssertEqual(AppState.terminationAction(isRecording: false, isPositioning: true), .cancelPositioning)
    }
    func testIdleCancelsDownloadOnly() {
        XCTAssertEqual(AppState.terminationAction(isRecording: false, isPositioning: false), .cancelDownloadOnly)
    }
}

final class EnvironmentMessageTests: XCTestCase {
    func testMissingToolsTakePriorityOverPermission() {
        let msg = AppState.environmentMessage(missingTools: ["yt-dlp"], screenGranted: false)
        XCTAssertEqual(msg, "缺少元件：yt-dlp。請重新安裝 App。")   // 缺元件優先、且不提權限
    }
    func testMultipleToolsJoined() {
        let msg = AppState.environmentMessage(missingTools: ["yt-dlp", "ffmpeg"], screenGranted: true)
        XCTAssertEqual(msg, "缺少元件：yt-dlp, ffmpeg。請重新安裝 App。")
    }
    func testPermissionMessageWhenNotGranted() {
        let msg = AppState.environmentMessage(missingTools: [], screenGranted: false)
        XCTAssertEqual(msg, "尚未授權「螢幕與系統音訊錄音」，螢幕側錄無法運作。（勾選後請重開 App 生效）")
    }
    func testNilWhenAllGood() {
        XCTAssertNil(AppState.environmentMessage(missingTools: [], screenGranted: true))
    }
}
