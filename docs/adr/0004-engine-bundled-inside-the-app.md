# The Engine ships inside Pounce.app

The Java Engine and its trimmed Java runtime are bundled inside the `Pounce.app` bundle and started by the app as a login item, instead of being installed as a standalone Java process. macOS asks the user's permission before a program reads Documents, Desktop or Downloads; bundling makes those prompts say "Pounce" and ties the permission to the app, instead of to a generic `java` binary.

## Consequences

The Java runtime must be code-signed and notarized together with the app, and the Swift app is responsible for starting and stopping the Engine.
