package dev.pounce.infrastructure;

import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.within;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/** Contract test for the real Embedder: real model, real text. Needs scripts/fetch-model.sh to have run. */
class OnnxEmbedderTest {

    static OnnxEmbedder embedder;

    @BeforeAll
    static void load() {
        Path modelDir = Path.of(System.getProperty("pounce.model.dir", "models/bge-small-en-v1.5"));
        assumeTrue(Files.exists(modelDir.resolve("model.onnx")), "model missing: run scripts/fetch-model.sh");
        embedder = OnnxEmbedder.load(modelDir);
    }

    @AfterAll
    static void close() {
        if (embedder != null) {
            embedder.close();
        }
    }

    @Test
    void vectors_have_384_dimensions_and_unit_length() {
        float[] query = embedder.embedQuery("canary deployment");
        float[] passage = embedder.embedPassages(List.of("We shifted 10% of traffic to the new version.")).getFirst();

        assertThat(query).hasSize(384);
        assertThat(length(query)).isCloseTo(1.0, within(1e-3));
        assertThat(length(passage)).isCloseTo(1.0, within(1e-3));
    }

    @Test
    void a_query_is_closest_to_the_passage_that_means_the_same_without_sharing_its_words() {
        float[] query = embedder.embedQuery("how do I renew my TLS certificate");
        List<float[]> passages = embedder.embedPassages(List.of(
                "Steps to rotate an expiring SSL cert on the load balancer before it lapses.",
                "Mix the mashed bananas with flour, sugar and butter, then bake for an hour.",
                "The quarterly budget review moved to Thursday afternoon."));

        double related = dot(query, passages.get(0));
        assertThat(related).isGreaterThan(dot(query, passages.get(1)));
        assertThat(related).isGreaterThan(dot(query, passages.get(2)));
    }

    @Test
    void a_passage_longer_than_the_model_accepts_is_still_embedded() {
        String tooLong = "the canary rollout shifted traffic to the new version ".repeat(200); // ~2,000 tokens

        float[] vector = embedder.embedPassages(List.of(tooLong)).getFirst();

        assertThat(vector).hasSize(384);
        assertThat(length(vector)).isCloseTo(1.0, within(1e-3));
    }

    private static double dot(float[] a, float[] b) {
        double sum = 0;
        for (int i = 0; i < a.length; i++) {
            sum += a[i] * b[i];
        }
        return sum;
    }

    private static double length(float[] v) {
        return Math.sqrt(dot(v, v));
    }
}
