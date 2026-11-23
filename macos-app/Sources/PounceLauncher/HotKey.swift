import Carbon.HIToolbox
import os

let launcherLog = Logger(subsystem: "dev.pounce.Pounce", category: "launcher")

/// A system-wide keyboard shortcut through Carbon's RegisterEventHotKey: works from any app and needs no
/// Accessibility permission. The action runs on the main thread.
@MainActor
final class HotKey {
    private static var actions: [UInt32: () -> Void] = [:]
    private static var handlerInstalled = false
    private var reference: EventHotKeyRef?
    /// The status macOS returned when registering; noErr means the shortcut is registered.
    let status: OSStatus

    init(keyCode: UInt32, modifiers: UInt32, id: UInt32, action: @escaping () -> Void) {
        let installed = Self.installHandler()
        Self.actions[id] = action
        let hotKeyID = EventHotKeyID(signature: OSType(0x504F_4E43), id: id) // 'PONC'
        status = RegisterEventHotKey(keyCode, modifiers, hotKeyID, GetApplicationEventTarget(), 0, &reference)
        launcherLog.notice("hotkey register: status \(self.status), handler \(installed)")
    }

    /// The shortcut by name: option-space (default), control-option-space or command-option-space.
    static func named(_ name: String, action: @escaping () -> Void) -> HotKey {
        let modifiers = switch name {
        case "control-option-space": controlKey | optionKey
        case "command-option-space": cmdKey | optionKey
        default: optionKey
        }
        return HotKey(keyCode: UInt32(kVK_Space), modifiers: UInt32(modifiers), id: 1, action: action)
    }

    private static var handlerStatus: OSStatus = noErr

    private static func installHandler() -> OSStatus {
        guard !handlerInstalled else { return handlerStatus }
        handlerInstalled = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        handlerStatus = InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var hotKeyID = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hotKeyID)
            let id = hotKeyID.id
            launcherLog.notice("hotkey pressed: id \(id)")
            MainActor.assumeIsolated { HotKey.actions[id]?() }
            return noErr
        }, 1, &spec, nil, nil)
        return handlerStatus
    }
}
