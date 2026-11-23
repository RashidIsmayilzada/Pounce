# Lucene is the only storage engine

The whole Index (Passage text, embedding vectors, and Document metadata) lives in a single Lucene index on disk. Keyword Matches (BM25) and Semantic Matches (HNSW vector search) both come from it, and their rankings are merged with Reciprocal Rank Fusion.

## Considered Options

- **SQLite + sqlite-vec**: light, but keyword search would need a second mechanism (FTS5) and the two would have to be kept in sync.
- **ChromaDB or another vector database**: a separate process, more RAM, and still no good keyword search.
- **Lucene for keywords + a separate vector store**: two stores to keep consistent for no gain once Lucene has native vector search.
