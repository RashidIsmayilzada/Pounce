package dev.pounce.domain;

import java.nio.file.Path;

/** A folder the user has chosen for Pounce to keep searchable, including everything beneath it. */
public record WatchedFolder(Path path) {
}
