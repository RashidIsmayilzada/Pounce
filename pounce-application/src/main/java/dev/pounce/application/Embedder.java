package dev.pounce.application;

import java.util.List;

/** Port: turns text into unit-length meaning vectors. Query and Passage embeddings may differ (model prefixes). */
public interface Embedder {
    float[] embedQuery(String query);

    List<float[]> embedPassages(List<String> passages);
}
