import CoreGraphics

/// 純擷取幾何（與 Windows `YtRec.Core/CaptureGeometry.cs` 對齊；behavior-spec「Capture geometry」）。
/// 成品的像素尺寸只由**來源影片**＋畫質設定決定，與螢幕大小／DPI 無關——這就是錄影在任何機器上都
/// 一致的原因。macOS 把離屏視窗直接做成輸出尺寸交給 SCK 縮放，故不需要 Windows 的 FitWindow。
enum Orientation { case landscape, portrait }

enum CaptureGeometry {
    /// 來源比寬更高 → 直式；正方形／橫式／未知（videoWidth==0）→ 橫式。
    static func orientation(videoWidth: Int, videoHeight: Int) -> Orientation {
        (videoWidth > 0 && videoHeight > videoWidth) ? .portrait : .landscape
    }

    /// 輸出尺寸（quality＝長邊目標，1080 預設或 720）。橫式→1920×1080／1280×720；直式→1080×1920／720×1280。永遠偶數。
    static func outputSize(videoWidth: Int, videoHeight: Int, quality: Int) -> CGSize {
        let shortEdge = quality == 720 ? 720 : 1080
        let longEdge = shortEdge == 720 ? 1280 : 1920
        return orientation(videoWidth: videoWidth, videoHeight: videoHeight) == .portrait
            ? CGSize(width: shortEdge, height: longEdge)
            : CGSize(width: longEdge, height: shortEdge)
    }
}
