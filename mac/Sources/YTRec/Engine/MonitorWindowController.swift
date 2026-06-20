import AppKit
import WebKit
import AVFoundation
import CoreMedia

/// 監看視窗（v2.1：攝影機／尋像器分離架構）
///
/// owner 洞見：真正要錄的是高畫質，但看的只要「確認有在錄、錄對內容」。所以拆成兩個視窗：
/// - **擷取視窗（captureWindow）**：藏在螢幕外、以**設定畫質（1080p/720p）全尺寸**算繪的 WebView。
///   SCK 用 `desktopIndependentWindow` 錄的是**這個**——使用者看不到它、它也不佔工作螢幕。
/// - **監看小窗（previewWindow）**：可見、緊湊、可拖/縮/置頂。內容是一片 `AVSampleBufferDisplayLayer`，
///   **鏡像 SCK 正在錄的同一批畫面**（我們本來就逐格收到）。所以小窗縮到多小都**不影響錄製畫質**
///   ——畫質由藏起來的全尺寸擷取視窗決定。錄製紅指示＋時長直接畫在小窗上（不是 SCK 目標，不會進成品）。
///
/// 「縮到背景」＝把監看小窗收起來（擷取視窗本來就藏著、照錄）。注入 JS 沿用 v1。
@MainActor
final class MonitorWindowController: NSObject, WKNavigationDelegate, WKScriptMessageHandler {
    // 隱藏的高畫質擷取視窗（SCK 真正錄的是這個）
    private(set) var captureWindow: NSWindow?
    private(set) var captureWebView: WKWebView?
    // 可見的小監看視窗（鏡像 SCK 畫面 ＋ 錄製指示）
    private var previewWindow: NSWindow?
    private var previewLayer: AVSampleBufferDisplayLayer?
    private var badgeLabel: NSTextField?

    var onPlayerEvent: ((String) -> Void)?   // playing / ended / error
    var onTitle: ((String) -> Void)?
    var onStopTapped: (() -> Void)?          // 小窗「停止」鈕
    var onHideTapped: (() -> Void)?          // 小窗「隱藏」鈕

    /// 監看小窗目前是否顯示
    private(set) var isShown = true
    private var alwaysOnTop = false
    /// 給 nonisolated 的鏡像路徑讀的「是否在顯示」旗標：縮到背景時連畫面拷貝都省掉（長錄省 CPU）。
    nonisolated(unsafe) private var previewActive = true

    /// SCK 要擷取的是隱藏的高畫質擷取視窗
    var windowNumber: Int { captureWindow?.windowNumber ?? -1 }

    // MARK: - 純函式（可測，與 WebView/WindowServer 解耦）

    /// 倒帶到「落後直播 behindSec 秒」的 JS（0＝即時邊緣）。behindSec 必須有限且 ≥0，否則回 nil 不送
    /// （避免把 'nan'/'inf'/負值插進 JS，讓 seekTo 跳到亂點或 no-op）。
    nonisolated static func seekToBehindJS(behindSec: Double) -> String? {
        guard behindSec.isFinite, behindSec >= 0 else { return nil }
        return """
        (function(){var p=document.getElementById('movie_player');
        if(!p||!p.getProgressState||!p.seekTo)return;
        var st=p.getProgressState();if(!st)return;
        var t=st.seekableEnd-(\(behindSec));
        t=Math.max(st.seekableStart,Math.min(t,st.seekableEnd));
        p.seekTo(t,true);})();
        """
    }

    /// 相對目前位置 seek 的 JS（負值＝倒帶，合法）。非有限數回 nil。
    nonisolated static func seekRelativeJS(seconds: Double) -> String? {
        guard seconds.isFinite else { return nil }
        return """
        (function(){var p=document.getElementById('movie_player');
        if(!p||!p.getProgressState||!p.seekTo)return;
        var st=p.getProgressState();if(!st)return;
        var t=st.current+(\(seconds));
        t=Math.max(st.seekableStart,Math.min(t,st.seekableEnd));
        p.seekTo(t,true);})();
        """
    }

    /// 跳回直播即時邊緣（seekableEnd）。
    nonisolated static let seekToLiveJS = """
    (function(){var p=document.getElementById('movie_player');
    if(!p||!p.getProgressState||!p.seekTo)return;
    var st=p.getProgressState();if(!st)return;
    p.seekTo(st.seekableEnd,true);})();
    """

