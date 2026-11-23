package dev.pounce.infrastructure;

import dev.pounce.application.Indexer;
import dev.pounce.application.Searcher;
import dev.pounce.domain.Exclusions;
import dev.pounce.domain.PassageSplitter;
import dev.pounce.domain.WatchedFolder;

import java.nio.file.Path;
import java.util.List;

/** Wires the real Indexer and Searcher over one Watched Folder, an in-memory Lucene index and a fake embedder. */
record TestEngine(Indexer indexer, Searcher searcher, LucenePassageIndex index) {

    static TestEngine over(Path folder) {
        var index = LucenePassageIndex.inMemory();
        var embedder = new HashingEmbedder();
        var indexer = new Indexer(List.of(new WatchedFolder(folder)), Exclusions.defaults(folder.resolve("no-home")),
                new PlainTextExtractor(), new PassageSplitter(), embedder, index);
        return new TestEngine(indexer, new Searcher(embedder, index), index);
    }
}
