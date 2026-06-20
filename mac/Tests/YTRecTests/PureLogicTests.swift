import XCTest
@testable import YTRec

final class YtURLTests: XCTestCase {
    func testWatchURL() {
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "dQw4w9WgXcQ")
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10s"), "dQw4w9WgXcQ")
    }

    func testShortAndLiveURL() {
        XCTAssertEqual(YtURL.videoID("https://youtu.be/jNQXAC9IVRw"), "jNQXAC9IVRw")
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/live/abcDEF12345?feature=share"), "abcDEF12345")
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/shorts/abcDEF12345"), "abcDEF12345")
    }

    func testInvalid() {
        XCTAssertNil(YtURL.videoID("https://example.com/watch?v=dQw4w9WgXcQ" + "x"))
        XCTAssertFalse(YtURL.isProbablyYouTube("https://vimeo.com/123"))
        XCTAssertTrue(YtURL.isProbablyYouTube("https://www.youtube.com/watch?v=dQw4w9WgXcQ"))
    }

    func testEmbedAndVPaths() {
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/embed/abcDEF12345"), "abcDEF12345")
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/v/abcDEF12345"), "abcDEF12345")
    }

    func testIDLengthBoundary() {
        // ID 必須剛好 11 碼：10 碼或 12 碼都不接受
        XCTAssertNil(YtURL.videoID("https://www.youtube.com/watch?v=abcDEF1234"))     // 10 碼
        XCTAssertNil(YtURL.videoID("https://youtu.be/abcDEF123456"))                  // 12 碼
        XCTAssertEqual(YtURL.videoID("https://www.youtube.com/watch?v=ab-DE_1234x"), "ab-DE_1234x") // 含 - _ 合法
    }

    func testSubdomainsAreYouTube() {
        XCTAssertTrue(YtURL.isProbablyYouTube("https://m.youtube.com/watch?v=dQw4w9WgXcQ"))
        XCTAssertTrue(YtURL.isProbablyYouTube("https://music.youtube.com/watch?v=dQw4w9WgXcQ"))
        XCTAssertTrue(YtURL.isProbablyYouTube("https://youtu.be/jNQXAC9IVRw"))
    }

    func testLeadingWhitespaceTrimmed() {
        // 從剪貼簿貼上常帶頭尾空白／換行
        XCTAssertEqual(YtURL.videoID("  https://youtu.be/jNQXAC9IVRw \n"), "jNQXAC9IVRw")
    }
}

final class YtDlpParseTests: XCTestCase {
    func testTerminalFailure() {
        XCTAssertTrue(YtDlpParse.isTerminalFailure("ERROR: [youtube] xxx: Private video. Sign in if..."))
        XCTAssertTrue(YtDlpParse.isTerminalFailure("ERROR: Video unavailable"))
        XCTAssertTrue(YtDlpParse.isTerminalFailure("This video is available to this channel's members-only"))
        XCTAssertFalse(YtDlpParse.isTerminalFailure("ERROR: HTTP Error 404: Not Found"))
        XCTAssertFalse(YtDlpParse.isTerminalFailure("This live event has ended"))
    }

    func testProgress() {
        let t = YtDlpParse.progressText("[download]  45.2% of ~  1.20GiB at    3.40MiB/s ETA 00:12")
        XCTAssertNotNil(t)
        XCTAssertTrue(t!.contains("45.2%"))
        XCTAssertTrue(t!.contains("3.40MiB/s"))
        XCTAssertNil(YtDlpParse.progressText("[youtube] Extracting URL"))
    }

    func testProbeParse() {
        let info = YtDlpParse.parseProbe("dQw4w9WgXcQ\t標題 有 空格\tis_live")
        XCTAssertEqual(info?.id, "dQw4w9WgXcQ")
        XCTAssertEqual(info?.title, "標題 有 空格")
        XCTAssertEqual(info?.liveStatus, "is_live")
        XCTAssertNil(info?.releaseTimestamp)            // 3 欄位（無時間戳）相容
        XCTAssertNil(YtDlpParse.parseProbe("not a probe line"))
    }

