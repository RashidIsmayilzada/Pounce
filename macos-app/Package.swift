// swift-tools-version:6.0
// The ⌥Space Launcher. PounceKit holds everything testable (Engine client, search-as-you-type);
// PounceLauncher is the thin AppKit/SwiftUI shell around it.
import PackageDescription

let package = Package(
    name: "PounceLauncher",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "PounceLauncher", targets: ["PounceLauncher"]),
    ],
    targets: [
        .target(name: "PounceKit"),
        .executableTarget(name: "PounceLauncher", dependencies: ["PounceKit"]),
        .testTarget(name: "PounceKitTests", dependencies: ["PounceKit"]),
    ]
)
