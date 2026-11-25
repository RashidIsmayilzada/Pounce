package dev.pounce.infrastructure;

import dev.pounce.application.Embedder;
import dev.pounce.application.Indexer;
import dev.pounce.application.Searcher;
import dev.pounce.domain.Exclusions;
import dev.pounce.domain.PassageSplitter;
import dev.pounce.domain.WatchedFolder;

import java.nio.file.Path;
import java.util.List;

/** Wires the real Indexer and Searcher over Watched Folders, a real Lucene index and a fake embedder. */
record TestEngine(Indexer indexer, Searcher searcher, LucenePassageIndex index, Exclusions exclusions) {

    static TestEngine over(Path folder) {
        return over(folder, new HashingEmbedder());
    }

    static TestEngine over(Path folder, Embedder embedder) {
        return over(List.of(folder), exclusionsFor(folder), embedder, LucenePassageIndex.inMemory());
    }

    static TestEngine over(List<Path> folders, Exclusions exclusions, Embedder embedder, LucenePassageIndex index) {
        var indexer = new Indexer(folders.stream().map(WatchedFolder::new).toList(), exclusions,
                new FileTypeTextExtractor(), new PassageSplitter(), embedder, index);
        return new TestEngine(indexer, new Searcher(embedder, index), index, exclusions);
    }

    /** The default rules, with a user home and Pounce home that exist nowhere near the folder. */
    static Exclusions exclusionsFor(Path folder) {
        return Exclusions.defaults(folder.resolve("no-home"), folder.resolve("no-pounce-home"));
    }
}
