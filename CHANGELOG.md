# Changelog

Notable changes to Pounce. The release workflow uses each version's section as its release notes.

## [Unreleased]

Pounce for Windows.

### Added

- **Pounce for Windows** (Windows 11, x64): `Pounce-<version>-windows-x64.zip` holds `Pounce.exe`, a C# WPF app on .NET 10, with the engine, a trimmed Java runtime and the model bundled. Unzip it and run `Pounce.exe`; nothing is installed.
- **Alt+Space search panel** on Windows: results update with every keystroke; applications first (Start menu and Store apps), then files with Explorer's icons and the matching passage; ↑↓ select, Enter open, Ctrl+Enter show in File Explorer, Esc closes.
- **Shortcut fallback** on Windows: when another program holds Alt+Space, Pounce takes Ctrl+Alt+Space, then Alt+Shift+Space; the `POUNCE_HOTKEY` environment variable picks one (for example `control-alt-space`).
- **Light and dark** panel following the Windows app mode and accent colour, with an acrylic backdrop on Windows 11 22H2 and later.
- **Tray icon** with Search…, the search engine's state, Launch at login, Settings file… and Quit Pounce. Starting `Pounce.exe` again shows the running one's panel; `Pounce.exe --quit` quits it.
- **First run on Windows** writes `%LOCALAPPDATA%\Pounce\pounce.yml` with your own Documents, Desktop and Downloads folders, wherever Windows keeps them (OneDrive included).
- **Markdown inside an Obsidian vault opens in Obsidian** on Windows too.
- **Missing Visual C++ runtime is named**: the tray menu and a notification say to install the Microsoft Visual C++ Redistributable (x64) instead of showing an exit code.
- **Linux**: the engine and the `pounce` command keep their data in `$XDG_DATA_HOME/pounce` (or `~/.local/share/pounce`) and watch the XDG Documents, Desktop and Downloads folders. There is no Linux app.
- **UTF-16 text files** with a byte order mark, as PowerShell and Notepad write them, are indexed instead of being found by name only.

### Changed

- The Pounce home folder (`~/Library/Application Support/Pounce` on macOS) is now readable by you only. On Windows an access list does this instead of file permissions.
- Default Watched Folders that don't exist are skipped instead of being watched.
- The index is saved to disk 10 seconds after a change and at the end of every catch-up, not only when the engine stops, so a crash or forced quit loses seconds of indexing, not everything since the start.
- Windows system files and folders (`$Recycle.Bin`, `System Volume Information`, `desktop.ini`, `Thumbs.db`), Office lock files (`~$report.docx`) and the Pounce home are no longer indexed.
- Files that OneDrive (or another cloud drive) keeps online only are skipped, so indexing never downloads them; they are read once they are on the disk.
- The file watcher no longer enters excluded folders such as `node_modules` and `.git`.
- Tokenizing is now pure Java ([ADR 0006](docs/adr/0006-pure-java-tokenizer.md)): the engine loads no native tokenizer library and is about 21 MB smaller.
- A release now carries both `Pounce-<version>-macos-arm64.zip` and `Pounce-<version>-windows-x64.zip`.

### Fixed

- A file that could not be read when Pounce saw it (held open by another program, for example) is read again at the next change or start, instead of being kept with no text.
- A folder that can't be read, or a Watched Folder that doesn't exist, no longer stops the indexing of everything else.
- Renaming a file so that only its letter case changes no longer leaves two results for it.
- A file that becomes excluded (for example by growing past 50 MB) is removed from the index.
- A Watched Folder inside a hidden folder is indexed, and a Pounce home inside a Watched Folder is no longer indexed into itself.
- A UTF-8 byte order mark is no longer part of a file's text, so a Markdown file's first heading counts as a heading.
- A crash while creating the signing key on first start no longer leaves an empty key that stops every later start.
- `pounce` output piped to another program no longer contains colour codes.

### Known limitations

- Windows support is new. The engine's tests run on macOS, Windows and Linux, the Windows Launcher's logic is tested on Linux and Windows, and CI starts the Windows build and checks a search, a quit and a forced quit. As of this writing nobody has used the Windows app by hand on a real PC yet; the manual checks are in [`windows-app/docs/panel-test-plan.md`](windows-app/docs/panel-test-plan.md).
- Windows: not code-signed, so SmartScreen warns on first run (More info → Run anyway).
- Windows: needs the Microsoft Visual C++ Redistributable (x64), which most PCs already have.
- Windows: in `pounce.yml`, write paths in single quotes or with forward slashes; a double-quoted path with backslashes is a YAML error that stops the search engine.
- Windows: Windows 11 on x64 only. On Windows 11 21H2 the panel has a plain background instead of the acrylic one.
- Windows: no counterpart to Quick Look, and the shortcut can only be changed through `POUNCE_HOTKEY`.
- Speed and memory have only been measured on a Mac.

## [0.1.0] - 2026-11-24

First release.

### Added

- **⌥Space search panel** (SwiftUI): results update with every keystroke; ↑↓ select, ⏎ open, ⌘⏎ show in Finder, ⌘Y Quick Look, Esc closes.
- **App search**: matching applications appear first, like Spotlight (exact name, name start, word start, initials), and ⏎ launches them.
- **Search by meaning**: hybrid ranking with Lucene 10 (BM25 keyword search plus int8 HNSW vectors, merged with Reciprocal Rank Fusion) and on-device `bge-small-en-v1.5` embeddings through ONNX Runtime.
- **Text, Markdown, code and PDFs**, each shown with its best-matching passage and the query's words highlighted.
- **Live indexing** from macOS file events; files changed while Pounce was off are caught up on start, and unchanged files are skipped.
- **Markdown inside an Obsidian vault opens in Obsidian.**
- **Private by design**: the engine listens on `127.0.0.1` only, every request needs a short-lived self-issued JWT, and nothing leaves the Mac.
- **`pounce` command** for the terminal.
- **`Pounce.app`** with the engine, a trimmed Java runtime and the model bundled; menu-bar icon with Launch at Login.

### Known limitations

- Not notarized: right-click → Open on first launch.
- The first index of large folders takes a while (roughly 20 passages per second), and the engine takes about 30 seconds to start.
- Markdown front matter is indexed as text, which can crowd out better matches.
- Searches unrelated to anything can still return weak matches on large folders.
