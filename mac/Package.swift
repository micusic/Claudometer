// swift-tools-version:5.7
import PackageDescription

let package = Package(
    name: "Claudometer",
    platforms: [.macOS(.v12)],
    targets: [
        .executableTarget(name: "Claudometer", path: "Sources/Claudometer")
    ]
)
