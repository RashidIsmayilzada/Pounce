import AppKit
import PounceKit
import SwiftUI

/// Renders the frames of a short demo video against a running Engine: the shortcut, typing a Query with real
/// Results after every keystroke, a second Query, and an end card. Writes numbered PNGs plus an ffmpeg concat list.
///   PounceLauncher --demo-frames OUT_DIR SHOWN_ROOT   (SHOWN_ROOT is displayed as ~/Documents)
@MainActor
enum Demo {

    static let canvas = CGSize(width: 1200, height: 675)
    static let scale: CGFloat = 1.6   // 1920 x 1080 pixels

    enum Scene {
        case desktop(keycap: Bool)
        case panel(query: String, caption: String?)
        case endCard
    }

    static func render(to folder: URL, shownRoot: String, engine: SearchEngine) {
        let model = SearchModel(engine: engine)
        var frames: [(Scene, Double)] = [(.desktop(keycap: false), 0.6), (.desktop(keycap: true), 0.6),
                                         (.panel(query: "", caption: nil), 0.4)]
        let first = "how much did I earn last year"
        for end in first.indices.map({ first.index(after: $0) }) {
            frames.append((.panel(query: String(first[..<end]), caption: nil), 0.085))
        }
        frames.append((.panel(query: first, caption: "Found by meaning: the word “earn” isn’t in the statement"), 3.4))
        frames.append((.panel(query: "", caption: nil), 0.35))
        let second = "that dessert recipe"
        for end in second.indices.map({ second.index(after: $0) }) {
            frames.append((.panel(query: String(second[..<end]), caption: nil), 0.075))
        }
        frames.append((.panel(query: second, caption: "Results update with every keystroke, all on your Mac"), 2.4))
        frames.append((.endCard, 2.6))

        Task { @MainActor in
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            var list = ""
            for (index, (scene, seconds)) in frames.enumerated() {
                if case .panel(let query, _) = scene { await settle(model, on: query) }
                let name = String(format: "frame-%03d.png", index)
                write(DemoFrame(scene: scene, model: model, shownRoot: shownRoot), to: folder.appendingPathComponent(name))
                list += "file '\(name)'\nduration \(seconds)\n"
            }
            list += "file '\(String(format: "frame-%03d.png", frames.count - 1))'\n"   // ffmpeg needs the last file twice
            try? list.write(to: folder.appendingPathComponent("frames.txt"), atomically: true, encoding: .utf8)
            let total = frames.reduce(0) { $0 + $1.1 }
            print("wrote \(frames.count) frames, \(String(format: "%.1f", total)) s, to \(folder.path)")
            exit(0)
        }
        NSApplication.shared.run()
    }

    /// Types the Query and waits for its Final Results, as the real panel would show them.
    private static func settle(_ model: SearchModel, on query: String) async {
        model.query = query
        let text = query.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return }
        for _ in 0..<300 where !(model.isFinal && model.shownQuery == text) {
            try? await Task.sleep(for: .milliseconds(10))
        }
    }

    private static func write(_ view: some View, to file: URL) {
        let renderer = ImageRenderer(content: view.environment(\.colorScheme, .light))
        renderer.scale = scale
        guard let image = renderer.cgImage else { return }
        let bitmap = NSBitmapImageRep(cgImage: image)
        try? bitmap.representation(using: .png, properties: [:])?.write(to: file)
    }
}

/// One frame: a desktop-like background with the panel in the upper third, an optional caption, or the end card.
private struct DemoFrame: View {
    let scene: Demo.Scene
    let model: SearchModel
    let shownRoot: String

    var body: some View {
        ZStack(alignment: .top) {
            LinearGradient(colors: [Color(red: 0.16, green: 0.20, blue: 0.42), Color(red: 0.45, green: 0.30, blue: 0.62),
                                    Color(red: 0.93, green: 0.55, blue: 0.45)],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            switch scene {
            case .desktop(let keycap):
                menuBar
                if keycap { keycaps.frame(maxHeight: .infinity) }
            case .panel(let query, let caption):
                menuBar
                LauncherView(model: model, renderedQuery: query, displayPath: display)
                    .shadow(color: .black.opacity(0.35), radius: 30, y: 12)
                    .padding(.top, 120)
                if let caption { captionView(caption) }
            case .endCard:
                VStack(spacing: 14) {
                    Image(systemName: "magnifyingglass").font(.system(size: 54, weight: .semibold))
                    Text("Pounce").font(.system(size: 60, weight: .bold))
                    Text("Private, on-device semantic search for macOS").font(.system(size: 24, weight: .medium))
                    Text("Java 25 · Spring Boot · Lucene · ONNX · SwiftUI").font(.system(size: 18)).opacity(0.85)
                    Text("github.com/RashidIsmayilzada/Pounce").font(.system(size: 18, design: .monospaced))
                        .padding(.top, 10).opacity(0.9)
                }
                .foregroundStyle(.white)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(width: Demo.canvas.width, height: Demo.canvas.height)
    }

    private var menuBar: some View {
        HStack(spacing: 14) {
            Spacer()
            Image(systemName: "magnifyingglass").font(.system(size: 13, weight: .semibold))
            Text("Mon 24 Nov  09:41").font(.system(size: 13, weight: .medium))
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 16)
        .frame(height: 26)
        .background(.black.opacity(0.18))
    }

    private var keycaps: some View {
        HStack(spacing: 10) {
            keycap("⌥ option")
            Text("+").font(.system(size: 28, weight: .semibold)).foregroundStyle(.white)
            keycap("space")
        }
    }

    private func keycap(_ label: String) -> some View {
        Text(label)
            .font(.system(size: 24, weight: .semibold))
            .foregroundStyle(Color(white: 0.2))
            .padding(.horizontal, 26).padding(.vertical, 14)
            .background(Color(white: 0.96), in: RoundedRectangle(cornerRadius: 12))
            .shadow(color: .black.opacity(0.3), radius: 8, y: 4)
    }

    private func captionView(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 20, weight: .medium))
            .foregroundStyle(.white)
            .padding(.horizontal, 22).padding(.vertical, 12)
            .background(.black.opacity(0.55), in: Capsule())
            .frame(maxHeight: .infinity, alignment: .bottom)
            .padding(.bottom, 40)
    }

    private func display(_ path: String) -> String {
        path.hasPrefix(shownRoot) ? "~/Documents" + path.dropFirst(shownRoot.count) : path
    }
}
