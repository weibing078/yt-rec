// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "YTRec",
    platforms: [.macOS("14.4")],
    targets: [
        .executableTarget(
            name: "YTRec",
            path: "Sources/YTRec"
        ),
        .testTarget(
            name: "YTRecTests",
            dependencies: ["YTRec"],
            path: "Tests/YTRecTests"
        ),
    ]
)
