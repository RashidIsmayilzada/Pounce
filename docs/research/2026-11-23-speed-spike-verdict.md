# Speed spike verdict: bge-small-en-v1.5 + Lucene 10.6 hybrid search

**Question:** can `bge-small-en-v1.5` (ONNX Runtime, Java) + Lucene 10.6 parent/child hybrid search + RRF hit Early < 10 ms, Final < 40 ms, and keep the Engine well under ~500 MB on the 8 GB M2?

**Verdict: yes, with the int8 model.** Final Results are ~3× inside the budget at p95 and memory is ~3× inside the budget, before Spring is added. Early Results are at the edge only at p99.

Code: branch `prototype/speed-spike`, folder `prototypes/PROTOTYPE-speed-spike/` (throwaway; `POUNCE_SCRATCH=<folder with models-bge/> ./run.sh int8 50000`).

## Setup

- Apple M2, 8 GB, macOS 26.5, Temurin 24, `-Xmx192m`, G1, compact object headers, Vector API incubator on.
- Corpus: 1,199 real files from `~/projects` and `~/Documents` (5,532 Passages), padded with synthetic copies (noised vectors) to **50,000 Documents / 228,867 Passages**. 11 index segments (no force-merge).
- Each Document is a Lucene block: Passage children + a parent. Early = `ToParentBlockJoinQuery` on Passage text + file-name match, prefix on the last word. Final = Early + `DiversifyingChildrenFloatKnnVectorQuery` (int8 quantized HNSW, `Lucene104HnswScalarQuantizedVectorsFormat`) mapped to parents + `TopDocs.rrf(8, 60, …)`.
- 20 queries × 10 repetitions after a warm-up round. Plain harness, not JMH.

## Results at 50,000 Documents

| | int8 model | fp32 model |
|---|---|---|
| Early Results p50 / p95 / p99 | 1.0 / 8.3 / 11.1 ms | 1.2 / 9.8 / 11.8 ms |
| Query embedding p50 / p95 | 2.1 / 3.2 ms | 4.3 / 5.6 ms |
| Vector search + RRF p50 / p95 | 1.9 / 3.4 ms | 2.0 / 3.6 ms |
| **Final Results p50 / p95 / p99** | **5.0 / 12.9 / 15.6 ms** | 7.9 / 16.8 / 19.6 ms |
| Footprint, model loaded | 120 MB | 248 MB |
| **Footprint, steady after searches** | **166 MB** (peak 245 MB) | 247 MB (peak 352 MB) |
| Passage embedding (indexing) | 46 ms ≈ 22 Passages/s | 78 ms ≈ 13 Passages/s |
| Index on disk | 618 MB | 618 MB |

## Decisions this settles

- **Use the int8 model.** Faster and ~80 MB lighter; quality vs fp32 is not yet evaluated (see open items).
- **Parent/child blocks work** and the child→parent mapping before `TopDocs.rrf` is required (the kNN query returns child doc ids).
- **Children must carry the Document's path too**, so one delete-by-term removes the whole block when a file changes.

## Open items

- **Early p99 ≈ 11 ms** at this scale: the prefix query on the last word is the likely cost. Candidates: edge n-grams on file names, or a cap on prefix expansions. Re-measure with JMH.
- **First indexing is slow:** 22 Passages/s → ~2.9 h for 229k Passages. Try batched embedding (several Passages per ONNX call) and run indexing at low priority.
- **Footprint excludes file-cache pages** of the memory-mapped index (macOS counts those as clean, reclaimable memory). The 618 MB index on disk is mostly stored Passage text and raw vectors.
- **Spring Boot overhead not measured yet** (estimate: +80–120 MB).
- **Retrieval quality** of int8 vs fp32 not measured; needs a small labelled query set.
- **Default Exclusions** must include `venv`, `.venv`, `site-packages`, `__pycache__`: sample results were dominated by third-party Python libraries.
