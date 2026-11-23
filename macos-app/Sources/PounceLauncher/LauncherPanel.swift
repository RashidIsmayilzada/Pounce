import AppKit
import PounceKit
import Quartz
import SwiftUI

/// A borderless floating panel that takes keyboard focus, hides when it loses it, and hosts Quick Look.
final class LauncherPanel: NSPanel, @preconcurrency QLPreviewPanelDataSource {

    var previewItem: (() -> URL?) = { nil }

    init(content: NSView) {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 680, height: 420),
                   styleMask: [.borderless, .fullSizeContentView],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .floating
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        hidesOnDeactivate = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        contentView = content
    }

    override var canBecomeKey: Bool { true }

    /// Upper third of the screen with the mouse, like Spotlight.
    func showCentered() {
        let screen = NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
        if let visible = screen?.visibleFrame {
            let size = contentView?.fittingSize ?? frame.size
            setFrame(NSRect(x: visible.midX - size.width / 2, y: visible.maxY - visible.height / 4 - size.height,
                            width: size.width, height: size.height), display: false)
        }
        NSApp.activate()
        makeKeyAndOrderFront(nil)
    }

    override func resignKey() {
        super.resignKey()
        if !(QLPreviewPanel.sharedPreviewPanelExists() && QLPreviewPanel.shared().isVisible) { orderOut(nil) }
    }

    // MARK: Quick Look

    override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }
    // Quick Look calls these on the main thread.
    override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated { panel.dataSource = self }
    }
    override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated { panel.dataSource = nil }
    }

    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int { previewItem() == nil ? 0 : 1 }
    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> (any QLPreviewItem)! {
        previewItem() as NSURL?
    }
}
