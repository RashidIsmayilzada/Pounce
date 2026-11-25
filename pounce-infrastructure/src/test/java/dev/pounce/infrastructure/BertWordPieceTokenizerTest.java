package dev.pounce.infrastructure;

import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * The pure-Java tokenizer without the reference library: checked against a frozen fixture of ids the reference
 * produced (written by {@link BertWordPieceTokenizerGoldenTest}), so platforms where the reference cannot run,
 * such as Windows, still verify it. Plus the behaviour {@code OnnxEmbedder} relies on.
 */
class BertWordPieceTokenizerTest {

    static BertWordPieceTokenizer tokenizer;

    @BeforeAll
    static void load() throws IOException {
        Path modelDir = Path.of(System.getProperty("pounce.model.dir", "models/bge-small-en-v1.5"));
        assumeTrue(Files.exists(modelDir.resolve("tokenizer.json")), "model missing: run scripts/fetch-model.sh");
        tokenizer = BertWordPieceTokenizer.load(modelDir.resolve("tokenizer.json"));
    }

    @Test
    void gives_the_ids_the_reference_tokenizer_gave_for_the_frozen_fixture() throws IOException {
        List<String> mismatches = new ArrayList<>();
        int cases = 0;
        try (InputStream in = getClass().getResourceAsStream("tokenizer-golden.jsonl")) {
            assertThat(in).as("tokenizer-golden.jsonl on the test classpath").isNotNull();
            for (String line : new String(in.readAllBytes(), StandardCharsets.US_ASCII).split("\r?\n")) {
                if (line.isBlank()) {
                    continue;
                }
                @SuppressWarnings("unchecked")
                var fixture = (Map<String, Object>) TinyJson.parse(line);
                String text = (String) fixture.get("text");
                long[] expected = ((List<?>) fixture.get("ids")).stream().mapToLong(id -> (Long) id).toArray();

                var actual = tokenizer.encode(text, 512);

                cases++;
                if (!Arrays.equals(actual.ids(), expected)) {
                    mismatches.add(line);
                }
                assertThat(actual.typeIds()).hasSameSizeAs(expected).containsOnly(0L);
                assertThat(actual.attentionMask()).hasSameSizeAs(expected).containsOnly(1L);
            }
        }
        assertThat(cases).isGreaterThan(500);
        assertThat(mismatches).as("fixture lines tokenized differently, out of %d", cases).isEmpty();
    }

    @Test
    void wraps_the_text_in_cls_and_sep() {
        var encoding = tokenizer.encode("Hello, World!", 512);

        assertThat(encoding.ids()).containsExactly(101, 7592, 1010, 2088, 999, 102);
        assertThat(encoding.typeIds()).containsExactly(0, 0, 0, 0, 0, 0);
        assertThat(encoding.attentionMask()).containsExactly(1, 1, 1, 1, 1, 1);
    }

    @Test
    void an_empty_text_is_just_cls_and_sep() {
        assertThat(tokenizer.encode("", 512).ids()).containsExactly(101, 102);
        assertThat(tokenizer.encode(" \t\n\u200B", 512).ids()).containsExactly(101, 102);
    }

    @Test
    void a_long_text_is_cut_to_the_limit_and_keeps_its_sep() {
        var encoding = tokenizer.encode("canary ".repeat(1_000), 512);

        assertThat(encoding.ids()).hasSize(512);
        assertThat(encoding.ids()[0]).isEqualTo(101);
        assertThat(encoding.ids()[510]).isEqualTo(tokenizer.encode("canary", 512).ids()[1]);
        assertThat(encoding.ids()[511]).isEqualTo(102);
    }

    @Test
    void a_special_token_written_in_the_text_stays_one_token() {
        long sep = 102;
        assertThat(tokenizer.encode("a[SEP]b", 512).ids()).containsExactly(101, 1037, sep, 1038, 102);
        assertThat(tokenizer.encode("a [sep] b", 512).ids()).hasSize(7); // lowercase is just punctuation and a word
    }

    @Test
    void strips_accents_lowercases_and_splits_cjk_and_punctuation() {
        assertThat(tokenizer.encode("Crème BRÛLÉE", 512).ids())
                .containsExactly(tokenizer.encode("creme brulee", 512).ids());
        assertThat(tokenizer.encode("中文", 512).ids()).hasSize(4);
        assertThat(tokenizer.encode("C:\\Users", 512).ids()).hasSize(6); // c : \ users
    }

    @Test
    void a_word_longer_than_the_limit_is_unknown() {
        assertThat(tokenizer.encode("a".repeat(101), 512).ids()).containsExactly(101, 100, 102);
    }

    @Test
    void refuses_a_tokenizer_it_does_not_implement(@TempDir Path folder) throws IOException {
        Path json = folder.resolve("tokenizer.json");
        Files.writeString(json, """
                {"added_tokens": [], "normalizer": null, "pre_tokenizer": {"type": "Metaspace"},
                 "post_processor": null, "model": {"type": "Unigram", "vocab": [["<unk>", 0.0]]}}
                """);

        assertThatThrownBy(() -> BertWordPieceTokenizer.load(json))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("unsupported tokenizer.json: model type Unigram");
    }
}
