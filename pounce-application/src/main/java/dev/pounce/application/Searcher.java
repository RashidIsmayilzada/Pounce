package dev.pounce.application;

import dev.pounce.domain.Query;
import dev.pounce.domain.Result;

import java.util.List;

/** Answers Queries with Early Results (Keyword Matches) or Final Results (Keyword and Semantic Matches merged). */
public final class Searcher {

    private final Embedder embedder;
    private final PassageIndex index;

    public Searcher(Embedder embedder, PassageIndex index) {
        this.embedder = embedder;
        this.index = index;
    }

    public List<Result> search(Query query, Phase phase) {
        if (phase == Phase.EARLY) {
            return index.keyword(query);
        }
        return index.hybrid(query, embedder.embedQuery(query.text()));
    }
}
