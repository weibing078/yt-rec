import XCTest
@testable import YTRec

/// L1：螢幕無關的擷取幾何契約（與 C# CaptureGeometryTests 同案例）。
final class CaptureGeometryTests: XCTestCase {
    func testOrientationFromSourceDims() {
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 1920, videoHeight: 1080), .landscape)
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 3840, videoHeight: 2160), .landscape)
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 640, videoHeight: 480), .landscape)
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 2560, videoHeight: 1080), .landscape)
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 100, videoHeight: 100), .landscape) // 正方→橫
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 1080, videoHeight: 1920), .portrait)
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 405, videoHeight: 720), .portrait)
        XCTAssertEqual(CaptureGeometry.orientation(videoWidth: 0, videoHeight: 0), .landscape)   // 未知→橫
    }

    func testOutputSizeIsContentDriven() {
        func sz(_ vw: Int, _ vh: Int, _ q: Int) -> (Int, Int) {
            let s = CaptureGeometry.outputSize(videoWidth: vw, videoHeight: vh, quality: q)
            return (Int(s.width), Int(s.height))
        }
        XCTAssertEqual(sz(1920, 1080, 1080).0, 1920); XCTAssertEqual(sz(1920, 1080, 1080).1, 1080)
        XCTAssertEqual(sz(1280, 720, 720).0, 1280);   XCTAssertEqual(sz(1280, 720, 720).1, 720)
        XCTAssertEqual(sz(3840, 2160, 1080).0, 1920); XCTAssertEqual(sz(3840, 2160, 1080).1, 1080) // 4K 來源仍輸出 1080
        XCTAssertEqual(sz(1080, 1920, 1080).0, 1080); XCTAssertEqual(sz(1080, 1920, 1080).1, 1920) // 直式
        XCTAssertEqual(sz(405, 720, 720).0, 720);     XCTAssertEqual(sz(405, 720, 720).1, 1280)    // 直式 720
        XCTAssertEqual(sz(640, 480, 1080).0, 1920);   XCTAssertEqual(sz(640, 480, 1080).1, 1080)   // 4:3 → 16:9
    }

    func testOutputSizeAlwaysEven() {
        for (vw, vh) in [(1920, 1080), (1080, 1920), (405, 720), (333, 777)] {
            for q in [1080, 720] {
                let s = CaptureGeometry.outputSize(videoWidth: vw, videoHeight: vh, quality: q)
                XCTAssertEqual(Int(s.width) % 2, 0)
                XCTAssertEqual(Int(s.height) % 2, 0)
            }
        }
    }
}
