package dev.pounce.application;

import dev.pounce.domain.Query;
import dev.pounce.domain.Result;

import java.nio.file.Path;
import java.util.List;
import java.util.Optional;
import java.util.Set;

/**
 * Port: the on-disk Index. Every lookup returns at most one Result per Document, best first.
 * Writes are visible to the next lookup.
 */
public interface PassageIndex {
    /** Store the Document at this path with these Passages, replacing whatever was stored for it before. */
    void replace(Path document, FileStamp stamp, List<EmbeddedPassage> passages);

    /**
     * The stamp the Document at this path was stored with. Empty if it is not in the Index; {@link FileStamp#UNKNOWN}
     * if it was indexed before stamps existed.
     */
    Optional<FileStamp> stamp(Path document);

    void remove(Path document);

    /** Paths of every Document currently in the Index. */
    Set<Path> documents();

    /** Keyword Matches on Passage text and file name; the last word of the Query also matches as a prefix. */
    List<Result> keyword(Query query);

    /** Keyword Matches and Semantic Matches merged into one ranking. */
    List<Result> hybrid(Query query, float[] queryVector);
}
