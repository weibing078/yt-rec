import Foundation
import SwiftUI
import AppKit
import CoreGraphics

/// 預覽視窗的素材來源（v2.2：只預覽已完成檔案；進行中側錄的即時畫面看監看小窗）
enum ClipperSource: Equatable {
    case none
    case file(URL)      // 已完成檔案
}

/// 全 App 總指揮：管理任務生命週期與「智慧雙軌切換」
@MainActor
final class AppState: ObservableObject {
    static let shared = AppState()

    @Published var job: JobViewModel?
    @Published var history: [CompletedFile] = []
    @Published var clipperSource: ClipperSource = .none
    @Published var globalMessage: String?       // 權限/環境問題提示
    @Published var updateNotice: String?        // 有新版時的提示（app 內更新檢查）
    var updateURL: URL?                          // 「下載更新」要開的連結

    private var trackAEngine: YtDlpEngine?
    private var recorder: RecorderEngine?
    private let monitor = MonitorWindowController()
    private var playerEndedStopScheduled = false
    private var lastDiskCheckSec: Double = 0     // 側錄中磁碟檢查節流（每 60 秒）
    private var stoppingTrackB = false           // 防重入：.finalizing 仍屬 isActive，收尾期間 onElapsed tick 可能再次觸發 stopTrackB

    /// 監看視窗目前是否顯示在螢幕上（UI 綁定用）
    @Published var monitorShown = true

    /// 由主視窗 onAppear 注入：點 Dock 圖示時叫回主視窗（選單列移除後唯一的重開路徑）。
    var reopenMainWindow: (() -> Void)?

    var isBusy: Bool { job?.isActive ?? false }
    /// 真正在寫檔錄影（預覽/定位階段不算）——選單列紅點、結束前保檔用這個。
    var isRecording: Bool { job?.trackB.isRecordingToFile ?? false }
    /// 正在預覽定位（SCK 擷取中、可倒帶、尚未寫檔）。
    var isPreviewing: Bool { if case .previewing = job?.trackB { return true }; return false }
    /// 正在「啟動監看 or 定位」——都還沒寫檔，取消時該直接收掉、不可當錄影失敗處理。
    var isPositioning: Bool {
        switch job?.trackB { case .preparing, .previewing: return true; default: return false }
    }

    private init() {
        Settings.ensureOutputRoot()
        loadHistory()
        checkEnvironment()
        Task { await recoverOrphanRecordings() }
        Task { await checkForUpdate() }
    }

    /// App 內更新檢查（每天最多一次、無網路時靜默）：抓 latest.json、比版本，有新版就設 `updateNotice`。
    /// 不自動下載/安裝（未簽章）—— 只通知，使用者自己按「下載更新」（shared/spec「App update check」）。
    @MainActor
    func checkForUpdate() async {
        let key = "lastUpdateCheck"
        let now = Date()
        if let last = UserDefaults.standard.object(forKey: key) as? Date, now.timeIntervalSince(last) < 86400 { return }
        guard let url = URL(string: "https://ytrec.resonaframe.com/latest.json") else { return }
        do {
            var req = URLRequest(url: url)
            req.timeoutInterval = 8
            req.cachePolicy = .reloadIgnoringLocalCacheData
            let (data, _) = try await URLSession.shared.data(for: req)
            UserDefaults.standard.set(now, forKey: key)   // 成功才記時間；失敗下次啟動再試
            let current = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String
            guard let m = AppUpdate.parseManifest(String(data: data, encoding: .utf8), platform: "mac"),
                  AppUpdate.isNewer(current: current, latest: m.version) else { return }
            updateNotice = "有新版 v\(m.version)" + ((m.notes?.isEmpty == false) ? " · \(m.notes!)" : "")
            updateURL = URL(string: m.url ?? m.page)
        } catch { /* 無網路 / 清單還沒上線：靜默 */ }
    }

    // MARK: - 環境

    func checkEnvironment() {
        globalMessage = Self.environmentMessage(missingTools: BinaryLocator.missingTools,
                                                screenGranted: CGPreflightScreenCaptureAccess())
    }

