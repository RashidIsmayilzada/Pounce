package dev.pounce.application;

import dev.pounce.domain.Exclusions;
import dev.pounce.domain.Passage;
import dev.pounce.domain.PassageSplitter;
import dev.pounce.domain.TextFormat;
import dev.pounce.domain.WatchedFolder;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.FileVisitResult;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.SimpleFileVisitor;
import java.nio.file.attribute.BasicFileAttributes;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.function.Consumer;

/** Keeps the Index in agreement with what is on disk in the Watched Folders. */
public final class Indexer {

    private static final System.Logger LOG = System.getLogger(Indexer.class.getName());

    private final List<WatchedFolder> folders;
    private final Exclusions exclusions;
    private final TextExtractor extractor;
    private final PassageSplitter splitter;
    private final Embedder embedder;
    private final PassageIndex index;

    public Indexer(List<WatchedFolder> folders, Exclusions exclusions, TextExtractor extractor,
                   PassageSplitter splitter, Embedder embedder, PassageIndex index) {
        this.folders = List.copyOf(folders);
        this.exclusions = exclusions;
        this.extractor = extractor;
        this.splitter = splitter;
        this.embedder = embedder;
        this.index = index;
    }

    /**
     * Walk every Watched Folder and make the whole Index agree with disk. A Watched Folder that does not exist, and
     * a file or folder that cannot be read, is logged and skipped; the rest is still indexed.
     */
    public void reconcile() {
        // Keys are compared as text: on Windows, paths that differ only in letter case are equal.
        Set<String> onDisk = new HashSet<>();
        List<Path> unreadable = new ArrayList<>();
        for (WatchedFolder folder : folders) {
            Optional<Path> root = existingRoot(folder);
            if (root.isEmpty()) {
                continue;
            }
            walk(root.get(), root.get(), file -> {
                try {
                    onDisk.add(file.toRealPath().toString());
                    refreshFile(root.get(), file);
                } catch (IOException | RuntimeException e) {
                    LOG.log(System.Logger.Level.WARNING, "Could not index {0}: {1}", file, e.toString());
                }
            }, unreadable::add);
        }
        for (Path document : index.documents()) {
            // Re-check the disk: the file may have been created (and indexed by a live refresh) after the walk.
            // Documents in a folder that could not be read this time are kept: the folder is still there.
            if (!onDisk.contains(document.toString()) && !isInside(document, unreadable) && !belongs(document)) {
                index.remove(document);
            }
        }
    }

    /**
     * False when the file is gone, when it is now excluded, and when it is now reached under another spelling: on a
     * case-insensitive disk (Windows, macOS) a rename that only changed letter case leaves the old path "existing".
     */
    private boolean belongs(Path document) {
        try {
            Path real = document.toRealPath();
            return real.toString().equals(document.toString())
                    && !exclusions.excludes(watchedFolderOf(real), real, Files.size(real));
        } catch (IOException gone) {
            return false;
        }
    }

    private static Optional<Path> existingRoot(WatchedFolder folder) {
        try {
            Path root = folder.path().toRealPath();
            if (Files.isDirectory(root)) {
                return Optional.of(root);
            }
        } catch (IOException missing) {
            // Logged below.
        }
        LOG.log(System.Logger.Level.INFO, "Watched Folder {0} does not exist; skipped", folder.path());
        return Optional.empty();
    }

    private static boolean isInside(Path document, List<Path> folders) {
        return folders.stream().anyMatch(document::startsWith);
    }