    func testProbeParseTimestamp() {
        let info = YtDlpParse.parseProbe("dQw4w9WgXcQ\t標題\tis_live\t1700000000")
        XCTAssertEqual(info?.releaseTimestamp, 1_700_000_000)
        // yt-dlp 取不到開播時間時印 "NA"
        XCTAssertNil(YtDlpParse.parseProbe("dQw4w9WgXcQ\t標題\tis_live\tNA")?.releaseTimestamp)
    }

    func testMarathon() {
        let now = 1_000_000_000.0
        // 仍在直播且開播逾 4 小時 → 馬拉松
        XCTAssertTrue(YtDlpParse.isMarathon(liveStatus: "is_live", releaseTimestamp: now - 5 * 3600, now: now))
        // 仍在直播但才開播 2 小時 → 一般流程
        XCTAssertFalse(YtDlpParse.isMarathon(liveStatus: "is_live", releaseTimestamp: now - 2 * 3600, now: now))
        // 已結束 → 不論多久都不是馬拉松
        XCTAssertFalse(YtDlpParse.isMarathon(liveStatus: "post_live", releaseTimestamp: now - 10 * 3600, now: now))
        // 取不到開播時間 → 照一般流程
        XCTAssertFalse(YtDlpParse.isMarathon(liveStatus: "is_live", releaseTimestamp: nil, now: now))
    }

    func testMarathonBoundary() {
        let now = 1_000_000_000.0
        // 剛好滿 4 小時 → 不算（門檻是「逾」4 小時，嚴格大於）
        XCTAssertFalse(YtDlpParse.isMarathon(liveStatus: "is_live", releaseTimestamp: now - 4 * 3600, now: now))
        // 4 小時又 1 秒 → 算
        XCTAssertTrue(YtDlpParse.isMarathon(liveStatus: "is_live", releaseTimestamp: now - (4 * 3600 + 1), now: now))
        // 3 小時 59 分 → 不算
        XCTAssertFalse(YtDlpParse.isMarathon(liveStatus: "is_live", releaseTimestamp: now - (3 * 3600 + 59 * 60), now: now))
    }

    func testProgressWithFragment() {
        // 直播流的逐段下載會帶 (frag N)
        let t = YtDlpParse.progressText("[download]  50.0% of ~ 10.00MiB at  1.00MiB/s (frag 42)")
        XCTAssertNotNil(t)
        XCTAssertTrue(t!.contains("50.0%"))
        XCTAssertTrue(t!.contains("第 42 段"))
        XCTAssertTrue(t!.contains("1.00MiB/s"))
    }

