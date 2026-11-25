# Onion layers are separate Maven modules

The Engine follows an onion architecture, and each layer is its own Maven module: `pounce-domain` (no framework dependencies), `pounce-application` (use cases and ports), `pounce-infrastructure` (Lucene, ONNX Runtime, PDFBox, file watching) and `pounce-api` (Spring Boot, REST, security, wiring). The compiler therefore rejects any dependency from an inner layer to an outer one, and ArchUnit tests check the remaining rules on every build. Spring is only allowed in `pounce-api`, so the core can be tested and reasoned about without it.

`pounce-platform`, which knows where Pounce keeps its data and which folders it watches by default on each operating system, is not a layer. It sits beside the onion because the CLI needs the same answers: it depends on the JDK only, `pounce-api` and `pounce-cli` use it, and `LayeringTest` keeps `pounce-domain` and `pounce-application` from using it, so the core never asks which system it runs on.
