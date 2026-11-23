# Pounce tech stack verification (2026-11-23)

Scope: check that the decided Pounce architecture is feasible and get the exact facts (versions, APIs, platform behaviour) from primary sources. Target machine: Apple M2, 8 GB RAM, macOS 26.5.

Method:
- Versions come from Maven Central `maven-metadata.xml`. Release dates are the `Last-Modified` header of the artifact's `.pom` on repo1.maven.org, unless a project page gives a date.
- API facts come from the project docs, and from `javap` on the released jars where the docs were silent.
- Performance and memory numbers are **local measurements on the target machine** (see the appendix), not vendor benchmarks.
- **unverified** means no primary source was found.

---

## Summary

The architecture is feasible. Most of the stack is current and fits together:

- **Java and Spring:** Java 25 is the current LTS. Spring Boot 4.2.0 (released 19 Nov 2026) supports Java 17 to 27 and manages Spring Security 7.2.0, Jackson 3.1.8 and JUnit 6.1.3. springdoc-openapi 3.2.1 is built against Boot 4.2.0.
- **Lucene 10.6.0** has everything the plan needs:
  - float and quantized HNSW vectors
  - a built-in Reciprocal Rank Fusion helper (`TopDocs.rrf`)
  - a block-join kNN query that returns the best chunk per parent document
  - the UnifiedHighlighter for best-passage snippets
- **ONNX Runtime Java 1.32.0** ships macOS arm64 natives in the standard Maven jar.
- **Query embedding is fast:** about 2 to 5 ms p50 for a short query on the M2 CPU, measured. That leaves most of the 40 ms semantic budget for the HNSW search.
- **File watching:** the JDK `WatchService` on macOS is still a 2-second poller in JDK 25. `io.methvin:directory-watcher` uses FSEvents through JNA, and JNA ships darwin-aarch64 natives.
- **macOS packaging:** apps that register a LaunchAgent with `SMAppService` become the TCC "responsible code" for it, per Apple DTS. Privacy prompts should then name Pounce.

The big problem is **memory**:

- The decided embedding setup (fp32 `model.onnx` with the Hugging Face tokenizer through DJL) measured **809 MB phys_footprint** steady state, with a **1.67 GB peak at load**. That breaks the "well under ~500 MB" target before Spring, Lucene or PDFBox are even counted.
- Two changes bring the embedding stack down to **310 MB steady (379 MB peak)**, also measured:
  - the published int8 ONNX file (118 MB)
  - the SentencePiece tokenizer (`ai.djl.sentencepiece`) instead of the Hugging Face `tokenizer.json` tokenizer, which alone cost about 350 MB
- The second packaging issue is **native libraries**. ONNX Runtime, DJL and JNA all unpack ad-hoc-signed `.dylib` files to temp or cache directories at runtime. Hardened Runtime library validation would refuse to load them, unless they are pre-extracted, re-signed and loaded from inside the bundle, or library validation is turned off.

---

## Changes / risks for the plan

Ordered by impact.

1. **RAM target is not met by the default embedding setup.** Measured on the M2 with Temurin 24 (see appendix):
   - fp32 `model.onnx` + DJL Hugging Face tokenizer: 809 MB steady, 1,668 MB peak.
   - int8 `model_qint8_avx512_vnni.onnx` + DJL Hugging Face tokenizer: 661 MB steady, 791 MB peak.
   - int8 + DJL SentencePiece tokenizer: **310 MB steady, 379 MB peak.**
   - fp32 + SentencePiece: 535 MB steady, 1,242 MB peak.

   **Change:** default to the int8 model and the SentencePiece tokenizer. Budget the remaining ~190 MB for JVM heap, Spring, Lucene and PDFBox. Even then, "well under 500 MB" is tight and not yet proven: Spring Boot plus Lucene footprint was **not measured**.

   Where the size comes from (my synthesis from `config.json`): `vocab_size` 250037 × `hidden_size` 384 ≈ 96 M of the model's ≈118 M fp32 parameters (470 MB / 4 bytes) are the token-embedding table. So vocabulary pruning would be the next lever. It is **unverified** and untested.

