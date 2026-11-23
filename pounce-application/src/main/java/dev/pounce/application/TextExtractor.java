package dev.pounce.application;

import java.nio.file.Path;
import java.util.Optional;

/** Port: reads the searchable text out of a file, or nothing if the file type has no readable text. */
public interface TextExtractor {
    Optional<String> extract(Path file);
}
