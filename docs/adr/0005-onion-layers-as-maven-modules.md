# Onion layers are separate Maven modules

The Engine follows an onion architecture, and each layer is its own Maven module: `pounce-domain` (no framework dependencies), `pounce-application` (use cases and ports), `pounce-infrastructure` (Lucene, ONNX Runtime, PDFBox, file watching) and `pounce-api` (Spring Boot, REST, security, wiring). The compiler therefore rejects any dependency from an inner layer to an outer one, and ArchUnit tests check the remaining rules on every build. Spring is only allowed in `pounce-api`, so the core can be tested and reasoned about without it.
