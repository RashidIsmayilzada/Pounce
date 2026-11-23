# Changelog

Notable changes to Pounce. The release workflow uses each version's section as its release notes.

## [Unreleased]

### Added

- App search: matching applications appear first, like Spotlight (exact name, name start, word start, initials), and ⏎ launches them.
- Markdown inside an Obsidian vault opens in Obsidian.

### Fixed

- ⌥Space opened an invisible panel (0 x 0 points) and, after a search, reopened the old search instead of an empty bar.
- Restarting Pounce re-read every file; unchanged files are now skipped.

## [0.1.0] - 2026-11-23

First release.

### Added

- Engine: hybrid search (Lucene 10 BM25 + int8 HNSW vectors, merged with Reciprocal Rank Fusion) over text, Markdown, code and PDFs, with on-device `bge-small-en-v1.5` embeddings through ONNX Runtime.
- Search as you type: Early Results from keywords, then Final Results with meaning, streamed over one request.
- Snippets with the query's words highlighted.
- Live indexing from macOS file events, and a catch-up scan on start.
- Localhost REST API secured with self-issued short-lived JWTs.
- ⌥Space Launcher (SwiftUI) with keyboard navigation, Quick Look (⌘Y) and Show in Finder (⌘⏎).
- `pounce` terminal command.
- `Pounce.app` with the Engine, a trimmed Java runtime and the model bundled; menu-bar icon with Launch at Login.

### Known limitations

- Not notarized: right-click → Open on first launch.
- Every start re-indexes all files; unchanged files are not skipped yet.
- Markdown front matter is indexed as text, which can crowd out better matches.
- Searches unrelated to anything can still return weak matches on large folders.
