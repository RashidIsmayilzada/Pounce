package dev.pounce.domain;

/** A contiguous stretch of a Document's text; the smallest unit a Query is matched against. */
public record Passage(String text) {
}
