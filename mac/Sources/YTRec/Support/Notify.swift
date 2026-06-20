import Foundation
import UserNotifications

/// 系統通知。`swift run`（無 .app bundle）時 UNUserNotificationCenter 會 crash，
/// 所以先檢查 bundle 是否存在。
enum Notify {
    private static var available: Bool {
        Bundle.main.bundleIdentifier != nil && Bundle.main.bundlePath.hasSuffix(".app")
    }
    private static var authRequested = false

    static func requestAuthIfNeeded() {
        guard available, !authRequested else { return }
        authRequested = true
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
    }

    static func post(title: String, body: String) {
        Log.info("notify", "\(title)：\(body)")
        guard available else { return }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        let req = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(req)
    }
}
