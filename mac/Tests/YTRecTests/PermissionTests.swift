import XCTest
import UserNotifications
@testable import YTRec

/// L1：權限狀態映射與「檢查權限」深連結顯示條件（R6）。
/// 都是同步純讀旗標的映射，TCC 真實授權結果仍需真機，但映射邏輯可在此釘死。

final class PermStateMappingTests: XCTestCase {
    func testScreenPreflightMapping() {
        XCTAssertEqual(PermState.fromScreenPreflight(true), .granted)
        XCTAssertEqual(PermState.fromScreenPreflight(false), .missing)
        // 螢幕走同步旗標，永遠不會是 unknown/checking
        XCTAssertNotEqual(PermState.fromScreenPreflight(false), .unknown)
    }

    func testNotifyMapping() {
        XCTAssertEqual(PermState.fromNotify(.authorized), .granted)
        XCTAssertEqual(PermState.fromNotify(.provisional), .granted)   // 不擋、只影響提醒
        // 註：.ephemeral 在 macOS 不可建構（iOS App Clip 專用），故不測；production switch 仍涵蓋它
        XCTAssertEqual(PermState.fromNotify(.notDetermined), .unknown) // 還沒問過＝待檢測
        XCTAssertEqual(PermState.fromNotify(.denied), .missing)
    }
}

final class BannerPermissionActionTests: XCTestCase {
    func testPermissionMessagesShowAction() {
        XCTAssertTrue(AppState.bannerShowsPermissionAction("尚未授權「螢幕與系統音訊錄音」，螢幕側錄無法運作。（勾選後請重開 App 生效）"))
        XCTAssertTrue(AppState.bannerShowsPermissionAction("尚未授權「螢幕與系統音訊錄音」，無法螢幕側錄。"))
    }
    func testNonPermissionMessagesHideAction() {
        XCTAssertFalse(AppState.bannerShowsPermissionAction("缺少元件：yt-dlp。請重新安裝 App。"))
        XCTAssertFalse(AppState.bannerShowsPermissionAction("這不是 YouTube 網址"))
    }
    func testEnvironmentMessagesConsistentWithBanner() {
        // 端到端一致性：environmentMessage 產出的權限訊息一定會觸發深連結；缺元件訊息一定不會
        let perm = AppState.environmentMessage(missingTools: [], screenGranted: false)!
        let missing = AppState.environmentMessage(missingTools: ["ffmpeg"], screenGranted: true)!
        XCTAssertTrue(AppState.bannerShowsPermissionAction(perm))
        XCTAssertFalse(AppState.bannerShowsPermissionAction(missing))
    }
}
