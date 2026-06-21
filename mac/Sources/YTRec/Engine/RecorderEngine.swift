import Foundation
import ScreenCaptureKit
import AVFoundation
import CoreMedia
import UniformTypeIdentifiers

/// 軌道 B：側錄引擎（v2：純 SCK，已砍掉行程音訊攔截）
/// - 影像：ScreenCaptureKit 指定擷取監看視窗（desktop independent，被遮蔽/離屏照錄）
/// - 聲音：SCK 單一視窗音訊（只收該視窗 App 的聲音，不含其他 App；有聲、乾淨、已實測）
/// - 寫檔：AVAssetWriter fMP4 切片（VideoToolbox 硬體編碼），邊錄邊可打點剪輯
final class RecorderEngine: NSObject, SCStreamOutput, SCStreamDelegate, AVAssetWriterDelegate {

    enum RecorderError: LocalizedError {
        case windowNotFound
        case permissionDenied
        case writerFailed(String)
        var errorDescription: String? {
            switch self {
            case .windowNotFound: return "找不到監看視窗"
            case .permissionDenied: return "沒有「螢幕與系統音訊錄音」權限"
            case .writerFailed(let m): return "寫檔失敗：\(m)"
            }
        }
    }

    let store: SegmentStore
    private var size: CGSize    // 可在開始寫檔前調整（偵測到直式影片→1080×1920）
    private let bitrate: Int

    private var stream: SCStream?
    private var writer: AVAssetWriter?
    private var videoInput: AVAssetWriterInput?
    private var audioInput: AVAssetWriterInput?
    private let writerQueue = DispatchQueue(label: "lcf.writer")
    private let sckQueue = DispatchQueue(label: "lcf.sck")

    private var baselinePTS: CMTime?
    private var sessionStarted = false
    private var sessionStartWall: Date?      // 真正開始寫檔的牆鐘時刻（驅動錄製時長，與寫檔背壓無關）
    private var framesAppended: Int = 0
    private var audioSamplesAppended: Int = 0
    private let stoppingLock = NSLock()
    private var _stopping = false
    /// 停止中旗標：main 執行緒設、SCK 取樣/delegate 緒讀——用鎖確保跨緒記憶體可見性，
    /// 避免 SCK 回呼在拆除後仍讀到 false 而觸發假的「中斷」通知（修資料競爭）。
    private var stopping: Bool {
        get { stoppingLock.lock(); defer { stoppingLock.unlock() }; return _stopping }
        set { stoppingLock.lock(); defer { stoppingLock.unlock() }; _stopping = newValue }
    }
    private var writing = false              // 是否已開始寫檔（SCK 擷取可先跑做預覽/定位，寫檔晚點才開）
    private var videoFramesReceived = 0      // 收到的完整畫格數（健康檢查用，與是否進檔無關）
    private var audioDisabled = false        // 逾時收不到任何音訊 → 轉純影像保命

    /// 給 UI 的回呼（任意執行緒）
    var onElapsed: ((Double) -> Void)?
    var onFatalError: ((String) -> Void)?
    var onNoFramesWarning: (() -> Void)?     // 逾時擷取不到畫面（監看視窗可能被最小化）
    var onPreviewSample: ((CMSampleBuffer) -> Void)?   // 把錄到的畫面鏡像給可見的監看小窗
    private var previewFrameCounter = 0

    private var healthTimer: Timer?
    private var elapsedTimer: Timer?         // 錄製時長顯示：每秒跳一次（與 2 秒健康檢查解耦，避免時間「兩秒兩秒跳」）
    private var noFrameWarned = false

    init(workDir: URL, size: CGSize, bitrate: Int) {
        self.store = SegmentStore(dir: workDir.appendingPathComponent("segments", isDirectory: true))
        self.size = size
        self.bitrate = bitrate
        super.init()
    }

    // MARK: - 啟動

    /// 啟動 SCK 擷取（先做預覽／定位用：畫面鏡像給監看小窗，但**還不寫檔**）。
    /// 真正開始錄到檔案要再呼叫 `beginWriting()`——讓使用者先倒帶定位、再從那一刻起錄。
    func start(captureWindowNumber: Int) async throws {

        // 找到監看視窗
        let scWindow = try await findWindow(number: captureWindowNumber)

        let filter = SCContentFilter(desktopIndependentWindow: scWindow)
        let config = makeConfig()

        let stream = SCStream(filter: filter, configuration: config, delegate: self)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: sckQueue)
        try stream.addStreamOutput(self, type: .audio, sampleHandlerQueue: sckQueue)
        self.stream = stream

