import Foundation

/// 軌道 A 的下載策略（純邏輯，可測試）。順序由 live_status 決定（PRD §4）。
enum DownloadStrategy: Equatable {
    case fromStart   // --live-from-start：直播從頭抓
    case normal      // 一般下載
    case degraded    // 降到 720p h264 保命

    /// 該策略附加到 yt-dlp 的參數
    func arguments(liveStatus: String) -> [String] {
        switch self {
        case .fromStart: return ["--live-from-start"] + (liveStatus == "is_upcoming" ? ["--wait-for-video", "60"] : [])
        case .normal:    return []
        case .degraded:  return ["-S", "res:720,vcodec:h264"]
        }
    }
}

/// yt-dlp 輸出解析（純邏輯，可測試）
enum YtDlpParse {

    /// 依 live_status 決定策略嘗試順序：直播中優先「從頭抓」；已結束優先「一般下載」。
    static func strategyOrder(liveStatus: String) -> [DownloadStrategy] {
        switch liveStatus {
        case "post_live", "was_live", "not_live":
            return [.normal, .fromStart, .degraded]
        default:   // is_live / is_upcoming / NA / 探測不到 → 直播優先
            return [.fromStart, .normal, .degraded]
        }
    }

    /// 不可挽回的失敗（影片被隱藏/刪除/權限不足）→ 停止輪詢
    static func isTerminalFailure(_ output: String) -> Bool {
        let lower = output.lowercased()
        let patterns = [
            "private video", "this video is private",
            "video unavailable", "removed by the uploader",
            "account associated with this video has been terminated",
            "members-only", "join this channel",
            "sign in to confirm your age", "age-restricted",
            "who has blocked it in your country",
            "sign in to confirm you", // 機器人驗證
        ]
        return patterns.contains { lower.contains($0) }
    }

    /// 馬拉松直播判定（純邏輯，可測試）：仍在直播且已開播逾門檻 → 只側錄，不啟動下載軌。
    /// 對永不結束的直播（24hr 台），「從頭抓」會試圖下載數天內容。
    static func isMarathon(liveStatus: String, releaseTimestamp: Double?, now: Double,
                           thresholdHours: Double = 4) -> Bool {
        guard liveStatus == "is_live", let start = releaseTimestamp else { return false }
        return (now - start) > thresholdHours * 3600
    }

    /// D2 自動模式：是否該下載。只有「已結束、長度有限的 VOD」才下載；
    /// 進行中直播（is_live/is_upcoming）或探測不到（NA）→ 只螢幕側錄，不下載。
    static func autoShouldDownload(liveStatus: String) -> Bool {
        switch liveStatus {
        case "post_live", "was_live", "not_live": return true
        default: return false   // is_live / is_upcoming / NA / 未知
        }
    }

