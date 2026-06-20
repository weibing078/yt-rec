import XCTest
@testable import YTRec

/// 從大函式抽出來、現在可單獨測的決策邏輯：
/// PRD §4 策略順序、使用者失敗文案、以及 D2/D3 兩個 bug 修正後的正確行為。

final class StrategyOrderTests: XCTestCase {
    func testLiveOrdersFromStartFirst() {
        // 直播中／即將開始：先 --live-from-start（從頭抓）
        XCTAssertEqual(YtDlpParse.strategyOrder(liveStatus: "is_live"), [.fromStart, .normal, .degraded])
        XCTAssertEqual(YtDlpParse.strategyOrder(liveStatus: "is_upcoming"), [.fromStart, .normal, .degraded])
    }

    func testEndedOrdersNormalFirst() {
        // 已結束：VOD 多半就緒，先一般下載
        XCTAssertEqual(YtDlpParse.strategyOrder(liveStatus: "post_live"), [.normal, .fromStart, .degraded])
        XCTAssertEqual(YtDlpParse.strategyOrder(liveStatus: "was_live"), [.normal, .fromStart, .degraded])
        XCTAssertEqual(YtDlpParse.strategyOrder(liveStatus: "not_live"), [.normal, .fromStart, .degraded])
    }

    func testUnknownDefaultsToLiveOrder() {
        // 探測不到狀態（NA）→ 當直播處理，先從頭抓
        XCTAssertEqual(YtDlpParse.strategyOrder(liveStatus: "NA"), [.fromStart, .normal, .degraded])
    }

    func testStrategyArguments() {
        // is_upcoming 要多等影片開播
        XCTAssertEqual(DownloadStrategy.fromStart.arguments(liveStatus: "is_upcoming"),
                       ["--live-from-start", "--wait-for-video", "60"])
        XCTAssertEqual(DownloadStrategy.fromStart.arguments(liveStatus: "is_live"), ["--live-from-start"])
        XCTAssertEqual(DownloadStrategy.normal.arguments(liveStatus: "is_live"), [])
        XCTAssertEqual(DownloadStrategy.degraded.arguments(liveStatus: "is_live"), ["-S", "res:720,vcodec:h264"])
    }
}

/// D2：自動模式只下載「已結束、長度有限的 VOD」；進行中直播或探測不到 → 只螢幕側錄。
/// 這是 v2 新定位「螢幕側錄為主、下載為輔」的決策核心。
final class AutoDownloadDecisionTests: XCTestCase {
    func testEndedVODsShouldDownload() {
        // 已結束的有限長度影片：下載更快、畫質更好
        XCTAssertTrue(YtDlpParse.autoShouldDownload(liveStatus: "post_live"))
        XCTAssertTrue(YtDlpParse.autoShouldDownload(liveStatus: "was_live"))
        XCTAssertTrue(YtDlpParse.autoShouldDownload(liveStatus: "not_live"))
    }

    func testLiveStreamsShouldNotDownload() {
        // 進行中／即將開始的直播：下載長直播不切實際，只側錄
        XCTAssertFalse(YtDlpParse.autoShouldDownload(liveStatus: "is_live"))
        XCTAssertFalse(YtDlpParse.autoShouldDownload(liveStatus: "is_upcoming"))
    }

    func testUnknownStatusDoesNotDownload() {
        // 探測不到狀態（NA／未知）→ 保守起見不下載，只側錄
        XCTAssertFalse(YtDlpParse.autoShouldDownload(liveStatus: "NA"))
        XCTAssertFalse(YtDlpParse.autoShouldDownload(liveStatus: ""))
    }
}

/// D2 下載軌啟動計畫：把「螢幕側錄為主、下載為輔」三段模式的接線釘死。
final class DownloadPlanTests: XCTestCase {
    func testOffNeverStartsDownload() {
        let plan = AppState.downloadPlan(mode: .off)
        XCTAssertFalse(plan.start)            // 預設：完全不下載，只螢幕側錄
    }

    func testAutoStartsInAutoMode() {
        let plan = AppState.downloadPlan(mode: .auto)
        XCTAssertTrue(plan.start)
        XCTAssertTrue(plan.autoMode)          // 只下載已結束 VOD，進行中直播跳過
    }

    func testAlwaysStartsWithoutAutoMode() {
        let plan = AppState.downloadPlan(mode: .always)
        XCTAssertTrue(plan.start)
        XCTAssertFalse(plan.autoMode)         // 永遠嘗試下載（馬拉松仍由引擎擋）
    }
}

final class FriendlyFailureTests: XCTestCase {
    func testMapsKnownReasons() {
        XCTAssertEqual(YtDlpEngine.friendlyFailure("ERROR: Private video. Sign in if..."), "影片已被設為私人／隱藏")
        XCTAssertEqual(YtDlpEngine.friendlyFailure("This video is available to members-only content"),
                       "會員限定內容（v1 僅支援公開直播）")
        XCTAssertEqual(YtDlpEngine.friendlyFailure("ERROR: Sign in to confirm you're not a bot"),
                       "YouTube 要求登入驗證，此來源無法下載")
        XCTAssertEqual(YtDlpEngine.friendlyFailure("ERROR: Video unavailable"), "影片已下架／刪除")
    }

