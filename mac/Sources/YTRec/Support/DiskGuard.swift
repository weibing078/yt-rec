import Foundation

/// 磁碟保護（純邏輯，可測試）。閾值以十進位 GB 計，對齊 Finder 顯示。
enum DiskGuard {
    static let gb = 1_000_000_000.0

    /// 啟動側錄前的判定
    enum PreCheck: Equatable {
        case ok                 // 充足
        case warn(Int64)        // 偏低，警告後可續錄
        case refuse(Int64)      // 不足，拒絕啟動側錄（下載軌仍可跑）
    }

    static func preCheck(freeBytes: Int64, warnGB: Double = 15, refuseGB: Double = 8) -> PreCheck {
        let free = Double(freeBytes) / gb
        if free < refuseGB { return .refuse(freeBytes) }
        if free < warnGB { return .warn(freeBytes) }
        return .ok
    }

    /// 側錄中每隔一段時間檢查：低於門檻就收工保檔
    static func shouldStopRecording(freeBytes: Int64, stopGB: Double = 10) -> Bool {
        Double(freeBytes) / gb < stopGB
    }

    /// 可注入版（測試用）：resolver 回 nil＝查不到 → .max（不誤擋）。
    static func freeBytes(resolver: () -> Int64?) -> Int64 {
        resolver() ?? .max
    }

    /// 查詢某路徑所在磁碟的可用空間。查不到時回傳 .max（不誤擋）。
    static func freeBytes(at url: URL) -> Int64 {
        freeBytes {
            let keys: Set<URLResourceKey> = [.volumeAvailableCapacityForImportantUsageKey]
            if let v = try? url.resourceValues(forKeys: keys),
               let bytes = v.volumeAvailableCapacityForImportantUsage {
                return Int64(bytes)
            }
            return nil
        }
    }
}
