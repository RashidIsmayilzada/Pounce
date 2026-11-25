package dev.pounce.application;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Optional;

/**
 * Port: reads the searchable text out of a file. Empty means the file has no readable text (a type without text, a
 * broken or password-protected file): that is an answer, kept until the file changes. An {@link IOException} means
 * the file could not be read right now (another program holds it, permissions): nothing is kept, so it is read again.
 */
public interface TextExtractor {
    Optional<String> extract(Path file) throws IOException;
}
