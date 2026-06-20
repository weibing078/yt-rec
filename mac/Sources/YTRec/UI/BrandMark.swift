import SwiftUI

/// 品牌標記（logo mark）：取景框（四角圓角括號）＋ 圓角播放三角。
/// 向量重畫自 `branding/YTRec-MenuBar.svg`（座標同為 100×100），可任意縮放、上色。
/// 用在介面內當品牌標記——不是單純的「框框」，框裡有播放三角才是完整 logo。
struct BrandMark: View {
    var color: Color = .lcSignal

    var body: some View {
        GeometryReader { geo in
            let d = min(geo.size.width, geo.size.height)
            let s = d / 100
            ZStack {
                ViewfinderBrackets()
                    .stroke(color, style: StrokeStyle(lineWidth: 9 * s, lineCap: .round, lineJoin: .round))
                // 填色＋round-join 描邊，讓三角的角變圓（呼應括號圓端點）
                PlayTriangle().fill(color)
                PlayTriangle()
                    .stroke(color, style: StrokeStyle(lineWidth: 7 * s, lineCap: .round, lineJoin: .round))
            }
            .frame(width: d, height: d)
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityLabel(AppInfo.displayName)
    }
}

/// 取景框四角括號（100×100 座標）。
private struct ViewfinderBrackets: Shape {
    func path(in rect: CGRect) -> Path {
        let s = min(rect.width, rect.height) / 100
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * s, y: y * s) }
        var path = Path()
        // 左上
        path.move(to: p(18, 38)); path.addLine(to: p(18, 26))
        path.addQuadCurve(to: p(26, 18), control: p(18, 18)); path.addLine(to: p(38, 18))
        // 右上
        path.move(to: p(62, 18)); path.addLine(to: p(74, 18))
        path.addQuadCurve(to: p(82, 26), control: p(82, 18)); path.addLine(to: p(82, 38))
        // 右下
        path.move(to: p(82, 62)); path.addLine(to: p(82, 74))
        path.addQuadCurve(to: p(74, 82), control: p(82, 82)); path.addLine(to: p(62, 82))
        // 左下
        path.move(to: p(38, 82)); path.addLine(to: p(26, 82))
        path.addQuadCurve(to: p(18, 74), control: p(18, 82)); path.addLine(to: p(18, 62))
        return path
    }
}

/// 中央播放三角（100×100 座標）。
private struct PlayTriangle: Shape {
    func path(in rect: CGRect) -> Path {
        let s = min(rect.width, rect.height) / 100
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * s, y: y * s) }
        var path = Path()
        path.move(to: p(40, 36)); path.addLine(to: p(40, 64)); path.addLine(to: p(66, 50))
        path.closeSubpath()
        return path
    }
}
