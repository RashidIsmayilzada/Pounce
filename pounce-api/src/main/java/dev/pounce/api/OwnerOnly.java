package dev.pounce.api;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.file.FileStore;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.AclEntry;
import java.nio.file.attribute.AclEntryFlag;
import java.nio.file.attribute.AclEntryPermission;
import java.nio.file.attribute.AclEntryType;
import java.nio.file.attribute.AclFileAttributeView;
import java.nio.file.attribute.PosixFileAttributeView;
import java.nio.file.attribute.PosixFilePermissions;
import java.util.EnumSet;
import java.util.List;

/**
 * Makes a file or folder readable by its owner alone, in whatever way its disk supports: POSIX permissions
 * ({@code rw-------}, {@code rwx------} for a folder) on macOS and Linux, an access control list with one entry for
 * the owner on Windows. A folder's entry is inherited by everything later created in it.
 * <p>
 * Never fails: when the disk supports neither, or changing the permissions goes wrong, it logs a warning and leaves
 * the permissions as they are. The Engine must start anyway, and its home is in the user's own profile folder,
 * which other users normally cannot open.
 */
final class OwnerOnly {

    private static final Logger log = LoggerFactory.getLogger(OwnerOnly.class);

    private OwnerOnly() {
    }

    static void restrict(Path path) {
        try {
            FileStore store = Files.getFileStore(path);
            boolean folder = Files.isDirectory(path);
            if (store.supportsFileAttributeView(PosixFileAttributeView.class)) {
                Files.setPosixFilePermissions(path, PosixFilePermissions.fromString(folder ? "rwx------" : "rw-------"));
            } else if (store.supportsFileAttributeView(AclFileAttributeView.class)) {
                restrictAcl(path, folder);
            } else {
                log.warn("Can't make {} private: its disk ({}) has neither POSIX permissions nor access lists",
                        path, store.type());
            }
        } catch (Exception e) {
            log.warn("Can't make {} private; leaving its permissions as they are", path, e);
        }
    }

    /**
     * Replaces the whole list, inherited entries included, with a single entry that gives the owner every right.
     * Set explicitly after creation rather than at creation, so entries inherited from the parent folder are
     * dropped for certain.
     */
    private static void restrictAcl(Path path, boolean folder) throws IOException {
        var view = Files.getFileAttributeView(path, AclFileAttributeView.class);
        var entry = AclEntry.newBuilder()
                .setType(AclEntryType.ALLOW)
                .setPrincipal(view.getOwner())
                .setPermissions(EnumSet.allOf(AclEntryPermission.class));
        if (folder) {
            entry.setFlags(AclEntryFlag.FILE_INHERIT, AclEntryFlag.DIRECTORY_INHERIT);
        }
        view.setAcl(List.of(entry.build()));
    }
}