    /// 拖曳節流：非 commit 在 0.2s 內擋掉（防狂打 seekTo 拖垮播放器）；放開（commit）強制送。
    nonisolated static func shouldSendSeek(now: Date, last: Date?, commit: Bool) -> Bool {
        if commit { return true }
        guard let last else { return true }
        return now.timeIntervalSince(last) >= 0.2
    }

    /// 解析播放器回報的位置字串 "behind,window"。欄位數≠2／空／非數字→nil；負值夾 0。
    nonisolated static func parsePosition(_ str: String) -> (behind: Double, window: Double)? {
        let parts = str.split(separator: ",").compactMap { Double($0) }
        guard parts.count == 2 else { return nil }
        return (max(0, parts[0]), max(0, parts[1]))
    }

    /// 隱藏擷取視窗的離屏座標：永遠落在所有螢幕之外（取各螢幕極值＋512 緩衝）。
    nonisolated static func offscreenOrigin(screensMaxX: CGFloat, screensMinY: CGFloat, winHeight: CGFloat) -> CGPoint {
        CGPoint(x: screensMaxX + 512, y: screensMinY - winHeight - 512)
    }

    /// 監看小窗尺寸：高固定，寬依擷取畫質長寬比；高為 0 時除零保護走 16:9。
    nonisolated static func previewSize(captureSize: CGSize, height: CGFloat = 200) -> CGSize {
        let aspect = captureSize.height > 0 ? captureSize.width / captureSize.height : 16.0 / 9.0
        return CGSize(width: (height * aspect).rounded(), height: height)
    }

    /// 播放器訊息分流：`title:` 前綴→標題（保留冒號後全段）；否則＝事件（playing/ended/error）。
    nonisolated static func classifyMessage(_ body: String) -> (title: String?, event: String?) {
        if body.hasPrefix("title:") { return (String(body.dropFirst(6)), nil) }
        return (nil, body)
    }

    /// 小窗徽章文字：尚未落地（nil）顯「● 準備中」、否則「● 」+時長（不假裝在計時，修審查 #8）。
    nonisolated static func badgeText(elapsed: Double?) -> String {
        guard let elapsed else { return "● 準備中" }
        return "● " + FileUtil.formatDuration(elapsed)
    }

    func load(urlString: String, size: CGSize, alwaysOnTop: Bool, autoShow: Bool) {
        close()
        self.alwaysOnTop = alwaysOnTop
        setupCaptureWindow(urlString: urlString, size: size)
        setupPreviewWindow(captureSize: size)
        if autoShow { show() } else { hideToBackground() }
        startPositionPolling()
        Log.info("monitor", "監看啟動：擷取視窗 \(Int(size.width))x\(Int(size.height))(隱藏) window=\(windowNumber)，小窗鏡像顯示=\(autoShow)")
    }

    // MARK: - 隱藏的高畫質擷取視窗（SCK 目標）

    private func setupCaptureWindow(urlString: String, size: CGSize) {
        let cfg = WKWebViewConfiguration()
        cfg.mediaTypesRequiringUserActionForPlayback = []
        cfg.suppressesIncrementalRendering = false
        let ucc = WKUserContentController()
        ucc.addUserScript(WKUserScript(source: Self.visibilitySpoofJS,
                                       injectionTime: .atDocumentStart, forMainFrameOnly: false))
        ucc.addUserScript(WKUserScript(source: Self.playerTakeoverJS,
                                       injectionTime: .atDocumentEnd, forMainFrameOnly: true))
        ucc.add(self, name: "lcf")
        cfg.userContentController = ucc

        let web = WKWebView(frame: NSRect(origin: .zero, size: size), configuration: cfg)
        web.navigationDelegate = self

        let win = NSWindow(contentRect: NSRect(origin: .zero, size: size),
                           styleMask: [.borderless], backing: .buffered, defer: false)
        win.contentView = web
        win.isReleasedWhenClosed = false
        win.ignoresMouseEvents = true
        win.hasShadow = false
        win.backgroundColor = .black
        win.animationBehavior = .none
        win.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        win.isExcludedFromWindowsMenu = true
        win.title = "\(AppInfo.displayName) 擷取（隱藏）"
        positionCaptureOffscreen(win)
        win.orderBack(nil)

        self.captureWindow = win
        self.captureWebView = web
        if let u = URL(string: urlString) { web.load(URLRequest(url: u)) }
    }

