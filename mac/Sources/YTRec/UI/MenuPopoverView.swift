import SwiftUI

/// 選單列彈出主面板
struct MenuPopoverView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.openWindow) private var openWindow
    @State private var urlText = ""
    @State private var clipStart = ""
    @State private var clipEnd = ""
    @State private var showPermissions = false

    /// 若「只抓某段」兩欄都有填且有效 → 回 (yt-dlp 參數, 人話標籤)；否則 nil（含格式錯誤）。
    private var sectionInput: (arg: String, label: String)? {
        guard sectionFieldsTouched else { return nil }
        return Timecode.section(from: clipStart, to: clipEnd)
    }
    private var sectionFieldsTouched: Bool {
        !clipStart.trimmingCharacters(in: .whitespaces).isEmpty
            || !clipEnd.trimmingCharacters(in: .whitespaces).isEmpty
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header
            if let msg = app.globalMessage {
                warningBanner(msg)
            }
            if let note = app.updateNotice {
                updateBanner(note)
            }
            // 任務進行中就收起網址列：避免那顆已停用的「進行中」藍鈕跟真正的主操作搶注意力。
            if app.job?.isActive != true { inputRow }
            if !app.isBusy { sectionRow }
            if let job = app.job {
                JobCardView(job: job)
            }
            if !app.history.isEmpty {
                Divider()
                historyList
            }
            footer
        }
        .padding(16)
        .frame(width: 460)
        .onAppear {
            app.checkEnvironment()
            // 點 Dock 圖示叫回主視窗（選單列移除後唯一重開路徑）。
            app.reopenMainWindow = { openWindow(id: "main"); NSApp.activate(ignoringOtherApps: true) }
        }
    }

    private var header: some View {
        // 品牌 logo（取景框＋圓角播放三角）＋字標；右側權限／設定。破壞性動作不放這（結束走 ⌘Q）。
        HStack(spacing: 8) {
            BrandMark().frame(width: 20, height: 20)
            Text(AppInfo.displayName).font(.headline)
            Spacer()
            // 右上工具圖示：次要層級（secondary 灰）＋ 28×28 舒適點擊範圍＋ borderless hover 回饋（HIG）
            Button { showPermissions = true } label: {
                Image(systemName: "checkmark.shield").frame(width: 28, height: 28)
            }
            .buttonStyle(.borderless)
            .foregroundStyle(.secondary)
            .help("權限")
            SettingsLink { Image(systemName: "gearshape").frame(width: 28, height: 28) }
                .buttonStyle(.borderless)
                .foregroundStyle(.secondary)
                .help("設定（⌘,）")
        }
        .sheet(isPresented: $showPermissions) { PermissionPanelView() }
    }

    private func warningBanner(_ msg: String) -> some View {
        HStack(alignment: .top, spacing: 6) {
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.lcWarning)
            Text(msg).font(.caption)
            Spacer()
            if AppState.bannerShowsPermissionAction(msg) {
                Button("檢查權限") { showPermissions = true }
                    .font(.caption)
            }
        }
        .padding(8)
        .background(.lcWarning.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
    }

    /// 有新版時的提示橫幅（app 內更新檢查）。只通知；按「下載更新」開下載頁，不自動安裝。
    private func updateBanner(_ text: String) -> some View {
        HStack(alignment: .top, spacing: 6) {
            Image(systemName: "arrow.down.circle.fill").foregroundStyle(.lcSignal)
            Text(text).font(.caption).fixedSize(horizontal: false, vertical: true)
            Spacer()
            Button("下載更新") { if let u = app.updateURL { NSWorkspace.shared.open(u) } }
                .font(.caption)
        }
        .padding(8)
        .background(.lcSignal.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
    }

    private var inputRow: some View {
        HStack(spacing: 6) {
            TextField("貼上 YouTube 直播／影片網址", text: $urlText)
                .textFieldStyle(.roundedBorder)
                .onSubmit { go() }
                .disabled(app.isBusy)
            Button {
                if let s = NSPasteboard.general.string(forType: .string) {
                    urlText = s.trimmingCharacters(in: .whitespacesAndNewlines)
                }
            } label: { Image(systemName: "doc.on.clipboard") }
                .help("貼上剪貼簿網址")
                .disabled(app.isBusy)
            Button(action: go) {
                Text(app.isBusy ? "進行中" : (sectionInput != nil ? "下載這段" : "開始監看")).bold()
            }
            .keyboardShortcut(.defaultAction)
            .buttonStyle(.borderedProminent)
            .tint(.lcSignal)   // 主操作＝品牌紅，與「從這裡開始錄影」一致
            .help(sectionInput != nil ? "" : "先開直播可倒帶定位，再按「從這裡開始錄影」")
            .disabled(app.isBusy || urlText.isEmpty || (sectionFieldsTouched && sectionInput == nil))
        }
    }

    /// 「只抓某段」選填列：給已結束影片，只下載指定時間區間。
    private var sectionRow: some View {
        HStack(spacing: 6) {
            Text("只抓某段")
                .font(.caption).foregroundStyle(.secondary)
                .help("給已結束的影片：只下載這個時間區間（從–到）。留空＝錄整場／抓整支。")
            TextField("從 0:00", text: $clipStart).textFieldStyle(.roundedBorder).frame(width: 78)
            Image(systemName: "arrow.right").font(.caption2).foregroundStyle(.secondary)
            TextField("到 5:00", text: $clipEnd).textFieldStyle(.roundedBorder).frame(width: 78)
            // 固定保留錯誤提示空間，避免出現/消失時整列跳動（HIG：驗證回饋不應位移版面）
            Text(sectionFieldsTouched && sectionInput == nil ? "用 mm:ss" : "")
                .font(.caption2).foregroundStyle(.lcWarningText)
                .frame(width: 64, alignment: .leading)
            Spacer()
        }
        .controlSize(.small)
    }

    private func go() {
        guard !urlText.isEmpty, !app.isBusy else { return }
        if let sec = sectionInput {
            app.startSectionDownload(urlString: urlText, sectionArg: sec.arg, sectionLabel: sec.label)
        } else {
            app.startJob(urlString: urlText)
        }
    }

    private var historyList: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("最近成品（可直接拖進 Premiere）").font(.caption).foregroundStyle(.secondary)
            ForEach(app.history.prefix(5)) { item in
                CompletedFileRow(item: item) {
                    app.openClipper(source: .file(item.url))
                    openWindow(id: "clipper")
                    NSApp.activate(ignoringOtherApps: true)
                }
            }
        }
    }

    private var footer: some View {
        HStack {
            Button("打開輸出資料夾") {
                NSWorkspace.shared.open(Settings.outputRoot)
            }
            .font(.caption)
            Spacer()
            if let j = app.job, !j.isActive {
                Button("從清單移除") { app.dismissJob() }
                    .font(.caption)
                    .help("只是把這張卡片收掉，錄好的檔案不會被刪除")
            }
        }
    }
}

