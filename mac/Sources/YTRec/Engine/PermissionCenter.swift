import Foundation
import SwiftUI
import CoreGraphics
import UserNotifications
import AppKit

/// 單一權限的狀態
enum PermState: Equatable {
    case granted        // 已授權
    case missing        // 未授權
    case checking       // 檢測中
    case unknown        // 尚未檢測

    /// 螢幕錄製走同步旗標：要嘛授權、要嘛沒，無中間態。
    static func fromScreenPreflight(_ granted: Bool) -> PermState { granted ? .granted : .missing }

    /// 通知授權狀態映射：provisional/ephemeral 也算 granted（不擋，只影響提醒）；
    /// notDetermined＝還沒問過（待檢測）；denied/其他＝未授權。
    static func fromNotify(_ status: UNAuthorizationStatus) -> PermState {
        switch status {
        case .authorized, .provisional, .ephemeral: return .granted
        case .notDetermined:                        return .unknown
        default:                                    return .missing
        }
    }
}

/// 兩個系統權限的檢測中心（v2 D4：砍掉行程音訊攔截後，SCK 音訊屬「螢幕錄製」範疇，
/// 不再需要獨立的「系統音訊錄製」權限）。
/// - 螢幕錄製：`CGPreflightScreenCaptureAccess()` 直接判定（含 SCK 音訊）
/// - 通知：`UNUserNotificationCenter` 授權狀態
@MainActor
final class PermissionCenter: ObservableObject {
    @Published private(set) var screen: PermState = .unknown
    @Published private(set) var notify: PermState = .unknown

    /// 側錄能不能跑：螢幕錄製是硬需求
    var screenReady: Bool { screen == .granted }

    // MARK: - 檢測

    func refreshQuickStates() {
        screen = PermState.fromScreenPreflight(CGPreflightScreenCaptureAccess())
        UNUserNotificationCenter.current().getNotificationSettings { s in
            Task { @MainActor in self.notify = PermState.fromNotify(s.authorizationStatus) }
        }
    }

    /// 重新檢測全部（v2 不再有音訊功能性實測，純讀系統旗標）。
    func runSelfCheck() {
        refreshQuickStates()
    }

    // MARK: - 授權（開對應的系統設定面板）

    func authorizeScreen() {
        CGRequestScreenCaptureAccess()
        openSettings("Privacy_ScreenCapture")
    }

    func authorizeNotify() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in
            Task { @MainActor in self.refreshQuickStates() }
        }
        openSettings("Privacy_Notifications")
    }

    private func openSettings(_ anchor: String) {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(anchor)") {
            NSWorkspace.shared.open(url)
        }
    }
}
