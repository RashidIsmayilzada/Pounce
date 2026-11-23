import AppKit
import PounceKit
import Quartz
import SwiftUI

/// Wires the hotkey, the panel and the keyboard: ↑↓ select, ⏎ open, ⌘⏎ show in Finder, ⌘Y Quick Look, Esc close.
@MainActor
final class LauncherDelegate: NSObject, NSApplicationDelegate {

    private let model = SearchModel(engine: Configuration.engine())
    private var panel: LauncherPanel!
    private var hotKey: HotKey?
    private var keyMonitor: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let view = LauncherView(model: model, onOpen: { [weak self] in self?.open($0) })
        let hosting = NSHostingView(rootView: view)
        hosting.sizingOptions = [.preferredContentSize]
        panel = LauncherPanel(content: hosting)
        panel.previewItem = { [weak self] in self?.model.selected.map { URL(fileURLWithPath: $0.path) } }
        hotKey = HotKey.optionSpace { [weak self] in self?.toggle() }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            self?.handle(event) ?? event
        }
        print("Pounce Launcher ready: press ⌥Space")
    }

    private func toggle() {
        if panel.isVisible {
            panel.orderOut(nil)
        } else {
            panel.showCentered()
        }
    }

    /// Returns nil when the key was handled, so the search field doesn't also get it.
    private func handle(_ event: NSEvent) -> NSEvent? {
        guard panel.isKeyWindow else { return event }
        let command = event.modifierFlags.contains(.command)
        switch Int(event.keyCode) {
        case 125: model.moveSelection(by: 1)                                  // ↓
        case 126: model.moveSelection(by: -1)                                 // ↑
        case 53: panel.orderOut(nil)                                         // Esc
        case 36, 76:                                                          // ⏎
            guard let item = model.selected else { return nil }
            command ? reveal(item) : open(item)
        case 16 where command:                                                // ⌘Y
            togglePreview()
        default:
            return event
        }
        return nil
    }

    private func open(_ item: ResultItem) {
        panel.orderOut(nil)
        NSWorkspace.shared.open(URL(fileURLWithPath: item.path))
    }

    private func reveal(_ item: ResultItem) {
        panel.orderOut(nil)
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: item.path)])
    }

    private func togglePreview() {
        guard model.selected != nil else { return }
        let preview = QLPreviewPanel.shared()!
        if preview.isVisible { preview.orderOut(nil) } else { preview.makeKeyAndOrderFront(nil) }
    }
}