    /// 擷取視窗永遠藏在所有螢幕之外（離屏照錄已驗證；spoof JS 防背景節流）。
    private func positionCaptureOffscreen(_ win: NSWindow) {
        let maxX = NSScreen.screens.map { $0.frame.maxX }.max() ?? 1920
        let minY = NSScreen.screens.map { $0.frame.minY }.min() ?? 0
        win.setFrameOrigin(Self.offscreenOrigin(screensMaxX: maxX, screensMinY: minY, winHeight: win.frame.height))
        win.level = .normal
    }

    // MARK: - 可見的監看小窗（鏡像 SCK 畫面）

    private func setupPreviewWindow(captureSize: CGSize) {
        // 預設緊湊 16:9 小窗（與擷取畫質無關，純示意）
        let previewSize = Self.previewSize(captureSize: captureSize)

        let layer = AVSampleBufferDisplayLayer()
        layer.videoGravity = .resizeAspect
        layer.backgroundColor = NSColor.black.cgColor
        self.previewLayer = layer

        let content = MirrorView(frame: NSRect(origin: .zero, size: previewSize))
        content.wantsLayer = true
        content.layer?.backgroundColor = NSColor.black.cgColor
        content.previewLayer = layer
        layer.frame = content.bounds
        content.layer?.addSublayer(layer)

        // 錄製指示徽章（直接畫在小窗上；SCK 目標是擷取視窗，這不會被錄進成品）。
        // 起始顯示「準備中」——在檔案真的開始落地前不假裝在計時（修審查 #8）。
        let badge = NSTextField(labelWithString: Self.badgeText(elapsed: nil))
        badge.font = .monospacedDigitSystemFont(ofSize: 13, weight: .bold)
        badge.textColor = .lcSignal   // 錄製指示＝品牌直播紅
        badge.backgroundColor = NSColor.black.withAlphaComponent(0.55)
        badge.drawsBackground = true
        badge.isBezeled = false
        badge.isEditable = false
        badge.alignment = .center
        badge.frame = NSRect(x: 8, y: previewSize.height - 28, width: 116, height: 20)
        badge.wantsLayer = true
        badge.layer?.cornerRadius = 6
        badge.autoresizingMask = [.minYMargin]
        content.addSubview(badge)
        self.badgeLabel = badge

        // 控制鈕（畫在小窗上、不會進成品）：右上角「隱藏預覽」＋「停止錄影」。
        let hideBtn = Self.makeOverlayButton(symbol: "eye.slash", tint: .white, tooltip: "隱藏預覽（側錄繼續）")
        hideBtn.target = self
        hideBtn.action = #selector(hideButtonTapped)
        hideBtn.frame = NSRect(x: previewSize.width - 58, y: previewSize.height - 28, width: 22, height: 20)
        hideBtn.autoresizingMask = [.minXMargin, .minYMargin]
        content.addSubview(hideBtn)

        let stopBtn = Self.makeOverlayButton(symbol: "stop.fill", tint: .lcCrimson, tooltip: "停止錄影")
        stopBtn.target = self
        stopBtn.action = #selector(stopButtonTapped)
        stopBtn.frame = NSRect(x: previewSize.width - 30, y: previewSize.height - 28, width: 22, height: 20)
        stopBtn.autoresizingMask = [.minXMargin, .minYMargin]
        content.addSubview(stopBtn)

        let win = NSWindow(contentRect: NSRect(origin: .zero, size: previewSize),
                           styleMask: [.borderless, .resizable], backing: .buffered, defer: false)
        win.contentView = content
        win.isReleasedWhenClosed = false
        win.isMovableByWindowBackground = true     // 拖畫面就能移動
        win.hasShadow = true
        win.backgroundColor = .black
        win.animationBehavior = .none
        win.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        win.title = "\(AppInfo.displayName) 監看"
        win.minSize = NSSize(width: 200, height: 112)
        self.previewWindow = win
        applyLevel()
        placePreviewOnScreen(win)
    }

    /// 讓小窗在非 key 狀態（常浮在背景）下，第一下點擊就觸發動作，而非只是 activate 視窗。
    private final class OverlayButton: NSButton {
        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    }

