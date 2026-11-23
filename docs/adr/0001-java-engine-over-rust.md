# Java for the Engine, not Rust

The Engine is written in Java (with Spring Boot) rather than Rust, even though a search tool whose main selling point is speed would usually suggest Rust. The slowest step of a Query, embedding it, runs in the same native ONNX Runtime from either language, and the search libraries (Lucene vs Tantivy) differ by single milliseconds, so the user-visible speed difference is a few milliseconds per Query. Java was chosen because the project is a showcase of depth in the author's main language, and Lucene and PDFBox are more mature than their Rust counterparts.

## Consequences

The Engine uses roughly 100–250 MB more RAM and starts a few seconds slower than a Rust equivalent would. Both are acceptable because the Engine starts once at login and stays running.
