import SwiftUI
import AVKit

/// 預覽視窗：播放已錄好的檔案，確認錄對了、聲音在、畫面沒問題。
/// 不做剪接——「錄什麼有什麼」，要剪接把成品從主面板拖進 Premiere。
struct ClipperWindow: View {
    @EnvironmentObject var app: AppState

    var body: some View {
        Group {
            switch app.clipperSource {
            case .none:
                emptyState
            case .file(let url):
                PreviewPlayerView(mediaURL: url)
                    .id(url)
            }
        }
        .frame(minWidth: 640, minHeight: 420)
    }

    private var emptyState: some View {
        VStack(spacing: 10) {
            BrandMark(color: .secondary).frame(width: 48, height: 48)
            Text("目前沒有可預覽的內容").font(.headline)
            Text("在主面板的成品清單點預覽圖示，即可播放已錄好的檔案，確認錄對了、聲音在、畫面沒問題。\n（錄影中的即時畫面看右下角的監看小窗。）")
                .font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(40)
    }
}

/// 純預覽播放（AVPlayerView 內建拖曳控制軸即可前後找畫面）。剪接在 Premiere 做。
struct PreviewPlayerView: View {
    let mediaURL: URL

    @State private var player = AVPlayer()

    var body: some View {
        VStack(spacing: 12) {
            PlayerView(player: player)
                .background(Color.black)

            HStack(spacing: 8) {
                Image(systemName: "play.rectangle").foregroundStyle(.secondary)
                Text("預覽確認用——拖控制軸前後找畫面。要剪接就把成品從主面板拖進 Premiere。")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer()
                Button("在 Finder 顯示") { FileUtil.revealInFinder(mediaURL) }
                    .controlSize(.small)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 4)
        }
        .padding(20)
        .onAppear { setup() }
        .onDisappear { teardown() }
    }

    private func setup() {
        player.replaceCurrentItem(with: AVPlayerItem(url: mediaURL))
        player.play()
    }

    private func teardown() {
        player.pause()
        player.replaceCurrentItem(with: nil)
    }
}

/// 用 AVKit 的 AppKit `AVPlayerView` 包成 SwiftUI 視圖。
/// 不用 SwiftUI 的 `VideoPlayer`——它在 SwiftPM(`swift build`)的 release 包會在
/// `_AVKit_SwiftUI` 泛型 metadata 實例化時 SIGABRT（實機驗證踩到）。AVPlayerView 是
/// 純 AppKit class、沒有那條 metadata 路徑，穩定且自帶原生播放控制（含拖曳軸）。
struct PlayerView: NSViewRepresentable {
    let player: AVPlayer
    func makeNSView(context: Context) -> AVPlayerView {
        let v = AVPlayerView()
        v.player = player
        v.controlsStyle = .inline
        v.videoGravity = .resizeAspect
        return v
    }
    func updateNSView(_ nsView: AVPlayerView, context: Context) {
        if nsView.player !== player { nsView.player = player }
    }
}
