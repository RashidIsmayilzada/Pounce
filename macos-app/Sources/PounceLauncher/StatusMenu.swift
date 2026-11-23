import AppKit
import PounceKit
import ServiceManagement

/// The menu-bar icon: open the search panel, see the Engine's state, launch at login, quit.
@MainActor
final class StatusMenu: NSObject, NSMenuDelegate {

    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let supervisor: EngineSupervisor?
    private let onSearch: () -> Void
    private let state = NSMenuItem(title: "", action: nil, keyEquivalent: "")
    private let loginItem = NSMenuItem(title: "Launch at Login", action: #selector(toggleLogin), keyEquivalent: "")

    init(supervisor: EngineSupervisor?, onSearch: @escaping () -> Void) {
        self.supervisor = supervisor
        self.onSearch = onSearch
        super.init()
        item.button?.image = NSImage(systemSymbolName: "magnifyingglass", accessibilityDescription: "Pounce")
        let menu = NSMenu()
        menu.delegate = self
        let search = NSMenuItem(title: "Search…", action: #selector(search), keyEquivalent: " ")
        search.keyEquivalentModifierMask = [.option]
        search.target = self
        loginItem.target = self
        let quit = NSMenuItem(title: "Quit Pounce", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        state.isEnabled = false
        [search, .separator(), state, loginItem, .separator(), quit].forEach(menu.addItem)
        item.menu = menu
    }

    func menuWillOpen(_ menu: NSMenu) {
        state.title = switch supervisor?.state {
        case .running?, .reusing?: "Search engine running"
        case .failed(let message)?: message
        case .stopped?: "Search engine stopped"
        case nil: "Search engine started by hand"
        }
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
    }

    @objc private func search() { onSearch() }

    @objc private func toggleLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            NSAlert(error: error).runModal()
        }
    }

    @objc private func quit() { NSApp.terminate(nil) }
}