    func testUnknownReasonFallsBack() {
        XCTAssertTrue(YtDlpEngine.friendlyFailure("ERROR: some unexpected thing").hasPrefix("無法下載："))
    }
}

/// 修 D3：A 成功的通知文案，只有側錄真的在跑時才提「已自動收工」。
final class NativeSuccessBodyTests: XCTestCase {
    func testMentionsSidecarOnlyWhenActive() {
        XCTAssertEqual(AppState.nativeSuccessBody(fileName: "影片.mp4", sidecarActive: true),
                       "影片.mp4。側錄保險已自動收工。")
        // 側錄根本沒跑 → 不可說「已自動收工」
        XCTAssertEqual(AppState.nativeSuccessBody(fileName: "影片.mp4", sidecarActive: false), "影片.mp4")
    }
}

/// E5：永久失敗一定要給看得懂的中文原因，且與 isTerminalFailure 清單一致（不漏翻成英文亂碼）。
final class FriendlyFailureCoverageTests: XCTestCase {
    func testAddedReasonsAreTranslated() {
        XCTAssertEqual(YtDlpEngine.friendlyFailure("This video is age-restricted"),
                       "年齡限制內容，YouTube 要求登入確認年齡，此來源無法下載")
        XCTAssertEqual(YtDlpEngine.friendlyFailure("The account associated with this video has been terminated"),
                       "上傳此影片的帳號已被終止")
        XCTAssertEqual(YtDlpEngine.friendlyFailure("not made this video available in your country who has blocked it in your country"),
                       "此影片在你所在地區被封鎖")
    }
    func testAgeBeatsBotMessage() {
        // 'sign in to confirm your age' 含 'sign in to confirm you'，必須回年齡而非機器人文案
        XCTAssertEqual(YtDlpEngine.friendlyFailure("Sign in to confirm your age"),
                       "年齡限制內容，YouTube 要求登入確認年齡，此來源無法下載")
        // 'not a bot' 仍走機器人文案
        XCTAssertEqual(YtDlpEngine.friendlyFailure("Sign in to confirm you're not a bot"),
                       "YouTube 要求登入驗證，此來源無法下載")
    }
    func testTerminalAndFriendlyListsAgree() {
        // 凡 isTerminalFailure==true 的代表字串，friendlyFailure 不得落到通用「無法下載：」
        let terminalSamples = [
            "ERROR: Private video", "Video unavailable", "removed by the uploader",
            "account associated with this video has been terminated",
            "This is members-only content", "Join this channel to get access",
            "Sign in to confirm your age", "This video is age-restricted",
            "who has blocked it in your country", "Sign in to confirm you're not a bot",
        ]
        for s in terminalSamples {
            XCTAssertTrue(YtDlpParse.isTerminalFailure(s), "應判永久失敗：\(s)")
            XCTAssertFalse(YtDlpEngine.friendlyFailure(s).hasPrefix("無法下載："), "應有中文原因，不該通用文案：\(s)")
        }
    }
}

/// E3：探測後的編排決策（馬拉松/只抓某段/自動跳過/續跑）。
final class DecideOutcomeTests: XCTestCase {
    let now = 1_000_000_000.0
    func testMarathonWins() {
        // 進行中且開播逾 4h → 馬拉松（即使 always 模式也擋）
        XCTAssertEqual(YtDlpParse.decideOutcome(liveStatus: "is_live", releaseTimestamp: now - 5*3600,
                                                now: now, autoMode: false, hasSection: false), .marathon)
    }
    func testSectionAfterMarathon() {
        // VOD（非馬拉松）有 section → 走片段下載
        XCTAssertEqual(YtDlpParse.decideOutcome(liveStatus: "post_live", releaseTimestamp: nil,
                                                now: now, autoMode: true, hasSection: true), .section)
    }
    func testAutoSkipsLive() {
        // 自動模式 + 進行中直播（才 2h，非馬拉松）→ 只側錄
        XCTAssertEqual(YtDlpParse.decideOutcome(liveStatus: "is_live", releaseTimestamp: now - 2*3600,
                                                now: now, autoMode: true, hasSection: false), .skippedAutoLive)
        // 自動模式 + 已結束 VOD → 續跑下載
        XCTAssertEqual(YtDlpParse.decideOutcome(liveStatus: "post_live", releaseTimestamp: nil,
                                                now: now, autoMode: true, hasSection: false), .proceed)
    }
    func testAlwaysModeProceedsEvenOnNA() {
        // E4 風險登記：always 模式 + 探測失敗(NA) → 目前仍續跑（盲試 --live-from-start）。
        // 這是「永遠嘗試」的字面語意，且 NA 多為暫時性探測失敗；若日後要對 NA 保守，改這裡＋本斷言。
        XCTAssertEqual(YtDlpParse.decideOutcome(liveStatus: "NA", releaseTimestamp: nil,
                                                now: now, autoMode: false, hasSection: false), .proceed)
    }
}
