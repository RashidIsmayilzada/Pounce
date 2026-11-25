package dev.pounce.infrastructure;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.PosixFileAttributeView;
import java.nio.file.attribute.PosixFilePermission;
import java.util.Set;

import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * Takes every permission away from a file or folder until closed, the way a folder the user may not list or a file
 * another program holds open looks to the Engine. Skips the test where that cannot be arranged: without POSIX
 * permissions (Windows), or when running with rights that read anything (root).
 */
final class Unreadable implements AutoCloseable {

    private final Path path;
    private final Set<PosixFilePermission> before;

    private Unreadable(Path path, Set<PosixFilePermission> before) {
        this.path = path;
        this.before = before;
    }

    static Unreadable make(Path path) throws IOException {
        assumeTrue(Files.getFileStore(path).supportsFileAttributeView(PosixFileAttributeView.class),
                "needs POSIX permissions");
        var made = new Unreadable(path, Files.getPosixFilePermissions(path));
        Files.setPosixFilePermissions(path, Set.of());
        if (Files.isReadable(path)) {
            made.close();
            assumeTrue(false, "this user can read anything, so nothing can be made unreadable");
        }
        return made;
    }

    @Override
    public void close() throws IOException {
        Files.setPosixFilePermissions(path, before);
    }
}
