import SwiftUI
import AppKit

@main
struct YTRecApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var app = AppState.shared

    var body: some Scene {
        // 主視窗：一般視窗 App（Dock 有圖示、開機自動顯示），裝原本的主面板
        Window(AppInfo.displayName, id: "main") {
            MenuPopoverView()
                .environmentObject(app)
        }
        .windowResizability(.contentSize)

        Window("預覽", id: "clipper") {
            ClipperWindow()
                .environmentObject(app)
        }
        .defaultSize(width: 980, height: 700)
        .windowResizability(.contentSize)

        // 原生「設定」視窗：免費取得 ⌘, 與標準偏好設定行為
        // （用 SwiftUI. 限定，避免和本專案的 Settings 設定結構撞名）
        SwiftUI.Settings {
            SettingsView()
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var isTerminating = false
    // 每次「啟動」重新提醒一次就好——不可持久化，否則只會在這台機器提醒一輩子一次，
    // 之後每次關窗仍在背景錄都靜默（正是這提醒要防的情況）。（修審查發現）
    private var mainCloseNoticeShown = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)     // 一般 App：Dock 有圖示
        Log.info("app", "\(AppInfo.displayName) 啟動 v1.0 (\(ProcessInfo.processInfo.operatingSystemVersionString))")
        _ = ProcessInfo.processInfo.beginActivity(options: [.userInitiated, .idleSystemSleepDisabled],
                                                  reason: "\(AppInfo.displayName) capture")
        NotificationCenter.default.addObserver(self, selector: #selector(mainWindowWillClose(_:)),
                                               name: NSWindow.willCloseNotification, object: nil)
    }

    /// 第一次「關了主視窗但還在背景錄」時提醒一次：錄影沒停、監看小窗仍在。
    /// 視窗通知一律在主緒發出，故標 @MainActor 以安全存取 AppState。
    @MainActor @objc private func mainWindowWillClose(_ note: Notification) {
        guard !isTerminating,
              let win = note.object as? NSWindow, win.title == AppInfo.displayName,
              AppState.shared.isRecording,
              !mainCloseNoticeShown else { return }
        mainCloseNoticeShown = true
        Notify.post(title: "側錄仍在背景進行中",
                    body: "主視窗關閉了，但錄影還在背景進行。要叫回視窗點 Dock 圖示；要停止可在監看小窗上按停止鈕。")
    }

    /// 關掉主視窗不退出——側錄可能還在背景跑（監看小窗仍在）；要退出走 ⌘Q。
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    /// 點 Dock 圖示：把主視窗叫回來。主視窗可關（背景續錄），這是選單列移除後唯一的重開路徑。
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        MainActor.assumeIsolated {
            if let reopen = AppState.shared.reopenMainWindow {
                reopen()
            } else {
                // closure 還沒注入（極早期／狀態還原）→ 退而求其次：把既有主視窗帶到前景。
                NSApp.activate(ignoringOtherApps: true)
                NSApp.windows.first(where: { $0.title == AppInfo.displayName })?.makeKeyAndOrderFront(nil)
            }
        }
        return true
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        let state = AppState.shared
        guard state.isRecording else { isTerminating = true; return .terminateNow }
        // 錄影中誤關 = 丟掉錄不回來的直播。先確認，再保存收工。
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "還在錄影中，確定要結束嗎？"
        alert.informativeText = "結束會停止錄影並保存目前進度。"
        alert.addButton(withTitle: "繼續錄影")
        alert.addButton(withTitle: "結束並保存")
        NSApp.activate(ignoringOtherApps: true)
        // 取消（繼續錄影）→ 不終止，isTerminating 維持 false。
        guard alert.runModal() == .alertSecondButtonReturn else { return .terminateCancel }
        isTerminating = true
        Task { @MainActor in
            await state.emergencyFinalize()
            NSApp.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }
}
