import XCTest
import CoreGraphics
@testable import YTRec

/// L1：監看視窗先前「零單元測試」。把倒帶 seek、位置解析、離屏座標、徽章、訊息分流
/// 從 @MainActor + WebView/WindowServer 裡抽成純函式後逐條釘住。

final class MonitorSeekJSTests: XCTestCase {
    func testSeekToBehindBuildsClampedJS() {
        let js0 = MonitorWindowController.seekToBehindJS(behindSec: 0)
        XCTAssertNotNil(js0)
        XCTAssertTrue(js0!.contains("st.seekableEnd-(0.0)"))                    // 0＝即時邊緣
        XCTAssertTrue(js0!.contains("Math.max(st.seekableStart,Math.min(t,st.seekableEnd))"))
        // 釘住「實際送出強制 seek」與「播放器守衛」——防有人把 seekTo(t,true) 退化成 false 或刪守衛
        XCTAssertTrue(js0!.contains("p.seekTo(t,true)"))
        XCTAssertTrue(js0!.contains("if(!p||!p.getProgressState||!p.seekTo)return"))
        XCTAssertTrue(MonitorWindowController.seekToBehindJS(behindSec: 600)!.contains("st.seekableEnd-(600.0)"))
    }
    func testSeekToBehindRejectsBadInput() {
        XCTAssertNil(MonitorWindowController.seekToBehindJS(behindSec: .nan))       // 不可把 'nan' 塞進 JS
        XCTAssertNil(MonitorWindowController.seekToBehindJS(behindSec: .infinity))
        XCTAssertNil(MonitorWindowController.seekToBehindJS(behindSec: -5))         // 落後不能為負
    }
    func testSeekRelativeAllowsNegativeRejectsNonFinite() {
        let jsNeg = MonitorWindowController.seekRelativeJS(seconds: -30)
        XCTAssertTrue(jsNeg!.contains("st.current+(-30.0)"))  // 倒帶合法
        XCTAssertTrue(jsNeg!.contains("p.seekTo(t,true)"))
        XCTAssertTrue(jsNeg!.contains("if(!p||!p.getProgressState||!p.seekTo)return"))
        XCTAssertTrue(MonitorWindowController.seekRelativeJS(seconds: 30)!.contains("st.current+(30.0)"))
        XCTAssertNil(MonitorWindowController.seekRelativeJS(seconds: .nan))
        XCTAssertNil(MonitorWindowController.seekRelativeJS(seconds: .infinity))
    }
    func testSeekToLiveJS() {
        // 先前零覆蓋：跳回直播即時邊緣＝對 seekableEnd 強制 seek
        let js = MonitorWindowController.seekToLiveJS
        XCTAssertTrue(js.contains("p.seekTo(st.seekableEnd,true)"))
        XCTAssertTrue(js.contains("if(!p||!p.getProgressState||!p.seekTo)return"))
    }
}

final class SeekThrottleTests: XCTestCase {
    func testThrottleAndCommitBypass() {
        let last = Date()
        XCTAssertTrue(MonitorWindowController.shouldSendSeek(now: last, last: nil, commit: false))                 // 第一次
        XCTAssertFalse(MonitorWindowController.shouldSendSeek(now: last.addingTimeInterval(0.19), last: last, commit: false)) // 0.19s 內擋
        XCTAssertTrue(MonitorWindowController.shouldSendSeek(now: last.addingTimeInterval(0.2), last: last, commit: false))   // 剛好 0.2s 放行
        XCTAssertTrue(MonitorWindowController.shouldSendSeek(now: last.addingTimeInterval(0.05), last: last, commit: true))   // 放開強制送
    }
}

final class PositionParseTests: XCTestCase {
    func testValidAndClamp() {
        XCTAssertEqual(MonitorWindowController.parsePosition("12.5,3600")?.behind, 12.5)
        XCTAssertEqual(MonitorWindowController.parsePosition("12.5,3600")?.window, 3600)
        XCTAssertEqual(MonitorWindowController.parsePosition("-3,3600")?.behind, 0)   // 負值夾 0
    }
    func testRejectsMalformed() {
        XCTAssertNil(MonitorWindowController.parsePosition(""))
        XCTAssertNil(MonitorWindowController.parsePosition("abc"))
        XCTAssertNil(MonitorWindowController.parsePosition("1"))       // 欄位 <2
        XCTAssertNil(MonitorWindowController.parsePosition("1,2,3"))   // 欄位 >2
    }
}

