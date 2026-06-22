import XCTest
@testable import YTRec

final class AppUpdateTests: XCTestCase {
    func testIsNewerComparesNumerically() {
        XCTAssertTrue(AppUpdate.isNewer(current: "1.0.0", latest: "1.0.1"))
        XCTAssertTrue(AppUpdate.isNewer(current: "1.0.0", latest: "1.1.0"))
        XCTAssertTrue(AppUpdate.isNewer(current: "1.2.3", latest: "1.10.0"))   // 數字比較，不是字典序
        XCTAssertTrue(AppUpdate.isNewer(current: "v1.0.0", latest: "v1.2.0"))  // 容忍前導 v
        XCTAssertTrue(AppUpdate.isNewer(current: "1.0", latest: "1.0.1"))      // 缺位補 0
        XCTAssertTrue(AppUpdate.isNewer(current: "", latest: "1.0.0"))         // current 未知 → 提示
        XCTAssertFalse(AppUpdate.isNewer(current: "1.0.0", latest: "1.0.0"))   // 相同 → 不提示
        XCTAssertFalse(AppUpdate.isNewer(current: "1.1.0", latest: "1.0.9"))   // latest 較舊 → 不提示
        XCTAssertFalse(AppUpdate.isNewer(current: "1.0.0", latest: "1.0.0-beta")) // 後綴忽略、core 相同
        XCTAssertFalse(AppUpdate.isNewer(current: "1.0.0", latest: ""))        // latest 無法解析 → 絕不提示
    }

    func testParseManifest() {
        let json = """
        {"version":"1.1.0","notes":{"zh-Hant":"倒帶預覽","en":"Rewind"},
         "mac":{"url":"https://x/Y.dmg","minOS":"14.4"},"page":"https://p/#d"}
        """
        let m = AppUpdate.parseManifest(json, platform: "mac", lang: "zh-Hant")
        XCTAssertEqual(m?.version, "1.1.0")
        XCTAssertEqual(m?.notes, "倒帶預覽")
        XCTAssertEqual(m?.url, "https://x/Y.dmg")
        XCTAssertEqual(m?.page, "https://p/#d")
        XCTAssertEqual(AppUpdate.parseManifest(json, platform: "win", lang: "ja")?.notes, "Rewind") // 退回 en
        XCTAssertNil(AppUpdate.parseManifest("not json", platform: "mac"))
        XCTAssertNil(AppUpdate.parseManifest(nil, platform: "mac"))
    }
}
