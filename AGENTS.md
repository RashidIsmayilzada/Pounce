# Pounce

A private, on-device search bar for macOS: a Java Engine (indexing + hybrid search behind a localhost REST API), a `pounce` CLI, and a SwiftUI Launcher.

## Read before working

- **Vocabulary** → [GLOSSARY.md](GLOSSARY.md). Name classes, methods, endpoints and docs with its terms (Document, Passage, Result, Snippet, Engine, Early/Final Results…).
- **Module layout, the `Indexer` / `Searcher` interfaces, ports** → [docs/design/engine-modules.md](docs/design/engine-modules.md). Read before adding a class, port or module.
- **Why things are the way they are** → [docs/adr/](docs/adr/). Read before changing language, storage, auth, packaging or layering; record a new ADR when you reverse one.
- **Library versions, APIs, measured RAM/latency, signing gotchas** → [docs/research/](docs/research/). Read before adding a dependency or touching embeddings, Lucene codecs, JVM flags or packaging.

## Layering

The Engine is an onion of Maven modules: `pounce-domain` → `pounce-application` → `pounce-infrastructure` → `pounce-api`. Dependencies point inward only; ArchUnit tests enforce it. Spring, Lucene, ONNX Runtime and PDFBox each live only in their outer module. `pounce-cli` talks to the Engine over HTTP and depends on no Engine module.

## Tests

Work test-first. Test `Indexer` and `Searcher` through their interfaces with real temp folders, the real Lucene adapter on an in-memory directory, and `HashingEmbedder`; reserve the real ONNX model for embedder contract tests. Performance claims come from the JMH suite, never from estimates.

## Gotchas

- The shell's default `java` is 21 (sdkman). Build with JDK 25: `export JAVA_HOME=~/.sdkman/candidates/java/25.0.5.1+2-tem` before running Maven.
- Every model, index and benchmark corpus stays on the user's Mac; the Engine makes no network calls.
- Throwaway spikes live on `prototype/*` branches, never on `main`; their verdicts live in `docs/research/`.
