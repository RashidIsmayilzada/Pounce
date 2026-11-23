package dev.pounce.domain;

import java.nio.file.Path;

/** One Document returned for a Query. */
public record Result(Path path) {
}
