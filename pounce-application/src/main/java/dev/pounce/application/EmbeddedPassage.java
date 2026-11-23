package dev.pounce.application;

import dev.pounce.domain.Passage;

/** A Passage together with its meaning vector, ready to be stored in the Index. */
public record EmbeddedPassage(Passage passage, float[] vector) {
}
