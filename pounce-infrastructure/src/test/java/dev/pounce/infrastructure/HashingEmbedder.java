package dev.pounce.infrastructure;

import dev.pounce.application.Embedder;

import java.util.List;
import java.util.Locale;

/**
 * Deterministic fake embedder for fast tests: each word adds 1 to a dimension chosen by its hash, then the vector
 * is normalised. Texts sharing words end up close; texts sharing none are orthogonal.
 */
final class HashingEmbedder implements Embedder {

    static final int DIMENSIONS = 384;

    /** How many Passages have been embedded so far, so tests can see whether work was skipped. */
    int passagesEmbedded;

    @Override
    public float[] embedQuery(String query) {
        return embed(query);
    }

    @Override
    public List<float[]> embedPassages(List<String> passages) {
        passagesEmbedded += passages.size();
        return passages.stream().map(HashingEmbedder::embed).toList();
    }

    private static float[] embed(String text) {
        float[] vector = new float[DIMENSIONS];
        for (String word : text.toLowerCase(Locale.ROOT).split("\\W+")) {
            if (!word.isEmpty()) {
                vector[Math.floorMod(word.hashCode(), DIMENSIONS)] += 1;
            }
        }
        double length = 0;
        for (float v : vector) {
            length += v * v;
        }
        if (length == 0) {
            vector[0] = 1;
            return vector;
        }
        float norm = (float) Math.sqrt(length);
        for (int i = 0; i < vector.length; i++) {
            vector[i] /= norm;
        }
        return vector;
    }
}