final class OffscreenAndSizeTests: XCTestCase {
    func testOffscreenAlwaysOutsideAllScreens() {
        XCTAssertEqual(MonitorWindowController.offscreenOrigin(screensMaxX: 1920, screensMinY: 0, winHeight: 1080),
                       CGPoint(x: 2432, y: -1592))
        // 多螢幕（含負座標）取極值仍在所有螢幕外
        XCTAssertEqual(MonitorWindowController.offscreenOrigin(screensMaxX: 3840, screensMinY: -1080, winHeight: 1080),
                       CGPoint(x: 4352, y: -2672))
    }
    func testPreviewSizeAspectAndDivZeroGuard() {
        XCTAssertEqual(MonitorWindowController.previewSize(captureSize: CGSize(width: 1920, height: 1080)),
                       CGSize(width: 356, height: 200))   // round(200*16/9)
        XCTAssertEqual(MonitorWindowController.previewSize(captureSize: CGSize(width: 1280, height: 720)),
                       CGSize(width: 356, height: 200))
        XCTAssertEqual(MonitorWindowController.previewSize(captureSize: CGSize(width: 1920, height: 0)),
                       CGSize(width: 356, height: 200))   // 高=0 走 16:9，不崩
    }
}

final class MessageAndBadgeTests: XCTestCase {
    func testClassifyMessage() {
        XCTAssertEqual(MonitorWindowController.classifyMessage("title:某直播").title, "某直播")
        XCTAssertEqual(MonitorWindowController.classifyMessage("title:含:冒號的標題").title, "含:冒號的標題") // 冒號後全段保留
        XCTAssertEqual(MonitorWindowController.classifyMessage("ended").event, "ended")
        XCTAssertEqual(MonitorWindowController.classifyMessage("playing").event, "playing")
        XCTAssertNil(MonitorWindowController.classifyMessage("ended").title)
    }
    func testParseDims() {
        XCTAssertTrue(MonitorWindowController.parseDims("dims:1080x1920")! == (1080, 1920))
        XCTAssertTrue(MonitorWindowController.parseDims("dims:1920x1080")! == (1920, 1080))
        XCTAssertNil(MonitorWindowController.parseDims("dims:0x1920"))   // 非正整數
        XCTAssertNil(MonitorWindowController.parseDims("dims:abc"))      // 格式錯
        XCTAssertNil(MonitorWindowController.parseDims("playing"))       // 非 dims 前綴
    }
    func testBadgeText() {
        XCTAssertEqual(MonitorWindowController.badgeText(elapsed: nil), "● 準備中")  // 未落地不假裝計時
        XCTAssertEqual(MonitorWindowController.badgeText(elapsed: 0), "● 00:00")
        XCTAssertEqual(MonitorWindowController.badgeText(elapsed: 65), "● 01:05")
        XCTAssertEqual(MonitorWindowController.badgeText(elapsed: 3661), "● 1:01:01")  // 跨小時
    }
}

/// JS 注入字串的「不變式煙霧測」：防有人手滑刪掉黑邊/掉音/防背景節流的關鍵修法行。
final class InjectedJSSmokeTests: XCTestCase {
    func testPlayerTakeoverKeepsCriticalLines() {
        let js = MonitorWindowController.playerTakeoverJS
        XCTAssertTrue(js.contains("#movie_player"))                 // 撐滿播放器容器鏈（黑邊修法）
        XCTAssertTrue(js.contains("'hd1080'"))                      // 鎖 1080p
        XCTAssertTrue(js.contains("v.muted = false"))              // 強制解除靜音
        XCTAssertTrue(js.contains("postMessage('ended')"))         // ended 回報（自動收工靠它）
        XCTAssertTrue(js.contains("'title:'"))                      // 標題回報
        XCTAssertTrue(js.contains("'dims:'"))                       // 來源尺寸回報（直式偵測靠它）
        XCTAssertTrue(js.contains("ad-showing"))                    // 偵測廣告（沒買 Premium 也不錄到廣告）
        XCTAssertTrue(js.contains("ytp-ad-skip-button"))            // 自動略過廣告
    }
    func testVisibilitySpoofKeepsCriticalLines() {
        let js = MonitorWindowController.visibilitySpoofJS
        XCTAssertTrue(js.contains("visibilityState"))
        XCTAssertTrue(js.contains("return 'visible'"))             // 永遠裝可見（防背景節流）
        XCTAssertTrue(js.contains("visibilitychange"))
    }
}
