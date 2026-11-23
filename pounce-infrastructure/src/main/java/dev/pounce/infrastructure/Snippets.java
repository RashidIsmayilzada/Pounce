package dev.pounce.infrastructure;

import dev.pounce.domain.Snippet;
import dev.pounce.domain.Snippet.Highlight;
import org.apache.lucene.analysis.Analyzer;
import org.apache.lucene.index.Term;
import org.apache.lucene.search.BooleanClause;
import org.apache.lucene.search.BooleanQuery;
import org.apache.lucene.search.IndexSearcher;
import org.apache.lucene.search.Query;
import org.apache.lucene.search.Sort;
import org.apache.lucene.search.TermQuery;
import org.apache.lucene.search.TopDocs;
import org.apache.lucene.search.uhighlight.Passage;
import org.apache.lucene.search.uhighlight.PassageFormatter;
import org.apache.lucene.search.uhighlight.UnifiedHighlighter;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/** Picks a Document's best-matching Passage and turns it into a Snippet with the Query's words highlighted. */
final class Snippets {

    /** Longest Snippet in characters: about two lines in the Launcher. */
    static final int MAX_LENGTH = 240;

    private static final char OPEN = '\u0002';
    private static final char CLOSE = '\u0003';

    private final String pathField;
    private final String textField;
    private final Analyzer analyzer;
    private final Query documentEntries;

    /** {@code documentEntries} matches the per-Document parent entries, so everything else is a Passage. */
    Snippets(String pathField, String textField, Analyzer analyzer, Query documentEntries) {
        this.pathField = pathField;
        this.textField = textField;
        this.analyzer = analyzer;
        this.documentEntries = documentEntries;
    }

    /** The Snippet for the Document stored under {@code path}, given the Query as a query on Passage text. */
    Snippet of(IndexSearcher searcher, String path, Query textQuery) throws IOException {
        Query inThisDocument = new BooleanQuery.Builder()
                .add(textQuery, BooleanClause.Occur.MUST)
                .add(new TermQuery(new Term(pathField, path)), BooleanClause.Occur.FILTER)
                .build();
        TopDocs best = searcher.search(inThisDocument, 1);
        if (best.scoreDocs.length == 0) {
            return opening(searcher, path);
        }
        var highlighter = UnifiedHighlighter.builder(searcher, analyzer)
                .withFormatter(new MarkingFormatter())
                .withHandleMultiTermQuery(true)
                .build();
        String marked = highlighter.highlight(textField, textQuery, best, 1)[0];
        return marked == null ? Snippet.NONE : parse(marked);
    }

    /** No Passage contains the Query's words (e.g. matched by file name): show the start of the first Passage. */
    private Snippet opening(IndexSearcher searcher, String path) throws IOException {
        Query passagesOfThisDocument = new BooleanQuery.Builder()
                .add(new TermQuery(new Term(pathField, path)), BooleanClause.Occur.FILTER)
                .add(documentEntries, BooleanClause.Occur.MUST_NOT)
                .build();
        TopDocs first = searcher.search(passagesOfThisDocument, 1, Sort.INDEXORDER);
        if (first.scoreDocs.length == 0) {
            return Snippet.NONE;
        }
        String text = searcher.storedFields().document(first.scoreDocs[0].doc).get(textField);
        if (text.length() > MAX_LENGTH) {
            int space = text.lastIndexOf(' ', MAX_LENGTH);
            text = text.substring(0, space > 0 ? space : MAX_LENGTH);
        }
        return new Snippet(text.strip(), List.of());
    }

    /** Reads text with OPEN/CLOSE markers around matches into plain text plus highlight ranges. */
    private static Snippet parse(String marked) {
        var text = new StringBuilder();
        List<Highlight> highlights = new ArrayList<>();
        int start = -1;
        for (char c : marked.toCharArray()) {
            if (c == OPEN) {
                start = text.length();
            } else if (c == CLOSE) {
                highlights.add(new Highlight(start, text.length()));
            } else {
                text.append(c);
            }
        }
        return new Snippet(text.toString(), highlights);
    }

    /** Writes the best passage with OPEN/CLOSE markers around each match. */
    private static final class MarkingFormatter extends PassageFormatter {
        @Override
        public Object format(Passage[] passages, String content) {
            if (passages.length == 0) {
                return null;
            }
            Passage passage = passages[0];
            int[] window = window(passage, content);
            var out = new StringBuilder();
            int position = window[0];
            for (int i = 0; i < passage.getNumMatches(); i++) {
                int matchStart = Math.max(passage.getMatchStarts()[i], position);
                int matchEnd = Math.min(passage.getMatchEnds()[i], window[1]);
                if (matchEnd <= matchStart) {
                    continue;
                }
                out.append(content, position, matchStart).append(OPEN)
                        .append(content, matchStart, matchEnd).append(CLOSE);
                position = matchEnd;
            }
            out.append(content, position, window[1]);
            return out.toString().strip();
        }

        /** At most MAX_LENGTH characters of the passage, starting a little before the first match, cut at spaces. */
        private static int[] window(Passage passage, String content) {
            int from = passage.getStartOffset();
            int to = passage.getEndOffset();
            if (to - from <= MAX_LENGTH) {
                return new int[]{from, to};
            }
            int firstMatch = passage.getNumMatches() > 0 ? passage.getMatchStarts()[0] : from;
            int start = Math.max(from, firstMatch - MAX_LENGTH / 4);
            int end = Math.min(to, start + MAX_LENGTH);
            start = Math.max(from, end - MAX_LENGTH);
            if (start > from) {
                int space = content.indexOf(' ', start);
                start = space >= 0 && space < firstMatch ? space + 1 : start;
            }
            if (end < to) {
                int space = content.lastIndexOf(' ', end);
                end = space > firstMatch ? space : end;
            }
            return new int[]{start, end};
        }
    }
}