    /**
     * Make the Index agree with whatever is on disk at this path right now: a file, a folder (everything beneath
     * it), or a path that no longer exists (forget it and everything that was beneath it).
     * Calls are serialised, so a live change and the startup reconcile never interleave on the same file.
     */
    public synchronized void refresh(Path path) {
        try {
            Path lastKnown = lastKnownLocation(path);
            if (!Files.exists(path)) {
                forget(lastKnown);
                return;
            }
            Path real = path.toRealPath();
            if (!real.toString().equals(lastKnown.toString())) {
                // The same file under another spelling: on a case-insensitive disk (Windows, macOS) a rename that only
                // changed letter case reports the old name, which still "exists". Forget what was stored under it.
                forget(lastKnown);
            }
            if (Files.isDirectory(real)) {
                Path watchedFolder = watchedFolderOf(real);
                walk(watchedFolder, real, file -> {
                    try {
                        refreshFile(watchedFolder, file);
                    } catch (RuntimeException e) {
                        LOG.log(System.Logger.Level.WARNING, "Could not index {0}: {1}", file, e.toString());
                    }
                }, unreadable -> { });
            } else {
                refreshFile(watchedFolderOf(real), real);
            }
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /**
     * Visits every regular file below {@code start} (a link to a file counts; links to folders are not followed),
     * without entering excluded folders. A file or folder that cannot be read is logged, reported to
     * {@code onUnreadable} and skipped.
     */
    private void walk(Path watchedFolder, Path start, Consumer<Path> onFile, Consumer<Path> onUnreadable) {
        try {
            Files.walkFileTree(start, new SimpleFileVisitor<>() {
                @Override
                public FileVisitResult preVisitDirectory(Path dir, BasicFileAttributes attrs) {
                    return exclusions.excludesFolder(watchedFolder, dir) ? FileVisitResult.SKIP_SUBTREE
                            : FileVisitResult.CONTINUE;
                }

                @Override
                public FileVisitResult visitFile(Path file, BasicFileAttributes attrs) {
                    if (attrs.isRegularFile() || attrs.isSymbolicLink() && Files.isRegularFile(file)) {
                        onFile.accept(file);
                    }
                    return FileVisitResult.CONTINUE;
                }

                @Override
                public FileVisitResult visitFileFailed(Path file, IOException e) {
                    LOG.log(System.Logger.Level.INFO, "Skipped {0}: it cannot be read ({1})", file, e.toString());
                    onUnreadable.accept(file);
                    return FileVisitResult.CONTINUE;
                }

                @Override
                public FileVisitResult postVisitDirectory(Path dir, IOException e) {
                    if (e != null) {
                        visitFileFailed(dir, e);
                    }
                    return FileVisitResult.CONTINUE;
                }
            });
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /** Removes the Document stored under exactly this path, and every Document stored beneath it. */
    private void forget(Path gone) {
        String key = gone.toString();
        String separator = gone.getFileSystem().getSeparator();
        String beneath = key.endsWith(separator) ? key : key + separator;
        for (Path document : index.documents()) {
            String stored = document.toString();
            if (stored.equals(key) || stored.startsWith(beneath)) {
                index.remove(document);
            }
        }
    }

    /** Calls are serialised with {@link #refresh}, so a live change and the reconcile never interleave on a file. */
    private synchronized void refreshFile(Path watchedFolder, Path path) {
        try {
            Path file = path.toRealPath();
            FileStamp stamp = new FileStamp(Files.size(file), Files.getLastModifiedTime(file).toMillis());
            if (exclusions.excludes(watchedFolder, file, stamp.size())) {
                if (index.stamp(file).isPresent()) {
                    index.remove(file);
                }
                return;
            }
            Optional<FileStamp> stored = index.stamp(file);
            // Unchanged since it was indexed. Documents indexed before stamps existed count as unchanged too, so an
            // upgrade does not re-read every file; their next save refreshes them through the watcher.
            if (stored.filter(s -> s.equals(stamp) || s.equals(FileStamp.UNKNOWN)).isPresent()) {
                return;
            }
            String text;
            try {
                text = extractor.extract(file).orElse("");
            } catch (IOException e) {
                // Not "no text": store nothing, so the next reconcile or change reads it again.
                LOG.log(System.Logger.Level.INFO, "Could not read {0} for now ({1}); will try again", file, e.toString());
                return;
            }
            List<Passage> passages = splitter.split(text, formatOf(file));
            List<float[]> vectors = embedder.embedPassages(passages.stream().map(Passage::text).toList());
            List<EmbeddedPassage> embedded = new ArrayList<>();
            for (int i = 0; i < passages.size(); i++) {
                embedded.add(new EmbeddedPassage(passages.get(i), vectors.get(i)));
            }
            index.replace(file, stamp, embedded);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /**
     * The real location of the Watched Folder this real path lies in (the innermost, if they nest). A path in none
     * of them (reached through a link) is judged from the top of its file system, so every folder on it counts.
     */
    private Path watchedFolderOf(Path real) {
        Path best = null;
        for (WatchedFolder folder : folders) {
            try {
                Path root = folder.path().toRealPath();
                if (real.startsWith(root) && (best == null || root.getNameCount() > best.getNameCount())) {
                    best = root;
                }
            } catch (IOException missing) {
                // A Watched Folder that does not exist contains nothing.
            }
        }
        return best != null ? best : real.getRoot();
    }

    /** A deleted file has no real path any more; resolve its folder instead, which matches how it was stored. */
    private static Path lastKnownLocation(Path deleted) throws IOException {
        Path absolute = deleted.toAbsolutePath().normalize();
        Path parent = absolute.getParent();
        return parent != null && Files.exists(parent) ? parent.toRealPath().resolve(absolute.getFileName()) : absolute;
    }

    private static TextFormat formatOf(Path file) {
        return file.getFileName().toString().toLowerCase().endsWith(".md") ? TextFormat.MARKDOWN : TextFormat.PLAIN;
    }
}
