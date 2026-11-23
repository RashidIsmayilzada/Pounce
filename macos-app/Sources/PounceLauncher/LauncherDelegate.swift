import AppKit
import PounceKit
import Quartz
import SwiftUI

/// Wires the hotkey, the panel and the keyboard: ↑↓ select, ⏎ open, ⌘⏎ show in Finder, ⌘Y Quick Look, Esc close.
@MainActor
final class LauncherDelegate: NSObject, NSApplicationDelegate {

    private let model = SearchModel(engine: Configuration.engine(), apps: FolderAppSource.standard)
    private var panel: LauncherPanel!
    private var hotKey: HotKey?
    private var keyMonitor: Any?
    private var supervisor: EngineSupervisor?
    private var menu: StatusMenu?

    func applicationDidFinishLaunching(_ notification: Notification) {
        startBundledEngine()
        menu = StatusMenu(supervisor: supervisor, onSearch: { [weak self] in self?.toggle() })
        let view = LauncherView(model: model, onOpen: { [weak self] in self?.open($0) },
                                onHeightChange: { [weak self] in self?.panel?.fit(contentHeight: $0) })
        let hosting = NSHostingView(rootView: view)
        hosting.sizingOptions = [.preferredContentSize]
        panel = LauncherPanel(content: hosting)
        panel.previewItem = { [weak self] in self?.model.selected.map { URL(fileURLWithPath: Self.path(of: $0)) } }
        // Every open starts with an empty search bar; clearing while hidden also shrinks the panel back.
        panel.onHide = { [weak self] in self?.model.query = "" }
        hotKey = HotKey.named(Configuration.hotKeyName) { [weak self] in self?.toggle() }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            self?.handle(event) ?? event
        }
        Task { await model.reloadApps() }
        print("Pounce Launcher ready: press ⌥Space")
    }

    func applicationWillTerminate(_ notification: Notification) {
        supervisor?.stop()
    }

    /// Inside Pounce.app the Launcher owns the bundled Engine; run from `swift run`, the Engine is started by hand.
    private func startBundledEngine() {
        guard let resources = Bundle.main.resourceURL,
              FileManager.default.fileExists(atPath: resources.appendingPathComponent("engine/pounce-engine.jar").path)
        else { return }
        let layout = EngineLayout.bundled(resources: resources)
        let supervisor = EngineSupervisor(
            layout: layout,
            starter: FoundationProcessStarter(log: layout.home.appendingPathComponent("logs/engine.log")),
            probe: HTTPEngineProbe(baseURL: Configuration.engine().baseURL))
        self.supervisor = supervisor
        Task { await supervisor.start() }
    }

    private var lastToggle = Date.distantPast

    private func toggle() {
        // A held shortcut repeats; only the first press counts.
        guard Date().timeIntervalSince(lastToggle) > 0.35 else { return }
        lastToggle = Date()
        launcherLog.notice("toggle: panel visible \(self.panel.isVisible), app active \(NSApp.isActive)")
        if panel.isVisible {
            panel.orderOut(nil)
        } else {
            Task { await model.reloadApps() }   // pick up apps installed or removed since last time
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

    private func open(_ item: SearchModel.Item) {
        panel.orderOut(nil)
        switch item {
        case .app(let app):
            NSWorkspace.shared.open(URL(fileURLWithPath: app.path))
        case .file(let file):
            let obsidianInstalled = NSWorkspace.shared.urlForApplication(toOpen: URL(string: "obsidian://open")!) != nil
            NSWorkspace.shared.open(OpenTarget.url(for: file.path, obsidianInstalled: obsidianInstalled))
        }
    }

    private func reveal(_ item: SearchModel.Item) {
        panel.orderOut(nil)
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: Self.path(of: item))])
    }

    private static func path(of item: SearchModel.Item) -> String {
        switch item {
        case .app(let app): app.path
        case .file(let file): file.path
        }
    }

    private func togglePreview() {
        guard model.selected != nil else { return }
        let preview = QLPreviewPanel.shared()!
        if preview.isVisible { preview.orderOut(nil) } else { preview.makeKeyAndOrderFront(nil) }
    }
}