    func testRoundExtraction() {
        // AppState 用這個 pattern 從輪詢文字抽輪數
        XCTAssertEqual(YtDlpParse.firstMatch("素材尚未就緒，30 秒後重試（已輪詢 7 輪）", pattern: #"已輪詢 (\d+) 輪"#), "7")
        XCTAssertNil(YtDlpParse.firstMatch("沒有輪數", pattern: #"已輪詢 (\d+) 輪"#))
    }

    func testTerminalFailureMorePhrases() {
        XCTAssertTrue(YtDlpParse.isTerminalFailure("This video is age-restricted"))
        XCTAssertTrue(YtDlpParse.isTerminalFailure("The account associated with this video has been terminated"))
        XCTAssertTrue(YtDlpParse.isTerminalFailure("The uploader has not made this video available in your country who has blocked it in your country"))
        // 可重試的不可誤判為永久失敗
        XCTAssertFalse(YtDlpParse.isTerminalFailure("ERROR: unable to download video data: HTTP Error 503"))
        XCTAssertFalse(YtDlpParse.isTerminalFailure("WARNING: no video formats found"))
    }

    func testProbeParseRejectsMalformed() {
        XCTAssertNil(YtDlpParse.parseProbe("short\ttitle\tis_live"))   // id 非 11 碼
        XCTAssertNil(YtDlpParse.parseProbe("abcDEF12345\tonly_two"))    // 欄位不足 3
        XCTAssertNil(YtDlpParse.parseProbe(""))
    }

    func testProbeParseTitleWithTab() {
        // E7：標題本身含 tab → 欄位位移。從尾端取 live_status/timestamp，標題不污染決策。
        let info = YtDlpParse.parseProbe("dQw4w9WgXcQ\t標題\t含tab\tis_live\t1700000000")
        XCTAssertEqual(info?.id, "dQw4w9WgXcQ")
        XCTAssertEqual(info?.liveStatus, "is_live")           // 不是 "含tab"
        XCTAssertEqual(info?.releaseTimestamp, 1_700_000_000)
        XCTAssertEqual(info?.title, "標題\t含tab")             // 中間段都算標題
    }

    func testProgressTextPlainBytesPerSec() {
        // E5：極慢時 yt-dlp 印純 "B/s"（無 KMG 前綴）也要顯示速度
        let t = YtDlpParse.progressText("[download]   5.0% of ~1.00MiB at 0.50B/s")
        XCTAssertNotNil(t)
        XCTAssertTrue(t!.contains("下載 5.0%"))
        XCTAssertTrue(t!.contains("0.50B/s"))
    }
    func testProgressTextNonDownloadLineNil() {
        XCTAssertNil(YtDlpParse.progressText("[info] something else"))
    }
}

final class M3U8BuilderTests: XCTestCase {
    func testPlaylist() {
        let text = M3U8Builder.playlist(segments: [
            .init(name: "seg_00000.m4s", duration: 2.0),
            .init(name: "seg_00001.m4s", duration: 1.96),
        ], ended: false)
        XCTAssertTrue(text.contains("#EXT-X-MAP:URI=\"seg_init.mp4\""))
        XCTAssertTrue(text.contains("#EXTINF:2.000,\nseg_00000.m4s"))
        XCTAssertTrue(text.contains("#EXTINF:1.960,\nseg_00001.m4s"))
        XCTAssertTrue(text.contains("#EXT-X-PLAYLIST-TYPE:EVENT"))
        XCTAssertFalse(text.contains("#EXT-X-ENDLIST"))
    }

    func testEnded() {
        let text = M3U8Builder.playlist(segments: [.init(name: "seg_00000.m4s", duration: 2)], ended: true)
        XCTAssertTrue(text.hasSuffix("#EXT-X-ENDLIST\n"))
    }

    func testEmptySegments() {
        // 剛開錄、還沒有切片：仍要是合法 header（含 MAP），但無 EXTINF、無 ENDLIST
        let text = M3U8Builder.playlist(segments: [], ended: false)
        XCTAssertTrue(text.contains("#EXTM3U"))
        XCTAssertTrue(text.contains("#EXT-X-MAP:URI=\"seg_init.mp4\""))
        XCTAssertFalse(text.contains("#EXTINF"))
        XCTAssertFalse(text.contains("#EXT-X-ENDLIST"))
    }

    func testZeroDurationClamped() {
        // VFR 靜止畫面可能算出 0 時長 → 夾到 0.001 以免 EXTINF 為 0 壞掉播放器
        let text = M3U8Builder.playlist(segments: [.init(name: "seg_00000.m4s", duration: 0)], ended: false)
        XCTAssertTrue(text.contains("#EXTINF:0.001,"))
    }
    func testNegativeDurationClampedAndMapAndTargetDuration() {
        let text = M3U8Builder.playlist(segments: [.init(name: "seg_00000.m4s", duration: -5)],
                                        ended: false, targetDuration: 10)
        XCTAssertTrue(text.contains("#EXTINF:0.001,"))                    // 負時長也夾正
        XCTAssertTrue(text.contains("#EXT-X-MAP:URI=\"seg_init.mp4\""))   // init 映射存在（少了 reader 無法解碼）
        XCTAssertTrue(text.contains("#EXT-X-TARGETDURATION:10"))          // 自訂 targetDuration
    }
}

final class FileUtilTests: XCTestCase {
    func testSanitize() {
        XCTAssertEqual(FileUtil.sanitize("a/b:c\nd"), "a b c d")
        XCTAssertEqual(FileUtil.sanitize("   "), "未命名")
        XCTAssertEqual(FileUtil.sanitize(String(repeating: "x", count: 100)).count, 60)
    }

    func testSanitizeAllForbiddenChars() {
        // 路徑分隔、冒號、反斜線、null、換行、歸位、tab 全換成空白
        XCTAssertEqual(FileUtil.sanitize("a/b:c\\d\u{0}e\nf\rg\th"), "a b c d e f g h")
    }

    func testFormatDuration() {
        XCTAssertEqual(FileUtil.formatDuration(0), "00:00")
        XCTAssertEqual(FileUtil.formatDuration(65), "01:05")
        XCTAssertEqual(FileUtil.formatDuration(599), "09:59")
        XCTAssertEqual(FileUtil.formatDuration(600), "10:00")
        XCTAssertEqual(FileUtil.formatDuration(3661), "1:01:01")    // 跨小時改用 h:mm:ss
        // 守門：負數 / NaN / 無限大 都回 00:00，不崩
        XCTAssertEqual(FileUtil.formatDuration(-5), "00:00")
        XCTAssertEqual(FileUtil.formatDuration(.nan), "00:00")
        XCTAssertEqual(FileUtil.formatDuration(.infinity), "00:00")
    }
}

final class DiskGuardTests: XCTestCase {
    let gb: Int64 = 1_000_000_000

    func testPreCheck() {
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: 20 * gb), .ok)
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: 12 * gb), .warn(12 * gb))   // 8–15GB 警告
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: 5 * gb), .refuse(5 * gb))   // <8GB 拒錄
    }

    func testStopRecording() {
        XCTAssertFalse(DiskGuard.shouldStopRecording(freeBytes: 12 * gb))
        XCTAssertTrue(DiskGuard.shouldStopRecording(freeBytes: 8 * gb))          // <10GB 收工
    }

    func testPreCheckBoundaries() {
        // 剛好 8GB → 不是 refuse，是 warn（門檻是嚴格小於 8 才拒）
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: 8 * gb), .warn(8 * gb))
        // 剛好 15GB → ok（嚴格小於 15 才警告）
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: 15 * gb), .ok)
        // 7GB → refuse
        XCTAssertEqual(DiskGuard.preCheck(freeBytes: 7 * gb), .refuse(7 * gb))
    }

    func testStopRecordingBoundary() {
        // 剛好 10GB → 不收工（嚴格小於 10 才收）
        XCTAssertFalse(DiskGuard.shouldStopRecording(freeBytes: 10 * gb))
        XCTAssertTrue(DiskGuard.shouldStopRecording(freeBytes: 9 * gb))
    }
}

