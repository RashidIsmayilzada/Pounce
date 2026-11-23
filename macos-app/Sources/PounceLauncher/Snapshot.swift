import AppKit
import PounceKit
import SwiftUI

/// Renders the panel for one Query against the running Engine into a PNG, for checking the UI without a screen.
@MainActor
enum Snapshot {
    static func render(query: String, engine: SearchEngine, to output: URL) {
        let model = SearchModel(engine: engine)
        model.query = query
        Task { @MainActor in
            for _ in 0..<200 where !(model.isFinal && (model.message != nil || !model.results.isEmpty)) {
                try? await Task.sleep(for: .milliseconds(25))
            }
            let renderer = ImageRenderer(content: LauncherView(model: model).environment(\.colorScheme, .light))
            renderer.scale = 2
            if let image = renderer.nsImage, let tiff = image.tiffRepresentation,
               let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) {
                try? png.write(to: output)
                print("wrote \(output.path): \(model.results.count) results, message: \(model.message ?? "none")")
            }
            exit(0)
        }
        NSApplication.shared.run()
    }
}
