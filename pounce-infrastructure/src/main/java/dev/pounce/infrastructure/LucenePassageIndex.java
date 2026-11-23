package dev.pounce.infrastructure;

import dev.pounce.application.EmbeddedPassage;
import dev.pounce.application.PassageIndex;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import org.apache.lucene.analysis.Analyzer;
import org.apache.lucene.analysis.TokenStream;
import org.apache.lucene.analysis.standard.StandardAnalyzer;
import org.apache.lucene.analysis.tokenattributes.CharTermAttribute;
import org.apache.lucene.codecs.KnnVectorsFormat;
import org.apache.lucene.codecs.lucene104.Lucene104Codec;
import org.apache.lucene.codecs.lucene104.Lucene104HnswScalarQuantizedVectorsFormat;
import org.apache.lucene.document.Document;
import org.apache.lucene.document.Field;
import org.apache.lucene.document.KnnFloatVectorField;
import org.apache.lucene.document.StringField;
import org.apache.lucene.document.TextField;
import org.apache.lucene.index.IndexReader;
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
import org.apache.lucene.store.ByteBuffersDirectory;
import org.apache.lucene.store.Directory;
import org.apache.lucene.store.FSDirectory;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * The Index as one Lucene index. Each Document is a block: one child per Passage (text + int8-quantized vector),
 * then a parent carrying the path and file name. Every entry in a block carries the path, so a single delete-by-term
 * removes the whole Document.
 */
public final class LucenePassageIndex implements PassageIndex, AutoCloseable {

    private static final String PATH = "path";
    private static final String KIND = "kind";
    private static final String NAME = "name";
    private static final String TEXT = "text";
    private static final String VECTOR = "vector";
    private static final int CANDIDATES = 20;
    private static final int RRF_K = 60;

    private final Analyzer analyzer = new StandardAnalyzer();
    private final Snippets snippets = new Snippets(PATH, TEXT, analyzer, new TermQuery(new Term(KIND, "document")));
    private final IndexWriter writer;
    private final SearcherManager searchers;
    private final BitSetProducer parents = new QueryBitSetProducer(new TermQuery(new Term(KIND, "document")));

    private LucenePassageIndex(Directory directory) {
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

    public static LucenePassageIndex inMemory() {
        return new LucenePassageIndex(new ByteBuffersDirectory());
    }

    /** Opens (or creates) the Index stored in this folder. */
    public static LucenePassageIndex open(Path folder) {
        try {
            return new LucenePassageIndex(FSDirectory.open(folder));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    @Override
    public void replace(Path document, List<EmbeddedPassage> passages) {
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
        block.add(parent);
        write(() -> writer.updateDocuments(new Term(PATH, key), block));
    }

    @Override
    public void remove(Path document) {
        write(() -> writer.deleteDocuments(new Term(PATH, document.toString())));
    }

    @Override
    public Set<Path> documents() {
        return withSearcher(searcher -> {
            TopDocs all = searcher.search(new TermQuery(new Term(KIND, "document")),
                    Math.max(1, searcher.getIndexReader().maxDoc()));
            var stored = searcher.storedFields();
            Set<Path> paths = new HashSet<>();
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

    /** Any of the Query's words in the field; the last word also matches as a prefix (the user may be mid-word). */
    private org.apache.lucene.search.Query wordsQuery(String field, Query query) {
        List<String> words = analyze(query.text());
        if (words.isEmpty()) {
            return new MatchNoDocsQuery();
        }
        var any = new BooleanQuery.Builder();
        for (int i = 0; i < words.size(); i++) {
            any.add(term(field, words.get(i), i == words.size() - 1), BooleanClause.Occur.SHOULD);
        }
        return any.build();
    }

    private static org.apache.lucene.search.Query term(String field, String word, boolean prefix) {
        Term term = new Term(field, word);
        return prefix ? new PrefixQuery(term) : new TermQuery(term);
    }

    /** The kNN query returns the best child per parent; RRF fuses by doc id, so map each child to its parent. */
    private TopDocs toParents(IndexReader reader, TopDocs children) throws IOException {
        var leaves = reader.leaves();
        var mapped = new ScoreDoc[children.scoreDocs.length];
        for (int i = 0; i < mapped.length; i++) {
            ScoreDoc child = children.scoreDocs[i];
            var leaf = leaves.get(ReaderUtil.subIndex(child.doc, leaves));
            int parent = parents.getBitSet(leaf).nextSetBit(child.doc - leaf.docBase);
            mapped[i] = new ScoreDoc(leaf.docBase + parent, child.score);
        }
        return new TopDocs(children.totalHits, mapped);
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
