import XCTest
import CoreGraphics
@testable import YTRec

/// 設定預設值（PRD §5.3）。Settings 讀 UserDefaults.standard，這裡先快照再清空，跑完還原，
/// 避免污染（測試 runner 的 defaults domain 與正式 App 不同，但仍保持乾淨）。
final class SettingsTests: XCTestCase {
    private let keys = [
        SettingsKey.recordHeight, SettingsKey.downloadMaxHeight, SettingsKey.downloadTrackMode,
        SettingsKey.recordMaxHours, SettingsKey.trashSidecarOnNative, SettingsKey.outputDir,
        SettingsKey.monitorAlwaysOnTop, SettingsKey.monitorAutoShow,
    ]
    private var saved: [String: Any] = [:]

    override func setUp() {
        super.setUp()
        for k in keys {
            if let v = UserDefaults.standard.object(forKey: k) { saved[k] = v }
            UserDefaults.standard.removeObject(forKey: k)
        }
    }

    override func tearDown() {
        for k in keys {
            UserDefaults.standard.removeObject(forKey: k)
            if let v = saved[k] { UserDefaults.standard.set(v, forKey: k) }
        }
        super.tearDown()
    }

    func testDefaultsWhenUnset() {
        XCTAssertEqual(Settings.recordMaxHours, 6)                                  // 預設 6 小時上限
        XCTAssertEqual(Settings.downloadMaxHeight, 1080)                            // 預設 1080p
        XCTAssertEqual(Settings.downloadTrackMode, .off)                            // D2：預設只螢幕側錄、不下載
        XCTAssertTrue(Settings.trashSidecarOnNative)                               // 預設原生檔到手丟垃圾桶
        XCTAssertEqual(Settings.recordSize, CGSize(width: 1920, height: 1080))      // 預設 1080p 側錄
        XCTAssertEqual(Settings.recordBitrate, 12_000_000)
        XCTAssertFalse(Settings.monitorAlwaysOnTop)                                 // 預設不置頂（避免擋全螢幕 Premiere）
        XCTAssertTrue(Settings.monitorAutoShow)                                     // 預設開錄時自動顯示監看視窗
    }

    func test720pSettings() {
        UserDefaults.standard.set(720, forKey: SettingsKey.recordHeight)
        XCTAssertEqual(Settings.recordSize, CGSize(width: 1280, height: 720))
        XCTAssertEqual(Settings.recordBitrate, 6_000_000)
    }

    func testDownloadTrackModeParsing() {
        UserDefaults.standard.set("auto", forKey: SettingsKey.downloadTrackMode)
        XCTAssertEqual(Settings.downloadTrackMode, .auto)
        UserDefaults.standard.set("always", forKey: SettingsKey.downloadTrackMode)
        XCTAssertEqual(Settings.downloadTrackMode, .always)
        // 無法解析的值回退到預設 .off
        UserDefaults.standard.set("garbage", forKey: SettingsKey.downloadTrackMode)
        XCTAssertEqual(Settings.downloadTrackMode, .off)
    }

    func testMonitorTogglesCanBeChanged() {
        UserDefaults.standard.set(true, forKey: SettingsKey.monitorAlwaysOnTop)
        XCTAssertTrue(Settings.monitorAlwaysOnTop)
        UserDefaults.standard.set(false, forKey: SettingsKey.monitorAutoShow)
        XCTAssertFalse(Settings.monitorAutoShow)
    }

    func testUnlimitedOptions() {
        UserDefaults.standard.set(0, forKey: SettingsKey.recordMaxHours)
        XCTAssertEqual(Settings.recordMaxHours, 0)        // 0 = 不限時長
        UserDefaults.standard.set(0, forKey: SettingsKey.downloadMaxHeight)
        XCTAssertEqual(Settings.downloadMaxHeight, 0)     // 0 = 畫質不限制
    }

    func testTrashSidecarCanBeDisabled() {
        UserDefaults.standard.set(false, forKey: SettingsKey.trashSidecarOnNative)
        XCTAssertFalse(Settings.trashSidecarOnNative)     // 改成保留側錄檔
    }

    /// 命名單一事實來源：資料夾名 ASCII＋無空格（跨任何檔案系統安全），
    /// 且預設輸出根真的長在這個名字底下（防止哪天有人改了常數卻漏改路徑組裝）。
    func testFolderNameIsFilesystemSafeAndIsSingleSource() {
        XCTAssertFalse(AppInfo.folderName.contains(" "), "資料夾名不可有空格（腳本/shell 安全）")
        XCTAssertTrue(AppInfo.folderName.allSatisfy { $0.isASCII }, "資料夾名須純 ASCII（大小寫敏感磁碟安全）")
        XCTAssertEqual(Settings.outputRoot.lastPathComponent, AppInfo.folderName)  // 預設輸出根 = 常數
    }
}
