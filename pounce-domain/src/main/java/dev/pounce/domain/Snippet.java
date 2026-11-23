package dev.pounce.domain;

import java.util.List;

/** The excerpt of a Result's best Passage shown to the user, with the Query's words marked as highlights. */
public record Snippet(String text, List<Highlight> highlights) {

    public static final Snippet NONE = new Snippet("", List.of());

    public Snippet {
        highlights = List.copyOf(highlights);
    }

    /** A highlighted stretch of the Snippet's text, from {@code start} (inclusive) to {@code end} (exclusive). */
    public record Highlight(int start, int end) {
    }
}
