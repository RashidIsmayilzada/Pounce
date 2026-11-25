# The Engine ships inside Pounce.app

The Java Engine and its trimmed Java runtime are bundled inside the `Pounce.app` bundle and started by the app as a login item, instead of being installed as a standalone Java process. macOS asks the user's permission before a program reads Documents, Desktop or Downloads; bundling makes those prompts say "Pounce" and ties the permission to the app, instead of to a generic `java` binary.

The Launcher is the login item, and it starts the Engine as its own **child process** rather than registering the Engine as a separate launch agent (`SMAppService.agent`), because a child process is simpler, needs no second plist, and is still attributed to Pounce by macOS's privacy prompts. The Launcher reuses an Engine that already answers on the port, restarts it once if it dies, and stops it on quit; the Engine also exits on its own when the Launcher's process ends, so a force-quit never leaves it orphaned.

On Windows the Engine ships the same way, in the folder that holds `Pounce.exe` (`engine\`, `runtime\`, `models\` and ONNX Runtime's DLLs in `native\onnxruntime\`), and the Launcher starts it as a child process too. Windows does not ask the user before a desktop program reads their folders, so the reason above does not apply there; the bundle still means one download and one program to start and quit. Launch at login is a value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and the Launcher does not stop the Engine itself: the Engine stops when the Launcher's process ends ([ADR 0008](0008-engine-stops-itself-on-windows.md)).

## Consequences

The Java runtime must be code-signed and notarized together with the app before Pounce is distributed; local builds are signed ad hoc. On Windows nothing is code-signed yet, so SmartScreen warns on first run. The Engine only runs while the Launcher runs.
