# Engine module design

How the Engine is cut into modules, where the seams are, and what each interface promises. Architecture rules follow [ADR-0005](../adr/0005-onion-layers-as-maven-modules.md).

Status: implemented in 0.1.0, and updated for Windows support (the `pounce-platform` module, the ports' new signatures, the pure-Java tokenizer); based on [the stack research](../research/2026-11-23-tech-stack-verification.md) and [the speed measurements](../research/2026-11-23-speed-spike-verdict.md).

## Terms

| Term | Meaning |
|---|---|
| **Watched Folder** | A folder the user chose to keep searchable, with everything beneath it |
| **Exclusion** | A rule that keeps paths out of the Index: dependency, build, hidden and Windows system folders below the Watched Folder, folders where programs keep their files (`~/Library`, `~/AppData`, `~/snap`), the Pounce home, `desktop.ini`, `Thumbs.db` and Office lock files, cloud-only and system files on Windows, files over 50 MB |
| **Document** | One file in the Index, counted once however many paths lead to it |
| **Passage** | A stretch of a Document's text (about 400 tokens); the unit that is matched |
| **Query** | What the user typed |
| **Keyword Match / Semantic Match** | A Document matched by shared words / by closeness in meaning |
| **Early / Final Results** | Keyword Matches shown instantly, then replaced by both kinds merged |
| **Snippet** | The best Passage of a Result, shortened, with the Query's words highlighted |
| **Engine / Launcher** | The background Java service / the app with the search panel (⌥Space on macOS, Alt+Space on Windows) that starts the Engine |
| **Pounce home** | The folder holding the Index, the credentials, `pounce.yml` and the logs |

Verified stack: Java 25, Spring Boot 4.2 (Spring Security 7.2, Jackson 3, JUnit 6), Lucene 10.6, ONNX Runtime Java 1.32 (CPU provider — CoreML was slower), a pure-Java WordPiece tokenizer ([ADR-0006](../adr/0006-pure-java-tokenizer.md)), PDFBox, picocli, ArchUnit, JMH, `directory-watcher`, Maven 3.10. GC: G1 with a small max heap, not ZGC (lower idle footprint).

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
          PDFBox / plain text              ONNX bge-small ONNX bge-small    Lucene (+ RRF fusion)
```

## Maven modules

| Module | Contains | May depend on |
|---|---|---|
| `pounce-domain` | Value types (`Passage`, `Query`, `Result`, `Snippet`, `TextFormat`, `WatchedFolder`) and pure rules: `PassageSplitter`, `Exclusions` | JDK only |
| `pounce-application` | The two deep modules `Indexer` and `Searcher`, the ports they need (`TextExtractor`, `Embedder`, `PassageIndex`) and their values (`FileStamp`, `EmbeddedPassage`, `Phase`) | `pounce-domain` |
| `pounce-infrastructure` | Adapters: `LucenePassageIndex`, `OnnxEmbedder` with `BertWordPieceTokenizer`, `FileTypeTextExtractor` over `PdfTextExtractor` and `PlainTextExtractor`, `FolderWatcher` | `pounce-application`, Lucene, ONNX Runtime, PDFBox, directory-watcher |
| `pounce-api` | Spring Boot app: REST controllers, Spring Security + JWT, the credentials, config with per-OS defaults (`PlatformDefaults`), the Launcher watch (`ParentProcessWatch`), wiring all adapters together | everything above, `pounce-platform`, Spring |
| `pounce-platform` | Where Pounce keeps its data (`PounceHome`) and the default Watched Folders (`DefaultWatchedFolders`) on macOS, Windows and Linux. Not an Engine layer; built for Java 21 so the CLI can use it | JDK only. `pounce-domain` and `pounce-application` may not use it |
| `pounce-cli` | picocli `pounce` command, talks to the REST API over HTTP | `pounce-platform`, JDK HTTP client, picocli, Jackson — **no** Engine modules |

`LayeringTest` (ArchUnit, in `pounce-api`) checks the rules the compiler cannot: the domain and application layers use only the JDK and each other, infrastructure never uses Spring or the API, and `pounce-platform` stays JDK-only and out of the core.

## The two deep modules

### `Indexer`

```java
public final class Indexer {
    /** Make the Index agree with whatever is on disk at this path right now. */
    public void refresh(Path path);

    /** Walk every Watched Folder and make the whole Index agree with disk. */
    public void reconcile();
}
```

**What a caller must know (the full interface):**
- `refresh` is **idempotent** and handles every case itself: new file, changed file, deleted file, renamed file (old path disappears, new path appears — two calls), a rename that only changes letter case on a case-insensitive disk, file that is now excluded (its Document is removed), a folder path (refreshes everything under it), a file with no text (kept as a Document with no Passages).
- A file that cannot be read right now (`TextExtractor` throws `IOException`, for example because another program holds it) is not stored, so the next change or reconcile reads it again. A Windows file that is only in the cloud, or a system file, is skipped the same way, without reading it.
- Paths are resolved to their real location first, so a file reached twice is one Document. Exclusions are judged relative to the Watched Folder the path was reached through.
- Safe to call from several threads; calls are serialised.
- `reconcile` runs at Engine start and is slow (minutes on a first run); `refresh` for one file is milliseconds plus embedding time. A Watched Folder that does not exist, and a file or folder that cannot be read, is logged and skipped; Documents in a folder that could not be read are kept. `reconcile` ends with `PassageIndex.commit()`.

**Hidden behind it:** exclusion checks, Windows file attributes, file-type detection, text extraction, splitting into Passages, embedding, writing to and deleting from the Index, change detection (skip files whose size and modified-time are unchanged, stored as a `FileStamp`).

The file watcher becomes trivial: every event → `indexer.refresh(path)`. No event-type logic leaks out. It does not enter excluded folders and ignores a folder's own MODIFY events, which Windows sends whenever something inside changes.

### `Searcher`

```java
public final class Searcher {
    public List<Result> search(Query query, Phase phase);   // Phase = EARLY | FINAL
}
```

**What a caller must know:**
- `EARLY` uses Keyword Matches (content and file name) only and never touches the embedding model. Target < 10 ms.
- `FINAL` runs Keyword and Semantic Matches and merges them with Reciprocal Rank Fusion. Target < 40 ms.
- Always one Result per Document, at most `query.limit()` Results, best first; each carries its best Passage as a Snippet.
- Never throws for an empty Index or an empty Query; returns an empty list.

**Hidden behind it:** query embedding (with the model's retrieval instruction prefix), the two Index lookups, fusion (`TopDocs.rrf`), dropping Semantic Matches below a minimum similarity, Snippet highlighting (Lucene `UnifiedHighlighter`).

## Ports (the seams)

A port only exists where something really varies across it.

| Port | Adapters | Why it's a real seam |
|---|---|---|
| `TextExtractor` — `Optional<String> extract(Path) throws IOException`. Empty means the file has no text, an answer kept until the file changes; `IOException` means it cannot be read right now | `FileTypeTextExtractor`, which picks `PdfTextExtractor` or `PlainTextExtractor` (UTF-8, or UTF-16 with a byte order mark) (v2: screenshot OCR) | Several file types, more coming |
| `Embedder` — `float[] embedQuery(String)`, `List<float[]> embedPassages(List<String>)` | `OnnxEmbedder`; `HashingEmbedder` (deterministic fake for fast tests) | Two adapters today, and the model is meant to be swappable |
| `PassageIndex` — `replace(Path, FileStamp, List<EmbeddedPassage>)`, `stamp(Path)`, `remove(Path)`, `documents()`, `commit()`, `keyword(Query)`, `hybrid(Query, float[])` | `LucenePassageIndex` | Keeps Lucene out of the application layer (ADR-0005). Only one adapter, so **tests use the real Lucene adapter on an in-memory directory**, not a hand-written fake |

**Inside `LucenePassageIndex`:** each Document is stored as a Lucene *block* — one child entry per Passage (text + int8-quantized vector) followed by one parent entry for the file (path, name). `keyword` uses `ToParentBlockJoinQuery`, and `hybrid` adds `DiversifyingChildrenFloatKnnVectorQuery`, so **both lookups already return one hit per Document** and `TopDocs.rrf` can fuse them directly (it merges by internal doc id, so both lists must be at the same granularity). This replaces "group Passages by Document in Java". Writes are searchable at once and committed to disk 10 seconds after the first write that is not on disk yet, or on `commit()`, so an Engine that is stopped without warning loses seconds of work, not the Index ([ADR-0008](../adr/0008-engine-stops-itself-on-windows.md)). `documents()` returns a list, not a set of paths: on Windows two paths that differ only in letter case are equal, but they are different keys in the Index.

**Inside `OnnxEmbedder`:** the tokenizer lives behind the same port: `BertWordPieceTokenizer`, pure Java, reading the model's `tokenizer.json` ([ADR-0006](../adr/0006-pure-java-tokenizer.md)). **Model: English-only `BAAI/bge-small-en-v1.5`** (384 dimensions, 512-token input, so ~400-token Passages fit), chosen over `multilingual-e5-small` because the multilingual model's 250k-word vocabulary made RAM too tight on 8 GB (809 MB fp32, 310 MB at best). Dutch files are still found by Keyword Matches. Its speed and memory are in [the speed measurements](../research/2026-11-23-speed-spike-verdict.md). A golden test checks token ids against the reference tokenizer.

**Deliberately not ports:**
- **File-system walking.** Tests use real temporary folders; a fake file system would be more code than it saves.
- **Passage splitting, exclusions.** Pure logic in `pounce-domain`; tested directly with plain unit tests.
- **Clock / config.** Passed in as values, not abstracted.
- **The operating system.** Where the Pounce home and the default Watched Folders are is answered once, in `pounce-platform`, and reaches the Engine as configuration (`PlatformDefaults`). `Exclusions` holds the rules for every system at once.

## How the Launcher gets Early then Final Results

**Decided:** one streamed request per Query, `GET /search?q=…` with `Accept: text/event-stream`. The Engine sends an `early` event, then a `final` event, then closes. The Launcher cancels by closing the connection when the user types another key.

## Testing shape

- `pounce-domain` and `pounce-platform`: plain unit tests. `pounce-platform` decides every system's paths from text, so the cases for all of them run on any host.
- `Indexer` and `Searcher`: tested **through their interfaces only**, with real temp folders, the real Lucene adapter (in-memory) and `HashingEmbedder`. Example: write three files into a temp folder → `reconcile()` → `search("canary", FINAL)` → assert the right Document comes first.
- `OnnxEmbedder` and `PdfTextExtractor`: small contract tests against real files and the real model. `BertWordPieceTokenizer`: a golden test against DJL's tokenizer, and a frozen fixture where DJL cannot load (Windows).
- `pounce-api`: Spring slice tests for REST and JWT.
- ArchUnit: layer rules. The < 10 ms / < 40 ms targets were measured once in [the speed spike](../research/2026-11-23-speed-spike-verdict.md), with a plain harness; no benchmark runs in the build.
- CI runs every test on macOS, Windows and Linux.