/// 進行中任務卡片
struct JobCardView: View {
    @ObservedObject var job: JobViewModel
    @EnvironmentObject var app: AppState

    // 倒帶時間軸拖曳狀態（0＝最舊、1＝直播即時邊緣）
    @State private var dragFrac: Double? = nil          // 拖曳中的暫定位置
    @State private var settleTarget: Double? = nil      // 放開後 hold 住的目標（落後秒數），等位置輪詢追上再放
    @State private var awaitingSettle = false
    @State private var settleToken = 0                  // 作廢過期的 hold-逾時 Task（每次重新放開／清除就 +1）

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(job.title).font(.subheadline).bold().lineLimit(2)

            if isPositioning {
                positioningView
            } else {
                if !job.isSectionDownload {
                    if case .recording(let quality) = job.trackB {
                        recordingBanner(quality: quality)
                    } else {
                        trackBRow
                    }
                }
                trackARow

                if let info = job.infoMessage {
                    Text(info).font(.caption).foregroundStyle(.secondary)
                        .lineLimit(nil).fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                recordingButtons
            }
        }
        .padding(10)
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 8))
    }

    private var isPositioning: Bool {
        switch job.trackB { case .preparing, .previewing: return true; default: return false }
    }
    /// 監看是否已就緒可倒帶（.previewing）；.preparing 時還在啟動，控制先禁用。
    private var positioningReady: Bool {
        if case .previewing = job.trackB { return true }; return false
    }

    /// 預覽定位（倒帶錄影）：可拖的時間軸（含小時刻度）＋微調鈕＋從這裡開始錄影。
    private var positioningView: some View {
        let ready = positioningReady
        return VStack(alignment: .leading, spacing: 12) {
            // 狀態列：正在預覽、目前停在哪
            HStack(spacing: 8) {
                Image(systemName: "dot.radiowaves.left.and.right").foregroundStyle(.lcSignal)
                Text(ready ? "預覽中" : "啟動監看中…").font(.callout).bold()
                Spacer()
                if ready {
                    Text(positionText)
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                } else {
                    ProgressView().controlSize(.small)
                }
            }

            if ready {
                // 步驟 1：倒帶定位（次要控制群組，標清楚用途）
                VStack(alignment: .leading, spacing: 6) {
                    Text("先倒帶到要開始錄的時間點")
                        .font(.caption).foregroundStyle(.secondary)
                    if canScrub { timelineScrubber }
                    nudgeRow
                }

                // 步驟 2：主操作——大、填滿、品牌紅白字，一眼就知道按這顆
                Button { app.beginRecording() } label: {
                    Label("從這裡開始錄影", systemImage: "record.circle.fill")
                        .font(.headline)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 2)
                }
                .buttonStyle(.borderedProminent)
                .tint(.lcSignal)
                .controlSize(.large)
                .disabled(!ready)
            }

            // 取消監看：低調、置中、明顯次於主操作
            HStack {
                Spacer()
                Button("取消監看") { app.cancelPreview() }
                    .buttonStyle(.borderless)
                    .controlSize(.small)
                    .foregroundStyle(.secondary)
                Spacer()
            }

            if ready {
                Text("從那一刻往後即時側錄（1 倍速、不中斷）；錄完還能用「抓段」精修。")
                    .font(.caption2).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        // 解除 hold 的條件：輪詢回報的位置**真的落在目標附近**才放（不是「一變就放」）。
        // 否則 seek 還沒生效時先回來的舊值會讓 knob 往回跳——正是 hold 要防的事。
        .onChange(of: job.behindLiveSec) { _, newVal in
            if awaitingSettle, let t = settleTarget, abs(newVal - t) <= settleTolerance {
                awaitingSettle = false
                settleTarget = nil
            }
        }
    }

    /// 可拖的時間軸：整條＝DVR 可倒帶窗，左＝最舊、右＝直播即時；含小時刻度與位置讀數。
    private var timelineScrubber: some View {
        VStack(spacing: 5) {
            HStack {
                Label("最舊 \(FileUtil.formatDuration(dvrWindow)) 前", systemImage: "clock.arrow.circlepath")
                Spacer()
                Label("直播即時", systemImage: "dot.radiowaves.left.and.right").foregroundStyle(.lcSignal)
            }
            .font(.caption2).foregroundStyle(.secondary)

            GeometryReader { geo in
                let w = geo.size.width
                let knob: CGFloat = 16
                let x = max(0, min(w, w * CGFloat(shownFrac)))
                ZStack(alignment: .leading) {
                    Capsule().fill(.quaternary).frame(height: 8)
                    ForEach(tickFractions, id: \.self) { f in
                        Rectangle().fill(.secondary.opacity(0.35))
                            .frame(width: 1, height: 10)
                            .offset(x: w * CGFloat(f) - 0.5)
                    }
                    Capsule().fill(Color.lcSignal).frame(width: x, height: 8)
                    Circle().fill(Color.lcSignal)
                        .overlay(Circle().strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: 2))
                        .frame(width: knob, height: knob)
                        .offset(x: x - knob / 2)
                        .shadow(radius: 1)
                }
                .frame(maxHeight: .infinity)
                .contentShape(Rectangle())
                .gesture(
                    DragGesture(minimumDistance: 0)
                        .onChanged { v in
                            guard w > 0 else { return }
                            let f = Double(max(0, min(w, v.location.x)) / w)
                            dragFrac = f               // 拖曳中由暫定值主導，蓋過任何待 settle 狀態
                            settleTarget = nil
                            awaitingSettle = false
                            app.seekToBehind((1 - f) * dvrWindow, commit: false)
                        }
                        .onEnded { v in
                            guard w > 0 else { return }
                            let f = Double(max(0, min(w, v.location.x)) / w)
                            let target = (1 - f) * dvrWindow
                            dragFrac = nil
                            settleTarget = target
                            awaitingSettle = true
                            settleToken &+= 1
                            let token = settleToken
                            app.seekToBehind(target, commit: true)
                            // 後援：就算位置輪詢的值一直沒落在目標附近（或根本沒變動），也保證 2.5s 後解除 hold，knob 不會卡死
                            Task { @MainActor in
                                try? await Task.sleep(nanoseconds: 2_500_000_000)
                                if awaitingSettle, token == settleToken {
                                    awaitingSettle = false
                                    settleTarget = nil
                                }
                            }
                        }
                )
            }
            .frame(height: 22)

            HStack(spacing: 4) {
                Spacer()
                Text("位置 \(FileUtil.formatDuration(shownFrac * dvrWindow))").monospacedDigit()
                Text("/ \(FileUtil.formatDuration(dvrWindow))").foregroundStyle(.secondary).monospacedDigit()
                Spacer()
            }
            .font(.caption2).foregroundStyle(.secondary)
        }
    }

    /// 微調鈕：拖大範圍後用這些對齊到秒。
    private var nudgeRow: some View {
        HStack(spacing: 6) {
            Button("−5分") { nudge(-300) }
            Button("−1分") { nudge(-60) }
            Button("−10秒") { nudge(-10) }
            Button("+10秒") { nudge(10) }
            Button("+1分") { nudge(60) }
            Spacer()
            Button { jumpLive() } label: { Label("回到直播", systemImage: "forward.end") }
        }
        .controlSize(.small)
    }

    // MARK: 倒帶時間軸計算

    private var dvrWindow: Double { max(1, job.dvrWindowSec) }
    /// DVR 倒帶窗夠長才顯示時間軸（太短的來源拖不出意義，只給微調鈕）。
    private var canScrub: Bool { job.dvrWindowSec > 90 }
    private var liveFrac: Double { min(1, max(0, 1 - job.behindLiveSec / dvrWindow)) }
    /// 顯示用位置（0＝最舊、1＝直播）：拖曳中用暫定值、放開等待中用目標值、否則跟實際輪詢。
    private var shownFrac: Double {
        if let d = dragFrac { return d }
        if let t = settleTarget { return min(1, max(0, 1 - t / dvrWindow)) }
        return liveFrac
    }
    private var shownBehind: Double { max(0, (1 - shownFrac) * dvrWindow) }
    /// 視為「已 seek 到位」的容差：seekTo 落點很準（差幾秒，因 live 邊緣會微動），
    /// 但 seek 還沒生效時回來的舊值差距 ＝ 整段拖曳距離，遠大於此 → 被擋住、不會誤放 hold。
    private var settleTolerance: Double { max(2, dvrWindow * 0.02) }

    /// 刻度位置比例：≥2 小時用每小時、≥10 分用每 10 分、否則每分鐘。
    private var tickFractions: [Double] {
        let w = dvrWindow
        guard w > 120 else { return [] }
        let step: Double = w >= 2 * 3600 ? 3600 : (w >= 600 ? 600 : 60)
        var out: [Double] = []
        var t = step
        while t < w { out.append(t / w); t += step }
        return out
    }

    private func clearScrub() { dragFrac = nil; settleTarget = nil; awaitingSettle = false; settleToken &+= 1 }
    private func nudge(_ d: Double) { clearScrub(); app.rewind(d) }
    private func jumpLive() { clearScrub(); app.jumpToLive() }

    private var positionText: String {
        shownBehind < 3 ? "● 直播即時" : "落後直播 \(FileUtil.formatDuration(shownBehind))"
    }

    @ViewBuilder
    private var recordingButtons: some View {
        HStack(spacing: 8) {
            if job.trackB.isActive {
                // 隱藏鈕已移到監看小窗本身；這裡只在「預覽窗存在且被收起」時提供「叫回來」。
                // 用 isRecording/isPreviewing（非 isActive）排除 .finalizing——那時窗已 close、show() 會空轉。
                if (app.isRecording || app.isPreviewing) && !app.monitorShown {
                    Button {
                        app.showMonitor()
                    } label: {
                        Label("顯示監看預覽", systemImage: "eye")
                    }
                    .help("重新顯示監看小窗（側錄一直在進行）")
                    .fixedSize()
                }

                Spacer(minLength: 8)

                if downloadRunning {
                    Button("只停側錄") {
                        if confirmStop(all: false) { Task { await app.stopTrackB(reason: .userStopped) } }
                    }.fixedSize()
                    Button("全部停止", role: .destructive) {
                        if confirmStop(all: true) { app.stopAll() }
                    }.fixedSize()
                } else {
                    Button("停止錄影", role: .destructive) {
                        if confirmStop(all: false) { app.stopAll() }
                    }.fixedSize()
                }
            } else if job.isActive {
                Spacer(minLength: 8)
                Button("停止下載", role: .destructive) { app.stopAll() }.fixedSize()
            }
        }
        .controlSize(.small)
    }

    /// 錄影中的醒目狀態：脈動紅指示燈＋大時間＋畫質，一眼看清「正在錄、錄多久了」。
    private func recordingBanner(quality: String) -> some View {
        HStack(spacing: 8) {
            // 指示燈與大計時等高（.title2），標籤次級（.headline）——讓「錄多久了」是唯一視覺主角（HIG）
            Image(systemName: "record.circle.fill")
                .foregroundStyle(.lcSignal)
                .font(.title2)
                .symbolEffect(.pulse, options: .repeating)
            Text(job.recordSeconds > 0 ? "錄影中" : "準備中").font(.headline)
            Spacer()
            Text(FileUtil.formatDuration(job.recordSeconds))
                .font(.title2.monospacedDigit()).bold()
            Text(quality).font(.caption2).foregroundStyle(.secondary)
        }
        .padding(.horizontal, 8).padding(.vertical, 6)
        .background(.lcSignal.opacity(0.08), in: RoundedRectangle(cornerRadius: 8))
    }

    /// 下載軌是否正在實際下載（決定要不要顯示「只停側錄／全部停」的區分）。
    private var downloadRunning: Bool {
        switch job.trackA {
        case .probing, .running, .waitingRetry: return true
        default: return false
        }
    }

    /// 停止前確認（錄影中才問；只剩下載軌時直接停）。回傳 true = 使用者確認停止。
    private func confirmStop(all: Bool) -> Bool {
        guard job.trackB.isActive else { return true }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "確定要停止錄影嗎？"
        alert.informativeText = "停止後就無法繼續錄這場直播。目前進度會自動保存。"
        alert.addButton(withTitle: "繼續錄影")
        alert.addButton(withTitle: all ? "停止全部並保存" : "停止並保存")
        NSApp.activate(ignoringOtherApps: true)
        return alert.runModal() == .alertSecondButtonReturn
    }

    /// 側錄指示燈：錄製中＝直播紅、待命＝待命灰（CIS §5.2）、其餘中性。
    private var trackBDotColor: Color {
        if job.trackB.isActive { return .lcSignal }
        if case .idle = job.trackB { return .lcIdle }
        return .secondary
    }

    private var trackBRow: some View {
        HStack(spacing: 6) {
            Image(systemName: "record.circle")
                .foregroundStyle(trackBDotColor)
            Group {
                switch job.trackB {
                case .idle: Text("待命")
                case .preparing: Text("啟動監看視窗…")
                case .previewing: Text("預覽中・可倒帶")
                case .recording(let mode):
                    Text("錄影中・\(FileUtil.formatDuration(job.recordSeconds))・\(mode)").monospacedDigit().bold()
                case .finalizing: Text("收工封裝中…")
                case .finished: Label("已保存", systemImage: "checkmark.circle.fill").foregroundStyle(.lcSuccessText)
                case .failed(let r): Text("失敗：\(r)").foregroundStyle(.lcDangerText).lineLimit(2)
                case .discarded: Text("已捨棄（原生檔到手）").foregroundStyle(.secondary)
                }
            }
            .font(.caption)
            Spacer(minLength: 4)
        }
    }

    @ViewBuilder
    private var trackARow: some View {
        // 下載軌降為選用：關閉時不顯示這一列，避免干擾「側錄為主」的視覺重心。
        if case .disabled = job.trackA {
            EmptyView()
        } else {
            HStack(spacing: 6) {
                Image(systemName: job.isSectionDownload ? "scissors" : "arrow.down.circle").foregroundStyle(.blue)
                Text(job.isSectionDownload ? "下載片段（\(job.sectionLabel ?? "")）" : "下載軌・原生檔（選用）")
                    .font(.caption).bold()
                Spacer(minLength: 4)
                Group {
                    switch job.trackA {
                    case .idle: Text("待命")
                    case .disabled: EmptyView()
                    case .probing: Text("分析中…")
                    case .running(let s): Text(s).lineLimit(1)
                    case .waitingRetry(let n): Text("輪詢中（第 \(n) 輪）…")
                    case .succeeded: Label("已到手", systemImage: "checkmark.circle.fill").foregroundStyle(.lcSuccessText)
                    case .failedTerminal(let r): Text("失敗：\(r)").foregroundStyle(.lcDangerText).lineLimit(2)
                    case .marathonSkipped: Text("馬拉松直播：僅側錄").foregroundStyle(.secondary)
                    case .skippedAutoLive: Text("進行中直播：僅側錄").foregroundStyle(.secondary)
                    case .cancelled: Text("已取消").foregroundStyle(.secondary)
                    }
                }
                .font(.caption)
            }
        }
    }
}

