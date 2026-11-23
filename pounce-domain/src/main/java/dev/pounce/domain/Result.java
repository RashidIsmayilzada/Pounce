package dev.pounce.domain;

import java.nio.file.Path;

/** One Document returned for a Query, shown with its best Passage as a Snippet. */
public record Result(Path path, Snippet snippet) {
}
