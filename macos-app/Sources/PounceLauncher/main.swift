import AppKit
import PounceKit

// PounceLauncher: a menu-bar-less, Dock-less app that shows the search panel on ⌥Space.
//   PounceLauncher                          run as the Launcher
//   PounceLauncher --snapshot out.png QUERY  render the panel for QUERY against the running Engine, then exit
// Honours POUNCE_URL and POUNCE_HOME like the `pounce` command.

let arguments = CommandLine.arguments
let app = NSApplication.shared

if let flag = arguments.firstIndex(of: "--snapshot"), arguments.count > flag + 2 {
    let output = URL(fileURLWithPath: arguments[flag + 1])
    let query = arguments[(flag + 2)...].joined(separator: " ")
    MainActor.assumeIsolated {
        Snapshot.render(query: query, engine: Configuration.engine(), to: output)
    }
} else {
    app.setActivationPolicy(.accessory)
    let delegate = MainActor.assumeIsolated { LauncherDelegate() }
    app.delegate = delegate
    app.run()
}
