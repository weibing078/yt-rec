import SwiftUI

struct SettingsView: View {
    @AppStorage(SettingsKey.outputDir) private var outputDir = ""
    @AppStorage(SettingsKey.recordHeight) private var recordHeight = 1080
    @AppStorage(SettingsKey.downloadMaxHeight) private var downloadMaxHeight = 1080
    @AppStorage(SettingsKey.downloadTrackMode) private var downloadTrackMode = "off"
    @AppStorage(SettingsKey.recordMaxHours) private var recordMaxHours = 6
    @AppStorage(SettingsKey.trashSidecarOnNative) private var trashSidecarOnNative = true
    @AppStorage(SettingsKey.monitorAlwaysOnTop) private var monitorAlwaysOnTop = false
    @AppStorage(SettingsKey.monitorAutoShow) private var monitorAutoShow = true

    var body: some View {
        // 原生 grouped Form：自動 inset-grouped 外觀＋標籤欄對齊（同系統設定/Xcode），是 macOS 設定的標準慣用法。
        Form {
            Section("輸出") {
                HStack {
                    Text("輸出資料夾")
                    Spacer(minLength: 8)
                    Text(Settings.outputRoot.path)
                        .font(.caption).foregroundStyle(.secondary)
                        .lineLimit(1).truncationMode(.middle)
                    Button("選擇…") { pickFolder() }.controlSize(.small)
                }
                Toggle("已結束影片下載到手後，把側錄暫存丟到垃圾桶", isOn: $trashSidecarOnNative)
            }

            Section("擷取畫質") {
                Picker("畫質", selection: $recordHeight) {
                    Text("1080p").tag(1080)
                    Text("720p").tag(720)
                }
                note("真正錄製的是一個藏起來的全尺寸視窗；你看到的監看小窗只是鏡像示意，縮到多小都不影響錄製畫質。")
            }

            Section("監看視窗") {
                Toggle("監看小窗總在最上層", isOn: $monitorAlwaysOnTop)
                Toggle("開錄時自動顯示監看小窗", isOn: $monitorAutoShow)
                note("監看小窗只是鏡像示意（確認有在錄、錄對內容），可自由拖移縮放。「總在最上層」在全螢幕 Premiere 時建議關閉。不想盯著時按監看小窗右上角的「隱藏」收起來，側錄照跑；要再叫回來，主視窗會出現「顯示監看預覽」。")
            }

            Section("下載軌（選用）") {
                Picker("下載原生檔", selection: $downloadTrackMode) {
                    Text("關（只側錄）").tag("off")
                    Text("自動（依直播狀態）").tag("auto")
                    Text("永遠嘗試").tag("always")
                }
                Picker("下載畫質上限", selection: $downloadMaxHeight) {
                    Text("1080p").tag(1080)
                    Text("不限制（含 4K）").tag(0)
                }
                note("長直播下載不切實際，故預設只螢幕側錄；已結束的有限長度影片用「自動」會改走下載（更快、畫質更好）。")
            }

            Section("側錄保護") {
                Picker("側錄時長上限", selection: $recordMaxHours) {
                    Text("3 小時").tag(3)
                    Text("6 小時（推薦）").tag(6)
                    Text("12 小時").tag(12)
                    Text("不限").tag(0)
                }
                note("到達上限會自動收工保存並通知。磁碟空間不足時也會自動保存收工。")
            }

            Section {
                Button("打開 Log 資料夾") {
                    let dir = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask)[0]
                        .appendingPathComponent("Logs/\(AppInfo.folderName)")
                    NSWorkspace.shared.open(dir)
                }.controlSize(.small)
            }
        }
        .formStyle(.grouped)
        .frame(width: 480, height: 600)
    }

    /// 說明文字：強制完整換行、永不截斷（修文字被吃掉）。
    private func note(_ text: String) -> some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(nil)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func pickFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.directoryURL = Settings.outputRoot
        if panel.runModal() == .OK, let url = panel.url {
            outputDir = url.path
            Settings.ensureOutputRoot()
        }
    }
}
