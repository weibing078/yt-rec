import XCTest
@testable import YTRec

/// L1/L2：外部工具定位與子程序執行——出貨後最難現場 debug 的地基（找錯 binary、卡住、取消）。

final class BinaryLocatorTests: XCTestCase {
    private let candidates = ["/app/bin/ffmpeg", "/dev/ffmpeg", "/vendor/ffmpeg",
                              "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"].map { URL(fileURLWithPath: $0) }

    func testPrefersBundledOverHomebrew() {
        let execable: Set<String> = ["/app/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg"]
        XCTAssertEqual(BinaryLocator.firstExecutable(candidates) { execable.contains($0) }?.path,
                       "/app/bin/ffmpeg")   // 內嵌優先於 Homebrew
    }
    func testFallsBackToHomebrewWhenOnlyOption() {
        XCTAssertEqual(BinaryLocator.firstExecutable(candidates) { $0 == "/opt/homebrew/bin/ffmpeg" }?.path,
                       "/opt/homebrew/bin/ffmpeg")
    }
    func testNilWhenNoneExecutable() {
        XCTAssertNil(BinaryLocator.firstExecutable(candidates) { _ in false })
    }
}

final class ProcessRunnerTests: XCTestCase {
    func testStdoutAndZeroExit() async {
        let r = await ProcessRunner().run(executable: URL(fileURLWithPath: "/bin/echo"), arguments: ["hello world"])
        XCTAssertEqual(r.exitCode, 0)
        XCTAssertTrue(r.output.contains("hello world"))
        XCTAssertFalse(r.wasCancelled)
    }
    func testNonZeroExitPreserved() async {
        let r = await ProcessRunner().run(executable: URL(fileURLWithPath: "/usr/bin/false"), arguments: [])
        XCTAssertEqual(r.exitCode, 1)            // 非零退出忠實回傳（上層據此判 terminalFailure）
        XCTAssertFalse(r.wasCancelled)
    }
    func testLaunchFailureResumesWithMinusOne() async {
        // 不存在的執行檔 → 必須在有限時間內 resume（不死鎖）、exitCode -1
        let r = await ProcessRunner().run(executable: URL(fileURLWithPath: "/nonexistent/bin/nope"), arguments: [])
        XCTAssertEqual(r.exitCode, -1)
        XCTAssertTrue(r.output.hasPrefix("無法啟動："))
    }
    func testCancelStopsQuickly() async {
        let runner = ProcessRunner()
        let start = Date()
        async let result = runner.run(executable: URL(fileURLWithPath: "/bin/sh"), arguments: ["-c", "sleep 30"])
        try? await Task.sleep(nanoseconds: 400_000_000)   // 等子程序起來
        runner.cancel()
        let r = await result
        XCTAssertTrue(r.wasCancelled)
        XCTAssertLessThan(Date().timeIntervalSince(start), 10)   // SIGINT 即時生效，不會等滿 30s
    }
}

final class LineCollectorTests: XCTestCase {
    func testSplitsCRProgressLines() {
        let c = LineCollector(onLine: nil)
        c.feed(Data("a\rb\rc".utf8))   // yt-dlp 進度用 \r 覆寫同一行
        c.flush()
        XCTAssertEqual(c.tail, ["a", "b", "c"])
    }
    func testKeepsLast200Lines() {
        let c = LineCollector(onLine: nil)
        c.feed(Data((0..<250).map { "line\($0)" }.joined(separator: "\n").utf8))
        c.flush()
        XCTAssertEqual(c.tail.count, 200)
        XCTAssertEqual(c.tail.first, "line50")
        XCTAssertEqual(c.tail.last, "line249")
    }
    func testTrimsBlankAndWhitespace() {
        let c = LineCollector(onLine: nil)
        c.feed(Data("  spaced  \n\n\t\n".utf8))
        c.flush()
        XCTAssertEqual(c.tail, ["spaced"])   // 空行丟棄、前後空白/tab trim
    }
}