    /// 從進度行抽出人話狀態，例如「下載 45.2%・3.4MiB/s」
    static func progressText(_ line: String) -> String? {
        guard line.hasPrefix("[download]") else { return nil }
        let pct = firstMatch(line, pattern: #"(\d{1,3}(?:\.\d+)?)%"#)
        // [KMGT] 設為選填：極慢時 yt-dlp 印純 "B/s"（如 0.50B/s），不該漏顯示速度
        let speed = firstMatch(line, pattern: #"at\s+([0-9.]+\s*[KMGT]?i?B/s)"#)
        let frag = firstMatch(line, pattern: #"\(frag (\d+)"#)
        var parts: [String] = []
        if let pct { parts.append("下載 \(pct)%") }
        if let frag { parts.append("第 \(frag) 段") }
        if let speed { parts.append(speed) }
        return parts.isEmpty ? nil : parts.joined(separator: "・")
    }

    static func firstMatch(_ s: String, pattern: String) -> String? {
        guard let re = try? NSRegularExpression(pattern: pattern),
              let m = re.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)),
              m.numberOfRanges > 1,
              let r = Range(m.range(at: 1), in: s) else { return nil }
        return String(s[r])
    }

    struct ProbeInfo {
        let id: String
        let title: String
        let liveStatus: String        // is_live / was_live / post_live / not_live / is_upcoming / NA
        let releaseTimestamp: Double?  // 直播開始的 epoch 秒（取不到為 nil）
    }

    static func parseProbe(_ line: String) -> ProbeInfo? {
        let parts = line.components(separatedBy: "\t")
        guard parts.count >= 4, parts[0].count == 11 else {
            // 3 欄（無時間戳）相容；其餘（id 非 11 碼/欄位不足/空）→ nil
            if parts.count == 3, parts[0].count == 11 {
                return ProbeInfo(id: parts[0], title: parts[1], liveStatus: parts[2], releaseTimestamp: nil)
            }
            return nil
        }
        // 後兩欄固定＝live_status、release_timestamp；中間多出來的都算標題
        // （%(title)s 內含 tab 時欄位會位移，從尾端取才不會把標題碎片誤當 live_status）。
        let ts = Double(parts[parts.count - 1])   // "NA" → nil
        let liveStatus = parts[parts.count - 2]
        let title = parts[1..<(parts.count - 2)].joined(separator: "\t")
        return ProbeInfo(id: parts[0], title: title, liveStatus: liveStatus, releaseTimestamp: ts)
    }

    /// 探測後的編排決策（純函式）：把 start() 裡「馬拉松／只抓某段／自動模式跳過／續跑」四個早退判斷
    /// 抽出來，讓「側錄為主、下載為輔」的核心決策有單元測試守門（順序＝start() 內的順序）。
    enum ProbeOutcome: Equatable { case marathon, section, skippedAutoLive, proceed }
    static func decideOutcome(liveStatus: String, releaseTimestamp: Double?, now: Double,
                              autoMode: Bool, hasSection: Bool) -> ProbeOutcome {
        if isMarathon(liveStatus: liveStatus, releaseTimestamp: releaseTimestamp, now: now) { return .marathon }
        if hasSection { return .section }
        if autoMode, !autoShouldDownload(liveStatus: liveStatus) { return .skippedAutoLive }
        return .proceed
    }
}

/// 軌道 A：yt-dlp 智慧輪詢下載引擎
final class YtDlpEngine {
    enum Outcome {
        case success(URL)
        case terminalFailure(String)
        case marathon                // 馬拉松直播：不下載，只交給側錄軌
        case skippedAutoLive         // D2 自動模式：偵測為進行中直播，依設定不下載，只側錄
        case cancelled
    }

    private let runnerLock = NSLock()
    private var _runner: ProcessRunner?
    /// 目前的子程序執行器：start() 的 async 任務指派、cancel() 在 main 讀取——加鎖避免跨緒競爭（修審查 P2）。
    private var runner: ProcessRunner? {
        get { runnerLock.lock(); defer { runnerLock.unlock() }; return _runner }
        set { runnerLock.lock(); defer { runnerLock.unlock() }; _runner = newValue }
    }
    private var stopped = false
    private let queue = DispatchQueue(label: "lcf.trackA")

    var onStatus: ((String) -> Void)?          // 進度文字（任意執行緒）
    var onProbe: ((YtDlpParse.ProbeInfo) -> Void)?

    func cancel() {
        queue.sync { stopped = true }
        runner?.cancel()
    }

    private var isStopped: Bool { queue.sync { stopped } }

