package dev.pounce.infrastructure;

import ai.djl.huggingface.tokenizers.HuggingFaceTokenizer;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;
import java.util.stream.IntStream;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * Golden test: the pure-Java tokenizer must give exactly the token ids, token type ids and attention mask of the
 * Hugging Face reference (the Rust library through DJL, set up the way {@code OnnxEmbedder} used it).
 *
 * <p>Skips itself where DJL's native library cannot load, which is the case on Windows; {@link
 * BertWordPieceTokenizerTest} then still checks the frozen fixture this test writes.
 */
class BertWordPieceTokenizerGoldenTest {

    private static final int MAX_TOKENS = 512;
    private static final Path FIXTURE = Path.of("src/test/resources/dev/pounce/infrastructure/tokenizer-golden.jsonl");

    static Path modelDir;
    static Path repository;
    static BertWordPieceTokenizer tokenizer;
    static HuggingFaceTokenizer reference;
    static int compared;

    @BeforeAll
    static void load() throws IOException {
        modelDir = Path.of(System.getProperty("pounce.model.dir", "models/bge-small-en-v1.5"));
        repository = Path.of(System.getProperty("pounce.repository.dir", ".."));
        assumeTrue(Files.exists(modelDir.resolve("tokenizer.json")), "model missing: run scripts/fetch-model.sh");
        tokenizer = BertWordPieceTokenizer.load(modelDir.resolve("tokenizer.json"));
        try {
            // Exactly how OnnxEmbedder built its tokenizer before it went pure Java.
            reference = HuggingFaceTokenizer.builder()
                    .optTokenizerPath(modelDir.resolve("tokenizer.json"))
                    .optMaxLength(MAX_TOKENS)
                    .optTruncation(true)
                    .build();
        } catch (Exception | LinkageError e) {
            assumeTrue(false, "reference tokenizer unavailable on this platform (" + e + "); "
                    + "BertWordPieceTokenizerTest still checks the frozen fixture");
        }
    }

    @AfterAll
    static void close() {
        if (reference != null) {
            reference.close();
            System.out.printf("Tokenizer golden test: %,d texts compared with the reference%n", compared);
        }
    }

    @Test
    void hand_written_cases_for_each_tricky_rule() {
        assertSameAsReference(TokenizerCorpus.handwritten());
    }

    @Test
    void the_texts_the_repository_tests_use() {
        List<String> texts = TokenizerCorpus.repositoryTestFixtures(repository);
        assertThat(texts).hasSizeGreaterThan(100);
        assertSameAsReference(texts);
    }

    @Test
    void the_repository_documents_and_code() {
        assertSameAsReference(TokenizerCorpus.repositoryDocumentsAndCode(repository));
    }

    @Test
    void pseudo_random_strings_from_mixed_unicode_ranges() {
        assertSameAsReference(TokenizerCorpus.random(20261125L, 5_000));
    }

    @Test
    void every_code_point_alone_and_inside_a_word() {
        assertSameAsReference(TokenizerCorpus.codePointSweep(0, Character.MAX_CODE_POINT + 1, 48));
    }

    /**
     * The fixture {@link BertWordPieceTokenizerTest} checks without the reference must hold the reference's ids.
     * To write it again (after changing the cases): {@code mvn test -pl pounce-infrastructure
     * -Dtest=BertWordPieceTokenizerGoldenTest -Dpounce.tokenizer.writeFixture=true}.
     */
    @Test
    void the_frozen_fixture_holds_the_reference_ids() throws IOException {
        if (Boolean.getBoolean("pounce.tokenizer.writeFixture")) {
            writeFixture();
        }
        List<String> lines = Files.readAllLines(FIXTURE, StandardCharsets.US_ASCII);
        List<String> texts = new ArrayList<>();
        for (String line : lines) {
            @SuppressWarnings("unchecked")
            var fixture = (Map<String, Object>) TinyJson.parse(line);
            String text = (String) fixture.get("text");
            texts.add(text);
            assertThat(fixture.get("ids")).as(line)
                    .isEqualTo(Arrays.stream(reference.encode(text).getIds()).boxed().toList());
        }
        compared += texts.size();
    }

    private static void writeFixture() throws IOException {
        List<String> texts = new ArrayList<>(TokenizerCorpus.handwritten());
        texts.addAll(RankingTest.CORPUS.values());
        texts.addAll(RankingTest.FILLER.values());
        TokenizerCorpus.random(42L, 400).stream().filter(text -> text.length() < 200).forEach(texts::add);
        var lines = new StringBuilder();
        for (String text : texts) {
            if (text.length() > 5_000) {
                continue;
            }
            long[] ids = reference.encode(text).getIds();
            lines.append("{\"text\":").append(asciiJson(text)).append(",\"ids\":")
                    .append(Arrays.toString(ids).replace(" ", "")).append("}\n");
        }
        Files.createDirectories(FIXTURE.getParent());
        Files.writeString(FIXTURE, lines, StandardCharsets.US_ASCII);
    }

    private static void assertSameAsReference(List<String> texts) {
        List<String> mismatches = new ArrayList<>();
        for (String text : texts) {
            var expected = reference.encode(text);
            var actual = tokenizer.encode(text, MAX_TOKENS);
            boolean same = Arrays.equals(expected.getIds(), actual.ids())
                    && Arrays.equals(expected.getTypeIds(), actual.typeIds())
                    && Arrays.equals(expected.getAttentionMask(), actual.attentionMask());
            if (!same && mismatches.size() < 20) {
                mismatches.add(asciiJson(text.length() > 300 ? text.substring(0, 300) + "…" : text)
                        + "\n  reference " + Arrays.toString(expected.getIds())
                        + "\n  ours      " + Arrays.toString(actual.ids()));
            } else if (!same) {
                mismatches.add("…");
            }
        }
        compared += texts.size();
        assertThat(mismatches).as("texts tokenized differently from the reference, out of %d", texts.size()).isEmpty();
    }

    /** A JSON string literal with everything outside printable ASCII escaped, so the fixture is plain ASCII. */
    static String asciiJson(String text) {
        var out = new StringBuilder("\"");
        IntStream.range(0, text.length()).forEach(i -> {
            char c = text.charAt(i);
            if (c == '"' || c == '\\') {
                out.append('\\').append(c);
            } else if (c >= 0x20 && c < 0x7F) {
                out.append(c);
            } else {
                out.append(String.format("\\u%04X", (int) c));
            }
        });
        return out.append('"').toString();
    }
}
