# Changelog

Notable changes to Pounce. The release workflow uses each version's section as its release notes.

## [Unreleased]

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
