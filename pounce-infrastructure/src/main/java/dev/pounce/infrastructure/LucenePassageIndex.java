package dev.pounce.infrastructure;

import dev.pounce.application.EmbeddedPassage;
import dev.pounce.application.FileStamp;
import dev.pounce.application.PassageIndex;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import org.apache.lucene.analysis.Analyzer;
import org.apache.lucene.analysis.TokenStream;
import org.apache.lucene.analysis.CharArraySet;
import org.apache.lucene.analysis.en.EnglishAnalyzer;
import org.apache.lucene.analysis.tokenattributes.CharTermAttribute;
import org.apache.lucene.codecs.KnnVectorsFormat;
import org.apache.lucene.codecs.lucene104.Lucene104Codec;
import org.apache.lucene.codecs.lucene104.Lucene104HnswScalarQuantizedVectorsFormat;
import org.apache.lucene.document.Document;
import org.apache.lucene.document.Field;
import org.apache.lucene.document.KnnFloatVectorField;
import org.apache.lucene.document.StoredField;
import org.apache.lucene.document.StringField;
import org.apache.lucene.document.TextField;
import org.apache.lucene.index.IndexReader;
import org.apache.lucene.index.IndexableField;
import org.apache.lucene.index.IndexWriter;
import org.apache.lucene.index.IndexWriterConfig;
import org.apache.lucene.index.ReaderUtil;
import org.apache.lucene.index.Term;
import org.apache.lucene.index.VectorSimilarityFunction;
import org.apache.lucene.search.BooleanClause;
import org.apache.lucene.search.BooleanQuery;
import org.apache.lucene.search.BoostQuery;
import org.apache.lucene.search.IndexSearcher;
import org.apache.lucene.search.MatchNoDocsQuery;
import org.apache.lucene.search.PrefixQuery;
import org.apache.lucene.search.ScoreDoc;
import org.apache.lucene.search.SearcherManager;
import org.apache.lucene.search.TermQuery;
import org.apache.lucene.search.TopDocs;
import org.apache.lucene.search.join.BitSetProducer;
import org.apache.lucene.search.join.DiversifyingChildrenFloatKnnVectorQuery;
import org.apache.lucene.search.join.QueryBitSetProducer;
import org.apache.lucene.search.join.ScoreMode;
import org.apache.lucene.search.join.ToParentBlockJoinQuery;
import org.apache.lucene.store.AlreadyClosedException;
import org.apache.lucene.store.ByteBuffersDirectory;
import org.apache.lucene.store.Directory;
import org.apache.lucene.store.FSDirectory;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * The Index as one Lucene index. Each Document is a block: one child per Passage (text + int8-quantized vector),
 * then a parent carrying the path and file name. Every entry in a block carries the path, so a single delete-by-term
 * removes the whole Document.
 * <p>
 * Writes are searchable at once (near-real-time reader) and committed to disk {@link #COMMIT_DELAY} after the first
 * write that is not on disk yet, so whatever happens to the process, at most that much indexing work is lost.
 */
public final class LucenePassageIndex implements PassageIndex, AutoCloseable {

    private static final String PATH = "path";
    private static final String KIND = "kind";
    private static final String NAME = "name";
    private static final String TEXT = "text";
    private static final String VECTOR = "vector";
    private static final String SIZE = "size";
    private static final String MODIFIED = "modified";
    private static final int CANDIDATES = 20;
    private static final System.Logger LOG = System.getLogger(LucenePassageIndex.class.getName());

    /**
     * How long a write may wait before it is committed. A commit fsyncs the new segment files, so committing every
     * write would make each save in an editor cost several fsyncs; waiting gathers a burst of saves, or the writes
     * of a long reconcile, into one commit per interval. A crash loses at most this much work, which the next start
     * redoes (files whose stamps were not committed are read again). Searching never waits for a commit.
     */
    static final Duration COMMIT_DELAY = Duration.ofSeconds(10);
    private static final int RRF_K = 60;

    /**
     * Semantic Matches below this cosine similarity are dropped, so a Query unrelated to everything gets no Results.
     * Calibrated on the RankingTest corpus with bge-small-en-v1.5: correct Top Results scored 0.59–0.82,
     * unrelated or nonsense Queries at most 0.51. Revisit with a larger labelled set.
     */
    static final float MIN_SIMILARITY = 0.55f;

    /** Lucene scores DOT_PRODUCT vectors as (1 + cosine) / 2. */
    private static final float MIN_VECTOR_SCORE = (1 + MIN_SIMILARITY) / 2;

    /**
     * English stemming, minus filler words. Lucene's English stop list leaves out question words and pronouns,
     * which nearly every note contains, so they are added here.
     */
    private static final CharArraySet FILLER_WORDS = fillerWords();

    private final Analyzer analyzer = new EnglishAnalyzer(FILLER_WORDS);
    private final Snippets snippets = new Snippets(PATH, TEXT, analyzer, new TermQuery(new Term(KIND, "document")));
    private final IndexWriter writer;
    private final SearcherManager searchers;
    private final BitSetProducer parents = new QueryBitSetProducer(new TermQuery(new Term(KIND, "document")));
    private final Executor committer;
    private final AtomicBoolean commitScheduled = new AtomicBoolean();

    private LucenePassageIndex(Directory directory, Executor committer) {
        this.committer = committer;
        try {
            var config = new IndexWriterConfig(analyzer);
            config.setCodec(new Lucene104Codec() {
                private final KnnVectorsFormat vectors = new Lucene104HnswScalarQuantizedVectorsFormat();

                @Override
                public KnnVectorsFormat getKnnVectorsFormatForField(String field) {
                    return vectors;
                }
            });
            this.writer = new IndexWriter(directory, config);
            this.searchers = new SearcherManager(writer, null);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private static CharArraySet fillerWords() {
        var words = new CharArraySet(EnglishAnalyzer.ENGLISH_STOP_WORDS_SET, true);
        words.addAll(List.of(
                "how", "what", "why", "when", "where", "which", "who", "whom",
                "do", "does", "did", "doing", "done", "can", "could", "should", "would", "shall", "may", "might",
                "i", "me", "my", "mine", "we", "us", "our", "ours", "you", "your", "yours",
                "he", "him", "his", "she", "her", "hers", "its", "them",
                "am", "been", "being", "has", "have", "had", "about", "from", "so", "than", "too", "very"));
        return CharArraySet.unmodifiableSet(words);
    }

    public static LucenePassageIndex inMemory() {
        return new LucenePassageIndex(new ByteBuffersDirectory(), after(COMMIT_DELAY));
    }

    /** Opens (or creates) the Index stored in this folder. */
    public static LucenePassageIndex open(Path folder) {
        return open(folder, COMMIT_DELAY);
    }

    static LucenePassageIndex open(Path folder, Duration commitDelay) {
        return open(folder, after(commitDelay));
    }

    /** With the scheduled commits run by {@code committer}, which decides when; tests run them by hand. */
    static LucenePassageIndex open(Path folder, Executor committer) {
        try {
            return new LucenePassageIndex(FSDirectory.open(folder), committer);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private static Executor after(Duration delay) {
        return CompletableFuture.delayedExecutor(delay.toMillis(), TimeUnit.MILLISECONDS,
                task -> Thread.ofVirtual().name("index-commit").start(task));
    }

    @Override
    public void replace(Path document, FileStamp stamp, List<EmbeddedPassage> passages) {
        String key = document.toString();
        List<Document> block = new ArrayList<>();
        for (EmbeddedPassage p : passages) {
            var child = new Document();
            child.add(new StringField(PATH, key, Field.Store.NO));
            child.add(new TextField(TEXT, p.passage().text(), Field.Store.YES));
            child.add(new KnnFloatVectorField(VECTOR, p.vector(), VectorSimilarityFunction.DOT_PRODUCT));
            block.add(child);
        }
        var parent = new Document();
        parent.add(new StringField(KIND, "document", Field.Store.NO));
        parent.add(new StringField(PATH, key, Field.Store.YES));
        parent.add(new TextField(NAME, fileNameWords(document), Field.Store.NO));
        parent.add(new StoredField(SIZE, stamp.size()));
        parent.add(new StoredField(MODIFIED, stamp.modifiedMillis()));
        block.add(parent);
        write(() -> writer.updateDocuments(new Term(PATH, key), block));
    }

    @Override
    public void remove(Path document) {
        write(() -> writer.deleteDocuments(new Term(PATH, document.toString())));
    }

    @Override
    public Optional<FileStamp> stamp(Path document) {
        return withSearcher(searcher -> {
            var query = new BooleanQuery.Builder()
                    .add(new TermQuery(new Term(KIND, "document")), BooleanClause.Occur.FILTER)
                    .add(new TermQuery(new Term(PATH, document.toString())), BooleanClause.Occur.FILTER)
                    .build();
            TopDocs hit = searcher.search(query, 1);
            if (hit.scoreDocs.length == 0) {
                return Optional.<FileStamp>empty();
            }
            var stored = searcher.storedFields().document(hit.scoreDocs[0].doc);
            IndexableField size = stored.getField(SIZE);
            IndexableField modified = stored.getField(MODIFIED);
            return size == null || modified == null ? Optional.of(FileStamp.UNKNOWN)
                    : Optional.of(new FileStamp(size.numericValue().longValue(), modified.numericValue().longValue()));
        });
    }

    @Override
    public List<Path> documents() {
        return withSearcher(searcher -> {
            TopDocs all = searcher.search(new TermQuery(new Term(KIND, "document")),
                    Math.max(1, searcher.getIndexReader().maxDoc()));
            var stored = searcher.storedFields();
            List<Path> paths = new ArrayList<>();
            for (ScoreDoc hit : all.scoreDocs) {
                paths.add(Path.of(stored.document(hit.doc).get(PATH)));
            }
            return paths;
        });
    }

    @Override
    public List<Result> keyword(Query query) {
        return withSearcher(searcher ->
                results(searcher, searcher.search(keywordQuery(query), query.limit()), textQuery(query)));
    }

    @Override
    public List<Result> hybrid(Query query, float[] queryVector) {
        return withSearcher(searcher -> {
            TopDocs keyword = searcher.search(keywordQuery(query), CANDIDATES);
            TopDocs semantic = toParents(searcher.getIndexReader(), searcher.search(
                    new DiversifyingChildrenFloatKnnVectorQuery(VECTOR, queryVector, null, CANDIDATES, parents),
                    CANDIDATES));
            return results(searcher, TopDocs.rrf(query.limit(), RRF_K, new TopDocs[]{keyword, semantic}),
                    textQuery(query));
        });
    }

    @Override
    public void commit() {
        try {
            writer.commit();
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    @Override
    public void close() throws IOException {
        searchers.close();
        writer.close();
    }

    private org.apache.lucene.search.Query keywordQuery(Query query) {
        return new BooleanQuery.Builder()
                .add(new ToParentBlockJoinQuery(textQuery(query), parents, ScoreMode.Max), BooleanClause.Occur.SHOULD)
                .add(new BoostQuery(wordsQuery(NAME, query), 2f), BooleanClause.Occur.SHOULD)
                .build();
    }

    /** The Query's words against Passage text; matches Passages, not Documents. */
    private org.apache.lucene.search.Query textQuery(Query query) {
        return wordsQuery(TEXT, query);
    }

    /**
     * The Query's words in the field; the last word also matches as a prefix (the user may be mid-word).
     * Short Queries need one word to match. Longer ones need at least half, so a single shared word cannot
     * make a Keyword Match that outranks a strong Semantic Match.
     */
    private org.apache.lucene.search.Query wordsQuery(String field, Query query) {
        List<String> words = analyze(query.text());
        if (words.isEmpty()) {
            return new MatchNoDocsQuery();
        }
        var enough = new BooleanQuery.Builder();
        for (int i = 0; i < words.size(); i++) {
            enough.add(term(field, words.get(i), i == words.size() - 1), BooleanClause.Occur.SHOULD);
        }
        enough.setMinimumNumberShouldMatch(words.size() < 3 ? 1 : (words.size() + 1) / 2);
        return enough.build();
    }

    private static org.apache.lucene.search.Query term(String field, String word, boolean prefix) {
        Term term = new Term(field, word);
        return prefix ? new PrefixQuery(term) : new TermQuery(term);
    }

    /**
     * The kNN query returns the best child per parent; RRF fuses by doc id, so map each child to its parent.
     * Children below the minimum similarity are dropped: they are the nearest Documents, not near ones.
     */
    private TopDocs toParents(IndexReader reader, TopDocs children) throws IOException {
        var leaves = reader.leaves();
        List<ScoreDoc> mapped = new ArrayList<>();
        for (ScoreDoc child : children.scoreDocs) {
            if (child.score < MIN_VECTOR_SCORE) {
                continue;
            }
            var leaf = leaves.get(ReaderUtil.subIndex(child.doc, leaves));
            int parent = parents.getBitSet(leaf).nextSetBit(child.doc - leaf.docBase);
            mapped.add(new ScoreDoc(leaf.docBase + parent, child.score));
        }
        return new TopDocs(children.totalHits, mapped.toArray(ScoreDoc[]::new));
    }

    private List<Result> results(IndexSearcher searcher, TopDocs top, org.apache.lucene.search.Query textQuery)
            throws IOException {
        var stored = searcher.storedFields();
        List<Result> results = new ArrayList<>();
        for (ScoreDoc hit : top.scoreDocs) {
            String path = stored.document(hit.doc).get(PATH);
            results.add(new Result(Path.of(path), snippets.of(searcher, path, textQuery)));
        }
        return results;
    }

    private List<String> analyze(String text) {
        List<String> words = new ArrayList<>();
        try (TokenStream tokens = analyzer.tokenStream(TEXT, text)) {
            var term = tokens.addAttribute(CharTermAttribute.class);
            tokens.reset();
            while (tokens.incrementToken()) {
                words.add(term.toString());
            }
            tokens.end();
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
        return words;
    }

    private static String fileNameWords(Path document) {
        return document.getFileName().toString().replaceAll("[-_.]", " ");
    }

    private void write(IoAction action) {
        try {
            action.run();
            searchers.maybeRefreshBlocking();
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
        commitSoon();
    }

    /** Schedules one commit {@link #COMMIT_DELAY} from now, unless one is already on its way. */
    private void commitSoon() {
        if (commitScheduled.compareAndSet(false, true)) {
            committer.execute(() -> {
                commitScheduled.set(false);
                try {
                    commit();
                } catch (AlreadyClosedException e) {
                    if (writer.getTragicException() == null) {
                        // Closed meanwhile; closing commits.
                        LOG.log(System.Logger.Level.DEBUG, "Scheduled commit skipped: the Index is closed");
                    } else {
                        LOG.log(System.Logger.Level.WARNING, "Could not save the Index to disk", e);
                    }
                } catch (RuntimeException e) {
                    // For example antivirus or a backup tool holding an index file open. Logged once per failed
                    // commit; the next write schedules another, and the next start redoes what was not saved.
                    LOG.log(System.Logger.Level.WARNING, "Could not save the Index to disk; will try again", e);
                }
            });
        }
    }

    private <T> T withSearcher(IoFunction<IndexSearcher, T> function) {
        try {
            IndexSearcher searcher = searchers.acquire();
            try {
                return function.apply(searcher);
            } finally {
                searchers.release(searcher);
            }
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private interface IoAction {
        void run() throws IOException;
    }

    private interface IoFunction<A, B> {
        B apply(A a) throws IOException;
    }
}