/// 「只抓 VOD 某段」的時間碼解析與 yt-dlp section 參數（純邏輯）。
final class TimecodeTests: XCTestCase {
    func testParseSeconds() {
        XCTAssertEqual(Timecode.parse("90"), 90)
        XCTAssertEqual(Timecode.parse("0"), 0)
        XCTAssertEqual(Timecode.parse(" 12 "), 12)        // 去空白
    }

    func testParseMinuteSecond() {
        XCTAssertEqual(Timecode.parse("5:30"), 330)
        XCTAssertEqual(Timecode.parse("0:05"), 5)
    }

    func testParseHourMinuteSecond() {
        XCTAssertEqual(Timecode.parse("1:05:30"), 3930)
        XCTAssertEqual(Timecode.parse("2:00:00"), 7200)
    }

    func testParseInvalid() {
        XCTAssertNil(Timecode.parse(""))
        XCTAssertNil(Timecode.parse("abc"))
        XCTAssertNil(Timecode.parse("5:"))                // 空段
        XCTAssertNil(Timecode.parse("1:2:3:4"))           // 超過 3 段
        XCTAssertNil(Timecode.parse("-5"))                // 負數
    }

    func testFormatRoundTrip() {
        XCTAssertEqual(Timecode.format(3930), "01:05:30")
        XCTAssertEqual(Timecode.format(5), "00:00:05")
    }

    func testDownloadSectionArg() {
        // 正常範圍
        XCTAssertEqual(Timecode.downloadSectionArg(startSec: 330, endSec: 465), "*00:05:30-00:07:45")
        // 出點不在入點之後 → nil
        XCTAssertNil(Timecode.downloadSectionArg(startSec: 100, endSec: 100))
        XCTAssertNil(Timecode.downloadSectionArg(startSec: 200, endSec: 100))
    }

    func testSectionFromRawStrings() {
        let s = Timecode.section(from: "5:30", to: "7:45")
        XCTAssertEqual(s?.arg, "*00:05:30-00:07:45")
        XCTAssertEqual(s?.label, "5:30–7:45")
        // 任一無效 → nil
        XCTAssertNil(Timecode.section(from: "5:30", to: "abc"))
        XCTAssertNil(Timecode.section(from: "7:45", to: "5:30"))   // 反了
    }
}