    /// 主流程：探測 → 多策略嘗試 → 30 秒輪詢，直到成功 / 永久失敗 / 取消
    /// - autoMode（D2）：true＝只下載已結束 VOD，偵測為進行中直播就回 `.skippedAutoLive`（只側錄）。
    /// - section：非 nil＝「只抓某段」（yt-dlp `--download-sections "*START-END"`），單次下載、不輪詢、不走直播策略。
    func start(url: String, outputDir: URL, maxHeight: Int, autoMode: Bool = false, section: String? = nil) async -> Outcome {
        guard let ytdlp = BinaryLocator.url(for: .ytdlp) else {
            return .terminalFailure("找不到 yt-dlp 執行檔")
        }
        let ffmpegDir = BinaryLocator.url(for: .ffmpeg)?.deletingLastPathComponent().path

        var base = ["--newline", "--no-colors", "--no-playlist", "--ignore-config",
                    "--retries", "10", "--fragment-retries", "60",
                    "-N", "4",
                    "--merge-output-format", "mp4",
                    "-o", outputDir.path + "/%(title).70B [%(id)s].%(ext)s"]
        if let ffmpegDir { base += ["--ffmpeg-location", ffmpegDir] }
        let sort = maxHeight > 0 ? "res:\(maxHeight),vcodec:h264,acodec:m4a" : "res,vcodec:h264,acodec:m4a"

        // 探測（拿 id/title/live 狀態 + 開播時間，順便驗證網址）
        var liveStatus = "NA"
        var releaseTs: Double? = nil
        let probeRunner = ProcessRunner()
        runner = probeRunner
        onStatus?("正在分析直播狀態…")
        let probeArgs = ["--no-warnings", "--no-playlist", "--skip-download", "--ignore-config",
                         "--print", "%(id)s\t%(title)s\t%(live_status)s\t%(release_timestamp)s", url]
        let probeResult = await probeRunner.run(executable: ytdlp, arguments: probeArgs)
        if isStopped { return .cancelled }
        if probeResult.exitCode == 0,
           let info = probeResult.output.components(separatedBy: "\n").compactMap({ YtDlpParse.parseProbe($0) }).last {
            liveStatus = info.liveStatus
            releaseTs = info.releaseTimestamp
            onProbe?(info)
            Log.info("trackA", "探測成功 id=\(info.id) live_status=\(info.liveStatus) release=\(info.releaseTimestamp.map { String($0) } ?? "NA")")
        } else if YtDlpParse.isTerminalFailure(probeResult.output) {
            return .terminalFailure(Self.friendlyFailure(probeResult.output))
        } else {
            Log.info("trackA", "探測失敗（將直接盲試）：\(probeResult.output.suffix(300))")
        }

        // 探測後的編排決策（純函式 decideOutcome 守門；順序＝馬拉松→只抓某段→自動模式跳過→續跑）
        switch YtDlpParse.decideOutcome(liveStatus: liveStatus, releaseTimestamp: releaseTs,
                                        now: Date().timeIntervalSince1970,
                                        autoMode: autoMode, hasSection: section != nil) {
        case .marathon:
            Log.info("trackA", "判定為馬拉松直播，下載軌不啟動")
            return .marathon
        case .section:
            // decideOutcome 回 .section ⟺ hasSection ⟺ section != nil，故此處必有值。
            return await downloadSection(ytdlp: ytdlp, base: base, sort: sort, section: section!,
                                         url: url, outputDir: outputDir)
        case .skippedAutoLive:
            Log.info("trackA", "自動模式：live_status=\(liveStatus) 非已結束 VOD，下載軌不啟動，僅側錄")
            return .skippedAutoLive
        case .proceed:
            break
        }

        // 策略嘗試順序由 live_status 決定（純函式可測，見 YtDlpParse.strategyOrder）
        let order = YtDlpParse.strategyOrder(liveStatus: liveStatus)

        var round = 0
        while !isStopped {
            round += 1
            for (i, strat) in order.enumerated() {
                if isStopped { return .cancelled }
                let label = ["方案A(原始串流)", "方案B(就緒檔)", "方案C(降畫質)"][min(i, 2)]
                onStatus?("第 \(round) 輪・\(label) 嘗試中…")
                Log.info("trackA", "round=\(round) strategy=\(label)")

                let r = ProcessRunner()
                runner = r
                let startTime = Date()
                let extra = strat.arguments(liveStatus: liveStatus)
                var args = base + ["-S", sort] + extra + [url]
                if strat == .degraded {   // 降畫質策略用自己的排序
                    args = base + extra + [url]
                }
                let result = await r.run(executable: ytdlp, arguments: args) { [weak self] line in
                    if let t = YtDlpParse.progressText(line) {
                        self?.onStatus?("\(label)・\(t)")
                    }
                }
                if isStopped || result.wasCancelled { return .cancelled }

                if result.exitCode == 0 {
                    if let file = Self.newestVideoFile(in: outputDir, newerThan: startTime.addingTimeInterval(-5)) {
                        Log.info("trackA", "下載成功：\(file.lastPathComponent)")
                        return .success(file)
                    }
                    Log.error("trackA", "exit 0 但找不到輸出檔，視為可重試")
                }
                if YtDlpParse.isTerminalFailure(result.output) {
                    return .terminalFailure(Self.friendlyFailure(result.output))
                }
                Log.info("trackA", "策略失敗 exit=\(result.exitCode)：\(result.output.suffix(400))")
            }
            // 整輪都失敗 → 智慧輪詢：等 30 秒
            onStatus?("素材尚未就緒，30 秒後重試（已輪詢 \(round) 輪）")
            for _ in 0..<30 {
                if isStopped { return .cancelled }
                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
        }
        return .cancelled
    }

    /// 「只抓某段」：用 `--download-sections` 單次下載指定區間（keyframe 對齊、不重編碼、不輪詢）。
    private func downloadSection(ytdlp: URL, base: [String], sort: String,
                                 section: String, url: String, outputDir: URL) async -> Outcome {
        onStatus?("下載片段中…")
        Log.info("trackA", "只抓某段：\(section)")
        let r = ProcessRunner()
        runner = r
        let startTime = Date()
        let args = base + ["-S", sort, "--download-sections", section, url]
        let result = await r.run(executable: ytdlp, arguments: args) { [weak self] line in
            if let t = YtDlpParse.progressText(line) { self?.onStatus?("片段・\(t)") }
        }
        if isStopped || result.wasCancelled { return .cancelled }
        if result.exitCode == 0,
           let file = Self.newestVideoFile(in: outputDir, newerThan: startTime.addingTimeInterval(-5)) {
            Log.info("trackA", "片段下載成功：\(file.lastPathComponent)")
            return .success(file)
        }
        if YtDlpParse.isTerminalFailure(result.output) {
            return .terminalFailure(Self.friendlyFailure(result.output))
        }
        return .terminalFailure("片段下載失敗：" + String(result.output.suffix(160)))
    }

    static func friendlyFailure(_ output: String) -> String {
        let lower = output.lowercased()
        if lower.contains("private video") || lower.contains("this video is private") {
            return "影片已被設為私人／隱藏"
        }
        if lower.contains("members-only") || lower.contains("join this channel") {
            return "會員限定內容（v1 僅支援公開直播）"
        }
        // 年齡限制要排在「機器人驗證」之前：'sign in to confirm your age' 也含 'sign in to confirm you'
        if lower.contains("age-restricted") || lower.contains("sign in to confirm your age") {
            return "年齡限制內容，YouTube 要求登入確認年齡，此來源無法下載"
        }
        if lower.contains("account associated with this video has been terminated") {
            return "上傳此影片的帳號已被終止"
        }
        if lower.contains("blocked it in your country") {
            return "此影片在你所在地區被封鎖"
        }
        if lower.contains("sign in to confirm you") {
            return "YouTube 要求登入驗證，此來源無法下載"
        }
        if lower.contains("video unavailable") || lower.contains("removed") {
            return "影片已下架／刪除"
        }
        return "無法下載：" + String(output.suffix(160))
    }

    /// 找出資料夾中最新的影片成品（排除 .part 暫存）
    static func newestVideoFile(in dir: URL, newerThan: Date) -> URL? {
        let exts = ["mp4", "mkv", "webm", "mov", "m4a"]
        guard let items = try? FileManager.default.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: [.contentModificationDateKey, .fileSizeKey]) else { return nil }
        return items
            .filter { exts.contains($0.pathExtension.lowercased()) && !$0.lastPathComponent.contains(".part") }
            .filter { url in
                let d = (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                let size = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
                return d > newerThan && size > 100_000
            }
            .max {
                let a = (try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                let b = (try? $1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                return a < b
            }
    }
}