        do {
            try await stream.startCapture()
        } catch {
            let ns = error as NSError
            Log.error("recorder", "startCapture 失敗 domain=\(ns.domain) code=\(ns.code)：\(ns.localizedDescription)")
            if ns.domain == SCStreamErrorDomain, ns.code == SCStreamError.Code.userDeclined.rawValue {
                throw RecorderError.permissionDenied
            }
            throw error
        }

        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.healthTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { [weak self] _ in
                self?.healthCheck()
            }
            // 時長顯示獨立每秒跳一次（健康檢查仍維持 2 秒，但不再負責時間顯示）
            self.elapsedTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
                self?.tickElapsed()
            }
        }
        Log.info("recorder", "側錄啟動 \(Int(size.width))x\(Int(size.height)) @\(bitrate / 1_000_000)Mbps（純 SCK 音訊）")
    }

    /// SCK 串流設定（單一事實來源；start 與 updateOutputSize 共用）。輸出尺寸跟著 `size` 走。
    private func makeConfig() -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        config.width = Int(size.width)
        config.height = Int(size.height)
        config.minimumFrameInterval = CMTime(value: 1, timescale: 30)
        config.pixelFormat = kCVPixelFormatType_32BGRA
        config.showsCursor = false
        config.queueDepth = 8
        config.capturesAudio = true                  // 純 SCK 音訊：只收該視窗 App 的聲音
        config.excludesCurrentProcessAudio = false   // 我們要錄的就是自己（WebKit）的聲音
        config.sampleRate = 48_000
        config.channelCount = 2
        return config
    }

    /// 在「尚未開始寫檔」時調整輸出尺寸（偵測到直式影片→1080×1920）。已開始寫檔則一律忽略，
    /// 避免動到進行中的 fragment；失敗也只是維持原尺寸（直式仍會 letterbox 呈現，乾淨不壞）。
    func updateOutputSize(_ newSize: CGSize) async {
        guard !writing,
              Int(newSize.width) != Int(size.width) || Int(newSize.height) != Int(size.height) else { return }
        size = newSize  // start() 之前呼叫：start 會用新尺寸；之後呼叫：下面即時重設串流
        guard let stream else { return }
        do {
            try await stream.updateConfiguration(makeConfig())
            Log.info("recorder", "輸出尺寸更新為 \(Int(newSize.width))x\(Int(newSize.height))（直式偵測）")
        } catch {
            Log.error("recorder", "updateConfiguration 失敗，維持原尺寸：\(error.localizedDescription)")
        }
    }

    /// 開始寫檔（從現在的播放位置往後錄）。預覽/定位完、要正式錄時呼叫。
    func beginWriting() throws {
        guard !writing else { return }
        // writer/inputs 在 writerQueue 上建立並發布，與 handleVideo/appendAudio 的讀取同序（修審查資料競爭）。
        var setupError: Error?
        writerQueue.sync {
            do { try setupWriter() } catch { setupError = error }
            noFrameWarned = false   // 與 healthCheck 的 writerQueue 讀取同序
        }
        if let setupError { throw setupError }
        writing = true
        healthTicks = 0          // 健康檢查的倒數從「開始錄」起算，不含先前預覽定位的時間（只在 main 觸碰）
        Log.info("recorder", "開始寫檔（從目前播放位置往後錄）")
    }

    /// 同步標記停止中——關監看視窗前先設，避免 SCK 失去目標而觸發假的「中斷」通知。
    func markStopping() { stopping = true }

    /// 每秒回報錄製時長（牆鐘起算，穩定遞增、不受寫檔背壓影響；只在檔案真的在收時才跑，
    /// 避免徽章顯示在播但檔案還沒落地的假象——修審查 #2/#5/#8）。
    private func tickElapsed() {
        if let start = sessionStartWall { onElapsed?(Date().timeIntervalSince(start)) }
    }

    private var healthTicks = 0   // 只在 main（healthTimer）觸碰
    private func healthCheck() {
        healthTicks += 1
        guard writing else { return }   // 預覽/定位階段不做錄影健康檢查
        let ticks = healthTicks
        // writer 共享狀態（videoFramesReceived/sessionStarted/audioDisabled/noFrameWarned）一律在 writerQueue 上
        // 讀寫，與 handleVideo/appendAudio 同序，避免跨緒資料競爭（修審查 P1）。
        var fireNoFrames = false
        writerQueue.sync {
            let d = Self.healthDecision(ticks: ticks, framesReceived: videoFramesReceived,
                                        sessionStarted: sessionStarted, audioDisabled: audioDisabled,
                                        alreadyWarned: noFrameWarned)
            if d.warnNoFrames {   // ~8s 無完整畫格 → 監看視窗可能被最小化/未算繪
                noFrameWarned = true
                fireNoFrames = true
            }
            if d.disableAudio {   // ~12s 有畫面卻無音訊 → 轉純影像保命（讓視訊去起 session）
                audioDisabled = true
                Log.info("recorder", "逾時無音訊樣本，側錄轉純影像保命")
            }
        }
        if fireNoFrames {
            Log.error("recorder", "逾時擷取不到畫面，監看視窗可能未在算繪")
            onNoFramesWarning?()
        }
    }

    private func findWindow(number: Int) async throws -> SCWindow {
        for attempt in 0..<12 {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            if let w = content.windows.first(where: { $0.windowID == CGWindowID(number) }) {
                return w
            }
            Log.info("recorder", "等待視窗出現… (\(attempt))")
            try await Task.sleep(nanoseconds: 300_000_000)
        }
        throw RecorderError.windowNotFound
    }

    // MARK: - Writer

    private func setupWriter() throws {
        guard let type = UTType(AVFileType.mp4.rawValue) else { throw RecorderError.writerFailed("UTType") }
        let writer = AVAssetWriter(contentType: type)
        writer.shouldOptimizeForNetworkUse = false
        writer.outputFileTypeProfile = .mpeg4AppleHLS
        writer.preferredOutputSegmentInterval = CMTime(seconds: 2, preferredTimescale: 1)
        writer.initialSegmentStartTime = .zero
        writer.delegate = self

        let videoSettings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: Int(size.width),
            AVVideoHeightKey: Int(size.height),
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: bitrate,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
                AVVideoMaxKeyFrameIntervalDurationKey: 2,
                AVVideoAllowFrameReorderingKey: false,
                AVVideoExpectedSourceFrameRateKey: 30,
            ],
        ]
        let vInput = AVAssetWriterInput(mediaType: .video, outputSettings: videoSettings)
        vInput.expectsMediaDataInRealTime = true
        writer.add(vInput)

        let audioSettings: [String: Any] = [
            AVFormatIDKey: kAudioFormatMPEG4AAC,
            AVSampleRateKey: 48_000,
            AVNumberOfChannelsKey: 2,
            AVEncoderBitRateKey: 256_000,
        ]
        let aInput = AVAssetWriterInput(mediaType: .audio, outputSettings: audioSettings)
        aInput.expectsMediaDataInRealTime = true
        writer.add(aInput)

        guard writer.startWriting() else {
            throw RecorderError.writerFailed(writer.error?.localizedDescription ?? "startWriting")
        }
        self.writer = writer
        self.videoInput = vInput
        self.audioInput = aInput
    }

    // AVAssetWriterDelegate：收切片
    func assetWriter(_ writer: AVAssetWriter,
                     didOutputSegmentData segmentData: Data,
                     segmentType: AVAssetSegmentType,
                     segmentReport: AVAssetSegmentReport?) {
        switch segmentType {
        case .initialization:
            store.appendInitSegment(segmentData)
        case .separable:
            let dur = segmentReport?.trackReports.first?.duration.seconds ?? 2.0
            store.appendMediaSegment(segmentData, duration: dur)
        @unknown default:
            break
        }
    }

    // MARK: - SCK 輸出

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard !stopping, sampleBuffer.isValid else { return }
        switch type {
        case .screen:
            handleVideo(sampleBuffer)
        case .audio:
            appendAudio(sampleBuffer)
        default:
            break
        }
    }

    private func handleVideo(_ sbuf: CMSampleBuffer) {
        // 只收完整畫格
        guard let attachments = CMSampleBufferGetSampleAttachmentsArray(sbuf, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let statusRaw = attachments.first?[.status] as? Int,
              statusRaw == SCFrameStatus.complete.rawValue,
              CMSampleBufferGetImageBuffer(sbuf) != nil else { return }

        // 鏡像給監看小窗：每 2 格送 1 格（~15fps），夠確認在錄什麼又不吃資源；不影響落檔的全幀。
        previewFrameCounter += 1
        if previewFrameCounter % 2 == 0 { onPreviewSample?(sbuf) }

        writerQueue.async { [self] in
            guard let writer, let videoInput, writer.status == .writing else { return }
            videoFramesReceived += 1
            // session 一律由「第一筆音訊」起（見 sessionGate / appendAudio）；視訊在那之前先丟，
            // 唯有逾時無音訊（audioDisabled）才由視訊起 session 做純影像保命。
            switch Self.sessionGate(kind: .video, sessionStarted: sessionStarted, audioDisabled: audioDisabled) {
            case .dropWaitingForAudio, .dropAudioDisabled:
                return
            case .startSession:
                baselinePTS = CMSampleBufferGetPresentationTimeStamp(sbuf)
                writer.startSession(atSourceTime: .zero)
                sessionStarted = true
                sessionStartWall = Date()
                Log.info("recorder", "逾時未收到音訊，改純影像側錄")
            case .appendOnly:
                break
            }
            guard let base = baselinePTS, videoInput.isReadyForMoreMediaData,
                  let retimed = Self.retime(sbuf, offset: base),
                  CMSampleBufferGetPresentationTimeStamp(retimed).seconds >= 0 else { return }
            if videoInput.append(retimed) {
                framesAppended += 1
            } else if writer.status == .failed {
                failWriter()
            }
        }
    }

    private func appendAudio(_ sbuf: CMSampleBuffer) {
        writerQueue.async { [self] in
            guard let writer, let audioInput, writer.status == .writing else { return }
            // 第一筆音訊＝整個 session 的起點（音訊與視訊都從這裡起算，對齊不留空洞）。
            // audioDisabled（已轉純影像）時丟棄音訊，避免中途插入弄壞 fragment。
            switch Self.sessionGate(kind: .audio, sessionStarted: sessionStarted, audioDisabled: audioDisabled) {
            case .dropAudioDisabled, .dropWaitingForAudio:
                return
            case .startSession:
                baselinePTS = CMSampleBufferGetPresentationTimeStamp(sbuf)
                writer.startSession(atSourceTime: .zero)
                sessionStarted = true
                sessionStartWall = Date()
                Log.info("recorder", "第一筆音訊進檔，側錄正式起算（SCK 系統音訊）")
            case .appendOnly:
                break
            }
            guard let base = baselinePTS, audioInput.isReadyForMoreMediaData,
                  let retimed = Self.retime(sbuf, offset: base) else { return }
            // 丟掉 baseline 之前的音訊
            if CMSampleBufferGetPresentationTimeStamp(retimed).seconds < -0.5 { return }
            if audioInput.append(retimed) {
                audioSamplesAppended += 1
            } else if writer.status == .failed {
                failWriter()
            }
        }
    }

    private var failed = false
    private func failWriter() {
        guard !failed else { return }
        failed = true
        let msg = writer?.error?.localizedDescription ?? "未知錯誤"
        Log.error("recorder", "writer 失敗：\(msg)")
        onFatalError?(msg)
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        guard !stopping else { return }
        Log.error("recorder", "SCK 串流中斷：\(error.localizedDescription)")
        onFatalError?("畫面擷取中斷：\(error.localizedDescription)")
    }

    // MARK: - 收工

    /// 取消擷取（預覽/定位階段沒按開始錄就關掉）：停 SCK 與 timer，不產出檔案。
    func cancelCapture() async {
        stopping = true
        DispatchQueue.main.async { [weak self] in
            self?.healthTimer?.invalidate()
            self?.healthTimer = nil
            self?.elapsedTimer?.invalidate()
            self?.elapsedTimer = nil
        }
        if let stream {
            try? await stream.stopCapture()
        }
        stream = nil
    }

    /// 停止側錄並產出最終 MP4（先拼 fMP4，再用 ffmpeg remux 成標準 MP4）
    func stop(finalFile: URL) async -> URL? {
        stopping = true
        DispatchQueue.main.async { [weak self] in
            self?.healthTimer?.invalidate()
            self?.healthTimer = nil
            self?.elapsedTimer?.invalidate()
            self?.elapsedTimer = nil
        }
        if let stream {
            try? await stream.stopCapture()
        }
        stream = nil

        // 收尾 writer
        await withCheckedContinuation { (cont: CheckedContinuation<Void, Never>) in
            writerQueue.async { [self] in
                guard let writer, writer.status == .writing else {
                    cont.resume(); return
                }
                videoInput?.markAsFinished()
                audioInput?.markAsFinished()
                writer.finishWriting {
                    cont.resume()
                }
            }
        }
        store.markEnded()

        guard framesAppended > 0 else {
            Log.error("recorder", "沒有任何畫面進檔，側錄無產出")
            return nil
        }

        let combined = store.dir.appendingPathComponent("combined_fmp4.mp4")
        do {
            try store.assembleCombinedFile(to: combined)
        } catch {
            Log.error("recorder", "拼接失敗：\(error.localizedDescription)")
            return nil
        }

        // remux 成乾淨 MP4（單一 moov，Premiere 友善）
        if let ffmpeg = BinaryLocator.url(for: .ffmpeg) {
            let r = await ProcessRunner().run(executable: ffmpeg,
                                              arguments: ["-y", "-hide_banner", "-loglevel", "error",
                                                          "-i", combined.path, "-c", "copy", finalFile.path])
            if r.exitCode == 0, FileUtil.fileSize(finalFile) > 0 {
                try? FileManager.default.removeItem(at: combined)
                Log.info("recorder", "側錄完成：\(finalFile.lastPathComponent) (\(FileUtil.formatBytes(FileUtil.fileSize(finalFile))))")
                return finalFile
            }
            Log.error("recorder", "remux 失敗：\(r.output.suffix(300))")
        }
        // ffmpeg 不在就直接用 fMP4 檔
        try? FileManager.default.moveItem(at: combined, to: finalFile)
        return FileManager.default.fileExists(atPath: finalFile.path) ? finalFile : nil
    }

    // MARK: - 純決策（可測，與 SCK/Writer 解耦）

    enum SampleKind { case video, audio }
    enum SessionGateDecision: Equatable {
        case startSession           // 用這筆樣本起 session（記 baseline）
        case appendOnly             // session 已起，照常 append
        case dropWaitingForAudio    // 視訊但 session 未起且音訊未停用 → 丟棄，等第一筆音訊
        case dropAudioDisabled      // 音訊但已轉純影像 → 丟棄（避免中途插入弄壞 fragment）
    }

    /// 誰先起 session 的核心不變量（P0 修法）：session 一律由「第一筆音訊」起；
    /// 視訊在那之前一律先丟；唯有逾時無音訊（audioDisabled）才改由視訊起 session 做純影像保命。
    static func sessionGate(kind: SampleKind, sessionStarted: Bool, audioDisabled: Bool) -> SessionGateDecision {
        switch kind {
        case .audio:
            if audioDisabled { return .dropAudioDisabled }
            return sessionStarted ? .appendOnly : .startSession
        case .video:
            if sessionStarted { return .appendOnly }
            return audioDisabled ? .startSession : .dropWaitingForAudio
        }
    }

    struct HealthDecision: Equatable { var warnNoFrames: Bool; var disableAudio: Bool }
    /// 健康檢查門檻（純函式）：~8s（ticks≥4）無完整畫格→提醒視窗被最小化；
    /// ~12s（ticks≥6）有畫面卻仍無音訊未起 session→轉純影像保命。
    static func healthDecision(ticks: Int, framesReceived: Int, sessionStarted: Bool,
                               audioDisabled: Bool, alreadyWarned: Bool) -> HealthDecision {
        HealthDecision(
            warnNoFrames: !alreadyWarned && ticks >= 4 && framesReceived == 0,
            disableAudio: !sessionStarted && !audioDisabled && framesReceived > 0 && ticks >= 6)
    }

    // MARK: - 時間軸平移（把 host-time 時間戳平移成從 0 開始）

    static func retime(_ sbuf: CMSampleBuffer, offset: CMTime) -> CMSampleBuffer? {
        var count: CMItemCount = 0
        CMSampleBufferGetSampleTimingInfoArray(sbuf, entryCount: 0, arrayToFill: nil, entriesNeededOut: &count)
        if count == 0 { count = 1 }
        var infos = [CMSampleTimingInfo](repeating: CMSampleTimingInfo(), count: count)
        let status = CMSampleBufferGetSampleTimingInfoArray(sbuf, entryCount: count, arrayToFill: &infos, entriesNeededOut: &count)
        if status != noErr {
            infos = [CMSampleTimingInfo(duration: CMSampleBufferGetDuration(sbuf),
                                        presentationTimeStamp: CMSampleBufferGetPresentationTimeStamp(sbuf),
                                        decodeTimeStamp: .invalid)]
            count = 1
        }
        for i in 0..<Int(count) {
            infos[i].presentationTimeStamp = CMTimeSubtract(infos[i].presentationTimeStamp, offset)
            if infos[i].decodeTimeStamp.isValid {
                infos[i].decodeTimeStamp = CMTimeSubtract(infos[i].decodeTimeStamp, offset)
            }
        }
        var out: CMSampleBuffer?
        CMSampleBufferCreateCopyWithNewTiming(allocator: kCFAllocatorDefault, sampleBuffer: sbuf,
                                              sampleTimingEntryCount: count, sampleTimingArray: &infos,
                                              sampleBufferOut: &out)
        return out
    }
}