    /// 小窗右上角的半透明控制鈕。
    private static func makeOverlayButton(symbol: String, tint: NSColor, tooltip: String) -> NSButton {
        let b = OverlayButton()
        b.image = NSImage(systemSymbolName: symbol, accessibilityDescription: tooltip)
        b.imagePosition = .imageOnly
        b.isBordered = false
        b.contentTintColor = tint
        b.toolTip = tooltip
        b.wantsLayer = true
        b.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.55).cgColor
        b.layer?.cornerRadius = 6
        return b
    }

    @objc private func hideButtonTapped() { onHideTapped?() }
    @objc private func stopButtonTapped() { onStopTapped?() }

    /// CMSampleBuffer 跨執行緒只做顯示用（落檔走的是另一份 retimed 拷貝），安全交給主緒。
    private struct SampleBox: @unchecked Sendable { let buffer: CMSampleBuffer }

    /// 把 SCK 正在錄的畫面餵進小窗（從擷取引擎逐格鏡像）。任意執行緒呼叫，內部跳主緒。
    /// 先做**獨立拷貝**再上主緒：落檔執行緒同時在用原 sbuf（做 retime），我們在主緒改
    /// DisplayImmediately attachment 不能動到那一份，否則同一個 CMSampleBuffer 被兩緒競寫。
    nonisolated func enqueuePreview(_ sbuf: CMSampleBuffer) {
        guard previewActive else { return }   // 縮到背景：連拷貝都省（修審查 #3）
        var copy: CMSampleBuffer?
        guard CMSampleBufferCreateCopy(allocator: kCFAllocatorDefault,
                                       sampleBuffer: sbuf, sampleBufferOut: &copy) == noErr,
              let copy else { return }
        let box = SampleBox(buffer: copy)
        DispatchQueue.main.async { [weak self] in
            self?.enqueueOnMain(box.buffer)
        }
    }

    private func enqueueOnMain(_ sbuf: CMSampleBuffer) {
        guard isShown, let layer = previewLayer else { return }   // 縮到背景時不浪費算繪
        // 即時顯示：live 畫面不照時間軸排程，標 DisplayImmediately 直接上屏。
        if let arr = CMSampleBufferGetSampleAttachmentsArray(sbuf, createIfNecessary: true),
           CFArrayGetCount(arr) > 0 {
            let dict = unsafeBitCast(CFArrayGetValueAtIndex(arr, 0), to: CFMutableDictionary.self)
            CFDictionarySetValue(dict,
                                 Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(),
                                 Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
        }
        if #available(macOS 11.0, *), layer.requiresFlushToResumeDecoding { layer.flush() }
        if layer.status == .failed { layer.flush() }
        layer.enqueue(sbuf)
    }

    // MARK: - 顯示 / 縮到背景

    func toggleShown() { isShown ? hideToBackground() : show() }

    func show() {
        guard let win = previewWindow else { return }
        previewLayer?.flush()          // 丟掉收起期間的舊畫面，重顯第一格就是當下 live（修審查 #7）
        applyLevel()
        placePreviewOnScreen(win)
        win.orderFront(nil)
        isShown = true
        previewActive = true
        Log.info("monitor", "監看小窗顯示")
    }

    /// 縮到背景：把監看小窗收起來。擷取視窗本來就藏著、照錄。
    func hideToBackground() {
        previewActive = false
        previewWindow?.orderOut(nil)
        isShown = false
        Log.info("monitor", "監看小窗縮到背景（擷取續跑）")
    }

    func setAlwaysOnTop(_ on: Bool) {
        alwaysOnTop = on
        applyLevel()
    }

    private func applyLevel() {
        previewWindow?.level = alwaysOnTop ? .floating : .normal
    }

    /// 監看小窗放到主螢幕右下角（不擋中央工作區）。
    private func placePreviewOnScreen(_ win: NSWindow) {
        let screen = NSScreen.main ?? NSScreen.screens.first
        guard let vf = screen?.visibleFrame else { return }
        let margin: CGFloat = 24
        win.setFrameOrigin(NSPoint(x: vf.maxX - win.frame.width - margin, y: vf.minY + margin))
    }

    // MARK: - 倒帶 / 定位（驅動隱藏擷取 WebView 的 YouTube 播放器）

    /// 回報「落後直播多少秒、可倒帶窗多長」給 UI 顯示位置。
    var onPosition: ((_ behindLiveSec: Double, _ dvrWindowSec: Double) -> Void)?
    private var positionTimer: Timer?

    /// 相對目前位置 seek（負值＝倒帶）；用 YouTube 播放器 API（比 raw video.currentTime 對直播可靠），
    /// 夾在直播可倒帶範圍（getProgressState 的 seekableStart~seekableEnd）內。
    func seekBy(_ seconds: Double) {
        guard let js = Self.seekRelativeJS(seconds: seconds) else { return }
        captureWebView?.evaluateJavaScript(js)
    }

    /// 倒帶到「落後直播 behindSec 秒」的絕對位置（0＝直播即時邊緣）。給時間軸拖曳用。
    /// 拖曳會密集呼叫 → 預設節流（0.2s 內只送一次）避免狂打 seekTo 把播放器拖垮；
    /// 放開時用 `commit: true` 強制送最後一次，確保停在使用者要的點。
    private var lastSeekWall: Date?
    func seekToBehind(_ behindSec: Double, commit: Bool = false) {
        let now = Date()
        guard Self.shouldSendSeek(now: now, last: lastSeekWall, commit: commit) else { return }
        lastSeekWall = now
        guard let js = Self.seekToBehindJS(behindSec: behindSec) else { return }
        captureWebView?.evaluateJavaScript(js)
    }

    /// 跳回直播即時邊緣（seekableEnd）。
    func seekToLive() {
        captureWebView?.evaluateJavaScript(Self.seekToLiveJS)
    }

    private func startPositionPolling() {
        positionTimer?.invalidate()
        positionTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            // timer 在 main run loop 觸發；assumeIsolated 同步補上 @MainActor 隔離證明（零行為改變）。
            MainActor.assumeIsolated { self?.pollPosition() }
        }
    }

    private func pollPosition() {
        let js = """
        (function(){var p=document.getElementById('movie_player');
        if(!p||!p.getProgressState)return '';
        var st=p.getProgressState();if(!st)return '';
        return (st.seekableEnd-st.current)+','+(st.seekableEnd-st.seekableStart);})();
        """
        captureWebView?.evaluateJavaScript(js) { [weak self] result, _ in
            guard let str = result as? String, let pos = Self.parsePosition(str) else { return }
            self?.onPosition?(pos.behind, pos.window)
        }
    }

    /// 更新小窗上的錄製時長。
    func updateElapsed(_ seconds: Double) {
        badgeLabel?.stringValue = Self.badgeText(elapsed: seconds)
    }

    func close() {
        previewActive = false
        positionTimer?.invalidate()
        positionTimer = nil
        captureWebView?.configuration.userContentController.removeScriptMessageHandler(forName: "lcf")
        captureWebView?.stopLoading()
        captureWebView?.loadHTMLString("", baseURL: nil)
        captureWindow?.orderOut(nil)
        captureWindow?.contentView = nil
        captureWindow?.close()
        captureWindow = nil
        captureWebView = nil

        previewLayer?.flushAndRemoveImage()
        previewLayer = nil
        badgeLabel = nil
        previewWindow?.orderOut(nil)
        previewWindow?.contentView = nil
        previewWindow?.close()
        previewWindow = nil
        isShown = false
    }

    // MARK: - WKScriptMessageHandler

    func userContentController(_ userContentController: WKUserContentController,
                               didReceive message: WKScriptMessage) {
        guard let body = message.body as? String else { return }
        let m = Self.classifyMessage(body)
        if let title = m.title {
            onTitle?(title)
        } else if let event = m.event {
            Log.info("monitor", "播放器事件：\(event)")
            onPlayerEvent?(event)
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        if let t = captureWebView?.title, !t.isEmpty { onTitle?(t) }
    }

    // MARK: - 注入腳本（沿用 v1）

    /// 讓頁面永遠以為自己「看得見、有焦點」，避免背景節流/自動暫停（擷取視窗藏在背景尤其需要）。
    static let visibilitySpoofJS = """
    (function () {
      try {
        Object.defineProperty(Document.prototype, 'hidden', { get: function () { return false; }, configurable: true });
        Object.defineProperty(Document.prototype, 'visibilityState', { get: function () { return 'visible'; }, configurable: true });
      } catch (e) {}
      var swallow = function (e) { e.stopImmediatePropagation(); };
      window.addEventListener('visibilitychange', swallow, true);
      document.addEventListener('visibilitychange', swallow, true);
      window.addEventListener('pagehide', swallow, true);
      window.addEventListener('blur', swallow, true);
      document.hasFocus = function () { return true; };
    })();
    """

    /// 接管頁面：把整條播放器容器鏈撐滿視窗、video 填滿播放器、強制播放/解除靜音、鎖 1080p、回報事件
    ///
    /// 關鍵（實機驗證）：SCK 錄到的影像跟著 **YouTube 播放器圖層（#movie_player）** 的邊界走，
    /// 不是跟著我們重新定位的 `<video>` CSS 盒子。只把 `<video>` 設成 100vw/100vh 會：DOM 上 video 盒
    /// 確實滿版，但 #movie_player 仍是頁面預設的偏移小框（量到 107,80,1470,827），於是成品四邊出現黑邊
    /// （video 媒體圖層被畫在播放器框內）。所以要**連播放器容器鏈一起撐滿視窗**，媒體圖層才會滿版無黑邊。
    static let playerTakeoverJS = """
    (function () {
      var fill = 'visibility:visible!important;position:fixed!important;left:0!important;top:0!important;' +
        'right:0!important;bottom:0!important;width:100vw!important;height:100vh!important;' +
        'min-width:0!important;min-height:0!important;max-width:none!important;max-height:none!important;' +
        'margin:0!important;padding:0!important;transform:none!important;overflow:visible!important;background:#000!important';
      var css = 'html,body{background:#000!important;overflow:hidden!important;margin:0!important;padding:0!important}' +
        'body *{visibility:hidden!important}' +
        'ytd-app,#content,#page-manager,ytd-watch-flexy,#full-bleed-container,#player-full-bleed-container,' +
        '#player-theater-container,#columns,#primary,#primary-inner,#player,#player-container,' +
        '#player-container-inner,#player-api,#movie_player,.html5-video-player,.html5-video-container{' + fill + '}' +
        'video{visibility:visible!important;position:absolute!important;left:0!important;top:0!important;' +
        'width:100%!important;height:100%!important;object-fit:contain!important;' +
        'z-index:2147483647!important;background:#000!important;transform:none!important}';
      var style = document.createElement('style');
      style.textContent = css;
      document.documentElement.appendChild(style);
      var sentTitle = false;
      var tick = function () {
        try {
          if (!sentTitle && document.title) {
            sentTitle = true;
            window.webkit.messageHandlers.lcf.postMessage('title:' + document.title.replace(' - YouTube', ''));
          }
          var v = document.querySelector('video');
          if (!v) return;
          if (v.muted) v.muted = false;
          if (v.volume < 1) v.volume = 1.0;
          if (v.paused && !v.ended) { var p = v.play(); if (p && p.catch) p.catch(function () {}); }
          var mp = document.getElementById('movie_player');
          if (mp) {
            if (mp.unMute) mp.unMute();
            if (mp.setPlaybackQualityRange) mp.setPlaybackQualityRange('hd1080', 'hd1080');
            // 讓播放器把內部繪製尺寸對齊視窗（撐滿後媒體圖層才會滿版）
            if (mp.setSize) { try { mp.setSize(window.innerWidth, window.innerHeight); } catch (e) {} }
          }
          try { window.dispatchEvent(new Event('resize')); } catch (e) {}
          if (!v.__lcfHooked) {
            v.__lcfHooked = true;
            v.addEventListener('playing', function () { window.webkit.messageHandlers.lcf.postMessage('playing'); });
            v.addEventListener('ended', function () { window.webkit.messageHandlers.lcf.postMessage('ended'); });
            v.addEventListener('error', function () { window.webkit.messageHandlers.lcf.postMessage('error'); });
          }
        } catch (e) {}
      };
      tick();
      setInterval(tick, 2000);
    })();
    """
}

/// 監看小窗的內容視圖：讓 `AVSampleBufferDisplayLayer` 隨視窗縮放填滿。
private final class MirrorView: NSView {
    weak var previewLayer: AVSampleBufferDisplayLayer?
    override func layout() {
        super.layout()
        // 關掉隱式動畫，拖角縮放時畫面 1:1 跟著視窗、不滑動延遲（修審查 #6）。
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        previewLayer?.frame = bounds
        CATransaction.commit()
    }
}
