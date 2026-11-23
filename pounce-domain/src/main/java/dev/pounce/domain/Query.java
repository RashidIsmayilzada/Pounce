package dev.pounce.domain;

/** The text the user has typed, and how many Results they want back. */
public record Query(String text, int limit) {

    public static final int DEFAULT_LIMIT = 8;

    public static Query of(String text) {
        return new Query(text, DEFAULT_LIMIT);
    }
}