/// 成品列：整列可拖進 Premiere；hover 顯示預覽／開啟資料夾。
struct CompletedFileRow: View {
    let item: CompletedFile
    let onPreview: () -> Void
    @State private var hovering = false

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: iconName)
                .font(.system(size: 16))
                .foregroundStyle(kindColor)
                .frame(width: 22)
            VStack(alignment: .leading, spacing: 3) {
                Text(item.url.lastPathComponent)
                    .font(.callout)
                    .lineLimit(1).truncationMode(.middle)
                HStack(spacing: 6) {
                    Text(item.kind.rawValue)
                        .font(.caption2).fontWeight(.medium)
                        .foregroundStyle(kindColor)
                        .padding(.horizontal, 5).padding(.vertical, 1)
                        .background(kindColor.opacity(0.16), in: Capsule())
                    Text(FileUtil.formatBytes(FileUtil.fileSize(item.url)))
                        .font(.caption2).foregroundStyle(.secondary).monospacedDigit()
                }
            }
            Spacer(minLength: 8)
            HStack(spacing: 8) {
                rowButton("play.circle", "預覽", action: onPreview)
                rowButton("folder", "在 Finder 顯示") { FileUtil.revealInFinder(item.url) }
            }
            .opacity(hovering ? 1 : 0.45)
        }
        .padding(.horizontal, 8).padding(.vertical, 6)
        .background(hovering ? Color.primary.opacity(0.07) : .clear,
                    in: RoundedRectangle(cornerRadius: 8))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .help("把這一列直接拖到 Premiere 即可匯入")
        .onDrag { NSItemProvider(contentsOf: item.url) ?? NSItemProvider() }
    }

    private func rowButton(_ icon: String, _ helpText: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon).font(.system(size: 15)).foregroundStyle(.secondary)
        }
        .buttonStyle(.plain)
        .help(helpText)
    }

    private var iconName: String {
        switch item.kind {
        case .native: return "arrow.down.circle.fill"
        case .sidecar: return "record.circle.fill"
        case .clip: return "scissors.circle.fill"
        }
    }
    private var kindColor: Color {
        switch item.kind {
        case .native: return .blue          // 下載原生檔（系統藍，非品牌強調）
        case .sidecar: return .lcSignal     // 螢幕側錄＝核心成品，上品牌紅
        case .clip: return .purple          // 精華片段
        }
    }
}
