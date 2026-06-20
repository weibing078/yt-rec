import Foundation

/// App 識別常數（單一事實來源）。所有名稱只在這裡定義一次，避免字面值散落各檔導致大小寫／拼字分家。
enum AppInfo {
    /// 顯示名：使用者讀的品牌字（視窗、選單、權限說明、log 訊息）。有空格無妨——App 一律用 URL API 存取。
    static let displayName = "YT Rec"
    /// 資料夾名：檔案系統／腳本碰的（輸出、log、歷史路徑）。連字號＋純 ASCII＋固定大小寫，跨任何磁碟皆安全。
    static let folderName = "YT-Rec"
}

/// 設定（UserDefaults 單一來源；UI 用 @AppStorage 綁同一組 key）
enum SettingsKey {
    static let outputDir = "outputDir"
    static let recordHeight = "recordHeight"           // 720 / 1080（＝監看視窗內容尺寸＝擷取畫質）
    static let downloadMaxHeight = "downloadMaxHeight"  // 1080 / 0(不限)
    static let downloadTrackMode = "downloadTrackMode"  // off(關) / auto(依 live_status) / always(永遠嘗試)
    static let recordMaxHours = "recordMaxHours"        // 3 / 6 / 12 / 0(不限)
    static let trashSidecarOnNative = "trashSidecarOnNative"
    static let monitorAlwaysOnTop = "monitorAlwaysOnTop"   // 監看視窗總在最上層
    static let monitorAutoShow = "monitorAutoShow"          // 開錄時自動顯示監看視窗
}

/// 下載軌模式（D2）：螢幕側錄為主、下載為輔。
enum DownloadTrackMode: String, CaseIterable {
    case off     // 關（預設）：只螢幕側錄，永不啟動下載
    case auto    // 自動：依 live_status——已結束 VOD 才下載；進行中直播只側錄
    case always  // 永遠嘗試：不論直播或 VOD 都嘗試下載（側錄當退路；馬拉松仍會被擋）
}

struct Settings {
    static var outputRoot: URL {
        if let p = UserDefaults.standard.string(forKey: SettingsKey.outputDir), !p.isEmpty {
            return URL(fileURLWithPath: p, isDirectory: true)
        }
        return FileManager.default.urls(for: .moviesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent(AppInfo.folderName, isDirectory: true)
    }

    /// 擷取畫質＝監看視窗內容尺寸（v2 採 PRD §9 的誠實版本：畫質跟視窗內容尺寸走）。
    static var recordSize: CGSize {
        let h = UserDefaults.standard.integer(forKey: SettingsKey.recordHeight)
        return h == 720 ? CGSize(width: 1280, height: 720) : CGSize(width: 1920, height: 1080)
    }

    static var recordBitrate: Int {
        let h = UserDefaults.standard.integer(forKey: SettingsKey.recordHeight)
        return h == 720 ? 6_000_000 : 12_000_000
    }

    static var downloadMaxHeight: Int {
        let v = UserDefaults.standard.object(forKey: SettingsKey.downloadMaxHeight) as? Int
        return v ?? 1080
    }

    /// 下載軌模式（D2）。預設 .off：v2 以螢幕側錄為主，不再預設下載。
    static var downloadTrackMode: DownloadTrackMode {
        guard let raw = UserDefaults.standard.string(forKey: SettingsKey.downloadTrackMode),
              let mode = DownloadTrackMode(rawValue: raw) else { return .off }
        return mode
    }

    /// 側錄時長上限（小時）；0 = 不限
    static var recordMaxHours: Int {
        UserDefaults.standard.object(forKey: SettingsKey.recordMaxHours) as? Int ?? 6
    }

    static var trashSidecarOnNative: Bool {
        UserDefaults.standard.object(forKey: SettingsKey.trashSidecarOnNative) as? Bool ?? true
    }

    /// 監看視窗：總在最上層（預設關，避免擋住全螢幕 Premiere）
    static var monitorAlwaysOnTop: Bool {
        UserDefaults.standard.object(forKey: SettingsKey.monitorAlwaysOnTop) as? Bool ?? false
    }

    /// 監看視窗：開錄時自動顯示（預設開）
    static var monitorAutoShow: Bool {
        UserDefaults.standard.object(forKey: SettingsKey.monitorAutoShow) as? Bool ?? true
    }

    static func ensureOutputRoot() {
        try? FileManager.default.createDirectory(at: outputRoot, withIntermediateDirectories: true)
    }
}