    func requestScreenPermission() {
        CGRequestScreenCaptureAccess()
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    /// 磁碟空間偏低（< 15GB）時詢問是否仍要側錄。回傳 true = 繼續側錄。
    private func confirmContinueLowDisk(freeBytes: Int64) -> Bool {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "磁碟空間偏低"
        alert.informativeText = "輸出磁碟剩餘約 \(FileUtil.formatBytes(freeBytes))。螢幕側錄會持續寫入，可能很快用罄。仍要開始側錄嗎？"
        alert.addButton(withTitle: "仍要側錄")
        alert.addButton(withTitle: "取消")
        NSApp.activate(ignoringOtherApps: true)
        return alert.runModal() == .alertFirstButtonReturn
    }

    // MARK: - 純決策（可測試）

    /// A 成功的通知文案：只有側錄真的在跑時才提「側錄保險已自動收工」
    /// （修 D3：避免側錄根本沒跑、卻通知「已自動收工」與事實不符）
    nonisolated static func nativeSuccessBody(fileName: String, sidecarActive: Bool) -> String {
        sidecarActive ? "\(fileName)。側錄保險已自動收工。" : fileName
    }

    /// startJob / startSectionDownload 的三道守衛（純函式）。
    /// R3：進行中任務「優先」擋掉——錄製中若有路徑送進垃圾字串，不該污染 globalMessage。
    enum StartDecision: Equatable { case ignore, notYouTube, start }
    nonisolated static func startJobGuard(trimmedURL: String, hasActiveJob: Bool) -> StartDecision {
        if hasActiveJob { return .ignore }          // R3：有進行中任務一律擋（不覆蓋、不污染狀態）
        if trimmedURL.isEmpty { return .ignore }
        return YtURL.isProbablyYouTube(trimmedURL) ? .start : .notYouTube
    }

    /// 下載軌 A 成功時對側錄 B 的處置（純函式）。
    /// R1：B 還在「定位中」（SCK 在跑、尚未寫檔）時，A 探測極快可能搶先成功；
    /// 此時若無條件停 B＋關監看，會把使用者正在倒帶定位的視窗直接關掉、這場視同沒錄。
    /// → 只有 B 真的在寫檔才停並收工；定位中則保留監看，讓使用者自己決定。
    enum SidecarAction: Equatable { case stopAndFinalize, keepPositioning, leaveAsIs }
    nonisolated static func sidecarEffectOnNativeSuccess(recordingToFile: Bool, positioning: Bool) -> SidecarAction {
        if recordingToFile { return .stopAndFinalize }
        if positioning { return .keepPositioning }
        return .leaveAsIs
    }

    /// 時長上限：到點自動收工（R2）。maxHours=0（不限）永不觸發。
    nonisolated static func shouldStopForDuration(elapsedSec: Double, maxHours: Int) -> Bool {
        maxHours > 0 && elapsedSec >= Double(maxHours) * 3600
    }

    /// 側錄中磁碟檢查節流：每 60 秒一次，剛開錄前 60 秒先不查（R2）。
    nonisolated static func shouldRunDiskCheck(elapsedSec: Double, lastCheckSec: Double) -> Bool {
        elapsedSec - lastCheckSec >= 60
    }

    /// 播放器回報事件時是否該排程「20 秒後自動收工」。
    /// 只有「真的在寫檔」且「尚未排程過」的 ended 才算；預覽/定位階段的假 ended 不理會。
    nonisolated static func shouldScheduleEndedStop(event: String, recordingToFile: Bool, alreadyScheduled: Bool) -> Bool {
        event == "ended" && recordingToFile && !alreadyScheduled
    }

    /// 收工通知文案（依停止原因）。nativeSucceeded 由 handleTrackAOutcome 另發，這裡回 nil。
    nonisolated static func stopNotification(reason: StopReason, fileName: String) -> (title: String, body: String)? {
        switch reason {
        case .durationLimit: return ("已達側錄時長上限，自動保存收工", fileName)
        case .lowDisk:       return ("磁碟空間不足，側錄已自動保存收工", fileName)
        case .playerEnded, .userStopped: return ("螢幕側錄已保存", fileName)
        case .nativeSucceeded: return nil
        }
    }

    /// App 結束前的處置（純函式）：錄影中→保存收工；定位中→只取消不保檔；皆非→只收下載軌。
    enum TerminationAction: Equatable { case finalizeAndSave, cancelPositioning, cancelDownloadOnly }
    nonisolated static func terminationAction(isRecording: Bool, isPositioning: Bool) -> TerminationAction {
        if isRecording { return .finalizeAndSave }
        if isPositioning { return .cancelPositioning }
        return .cancelDownloadOnly
    }

    /// 頂部環境警告文案（純函式，R6）：缺元件優先於權限；權限通過回 nil（清警告）。
    nonisolated static func environmentMessage(missingTools: [String], screenGranted: Bool) -> String? {
        if !missingTools.isEmpty {
            return "缺少元件：\(missingTools.joined(separator: ", "))。請重新安裝 App。"
        }
        return screenGranted ? nil : "尚未授權「螢幕與系統音訊錄音」，螢幕側錄無法運作。（勾選後請重開 App 生效）"
    }

    /// 頂部警告是否顯示「檢查權限」深連結（純函式）：只有權限類訊息才出鈕，
    /// 「缺少元件」「這不是 YouTube」不出鈕（避免誤導使用者去開權限頁）。
    nonisolated static func bannerShowsPermissionAction(_ msg: String) -> Bool {
        msg.contains("螢幕") || msg.contains("權限")
    }

    // MARK: - 任務啟動（雙軌同時）

    func startJob(urlString: String) {
        let trimmed = urlString.trimmingCharacters(in: .whitespacesAndNewlines)
        switch Self.startJobGuard(trimmedURL: trimmed, hasActiveJob: job?.isActive ?? false) {
        case .ignore: return
        case .notYouTube: globalMessage = "這不是 YouTube 網址"; return
        case .start: break
        }

        Notify.requestAuthIfNeeded()
        Settings.ensureOutputRoot()
        let j = JobViewModel(url: trimmed, outputRoot: Settings.outputRoot)
        job = j
        globalMessage = nil
        lastDiskCheckSec = 0
        Log.info("job", "開始搶錄：\(trimmed)")

        // 倒帶錄影：先起 SCK 擷取做預覽/定位（畫面進監看小窗、可倒帶），但還不寫檔。
        // 使用者倒帶到要的點、按「從這裡開始錄影」才正式寫檔（見 beginRecording）。
        startPreview(j)
    }

    /// 定位完、開始正式寫檔（從目前播放位置往後錄）。下載軌也在此時依設定啟動。
    func beginRecording() {
        guard let j = job, let rec = recorder, case .previewing = j.trackB else { return }
        do {
            try rec.beginWriting()
            let quality = Int(Settings.recordSize.height) == 720 ? "720p" : "1080p"
            j.trackB = .recording(mode: quality)
            j.infoMessage = nil
            lastDiskCheckSec = 0
            startDownloadTrackIfEnabled(j)
            Notify.post(title: "開始錄影", body: j.title)
        } catch {
            j.trackB = .failed(error.localizedDescription)
            Notify.post(title: "無法開始錄影", body: error.localizedDescription)
        }
    }

    /// 取消預覽定位（啟動中或定位中、沒按開始錄就關掉）：停 SCK、關監看、丟掉這場（不產出檔案）。
    func cancelPreview() {
        guard let j = job, isPositioning else { return }
        let rec = recorder
        rec?.markStopping()        // 先標停止，關監看視窗時 SCK 才不會觸發假的「中斷」通知（修審查 #6）
        monitor.close()
        monitorShown = false
        recorder = nil
        Task { await rec?.cancelCapture() }
        cleanupWork(j)
        job = nil
    }

    // MARK: - 倒帶 / 定位控制（UI 綁定）

    func rewind(_ seconds: Double) { monitor.seekBy(seconds) }
    func jumpToLive() { monitor.seekToLive() }
    /// 時間軸拖曳：倒帶到「落後直播 behind 秒」的絕對位置（commit=放開那一刻）。
    func seekToBehind(_ behind: Double, commit: Bool) { monitor.seekToBehind(behind, commit: commit) }

    /// D2 下載軌啟動計畫（純邏輯，可測）：
    /// - off    → 不啟動（v2 預設：只螢幕側錄）
    /// - auto   → 啟動，但只下載已結束 VOD（autoMode=true）
    /// - always → 啟動，永遠嘗試下載（autoMode=false）
    nonisolated static func downloadPlan(mode: DownloadTrackMode) -> (start: Bool, autoMode: Bool) {
        switch mode {
        case .off:    return (false, false)
        case .auto:   return (true, true)
        case .always: return (true, false)
        }
    }

    /// 只抓 VOD 的某一段：不螢幕側錄，只跑下載軌並帶 `--download-sections`。
    func startSectionDownload(urlString: String, sectionArg: String, sectionLabel: String) {
        let trimmed = urlString.trimmingCharacters(in: .whitespacesAndNewlines)
        switch Self.startJobGuard(trimmedURL: trimmed, hasActiveJob: job?.isActive ?? false) {
        case .ignore: return
        case .notYouTube: globalMessage = "這不是 YouTube 網址"; return
        case .start: break
        }

        Notify.requestAuthIfNeeded()
        Settings.ensureOutputRoot()
        let j = JobViewModel(url: trimmed, outputRoot: Settings.outputRoot, sectionLabel: sectionLabel)
        job = j
        globalMessage = nil
        Log.info("job", "只抓某段：\(trimmed) [\(sectionLabel)]")
        // 片段下載：不螢幕側錄（trackB 維持 idle），只跑下載軌帶 section。
        startTrackA(j, autoMode: false, section: sectionArg)
    }

    /// 下載軌（D2）：依 `downloadTrackMode` 決定是否啟動，以及是否只下載已結束 VOD。
    private func startDownloadTrackIfEnabled(_ j: JobViewModel) {
        let plan = Self.downloadPlan(mode: Settings.downloadTrackMode)
        guard plan.start else {
            j.trackA = .disabled
            Log.info("job", "下載軌：關（僅螢幕側錄）")
            return
        }
        startTrackA(j, autoMode: plan.autoMode)
    }

    // MARK: - 軌道 A（yt-dlp）

    private func startTrackA(_ j: JobViewModel, autoMode: Bool, section: String? = nil) {
        j.trackA = .probing
        let engine = YtDlpEngine()
        trackAEngine = engine
        engine.onStatus = { [weak j] text in
            Task { @MainActor in
                guard let j, !j.trackA.isSettled else { return }
                if text.contains("輪") && text.contains("重試") {
                    let round = Int(YtDlpParse.firstMatch(text, pattern: #"已輪詢 (\d+) 輪"#) ?? "0") ?? 0
                    j.trackA = .waitingRetry(round: round)
                } else {
                    j.trackA = .running(text)
                }
            }
        }
        engine.onProbe = { [weak j] info in
            Task { @MainActor in
                guard let j else { return }
                if j.title == j.videoID || j.title == j.url { j.title = info.title }
            }
        }

        Task { [weak self] in
            let outcome = await engine.start(url: j.url,
                                             outputDir: j.jobDir,
                                             maxHeight: Settings.downloadMaxHeight,
                                             autoMode: autoMode,
                                             section: section)
            await MainActor.run {
                self?.handleTrackAOutcome(outcome, job: j)
            }
        }
    }

    private func handleTrackAOutcome(_ outcome: YtDlpEngine.Outcome, job j: JobViewModel) {
        switch outcome {
        case .success(let file):
            j.trackA = .succeeded(file)
            addHistory(file, kind: .native, title: j.title)
            let action = Self.sidecarEffectOnNativeSuccess(recordingToFile: j.trackB.isRecordingToFile,
                                                           positioning: isPositioning)
            Notify.post(title: "已成功獲取原生高畫質檔案",
                        body: Self.nativeSuccessBody(fileName: file.lastPathComponent,
                                                     sidecarActive: action == .stopAndFinalize))
            switch action {
            case .stopAndFinalize:
                Log.info("job", "智慧切換：原生檔到手，停止側錄")
                Task { await self.stopTrackB(reason: .nativeSucceeded) }
            case .keepPositioning:
                // R1：使用者還在倒帶定位，別關監看／別把這場當沒錄；讓他自己決定停不停。
                j.infoMessage = "原生高畫質檔已到手（已加入清單）。你仍在定位側錄，要停就按停止，或繼續側錄。"
            case .leaveAsIs:
                break
            }
        case .terminalFailure(let reason):
            j.trackA = .failedTerminal(reason)
            if j.trackB.isActive {
                j.infoMessage = "直接下載失敗（\(reason)），背景側錄持續進行中，收工時會保存完整 MP4。"
                Notify.post(title: "直接下載失敗", body: "原因：\(reason)。放心，背景側錄仍在進行。")
            } else {
                Notify.post(title: "直接下載失敗", body: reason)
            }
        case .marathon:
            j.trackA = .marathonSkipped
            j.infoMessage = "偵測為馬拉松直播（已開播逾 4 小時），下載軌不啟動，僅螢幕側錄（受時長上限保護）。"
            Notify.post(title: "馬拉松直播：僅側錄模式",
                        body: "此直播已開播超過 4 小時，為避免下載數天內容，只進行螢幕側錄。")
        case .skippedAutoLive:
            j.trackA = .skippedAutoLive
            j.infoMessage = "偵測為進行中直播：自動模式不下載長直播，僅螢幕側錄。已結束的影片才會自動改用下載。"
            Log.info("job", "自動模式：進行中直播，僅側錄")
        case .cancelled:
            j.trackA = .cancelled
        }
    }

    // MARK: - 軌道 B（螢幕側錄）：先預覽定位、再寫檔

    private func startPreview(_ j: JobViewModel) {
        guard CGPreflightScreenCaptureAccess() else {
            j.trackB = .failed("沒有「螢幕與系統音訊錄音」權限")
            globalMessage = "尚未授權「螢幕與系統音訊錄音」，無法螢幕側錄。"
            return
        }
        // 磁碟保護：空間不足就不啟動（連預覽都不開）
        let free = DiskGuard.freeBytes(at: Settings.outputRoot)
        switch DiskGuard.preCheck(freeBytes: free) {
        case .refuse(let bytes):
            j.trackB = .failed("磁碟空間不足（剩 \(FileUtil.formatBytes(bytes))），未啟動側錄")
            Notify.post(title: "磁碟空間不足，未啟動螢幕側錄",
                        body: "輸出磁碟剩餘約 \(FileUtil.formatBytes(bytes))。")
            return
        case .warn(let bytes):
            if !confirmContinueLowDisk(freeBytes: bytes) {
                j.trackB = .failed("磁碟空間偏低，已取消側錄")
                return
            }
        case .ok:
            break
        }
        j.trackB = .preparing
        let size = Settings.recordSize
        let rec = RecorderEngine(workDir: j.workDir, size: size, bitrate: Settings.recordBitrate)
        recorder = rec

        monitor.onTitle = { [weak j] t in
            Task { @MainActor in
                guard let j, let t = t.isEmpty ? nil : t else { return }
                if j.title == j.videoID || j.title == j.url { j.title = FileUtil.sanitize(t) }
            }
        }
        // 直式影片偵測：頁面回報來源尺寸 → 還沒寫檔時把輸出改成直式（1080×1920），避免直影片被 letterbox 成
        // 滿是黑邊的橫式。橫式影片 target == 現有尺寸 → 不動（出檔與原本完全相同）。寫檔後一律忽略。
        monitor.onDims = { [weak self, weak j, weak rec] w, h in
            Task { @MainActor in
                guard let self, let j, let rec, self.recorder === rec else { return }
                switch j.trackB { case .preparing, .previewing: break; default: return }
                let quality = Int(Settings.recordSize.height)   // 720 或 1080
                let target = CaptureGeometry.outputSize(videoWidth: w, videoHeight: h, quality: quality)
                guard Int(target.width) != Int(size.width) || Int(target.height) != Int(size.height) else { return }
                Log.info("job", "偵測到影片 \(w)x\(h) → 改直式輸出 \(Int(target.width))x\(Int(target.height))")
                self.monitor.resizeCaptureWindow(to: target)
                await rec.updateOutputSize(target)
            }
        }
        monitor.onPlayerEvent = { [weak self, weak j] event in
            Task { @MainActor in
                guard let self, let j else { return }
                // 只有「真的在寫檔」時 ended 才自動收工；預覽/定位階段不理會（倒帶到 DVR 邊界也可能觸發）。
                if Self.shouldScheduleEndedStop(event: event,
                                                recordingToFile: j.trackB.isRecordingToFile,
                                                alreadyScheduled: self.playerEndedStopScheduled) {
                    self.playerEndedStopScheduled = true
                    j.infoMessage = "直播畫面已結束，20 秒後自動收工側錄。"
                    Log.info("job", "播放器回報 ended，20 秒後自動停止側錄")
                    Task {
                        try? await Task.sleep(nanoseconds: 20_000_000_000)
                        await self.stopTrackB(reason: .playerEnded)
                    }
                }
            }
        }
        monitor.onPosition = { [weak j] behind, window in
            Task { @MainActor in
                guard let j else { return }
                j.behindLiveSec = behind
                j.dvrWindowSec = window
            }
        }
        // 監看小窗上的控制鈕（停止／隱藏）回呼到 AppState。
        monitor.onStopTapped = { [weak self] in self?.requestStopFromPreview() }
        monitor.onHideTapped = { [weak self] in self?.hideMonitor() }
        rec.onElapsed = { [weak self, weak j] sec in
            Task { @MainActor in
                guard let self, let j else { return }
                j.recordSeconds = sec
                self.monitor.updateElapsed(sec)
                guard j.trackB.isActive else { return }
                // 時長上限：到點自動收工保檔
                if Self.shouldStopForDuration(elapsedSec: sec, maxHours: Settings.recordMaxHours) {
                    Log.info("job", "側錄達時長上限 \(Settings.recordMaxHours)h，自動收工")
                    await self.stopTrackB(reason: .durationLimit)
                    return
                }
                // 磁碟保護：每 60 秒檢查一次，低於門檻自動收工保檔
                if Self.shouldRunDiskCheck(elapsedSec: sec, lastCheckSec: self.lastDiskCheckSec) {
                    self.lastDiskCheckSec = sec
                    if DiskGuard.shouldStopRecording(freeBytes: DiskGuard.freeBytes(at: Settings.outputRoot)) {
                        Log.info("job", "側錄中磁碟空間不足，自動收工")
                        await self.stopTrackB(reason: .lowDisk)
                    }
                }
            }
        }
        rec.onNoFramesWarning = { [weak self] in
            Task { @MainActor in
                self?.job?.infoMessage = "監看視窗似乎未在算繪畫面，請確認它沒有被最小化。"
            }
        }
        // 把 SCK 正在錄的畫面鏡像給可見的監看小窗（攝影機→尋像器）
        let mon = monitor
        rec.onPreviewSample = { [weak mon] sbuf in
            mon?.enqueuePreview(sbuf)
        }
        rec.onFatalError = { [weak self, weak j, weak rec] msg in
            Task { @MainActor in
                guard let self, let j, let rec, self.recorder === rec else { return }
                if j.trackB.isRecordingToFile {
                    j.trackB = .failed(msg)
                    Notify.post(title: "螢幕側錄中斷", body: msg)
                    await self.finalizeRecorderFile(j)
                } else if j.trackB.isActive {
                    // 預覽/定位中斷：沒在寫檔，不報「側錄中斷」、不嘗試保檔（修審查 #5）
                    j.trackB = .failed("預覽中斷：\(msg)")
                    self.monitor.close()
                    self.monitorShown = false
                    await rec.cancelCapture()
                    self.recorder = nil
                }
            }
        }

        monitor.load(urlString: j.url, size: size,
                     alwaysOnTop: Settings.monitorAlwaysOnTop, autoShow: Settings.monitorAutoShow)
        monitorShown = monitor.isShown

        Task {
            // 給 WebView 一點起跑時間，畫面從第一秒就開始抓
            try? await Task.sleep(nanoseconds: 800_000_000)
            do {
                try await rec.start(captureWindowNumber: self.monitor.windowNumber)
                await MainActor.run {
                    // 啟動期間（800ms+）這場可能已被取消或換成新的一場 → 這個 rec 是孤兒，收掉它、別動到別人（修審查 #2/#4）
                    guard self.recorder === rec, case .preparing = j.trackB else {
                        Task { await rec.cancelCapture() }
                        return
                    }
                    j.trackB = .previewing     // SCK 跑起來了，進入可倒帶定位狀態（還沒寫檔）
                    j.infoMessage = "倒帶到要的點，再按「從這裡開始錄影」。"
                }
            } catch {
                await MainActor.run {
                    guard self.recorder === rec else { return }   // 已被換掉/取消就別 stomp 新的一場
                    j.trackB = .failed(error.localizedDescription)
                    self.monitor.close()
                    self.recorder = nil   // 啟動失敗時釋放半初始化的 writer，避免懸置到下次開錄
                    if case RecorderEngine.RecorderError.permissionDenied = error {
                        self.globalMessage = "尚未授權「螢幕與系統音訊錄音」。請到 系統設定 > 隱私權與安全性 > 螢幕與系統音訊錄音 勾選 \(AppInfo.displayName) 後重開 App。"
                    }
                    Notify.post(title: "螢幕側錄無法啟動", body: error.localizedDescription)
                }
            }
        }
    }

    enum StopReason { case nativeSucceeded, playerEnded, userStopped, durationLimit, lowDisk }

    func stopTrackB(reason: StopReason) async {
        guard !stoppingTrackB else { return }   // 收尾中（.finalizing）期間擋掉重入的 tick，避免對同一 recorder 重複 stop
        guard let j = job, let rec = recorder, j.trackB.isActive else { return }
        stoppingTrackB = true
        defer { stoppingTrackB = false }
        j.trackB = .finalizing
        monitor.close()
        monitorShown = false

        let finalURL = j.jobDir.appendingPathComponent("側錄_\(FileUtil.sanitize(j.title)).mp4")
        let result = await rec.stop(finalFile: finalURL)
        recorder = nil
        playerEndedStopScheduled = false

        switch reason {
        case .nativeSucceeded:
            if let result, Settings.trashSidecarOnNative {
                FileUtil.trash(result)
                j.trackB = .discarded
                Log.info("job", "側錄檔已丟垃圾桶（原生檔已到手）")
            } else if let result {
                j.trackB = .finished(result)
                addHistory(result, kind: .sidecar, title: j.title)
            } else {
                j.trackB = .discarded
            }
            cleanupWork(j)
        case .playerEnded, .userStopped, .durationLimit, .lowDisk:
            if let result {
                j.trackB = .finished(result)
                addHistory(result, kind: .sidecar, title: j.title)
                if let note = Self.stopNotification(reason: reason, fileName: result.lastPathComponent) {
                    Notify.post(title: note.title, body: note.body)
                }
            } else {
                j.trackB = .failed("側錄沒有產出內容")
            }
            if !j.trackA.isSettled {
                j.infoMessage = "側錄已收工。下載軌仍在背景輪詢原生高畫質檔。"
            } else {
                cleanupWork(j)
            }
        }
    }

    private func finalizeRecorderFile(_ j: JobViewModel) async {
        // SCK 中斷等致命錯誤時盡力保檔
        guard let rec = recorder else { return }
        let finalURL = j.jobDir.appendingPathComponent("側錄_\(FileUtil.sanitize(j.title))_中斷保存.mp4")
        if let saved = await rec.stop(finalFile: finalURL) {
            addHistory(saved, kind: .sidecar, title: j.title)
            j.trackB = .finished(saved)
        }
        recorder = nil
        monitor.close()
        monitorShown = false
    }

    func stopAll() {
        if isPositioning { cancelPreview(); return }   // 還在啟動/定位、沒寫檔 → 直接關掉，不產檔（修審查 #1）
        trackAEngine?.cancel()
        Task { await stopTrackB(reason: .userStopped) }
    }

    // MARK: - 監看視窗控制（UI 綁定）

    /// 顯示監看小窗（主視窗「顯示監看預覽」鈕用）。
    func showMonitor() {
        monitor.show()
        monitorShown = monitor.isShown
    }

    /// 縮起監看小窗、側錄繼續（監看小窗上的「隱藏」鈕用）。
    func hideMonitor() {
        monitor.hideToBackground()
        monitorShown = monitor.isShown
    }

    /// 監看小窗「停止」鈕：還在預覽/定位（沒寫檔）直接收掉；真的在錄才跳確認後停止保存。
    func requestStopFromPreview() {
        if isPositioning { cancelPreview(); return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "確定要停止錄影嗎？"
        alert.informativeText = "停止後就無法繼續錄這場直播。目前進度會自動保存。"
        alert.addButton(withTitle: "繼續錄影")
        alert.addButton(withTitle: "停止並保存")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertSecondButtonReturn else { return }
        stopAll()
    }

    func setMonitorAlwaysOnTop(_ on: Bool) {
        monitor.setAlwaysOnTop(on)
    }

    func dismissJob() {
        guard let j = job, !j.isActive else { return }
        cleanupWork(j)
        job = nil
    }

    private func cleanupWork(_ j: JobViewModel) {
        // 側錄已成檔或已丟棄，清掉暫存切片
        try? FileManager.default.removeItem(at: j.workDir)
    }

    // MARK: - 預覽（只開已完成檔案）

    func openClipper(source: ClipperSource) {
        clipperSource = source
    }

    // MARK: - 歷史清單

    private var historyFile: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent(AppInfo.folderName, isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("history.json")
    }

    func addHistory(_ url: URL, kind: FileKind, title: String) {
        history.insert(CompletedFile(url: url, kind: kind, jobTitle: title, date: Date()), at: 0)
        if history.count > 20 { history = Array(history.prefix(20)) }
        saveHistory()
    }

    private func loadHistory() {
        guard let data = try? Data(contentsOf: historyFile),
              let items = try? JSONDecoder().decode([CompletedFile].self, from: data) else { return }
        history = items.filter(\.exists)
    }

    private func saveHistory() {
        if let data = try? JSONEncoder().encode(history) {
            try? data.write(to: historyFile)
        }
    }

    // MARK: - 災難復原：上次沒收工的切片自動拼回 MP4

    private func recoverOrphanRecordings() async {
        let fm = FileManager.default
        guard let jobDirs = try? fm.contentsOfDirectory(at: Settings.outputRoot, includingPropertiesForKeys: nil) else { return }
        for jobDir in jobDirs where jobDir.hasDirectoryPath {
            // 跳過正在錄的任務資料夾，避免讀到半寫切片或刪到使用中的 .work（修審查 P2）
            if let active = job?.jobDir, jobDir.standardizedFileURL == active.standardizedFileURL { continue }
            let segDir = jobDir.appendingPathComponent(".work/segments", isDirectory: true)
            let names = SegmentStore.existingSegmentNames(in: segDir)
            let recovered = jobDir.appendingPathComponent("側錄_上次未收工自動修復.mp4")
            let ffmpeg = BinaryLocator.url(for: .ffmpeg)
            guard SegmentStore.shouldRecover(names: names,
                                             recoveredExists: fm.fileExists(atPath: recovered.path),
                                             ffmpegAvailable: ffmpeg != nil),
                  let ffmpeg else { continue }
            Log.info("recover", "發現未收工側錄：\(jobDir.lastPathComponent)，自動修復中")
            let combined = segDir.appendingPathComponent("recover_combined.mp4")
            // 拼接是 8MB chunk 同步 I/O，移到背景執行緒避免卡啟動 UI（修 D1）
            let concatOK = await Task.detached(priority: .utility) {
                (try? SegmentStore.binaryConcat(dir: segDir, names: names, output: combined)) != nil
            }.value
            guard concatOK else {
                Log.error("recover", "拼接失敗：\(jobDir.lastPathComponent)")
                continue
            }
            let r = await ProcessRunner().run(executable: ffmpeg,
                                              arguments: ["-y", "-hide_banner", "-loglevel", "error",
                                                          "-i", combined.path, "-c", "copy", recovered.path])
            // 復原期間（上面有 await）使用者可能剛好開始錄這個資料夾——破壞性刪除前再查一次（修審查 P2）
            if let active = job?.jobDir, jobDir.standardizedFileURL == active.standardizedFileURL { continue }
            if r.exitCode == 0, FileUtil.fileSize(recovered) > 0 {
                try? fm.removeItem(at: combined)
                try? fm.removeItem(at: jobDir.appendingPathComponent(".work"))
                addHistory(recovered, kind: .sidecar, title: jobDir.lastPathComponent)
                Notify.post(title: "已修復上次未收工的側錄", body: recovered.lastPathComponent)
            }
        }
    }

    // MARK: - App 結束前保檔

    func emergencyFinalize() async {
        switch Self.terminationAction(isRecording: isRecording, isPositioning: isPositioning) {
        case .finalizeAndSave:
            Log.info("app", "App 結束前收工側錄")
            await stopTrackB(reason: .userStopped)
        case .cancelPositioning:
            recorder?.markStopping()
            monitor.close()              // 啟動中/定位中、沒寫檔 → 關掉即可，無檔案要保
            await recorder?.cancelCapture()
            recorder = nil
        case .cancelDownloadOnly:
            break
        }
        trackAEngine?.cancel()
    }
}
