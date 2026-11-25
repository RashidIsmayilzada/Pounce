# The Windows Launcher is a native WPF app on .NET

On Windows the Launcher (`windows-app/`) is a C# WPF app on .NET 10, built as a self-contained `Pounce.exe`, rather than a WinUI 3 app, a cross-platform UI toolkit or a web shell. WPF builds on Linux (`EnableWindowsTargeting`), so the whole Launcher, its tests and the Windows bundle (`scripts/build-windows.sh`) can be built and checked without a Windows machine, in WSL and in CI. It allows the same split as the macOS app (`PounceKit` and `PounceLauncher`): `Pounce.Core` holds the logic as plain `net10.0` with no Windows APIs, so its tests run on any system, and `Pounce.Launcher` is a thin shell over WPF and Win32. Pounce stays resident in the notification area, so the search panel is built once at start and only shown and hidden afterwards, and the shortcut shows it at once.

## Considered Options

- **WinUI 3 (Windows App SDK)**: the newer native toolkit, but its XAML compiler runs only on Windows, so nothing about the Launcher could be built or checked from Linux.
- **A cross-platform toolkit**: it would not replace the macOS Launcher, which stays SwiftUI and AppKit, and the panel needs Win32 directly anyway: the global shortcut, the foreground rules, the DWM backdrop, the shell's Apps folder and icons.
- **A web shell** (Electron or WebView2): a browser engine in a resident process, next to the Engine's JVM, on machines with 8 GB of RAM.

## Consequences

- The bundle carries a self-contained .NET runtime: about 155 MB, so no .NET install is needed. The SDK cannot trim WPF or WinForms apps (NETSDK1168, NETSDK1175), so it stays that size; Release builds keep only English resources and precompile the app with ReadyToRun.
- WinForms is referenced only for the tray icon (`NotifyIcon`), so its global usings are removed and WPF names stay unambiguous.
- There are two launchers to keep in step. Features, texts and keys of one (the panel, the tray or menu-bar menu, Launch at login, first-run settings) must be mirrored in the other by hand.
- What cannot run on Linux (the window, the tray icon, the calls into the shell) can only be checked on Windows: the CI smoke check starts `Pounce.exe` and its Engine, and the panel has a manual [test plan](../../windows-app/docs/panel-test-plan.md).
