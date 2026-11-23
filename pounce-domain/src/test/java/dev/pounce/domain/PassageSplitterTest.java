package dev.pounce.domain;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class PassageSplitterTest {

    private final PassageSplitter splitter = new PassageSplitter();

    @Test
    void markdown_is_split_at_each_heading() {
        String note = """
                # Deploys
                The canary rollout went fine.
                ## Rollback
                Revert the helm release.
                """;

        assertThat(splitter.split(note, TextFormat.MARKDOWN))
                .extracting(Passage::text)
                .containsExactly("# Deploys\nThe canary rollout went fine.", "## Rollback\nRevert the helm release.");
    }

    @Test
    void long_plain_text_is_cut_into_overlapping_windows() {
        String text = "a".repeat(1_400) + "b".repeat(1_400) + "c".repeat(100);

        var passages = splitter.split(text, TextFormat.PLAIN);

        assertThat(passages).extracting(Passage::text).containsExactly(
                "a".repeat(1_400) + "b".repeat(200),
                "b".repeat(1_400) + "c".repeat(100));
    }

    @Test
    void blank_text_has_no_passages() {
        assertThat(splitter.split("  \n\n ", TextFormat.PLAIN)).isEmpty();
        assertThat(splitter.split("", TextFormat.MARKDOWN)).isEmpty();
    }
}
