package dev.pounce.domain;

import java.nio.file.Path;
import java.util.Set;

/** Rules that keep paths inside a Watched Folder out of the Index. */
public final class Exclusions {

    private static final long MAX_SIZE_BYTES = 50L * 1024 * 1024;
    private static final Set<String> SKIPPED_FOLDERS = Set.of(
            "node_modules", "venv", "site-packages", "__pycache__", "target", "build");

    private final Path library;

    private Exclusions(Path library) {
        this.library = library;
    }

    public static Exclusions defaults(Path home) {
        return new Exclusions(home.resolve("Library"));
    }

    public boolean excludes(Path path, long sizeBytes) {
        if (sizeBytes > MAX_SIZE_BYTES || path.startsWith(library)) {
            return true;
        }
        for (Path part : path.getParent() == null ? Path.of("") : path.getParent()) {
            String name = part.toString();
            if (name.startsWith(".") || SKIPPED_FOLDERS.contains(name)) {
                return true;
            }
        }
        return false;
    }
}