2. **The DJL Hugging Face tokenizer costs about 350 MB of native memory for this model.** Measured: JVM + `HuggingFaceTokenizer` loading the 17 MB `tokenizer.json` came to 380 MB phys_footprint; a bare JVM was 22 MB.
   - `ai.djl.sentencepiece:sentencepiece:0.40.0` loading the 5 MB `sentencepiece.bpe.model` came to 96 MB.
   - Taking the SentencePiece ids and applying the XLM-R fairseq mapping (+1 offset, unk→3, wrap in `<s>`=0 … `</s>`=2) gave **exactly the same ids** as the HF tokenizer on the test sentence. The mapping is defined in [transformers `tokenization_xlm_roberta.py`](https://github.com/huggingface/transformers/blob/v4.46.0/src/transformers/models/xlm_roberta/tokenization_xlm_roberta.py#L133-L136).
   - **Change:** put tokenization behind the embedding port and add a golden test. It should compare the SentencePiece and HF tokenizer ids on a multilingual corpus, because one sentence is not proof.

3. **The int8 ONNX file is an x86-targeted export.** The filename says AVX512-VNNI ([model repo files](https://huggingface.co/intfloat/multilingual-e5-small/tree/main/onnx)). It still loads and runs on the M2's CPU execution provider:
   - Short-query latency: p50 2.1 ms, against 4.2 ms for fp32.
   - Passage of 245 tokens: about 19–24 ms, against about 40 ms for fp32.
   - The query vector's cosine similarity to the fp32 vector was 0.997 on one sample.

   **Risk:** retrieval quality after quantization is **unverified**. Run a small retrieval evaluation (fp32 against int8) before committing.

4. **The CoreML execution provider does not help for this model.** It is callable from Java (`SessionOptions.addCoreML(Map)` exists in 1.32.0). Measured with `MLProgram` and `CPUAndNeuralEngine` or `ALL`:
   - Only 562 of 623 nodes went to CoreML, split into 38 partitions.
   - The Neural Engine compiler logged "unbounded dimension" errors, because the input shapes are dynamic.
   - Session load took 7.4 s, against about 1.5 s on CPU.
   - Query p50 was 10.8–12.2 ms, against 4.2 ms on the CPU execution provider.

   **Change:** use the CPU execution provider. CoreML would need static input shapes (padding to fixed lengths), which is **untested**.

5. **Native libraries and code signing.** All three affected libraries unpack natives at runtime. `codesign -dv` on the shipped dylibs shows they are signed ad-hoc only, with no Team ID:
   - The ONNX Runtime jar ships `ai/onnxruntime/native/osx-aarch64/libonnxruntime.dylib` (47.6 MB, `Signature=adhoc`). It also contains `.dSYM` debug bundles (about 58 MB) and the natives for other platforms, which makes the jar 56 MB.
   - DJL's tokenizer jars ship `native/lib/osx-aarch64/cpu/*.dylib` (`Signature=adhoc`) and extract them to a cache directory.
   - JNA extracts `libjnidispatch.jnilib` to a temp directory.

   Under the Hardened Runtime, library validation only loads libraries signed by Apple or with the same Team ID as the main executable, unless `com.apple.security.cs.disable-library-validation` is set. Gatekeeper then runs extra checks ([Apple: Disable Library Validation](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.disable-library-validation)).

   **Change:**
   - Pre-extract the arm64 dylibs into `Contents/Frameworks/`, the location Apple specifies for dynamic libraries ([Placing content in a bundle](https://developer.apple.com/documentation/bundleresources/placing-content-in-a-bundle)).
   - Sign them with the Developer ID.
   - Point the loaders at them:
     - ONNX Runtime: system property `onnxruntime.native.path`, found in the `ai.onnxruntime.OnnxRuntime` bytecode.
     - JNA: `jna.boot.library.path` plus `jna.nounpack`, found in the `com.sun.jna.Native` bytecode.
     - DJL tokenizers: environment variable `RUST_LIBRARY_PATH`, found in the `LibUtils` bytecode.
     - DJL SentencePiece: override not checked, **unverified**.
   - Also set `DJL_OFFLINE=true`. If a native is missing, DJL's `LibUtils` contains code to download it from `https://publish.djl.ai/tokenizers/`. The `ai.djl.util.Ec2Utils` class in `ai.djl:api` contains a telemetry endpoint and an `OPT_OUT_TRACKING` switch. Whether the tokenizer path calls it is **unverified**, so set `OPT_OUT_TRACKING=true` for a privacy product.

6. **Lucene fusion and "one result per file" need a document-model decision.**
   - `TopDocs.rrf(int topN, int k, TopDocs[] hits)` exists, added in 10.2.0 ([Lucene news](https://lucene.apache.org/core/corenews.html), [javadoc](https://lucene.apache.org/core/10_5_0/core/org/apache/lucene/search/TopDocs.html)). It fuses by `(shardIndex, doc)`; I checked the bytecode, which uses `TopDocs$ShardIndexAndDoc`. Both input lists must therefore be at the same granularity.
   - If files are indexed as chunks, use one of these:
     - a parent/child block per file, with `DiversifyingChildrenFloatKnnVectorQuery` from `lucene-join`, which returns the best child per parent, and `ToParentBlockJoinQuery` for BM25
     - chunk-level search, then collapse to one result per file in application code
   - Lucene 10.5 also added `LogOddsFusionQuery` and `BayesianScoreQuery` for score-based (not rank-based) hybrid search. They are experimental ([Lucene 10.6.0 CHANGES](https://lucene.apache.org/core/10_6_0/changes/Changes.html)).

7. **Lucene SIMD needs the incubator vector module.** `lucene-core` logs: "Java vector incubator module is not readable. For optimal vector performance, pass '--add-modules jdk.incubator.vector'". It is a string in `VectorizationProvider`.

   **Change:** include `jdk.incubator.vector` in the jlink image and pass `--add-modules jdk.incubator.vector`.

   Related: since JDK 24 the JVM warns on JNI use unless `--enable-native-access` is given ([JEP 472](https://openjdk.org/jeps/472)). I saw this warning for JNA. Add `--enable-native-access=ALL-UNNAMED`, or name the modules if the app becomes modular.

8. **"JUnit 5" is out of date.** JUnit 6.0.0 shipped on 30 Sep 2025 with a Java 17 baseline and one version number for Platform, Jupiter and Vintage ([release notes](https://docs.junit.org/6.0.0/release-notes/)). Current stable is 6.1.3, and Spring Boot 4.2.0 manages `junit-jupiter.version` 6.1.3 ([spring-boot-dependencies-4.2.0.pom](https://repo1.maven.org/maven2/org/springframework/boot/spring-boot-dependencies/4.2.0/spring-boot-dependencies-4.2.0.pom)). The Jupiter API packages are unchanged.

   **Change:** say "JUnit 6 (Jupiter)" in the plan.

9. **Spring Boot 4 renames and Jackson 3** ([Boot 4.0 migration guide](https://github.com/spring-projects/spring-boot/wiki/Spring-Boot-4.0-Migration-Guide)):
   - Use `spring-boot-starter-webmvc`; `spring-boot-starter-web` is deprecated.
   - Use `spring-boot-starter-security-oauth2-resource-server`; the old `spring-boot-starter-oauth2-resource-server` is deprecated.
   - JSON uses Jackson 3, package `tools.jackson`.
   - Undertow is removed.

   The ArchUnit rules and the DTO and serialization code in the `api` module should target Jackson 3.

10. **Maven:** current is **3.10.2**, released 18 Nov 2026. Maven 4 is still a preview (4.0.0-rc-7) ([download page](https://maven.apache.org/download.cgi)).

11. **directory-watcher caveats** ([README](https://github.com/gmethvin/directory-watcher)):
    - **Old.** The last release is 0.19.1 from 23 Jun 2025, 17 months ago.
    - **File hashing.** `fileHashing` defaults to true and hashes every file it sees into memory. That costs CPU, I/O and RAM on a large home folder. Use `fileHashing(false)` or `FileHasher.LAST_MODIFIED_TIME`.
    - **No catch-up after restart.** The macOS implementation creates its FSEvents stream with `kFSEventStreamEventIdSinceNow`, so changes made while Pounce was not running are never replayed. Pounce needs its own startup reconciliation, such as an mtime/size scan against the index. The alternative is a custom FSEvents binding that saves the last event id.
    - **Defaults.** Latency is 0.5 s and `fileLevelEvents()` is false (both from the `MacOSXListeningWatchService$Config` bytecode).

12. **TCC attribution depends on Apple behaviour that is not documented.** Apple DTS says that if a LaunchAgent "was installed by `SMAppService`, that makes the app the responsible code". DTS also says the algorithm "is not documented, has changed in the past, and may well change in the future" ([forum thread 756510](https://developer.apple.com/forums/thread/756510)).
    - **Change:** register the backend with `SMAppService.agent(plistName:)`. Its plist lives in `Contents/Library/LaunchAgents` and uses `BundleProgram`. Test the actual prompt text on macOS 26.5 early.
    - Put `NSDocumentsFolderUsageDescription`, `NSDesktopFolderUsageDescription` and `NSDownloadsFolderUsageDescription` in Pounce.app's Info.plist.

13. **Entitlements.** The vendor JDK `java` binaries on this Mac carry these entitlements under the Hardened Runtime:
    - `allow-jit`
    - `allow-unsigned-executable-memory`
    - `disable-library-validation`
    - `allow-dyld-environment-variables`
    - `debugger`
    - Oracle JDK 23 also has `device.audio-input`

    Which of these HotSpot 25 on arm64 actually needs is **unverified**. Start from `allow-jit` and add only what testing shows is needed. Apple says to "use only the entitlements that are absolutely necessary" ([Hardened Runtime](https://developer.apple.com/documentation/security/hardened-runtime)).

    Do **not** reuse jpackage's default `sandbox.plist`. It turns on `com.apple.security.app-sandbox` and `device.audio-input` ([JDK 25 source](https://github.com/openjdk/jdk/blob/jdk-25%2B36/src/jdk.jpackage/macosx/classes/jdk/jpackage/internal/resources/sandbox.plist)). Apple says App Sandbox "enforces stricter limits" on folder access ([NSDocumentsFolderUsageDescription](https://developer.apple.com/documentation/bundleresources/information-property-list/nsdocumentsfolderusagedescription)).

14. **GC choice.** ZGC exists only in generational mode since JDK 24 ([JEP 490](https://openjdk.org/jeps/490)). It is not the default: G1 becomes the default everywhere only in JDK 27 ([JEP 523](https://openjdk.org/jeps/523)).
    - Measured idle footprint of a trivial program: ZGC 40 MB, G1 22 MB, Serial 21 MB.
    - **Change:** use G1 (or Serial) with a small `-Xmx`, not ZGC. See Q8.

15. **Measurement caveat.** No JDK 25 is installed on the target Mac. All measurements used Temurin 24.0.1. The native-dominated numbers (ONNX Runtime, tokenizers) should carry over, but repeat them on 25 with JMH.

---

## Q1. Current versions (as of 2026-11-23)

| Component | Latest stable | Released | Source |
|---|---|---|---|
| Java LTS | **25** (GA 2025-09-16). Adoptium's latest 25 build is `jdk-25.0.5.1+2` (binary published 2026-11-18). Latest feature release is 27. | 2025-09-16 | [OpenJDK JDK 25](https://openjdk.org/projects/jdk/25/), [Adoptium available_releases](https://api.adoptium.net/v3/info/available_releases) (`most_recent_lts: 25`), [Adoptium latest 25 mac aarch64](https://api.adoptium.net/v3/assets/latest/25/hotspot?architecture=aarch64&image_type=jdk&os=mac) |
| Spring Boot | **4.2.0** (also 4.1.3 the same day) | 2026-11-19 | [Maven Central metadata](https://repo1.maven.org/maven2/org/springframework/boot/spring-boot/maven-metadata.xml) |
| Spring Framework | 7.1.0 (required by Boot 4.2.0) | 2026-11-19 | [metadata](https://repo1.maven.org/maven2/org/springframework/spring-core/maven-metadata.xml), [Boot system requirements](https://docs.spring.io/spring-boot/system-requirements.html) |
| Spring Security | **7.2.0** (managed by Boot 4.2.0) | 2026-11-19 | [metadata](https://repo1.maven.org/maven2/org/springframework/security/spring-security-core/maven-metadata.xml) |
| springdoc-openapi | **3.2.1**. Supports Boot 4: its parent POM inherits `spring-boot-starter-parent` **4.2.0**. | 2026-11-20 | [metadata](https://repo1.maven.org/maven2/org/springdoc/springdoc-openapi-starter-webmvc-ui/maven-metadata.xml), [springdoc-openapi-3.2.1.pom](https://repo1.maven.org/maven2/org/springdoc/springdoc-openapi/3.2.1/springdoc-openapi-3.2.1.pom) |
| Apache Lucene | **10.6.0** (no 11.x on Central) | 2026-10-29 | [Lucene news](https://lucene.apache.org/core/corenews.html), [metadata](https://repo1.maven.org/maven2/org/apache/lucene/lucene-core/maven-metadata.xml) |
| ONNX Runtime Java | **1.32.0** | 2026-11-13 | [metadata](https://repo1.maven.org/maven2/com/microsoft/onnxruntime/onnxruntime/maven-metadata.xml) |
| Apache PDFBox | **3.0.8** (2.0.37 is the older line) | 2026-07-11 | [pdfbox.apache.org](https://pdfbox.apache.org/), [metadata](https://repo1.maven.org/maven2/org/apache/pdfbox/pdfbox/maven-metadata.xml) |
| picocli | **4.7.7** | 2025-04-19 | [metadata](https://repo1.maven.org/maven2/info/picocli/picocli/maven-metadata.xml) |
| ArchUnit | **1.5.1**. Java 25 class files supported since 1.4.1. | 2026-09-25 | [metadata](https://repo1.maven.org/maven2/com/tngtech/archunit/archunit/maven-metadata.xml), [ArchUnit releases](https://github.com/TNG/ArchUnit/releases) |
| JUnit | **6.1.3** stable; 6.2.0-M1 milestone on 2026-11-15 | 2026-08-07 | [metadata](https://repo1.maven.org/maven2/org/junit/jupiter/junit-jupiter-api/maven-metadata.xml) |
| JMH | **1.37** (no newer release) | 2023-08-03 | [metadata](https://repo1.maven.org/maven2/org/openjdk/jmh/jmh-core/maven-metadata.xml). Compatibility with JDK 25 is **unverified** (not tested here). |
| io.methvin:directory-watcher | **0.19.1** | 2025-06-23 | [metadata](https://repo1.maven.org/maven2/io/methvin/directory-watcher/maven-metadata.xml) |
| Apache Maven | **3.10.2** (previous stable 3.9.16; preview 4.0.0-rc-7) | 2026-11-18 | [maven.apache.org/download](https://maven.apache.org/download.cgi), [apache-maven metadata](https://repo1.maven.org/maven2/org/apache/maven/apache-maven/maven-metadata.xml) |
| DJL tokenizers / sentencepiece | **0.40.0** (HF tokenizers native 0.21.0) | 2026-10-30 | [tokenizers metadata](https://repo1.maven.org/maven2/ai/djl/huggingface/tokenizers/maven-metadata.xml), [sentencepiece metadata](https://repo1.maven.org/maven2/ai/djl/sentencepiece/sentencepiece/maven-metadata.xml) |

Other details:
- **Spring Boot 4.2.0 requirements:** "requires at least Java 17 and is compatible with versions up to and including Java 27". Embedded Tomcat 11.0.x / Jetty 12.1.x (Servlet 6.1). Maven 3.6.3+ ([system requirements](https://docs.spring.io/spring-boot/system-requirements.html)).
- **Versions Boot 4.2.0 manages:** Jackson 3.1.8 (`jackson-bom`), Jackson 2.21.8 (`jackson-2-bom`), Tomcat 11.0.27, SLF4J 2.0.20, Logback 1.6.6, JUnit Jupiter 6.1.3 ([BOM](https://repo1.maven.org/maven2/org/springframework/boot/spring-boot-dependencies/4.2.0/spring-boot-dependencies-4.2.0.pom)).
- **Spring Security dependency:** `spring-security-oauth2-jose` 7.2.0 depends on `nimbus-jose-jwt` 10.9.1 ([POM](https://repo1.maven.org/maven2/org/springframework/security/spring-security-oauth2-jose/7.2.0/spring-security-oauth2-jose-7.2.0.pom)).
- **Lucene Java requirement:** Lucene 10 needs Java 21+. The `lucene-core` 10.6.0 classes are class-file major version 65 (Java 21), checked with `javap -v`. The news page states JDK 21 for 10.0.0 ([news](https://lucene.apache.org/core/corenews.html)).

## Q2. intfloat/multilingual-e5-small

| Fact | Value | Source |
|---|---|---|
| License | MIT | [HF model API `cardData.license`](https://huggingface.co/api/models/intfloat/multilingual-e5-small) |
| Architecture | BERT-style, 12 layers, 12 heads, hidden size 384, vocabulary 250,037, `XLMRobertaTokenizer` | [config.json](https://huggingface.co/intfloat/multilingual-e5-small/blob/main/config.json) |
| Embedding dimension | 384 | [model card](https://huggingface.co/intfloat/multilingual-e5-small), [1_Pooling/config.json](https://huggingface.co/intfloat/multilingual-e5-small/blob/main/1_Pooling/config.json) |
| Max tokens | 512. The card says "Long texts will be truncated to at most 512 tokens". `max_position_embeddings` and `max_seq_length` are both 512. | [model card](https://huggingface.co/intfloat/multilingual-e5-small), [sentence_bert_config.json](https://huggingface.co/intfloat/multilingual-e5-small/blob/main/sentence_bert_config.json) |
| Pooling | Mean pooling over the attention mask (`pooling_mode_mean_tokens: true`), then L2 normalization (`2_Normalize` module) | [1_Pooling/config.json](https://huggingface.co/intfloat/multilingual-e5-small/blob/main/1_Pooling/config.json), [modules.json](https://huggingface.co/intfloat/multilingual-e5-small/blob/main/modules.json), card code `average_pool` + `F.normalize` |
| Prefixes | Required. The card says: "Each input text should start with "query: " or "passage: ", even for non-English texts". FAQ: without them "you will see a performance degradation". | [model card FAQ](https://huggingface.co/intfloat/multilingual-e5-small) |
| Similarity scores | The card says cosine scores cluster in 0.7–1.0 (InfoNCE temperature 0.01). Only ranking matters, so **do not use absolute thresholds** for "no semantic match". | [model card FAQ](https://huggingface.co/intfloat/multilingual-e5-small) |
| ONNX in the official repo | `onnx/model.onnx` 470,268,510 B (fp32, added 2023-09-08). `onnx/model_qint8_avx512_vnni.onnx` 118,346,824 B (int8, added 2025-02-17). `onnx/model_O4.onnx` 235,052,531 B (added 2025-02-17). The `onnx/` folder also holds `tokenizer.json` (17 MB) and `sentencepiece.bpe.model` (5 MB). | [HF file tree API](https://huggingface.co/api/models/intfloat/multilingual-e5-small/tree/main?recursive=true), [commit history](https://huggingface.co/api/models/intfloat/multilingual-e5-small/commits/main) |
| ONNX graph I/O | Inputs `input_ids`, `attention_mask`, `token_type_ids` (int64). Output `last_hidden_state` [1, seq, 384]. **Pooling and normalization must be done in Java.** | Read from the files and confirmed by `OrtSession.getInputNames()/getOutputNames()` locally |
| Quantized ONNX | Yes: `model_qint8_avx512_vnni.onnx`, an x86 VNNI-targeted export that also runs on arm64 (measured). What `model_O4.onnx` is was not tested and is **unverified**. | as above |

**Tokenizing in Java.** The options, measured with DJL 0.40.0:

- `ai.djl.huggingface:tokenizers:0.40.0`
  - Ships `native/lib/osx-aarch64/cpu/libtokenizers.dylib`, ad-hoc signed, `minos` 11.0 (jar inspected).
  - Depends on `ai.djl:api:0.40.0`, which pulls in gson, JNA 5.17.0, commons-compress and slf4j-api 2.0.17 ([POM](https://repo1.maven.org/maven2/ai/djl/api/0.40.0/api-0.40.0.pom)).
  - Works, with about 0.03 ms per query, but uses about 350 MB of native memory with this tokenizer (see risk 2).
- `ai.djl.sentencepiece:sentencepiece:0.40.0`
  - Ships `native/lib/osx-aarch64/libsentencepiece_native.dylib` (jar inspected).
  - About 0.005 ms per encode and 96 MB total process footprint.
  - Needs the XLM-R id mapping ([transformers source](https://github.com/huggingface/transformers/blob/v4.46.0/src/transformers/models/xlm_roberta/tokenization_xlm_roberta.py#L133-L136)).

## Q3. ONNX Runtime Java on Apple Silicon

- **macOS arm64 natives are included in the standard jar.** `onnxruntime-1.32.0.jar` contains:
  - `ai/onnxruntime/native/osx-aarch64/libonnxruntime.dylib` and `libonnxruntime4j_jni.dylib`
  - `linux-x64`, `linux-aarch64` and `win-x64` natives, but **no osx-x64** natives
  - `.dSYM` debug bundles

  The dylib's `LC_BUILD_VERSION minos` is 14.0 and it is ad-hoc signed (inspected with `unzip -l`, `otool` and `codesign`). The [Java getting-started page](https://onnxruntime.ai/docs/get-started/with-java.html) still lists "macOS x64" only, so it is out of date against the artifact. The Java API classes are Java 8 bytecode (major 52), and the docs say "Java 8 or newer".
- **CoreML from Java works.** The arm64 dylib links `CoreML.framework` (`otool -L`). `OrtSession.SessionOptions` has `addCoreML()`, `addCoreML(EnumSet<CoreMLFlags>)` and `addCoreML(Map<String,String>)`, plus `addXnnpack(Map)` and `addWebGPU(Map)` (`javap`). Apple's docs say the CoreML EP "can be used via the C, C++, Objective-C, C# and Java APIs". Options include `ModelFormat` (NeuralNetwork/MLProgram), `MLComputeUnits`, `RequireStaticInputShapes` and `ModelCacheDirectory` ([CoreML EP docs](https://onnxruntime.ai/docs/execution-providers/CoreML-ExecutionProvider.html)). It is not useful for this model as-is: see risk 4.
- **Native loading** can be redirected with the system property `onnxruntime.native.path`, or per library with `onnxruntime.native.<lib>.path`. By default the natives are extracted from the jar to a temp directory. These property names come from the strings in `ai.onnxruntime.OnnxRuntime`. The official README could not be fetched, so the exact semantics are **unverified** beyond the property names.
- **Latency.** No primary vendor benchmark exists for this model on M2, so **vendor numbers are unverified**. Local measurements on the target machine (M2, 8 GB, macOS 26.5, Temurin 24.0.1, ORT 1.32.0, 4 intra-op threads; p50 / p95 over 200 runs after warm-up, including tokenize, inference and mean-pool):

| Configuration | Query (18 tokens) p50 / p95 | Passage (245 tokens) p50 | Session load | phys_footprint steady / peak |
|---|---|---|---|---|
| fp32, CPU EP, HF tokenizer | 4.2 / 7.2 ms | 40 ms | 1.6 s | 809 / 1,668 MB |
| fp32, CPU EP, 1 thread | 10.7 / 15.3 ms | 145 ms | 1.5 s | (not comparable, see appendix) |
| int8 qint8, CPU EP, HF tokenizer | 2.1 / 2.8 ms | 19–24 ms | 1.0 s | 661 / 791 MB |
| int8, CPU EP, SentencePiece | 3.6 / 9.5 ms (arena off) | n/a | n/a | **310 / 379 MB** |
| fp32, CPU EP, SentencePiece | 5.0 / 5.9 ms (arena off) | n/a | n/a | 535 / 1,242 MB |
| fp32, CoreML EP `CPUAndNeuralEngine` | 10.8 / 12.3 ms | 76 ms | 7.4 s | 645 / 2,027 MB |
| fp32, CoreML EP `ALL` | 12.2 / 13.8 ms | 90 ms | 7.4 s | 880 / 2,033 MB |

## Q4. Lucene 10 vector search, fusion and highlighting

Class names below were checked against the 10.6.0 jars with `javap` and `unzip -l`. Javadoc links point to 10.5.0, because the 10.6.0 javadoc paths returned 404 when checked.

- **Indexing and querying:**
  - Indexing: `org.apache.lucene.document.KnnFloatVectorField(String, float[], VectorSimilarityFunction)`.
  - Querying: `org.apache.lucene.search.KnnFloatVectorQuery(String field, float[] target, int k[, Query filter[, KnnSearchStrategy]])`, or the helper `KnnFloatVectorField.newVectorQuery(...)`.
  - Byte variants: `KnnByteVectorField` / `KnnByteVectorQuery`.
  - Similarity values: `EUCLIDEAN`, `DOT_PRODUCT`, `COSINE`, `MAXIMUM_INNER_PRODUCT`. Because e5 vectors are L2-normalized, `DOT_PRODUCT` fits; that last point is my synthesis.
  - Also available: `SeededKnnVectorQuery`, `PatienceKnnVectorQuery`.
- **Codecs in 10.6.0:**
  - Default codec: `Lucene104Codec`.
  - Vector formats registered for lookup by name (Java service-loader file in the jar): `Lucene99HnswVectorsFormat` (float), `Lucene104ScalarQuantizedVectorsFormat`, `Lucene104HnswScalarQuantizedVectorsFormat`.
  - Encodings: the quantized format takes a `QuantizedByteVectorValues.ScalarEncoding` of `UNSIGNED_BYTE`, `SEVEN_BIT`, `PACKED_NIBBLE` (4-bit), `SINGLE_BIT_QUERY_NIBBLE` or `DIBIT_QUERY_NIBBLE`.
  - Release history: 10.4.0 added "codecs for 4 and 8 bit Optimized Scalar Quantization vectors" that replace the Lucene99 scalar-quantized format; the Lucene102 binary formats were also replaced ([CHANGES 10.4.0](https://lucene.apache.org/core/10_6_0/changes/Changes.html)). `Lucene99HnswScalarQuantizedVectorsFormat` and `Lucene102HnswBinaryQuantizedVectorsFormat` now live in `lucene-backward-codecs`.
  - Oddity: the `NAME` constant in `Lucene104HnswScalarQuantizedVectorsFormat` reads `"Lucene104HnswBinaryQuantizedVectorsFormat"` in the bytecode. Select the format by class, not by name string.
- **Max dimensions:** `KnnVectorsFormat.DEFAULT_MAX_DIMENSIONS = 1024`. 384 is well within it.
- **Combining with BM25:**
  - Run a BM25 `Query` and a `KnnFloatVectorQuery` separately and fuse them with `TopDocs.rrf(topN, k, TopDocs[])`. This is "Reciprocal Rank Fusion… especially well suited when combining hits computed via different methods, whose score distributions are hardly comparable" ([javadoc](https://lucene.apache.org/core/10_5_0/core/org/apache/lucene/search/TopDocs.html)). It was added in 10.2.0 ([news](https://lucene.apache.org/core/corenews.html)).
  - Or put both in a `BooleanQuery` as SHOULD clauses. That is a linear score sum, which the plan does not want.
  - `LogOddsFusionQuery` (10.5.0, experimental) is a single-query probabilistic alternative ([javadoc](https://lucene.apache.org/core/10_5_0/core/org/apache/lucene/search/LogOddsFusionQuery.html)).
  - For one result per file: `org.apache.lucene.search.join.DiversifyingChildrenFloatKnnVectorQuery(field, target, childFilter, k, BitSetProducer parents)` in `lucene-join` 10.6.0.
- **Highlighter:** `org.apache.lucene.search.uhighlight.UnifiedHighlighter`, described as "a flexible highlighter that can get offsets from postings, term vectors, or analysis". It treats the document as the corpus, scores each passage, and returns the best passages via `PassageScorer` and `PassageFormatter` ([javadoc](https://lucene.apache.org/core/10_5_0/highlighter/org/apache/lucene/search/uhighlight/UnifiedHighlighter.html)). Calling it "the recommended" highlighter is **unverified** wording. `MatchHighlighter` and `FastVectorHighlighter` also exist. For semantic-only hits there are no query terms to highlight, so the best passage has to be the matched chunk itself (my synthesis).
- **SIMD:** pass `--add-modules jdk.incubator.vector` (risk 7).

## Q5. Spring Boot 4 specifics

- **Virtual threads:** set `spring.threads.virtual.enabled=true`. The docs say: "Virtual threads require Java 21 or later. For the best experience, Java 24 or later is strongly recommended". Set `spring.main.keep-alive=true` if only virtual threads would keep the JVM alive. Thread-pool properties stop having an effect ([Boot 4.2 reference](https://docs.spring.io/spring-boot/reference/features/spring-application.html#features.spring-application.virtual-threads)).
- **Self-issued JWTs:**
  - Starter: `org.springframework.boot:spring-boot-starter-security-oauth2-resource-server:4.2.0` exists on Central.
  - Spring Security 7.2.0 has `NimbusJwtEncoder.withSecretKey(SecretKey)`, `withKeyPair(RSAPublicKey, RSAPrivateKey)` and `withKeyPair(ECPublicKey, ECPrivateKey)`, plus `NimbusJwtDecoder.withSecretKey(SecretKey)` and `withPublicKey(RSAPublicKey)` (checked with `javap` on the 7.2.0 jar).
  - Wire the decoder with `http.oauth2ResourceServer(o -> o.jwt(Customizer.withDefaults()))` or `.jwt(j -> j.decoder(...))`. The resource server's default clock skew is 60 s ([Spring Security 7.2 JWT docs](https://docs.spring.io/spring-security/reference/servlet/oauth2/resource-server/jwt.html)).
  - Which validators `withSecretKey(...).build()` applies by default is not stated on that page, so it is **unverified**. Set the `JwtTimestampValidator` explicitly.
  - A single process both issues and verifies the tokens, so an HMAC `SecretKey` (encoder and decoder share it) is the simplest choice (my synthesis).
- **Breaking changes that matter here** ([migration guide](https://github.com/spring-projects/spring-boot/wiki/Spring-Boot-4.0-Migration-Guide)):
  - Starters are modularized; `spring-boot-starter-web` is deprecated in favour of `spring-boot-starter-webmvc` (Central's 4.2.0 POM description confirms this).
  - Jackson 3 is preferred: packages and groupId are now `tools.jackson`, `jackson-annotations` stays `com.fasterxml`, and you define a `JsonMapper` bean, not an `ObjectMapper` bean. A `spring-boot-jackson2` bridge exists but is deprecated.
  - `@MockBean` becomes `@MockitoBean`.
  - `@SpringBootTest` no longer auto-provides MockMvc; add `@AutoConfigureMockMvc`.
  - Undertow is removed.

## Q6. Java file watching on macOS

- **The JDK polls.** In JDK 25 (`jdk-25+36`) and in `master`, `BsdFileSystem.newWatchService()` returns `new PollingWatchService()`, with the comment "use polling implementation until we implement a BSD/kqueue one" ([source](https://github.com/openjdk/jdk/blob/jdk-25%2B36/src/java.base/macosx/classes/sun/nio/fs/BsdFileSystem.java#L51-L57)). `PollingWatchService.POLLING_INTERVAL = 2` seconds, and there is no recursive (`FILE_TREE`) watching ([source](https://github.com/openjdk/jdk/blob/jdk-25%2B36/src/java.base/share/classes/sun/nio/fs/PollingWatchService.java)). The open enhancement is [JDK-7133447](https://bugs.openjdk.org/browse/JDK-7133447), referenced from the directory-watcher README.
- **directory-watcher uses FSEvents.** The README says it "provides a JNA-based `WatchService` for Mac OS X… based on the Carbon File System Events API" ([README](https://github.com/gmethvin/directory-watcher)). In the jar, `io.methvin.watchservice.jna.CarbonAPI` loads the `Carbon` library and calls `FSEventStreamCreate`, through `MacOSXListeningWatchService`.
- **Runtime dependencies:** JNA 5.16.0 and slf4j-api 1.7.36 ([POM](https://repo1.maven.org/maven2/io/methvin/directory-watcher/0.19.1/directory-watcher-0.19.1.pom)). Boot's dependency management will raise SLF4J to 2.0.20; that this is API-compatible is my synthesis.
- **arm64:** the JNA jar (checked 5.17.0) ships `com/sun/jna/darwin-aarch64/libjnidispatch.jnilib`. The latest JNA is 5.19.1 ([metadata](https://repo1.maven.org/maven2/net/java/dev/jna/jna/maven-metadata.xml)). directory-watcher itself is pure Java, so it works on arm64 through JNA. Running it on M2 was **not tested** here.
- See risk 11 for the hashing default and the missing replay after restart.

## Q7. Bundling the JVM inside Pounce.app

- **Minimal runtime with jlink.**
  - Options: `--add-modules`, `--strip-debug`, `--no-header-files`, `--no-man-pages`, `--generate-cds-archive`, `--include-locales` ([jlink JDK 25 man page](https://docs.oracle.com/en/java/javase/25/docs/specs/man/jlink.html)).
  - Compression: `jlink --list-plugins` on the local Temurin 24 lists `--compress zip-[0-9]` (default `zip-6`) and `--strip-native-commands`. The JDK 25 man page was reported to show `--compress=0|1|2`; confirm the accepted values with `jlink --list-plugins` on the JDK 25 build.
  - Some vendor JDKs have no `jmods/` directory. Temurin 24 on this Mac has none. [JEP 493](https://openjdk.org/jeps/493) (JDK 24) lets jlink link from the run-time image instead, but it is a build-time option that "some JDK vendors may choose not to enable".
  - Remember `jdk.incubator.vector` (risk 7).
- **jpackage:**
  - Options: `--type app-image`, `--runtime-image`, `--mac-sign`, `--mac-signing-key-user-name`, `--mac-entitlements`, `--mac-package-identifier`. Its default entitlements resource is `sandbox.plist` ([jpackage JDK 25 man page](https://docs.oracle.com/en/java/javase/25/docs/specs/man/jpackage.html)); see risk 13.
  - Since Pounce.app is an Xcode/SwiftUI app, it is simpler to embed a jlink image and sign it inside the Xcode build than to use jpackage's app image (my synthesis).
- **Helper and login item with `SMAppService` (macOS 13+)** ([SMAppService](https://developer.apple.com/documentation/servicemanagement/smappservice)):
  - `agent(plistName:)`: "The property list name must correspond to a property list in the calling app's `Contents/Library/LaunchAgents` directory" ([docs](https://developer.apple.com/documentation/servicemanagement/smappservice/agent(plistname:))).
  - `loginItem(identifier:)`: needs a helper *app bundle* in `Contents/Library/LoginItems` ([docs](https://developer.apple.com/documentation/servicemanagement/smappservice/loginitem(identifier:))).
  - In agent plists, "replace the `Program` key with the `BundleProgram` key and make the path relative to the bundle". Agents in the bundle "automatically associate with that app in the Login Items panel" ([Updating helper executables](https://developer.apple.com/documentation/servicemanagement/updating-helper-executables-from-earlier-versions-of-macos)).
  - `.requiresApproval` means "the user needs to take action in System Settings" ([docs](https://developer.apple.com/documentation/servicemanagement/smappservice/status-swift.enum/requiresapproval)).
  - **Recommendation:** use `agent` with `BundleProgram` pointing at the embedded `java` launcher, or at a small native launcher, which would also make Activity Monitor show "Pounce" rather than "java". The launcher name is cosmetic and **unverified** for TCC.
  - Apple's bundle layout says helper tools go in `Contents/MacOS/` or `Contents/Helpers/` and dylibs in `Contents/Frameworks/`. It warns that wrongly placed code "might cause problems during notarization" ([Placing content in a bundle](https://developer.apple.com/documentation/bundleresources/placing-content-in-a-bundle)). Where a full JDK runtime tree belongs is not covered by that table; **unverified**.
- **TCC responsible code** (Apple DTS forum posts):
  - Child process spawned by an app: "The system treats the app as the responsible code."
  - LaunchAgent: "If the daemon or agent was installed by `SMAppService`, that makes the app the responsible code."
  - The algorithm "is not documented, has changed in the past, and may well change in the future" ([thread 756510](https://developer.apple.com/forums/thread/756510), Jun 2024).
  - Responsible *code* can exist without a responsible *process*, "if an app has a login item, the login item might be running when the app is not" ([thread 731504](https://developer.apple.com/forums/thread/731504), Jun 2023).
  - The child "might be doing things to break the chain" ([thread 805245](https://developer.apple.com/forums/thread/805245), Nov 2025).
  - The folder prompts are driven by `NSDocumentsFolderUsageDescription` and its siblings in the app's Info.plist, and can be reset with `tccutil reset SystemPolicyDocumentsFolder <bundleID>` ([docs](https://developer.apple.com/documentation/bundleresources/information-property-list/nsdocumentsfolderusagedescription)).
- **Signing and notarization:**
  - Notarization requires every executable to be signed with a Developer ID certificate, with the Hardened Runtime on, a secure timestamp, and no `get-task-allow` ([Notarizing macOS software](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)).
  - Hardened Runtime exceptions are added "only to executables. Shared libraries… inherit the entitlements of their host executable" ([Hardened Runtime](https://developer.apple.com/documentation/security/hardened-runtime)).
  - `allow-jit` allows `MAP_JIT` memory ([docs](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.allow-jit)).
  - `allow-unsigned-executable-memory` "exposes your app to common vulnerabilities" ([docs](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.allow-unsigned-executable-memory)).
  - `disable-library-validation`: see risk 5.
  - The vendor JDK launchers on this Mac (Temurin 24, Oracle 23) are signed with `flags=0x10000(runtime)` and the entitlement set listed in risk 13 (`codesign -d --entitlements`).
  - Which entitlements HotSpot 25 strictly needs on arm64 is **unverified**.

## Q8. GC choice

- **Generational ZGC** is ZGC's only mode in Java 25. It became the default ZGC mode in JDK 23 ([JEP 474](https://openjdk.org/jeps/474)) and non-generational mode was removed in JDK 24 ([JEP 490](https://openjdk.org/jeps/490)). It is available with `-XX:+UseZGC`. ZGC on macOS/aarch64 ran locally on Temurin 24; on 25 it is **not tested here**.
- **Default GC in Java 25:** ZGC is not the default. JDK 27 makes G1 the default in all environments, replacing the case where the JVM "previously selected the Serial collector" ([JEP 523](https://openjdk.org/jeps/523)). The JDK 25 ergonomics on an M2 with 8 GB were not checked against a primary source, so they are **unverified**. Run `java -XX:+PrintFlagsFinal -version | grep Use.*GC` on the target.
- **Measured idle footprint** (trivial program, `-Xmx256m`, Temurin 24): Serial 21 MB, G1 22 MB, ZGC 40 MB.
- **Suggested flags for a small always-on service.** Each is sourced, but the values should be benchmarked:
  - `-XX:+UseG1GC` (or `-XX:+UseSerialGC`)
  - a small `-Xmx` (128–192 MB; the heap was nearly empty in tests, because model and tokenizer memory is native)
  - `-XX:+UseCompactObjectHeaders`, a product feature in JDK 25 ([JEP 519](https://openjdk.org/jeps/519))
  - `-XX:G1PeriodicGCInterval=<ms>`, so G1 "automatically return[s] Java heap memory to the operating system when idle" ([JEP 346](https://openjdk.org/jeps/346))
  - for startup of the login agent, the AOT cache ([JEP 514](https://openjdk.org/jeps/514), [JEP 515](https://openjdk.org/jeps/515), both in [JDK 25](https://openjdk.org/projects/jdk/25/))
  - `--add-modules jdk.incubator.vector`
  - `--enable-native-access=ALL-UNNAMED`
- **ZGC is not recommended here.** Its low-latency advantage does not matter for a heap this small, and its baseline footprint is higher (measured above). This is my synthesis.

---

## Appendix: how the measurements were taken

- **Machine:** Apple M2, 8 GB, macOS 26.5 (25F71). JVM: Temurin 24.0.1 (no JDK 25 installed).
- **Libraries:** `onnxruntime` 1.32.0, `ai.djl.huggingface:tokenizers` 0.40.0, `ai.djl.sentencepiece:sentencepiece` 0.40.0, JNA 5.17.0.
- **Models** from `intfloat/multilingual-e5-small/onnx/`: `model.onnx`, `model_qint8_avx512_vnni.onnx`, `tokenizer.json`, `sentencepiece.bpe.model`.
- **Harness:** a plain Java harness, not JMH. ORT `ALL_OPT`, `setIntraOpNumThreads(4)`. 30 warm-up queries, then 200 timed single queries (`"query: how do I renew my passport before travelling abroad"`, 18 tokens) and 30 timed passages (245 tokens). Each configuration ran in a fresh JVM with `-Xmx256m` (128m for the SentencePiece runs).
- **Memory:** `footprint -p <pid>` phys_footprint after `System.gc()`. `ps` RSS was unreliable under memory pressure: for example, 170 MB RSS while phys_footprint was much higher. The "1 thread" row has only an RSS reading, so it is not comparable.
- The "arena off" rows also set `setCPUArenaAllocator(false)` and `setMemoryPatternOptimization(false)`. Turning the arena off did not change footprint much (int8 + HF: 674 MB, against 661 MB with it on).
- **Treat all numbers as indicative.** Re-measure with JMH on JDK 25 inside the real Spring process.
