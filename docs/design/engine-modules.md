# Engine module design

How the Engine is cut into modules, where the seams are, and what each interface promises. Vocabulary follows [GLOSSARY.md](../../GLOSSARY.md); architecture rules follow [ADR-0005](../adr/0005-onion-layers-as-maven-modules.md).

Status: **draft**, updated after [the stack research](../research/2026-11-23-tech-stack-verification.md); still waiting on the speed prototype.

Verified stack: Java 25, Spring Boot 4.2 (Spring Security 7.2, Jackson 3, JUnit 6), Lucene 10.6, ONNX Runtime Java 1.32 (CPU provider — CoreML was slower), PDFBox, picocli, ArchUnit, JMH, `directory-watcher`, Maven 3.10. GC: G1 with a small max heap, not ZGC (lower idle footprint).

## The idea in one picture

The whole Engine is two deep modules — **Indexer** and **Searcher** — with everything else hidden behind them. Callers (REST controllers, the file watcher, tests) only ever learn these two interfaces.

```
        file watcher ──► Indexer.refresh(path)          REST /search ──► Searcher.search(query, phase)
                              │                                               │
              ┌───────────────┼──────────────────┐            ┌───────────────┼──────────────┐
              ▼               ▼                  ▼            ▼               ▼              ▼
        TextExtractor   PassageSplitter     Embedder     Embedder      PassageIndex
        (port)          (domain, pure)      (port)       (port)        (port)
              │                                  │            │               │
          PDFBox / plain text              ONNX e5-small  ONNX e5-small     Lucene (+ RRF fusion)
```

## Maven modules

| Module | Contains | May depend on |
|---|---|---|
| `pounce-domain` | Value types (`Document`, `Passage`, `Query`, `Result`, `Snippet`, `WatchedFolder`, `Exclusion`) and pure rules: `PassageSplitter`, `Exclusions` | JDK only |
| `pounce-application` | The two deep modules `Indexer` and `Searcher`, and the ports they need: `TextExtractor`, `Embedder`, `PassageIndex` | `pounce-domain` |
| `pounce-infrastructure` | Adapters: `LucenePassageIndex`, `OnnxEmbedder`, `PdfTextExtractor`, `PlainTextExtractor`, `FolderWatcher` | `pounce-application`, Lucene, ONNX Runtime, PDFBox, directory-watcher |
| `pounce-api` | Spring Boot app: REST controllers, Spring Security + JWT, OpenAPI, config, wiring all adapters together | everything above, Spring |
| `pounce-cli` | picocli `pounce` command, talks to the REST API over HTTP | JDK HTTP client, picocli — **no** Engine modules |

## The two deep modules

### `Indexer`

```java
public interface Indexer {
    /** Make the Index agree with whatever is on disk at this path right now. */
    void refresh(Path path);

    /** Walk every Watched Folder and make the whole Index agree with disk. */
    void reconcile();
}
```

**What a caller must know (the full interface):**
- `refresh` is **idempotent** and handles every case itself: new file, changed file, deleted file, renamed file (old path disappears, new path appears — two calls), file that is now excluded, a folder path (refreshes everything under it), an unreadable or text-less file (kept as a Document with no Passages).
- Paths are resolved to their real location first, so a file reached twice is one Document.
- Safe to call concurrently for different paths; calls for the same path are serialised.
- `reconcile` runs at Engine start and is slow (minutes on a first run); `refresh` for one file is milliseconds plus embedding time.

**Hidden behind it:** exclusion checks, file-type detection, text extraction, splitting into Passages, embedding, writing to and deleting from the Index, change detection (skip files whose size and modified-time are unchanged).

The file watcher becomes trivial: every event → `indexer.refresh(path)`. No event-type logic leaks out.

### `Searcher`

```java
public interface Searcher {
    List<Result> search(Query query, Phase phase);   // Phase = EARLY | FINAL
}
```

**What a caller must know:**
- `EARLY` uses Keyword Matches (content and file name) only and never touches the embedding model. Target < 10 ms.
- `FINAL` runs Keyword and Semantic Matches and merges them with Reciprocal Rank Fusion. Target < 40 ms.
- Always one Result per Document, at most `query.limit()` Results, best first; each carries its best Passage as a Snippet.
- Queries of one or two characters return the same as `EARLY` even when `FINAL` is asked for.
- Never throws for an empty Index or an empty Query; returns an empty list.

**Hidden behind it:** query embedding (with the model's `query:` prefix), the two Index lookups, fusion (`TopDocs.rrf`), Snippet highlighting (Lucene `UnifiedHighlighter`).

## Ports (the seams)

A port only exists where something really varies across it.

| Port | Adapters | Why it's a real seam |
|---|---|---|
| `TextExtractor` — `Optional<String> extract(Path)` | `PlainTextExtractor`, `PdfTextExtractor` (v2: screenshot OCR) | Several file types, more coming |
| `Embedder` — `float[] embedQuery(String)`, `List<float[]> embedPassages(List<String>)` | `OnnxEmbedder`; `HashingEmbedder` (deterministic fake for fast tests) | Two adapters today, and the model is meant to be swappable |
| `PassageIndex` — `replace(Document, List<EmbeddedPassage>)`, `remove(Path)`, `keyword(Query, n)`, `nearest(float[], n)` | `LucenePassageIndex` | Keeps Lucene out of the application layer (ADR-0005). Only one adapter, so **tests use the real Lucene adapter on an in-memory directory**, not a hand-written fake |

**Inside `LucenePassageIndex`:** each Document is stored as a Lucene *block* — one parent entry for the file (path, name) followed by one child entry per Passage (text + int8-quantized vector). `keyword` uses `ToParentBlockJoinQuery` and `nearest` uses `DiversifyingChildrenFloatKnnVectorQuery`, so **both already return one hit per Document** and `TopDocs.rrf` can fuse them directly (it merges by internal doc id, so both lists must be at the same granularity). This replaces "group Passages by Document in Java".

**Inside `OnnxEmbedder`:** the tokenizer lives behind the same port. **Model: English-only `BAAI/bge-small-en-v1.5`** (384 dimensions, 512-token input, so ~400-token Passages fit), chosen over `multilingual-e5-small` because the multilingual model's 250k-word vocabulary made RAM too tight on 8 GB (809 MB fp32, 310 MB at best). Dutch files are still found by Keyword Matches. Its RAM and speed are **not yet measured** — the prototype does that. A golden test checks token ids against the reference tokenizer.

**Deliberately not ports:**
- **File-system walking.** Tests use real temporary folders; a fake file system would be more code than it saves.
- **Passage splitting, exclusions.** Pure logic in `pounce-domain`; tested directly with plain unit tests.
- **Clock / config.** Passed in as values, not abstracted.

## How the Launcher gets Early then Final Results

**Decided:** one streamed request per Query, `GET /search?q=…` with `Accept: text/event-stream`. The Engine sends an `early` event, then a `final` event, then closes. The Launcher cancels by closing the connection when the user types another key.

## Testing shape

- `pounce-domain`: plain unit tests.
- `Indexer` and `Searcher`: tested **through their interfaces only**, with real temp folders, the real Lucene adapter (in-memory) and `HashingEmbedder`. Example: write three files into a temp folder → `reconcile()` → `search("canary", FINAL)` → assert the right Document comes first.
- `OnnxEmbedder` and `PdfTextExtractor`: small contract tests against real files and the real model.
- `pounce-api`: Spring slice tests for REST and JWT.
- ArchUnit: layer rules. JMH: the < 10 ms / < 40 ms targets on a fixed corpus.
