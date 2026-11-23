import AppKit
import PounceKit
import Quartz
import SwiftUI

/// A borderless floating panel that takes keyboard focus, hides when it loses it, and hosts Quick Look.
final class LauncherPanel: NSPanel, @preconcurrency QLPreviewPanelDataSource {

    var previewItem: (() -> URL?) = { nil }

    init(content: NSView) {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 680, height: 420),
                   // Non-activating: the panel takes keyboard focus without making Pounce the active app.
                   // Since macOS 14 an app can't bring itself to the front from a hotkey (cooperative
                   // activation), so a panel that needed Pounce to be active never appeared.
                   styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .floating
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        hidesOnDeactivate = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        contentView = content
    }

    override var canBecomeKey: Bool { true }

    /// The search bar alone: the smallest the panel ever is.
    static let minimumSize = NSSize(width: 680, height: 56)

    /// Upper third of the screen with the mouse, like Spotlight.
    func showCentered() {
        let screen = NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
        // Lay the SwiftUI content out first: before that its fitting size is zero and the panel would be invisible.
        contentView?.layoutSubtreeIfNeeded()
        let size = Self.atLeastMinimum(contentView?.fittingSize ?? .zero)
        if let visible = screen?.visibleFrame {
            setFrame(NSRect(x: visible.midX - size.width / 2, y: visible.maxY - visible.height / 4 - size.height,
                            width: size.width, height: size.height), display: true)
        }
        makeKeyAndOrderFront(nil)
        launcherLog.notice("panel shown: key \(self.isKeyWindow), visible \(self.isVisible), app active \(NSApp.isActive)")
        let screenFrame = screen?.frame ?? .zero
        launcherLog.notice("panel frame \(NSStringFromRect(self.frame), privacy: .public) on screen \(NSStringFromRect(screenFrame), privacy: .public), active space \(self.isOnActiveSpace), occlusion visible \(self.occlusionState.contains(.visible)), alpha \(self.alphaValue), content fitting \(NSStringFromSize(self.contentView?.fittingSize ?? .zero), privacy: .public)")
    }

    /// Follows the content's height as Results arrive, keeping the top edge where it is.
    func fit(contentHeight: CGFloat) {
        let size = Self.atLeastMinimum(NSSize(width: frame.width, height: contentHeight))
        guard abs(size.height - frame.height) > 0.5 else { return }
        setFrame(NSRect(x: frame.minX, y: frame.maxY - size.height, width: size.width, height: size.height), display: true)
    }

    private static func atLeastMinimum(_ size: NSSize) -> NSSize {
        NSSize(width: max(size.width, minimumSize.width), height: max(size.height, minimumSize.height))
    }

    private var outsideClicks: Any?

    /// Losing keyboard focus alone does not hide the panel (macOS may briefly hand focus back to the app you
    /// were in); a click outside it does, like Spotlight.
    override func resignKey() {
        super.resignKey()
        let front = NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? "?"
        launcherLog.notice("panel resigned key: app active \(NSApp.isActive), frontmost \(front, privacy: .public)")
    }

    override func makeKeyAndOrderFront(_ sender: Any?) {
        super.makeKeyAndOrderFront(sender)
        if outsideClicks == nil {
            outsideClicks = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
                MainActor.assumeIsolated { self?.orderOut(nil) }
            }
        }
    }

    override func orderOut(_ sender: Any?) {
        super.orderOut(sender)
        if let outsideClicks { NSEvent.removeMonitor(outsideClicks) }
        outsideClicks = nil
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
