import SwiftUI

/// 權限面板（v2 D4：兩件套——螢幕錄製＋通知）。SCK 音訊屬螢幕錄製範疇，不再需要獨立系統音訊權限。
struct PermissionPanelView: View {
    @StateObject private var perm = PermissionCenter()
    @EnvironmentObject private var app: AppState
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            Divider()

            permRow(
                icon: "rectangle.dashed.badge.record",
                title: "螢幕與系統音訊錄音",
                why: "螢幕側錄靠它擷取監看視窗的畫面與聲音。缺了側錄就無法運作。",
                state: perm.screen,
                detail: perm.screen == .missing ? "未授權——無法螢幕側錄。" : nil,
                authorize: { perm.authorizeScreen() }
            )

            permRow(
                icon: "bell.badge",
                title: "通知",
                why: "側錄收工、磁碟不足、達時長上限、精華匯出等事件會用系統通知提醒你。",
                state: perm.notify,
                detail: nil,
                authorize: { perm.authorizeNotify() }
            )

            Divider()
            footer
        }
        .padding(20)
        .frame(width: 470)
        .onAppear { perm.refreshQuickStates() }
    }

    // MARK: - Header

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 2) {
                Text("權限").font(.headline)
                Text(overallSummary).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button {
                perm.runSelfCheck()
                app.checkEnvironment()   // 同步主面板頂部警告，授權後不殘留
            } label: {
                HStack(spacing: 5) {
                    Image(systemName: "arrow.clockwise")
                    Text("重新檢查")
                }
            }
            .keyboardShortcut("r", modifiers: [.command])
            .help("重新讀取「螢幕與系統音訊錄音」與通知的授權狀態")
        }
    }

    private var overallSummary: String {
        perm.screen == .granted ? "「螢幕與系統音訊錄音」已就緒，可以螢幕側錄。" : "尚缺「螢幕與系統音訊錄音」——螢幕側錄無法運作。"
    }

    // MARK: - 單列權限

    @ViewBuilder
    private func permRow(icon: String, title: String, why: String, state: PermState,
                         detail: String?, authorize: @escaping () -> Void) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon)
                .font(.title3)
                .foregroundStyle(.tint)
                .frame(width: 26)
                .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 8) {
                    Text(title).font(.subheadline).bold()
                    statusPill(state)
                }
                Text(why).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                if let detail {
                    Text(detail).font(.caption2)
                        .foregroundStyle(state == .missing ? Color.lcWarningText : Color.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }

            Spacer(minLength: 8)

            if state == .missing || state == .unknown {
                Button("前往授權", action: authorize)
                    .controlSize(.small)
            }
        }
    }

    /// 狀態標籤：永遠 icon＋文字＋顏色三者並用（不只靠顏色），色盲也分得出
    @ViewBuilder
    private func statusPill(_ state: PermState) -> some View {
        switch state {
        case .granted:
            pill("checkmark.circle.fill", "已授權", .lcSuccessText, tint: .lcSuccess)
        case .missing:
            pill("exclamationmark.triangle.fill", "未授權", .lcWarningText, tint: .lcWarning)
        case .checking:
            HStack(spacing: 4) { ProgressView().controlSize(.mini); Text("檢測中").font(.caption2) }
                .foregroundStyle(.secondary)
        case .unknown:
            pill("questionmark.circle", "待檢測", .secondary, tint: .secondary)
        }
    }

    /// 狀態膠囊：文字／圖示用對比安全色 `textColor`，底色用鮮明品牌色 `tint` 的淡淡 wash。
    /// 文字與底色分流，避免「同色文字疊同色底」對比不足（CIS §5.1 強調而非重漆）。
    private func pill(_ icon: String, _ text: String, _ textColor: Color, tint: Color) -> some View {
        HStack(spacing: 3) {
            Image(systemName: icon)
            Text(text)
        }
        .font(.caption2.bold())
        .foregroundStyle(textColor)
        .padding(.horizontal, 6).padding(.vertical, 2)
        .background(tint.opacity(0.14), in: Capsule())
        .accessibilityElement(children: .combine)
        .accessibilityLabel(text)
    }

    // MARK: - Footer

    private var footer: some View {
        HStack(spacing: 10) {
            Image(systemName: "info.circle").foregroundStyle(.secondary)
            Text("授權後若狀態沒更新，按「重新檢查」即可。macOS 每月可能要求重新確認「螢幕與系統音訊錄音」。")
                .font(.caption2).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer()
            Button("完成") { app.checkEnvironment(); dismiss() }.keyboardShortcut(.defaultAction)
        }
    }
}
