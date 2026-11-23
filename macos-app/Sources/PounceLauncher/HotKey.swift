import Carbon.HIToolbox

/// A system-wide keyboard shortcut through Carbon's RegisterEventHotKey: works from any app and needs no
/// Accessibility permission. The action runs on the main thread.
@MainActor
final class HotKey {
    private static var actions: [UInt32: () -> Void] = [:]
    private static var handlerInstalled = false
    private var reference: EventHotKeyRef?

    init(keyCode: UInt32, modifiers: UInt32, id: UInt32, action: @escaping () -> Void) {
        Self.installHandler()
        Self.actions[id] = action
        let hotKeyID = EventHotKeyID(signature: OSType(0x504F_4E43), id: id) // 'PONC'
        RegisterEventHotKey(keyCode, modifiers, hotKeyID, GetApplicationEventTarget(), 0, &reference)
    }

    /// ⌥Space.
    static func optionSpace(action: @escaping () -> Void) -> HotKey {
        HotKey(keyCode: UInt32(kVK_Space), modifiers: UInt32(optionKey), id: 1, action: action)
    }

    private static func installHandler() {
        guard !handlerInstalled else { return }
        handlerInstalled = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var hotKeyID = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hotKeyID)
            let id = hotKeyID.id
            MainActor.assumeIsolated { HotKey.actions[id]?() }
            return noErr
        }, 1, &spec, nil, nil)
    }
}
