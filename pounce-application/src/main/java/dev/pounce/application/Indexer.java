package dev.pounce.application;

import dev.pounce.domain.Exclusions;
import dev.pounce.domain.Passage;
import dev.pounce.domain.PassageSplitter;
import dev.pounce.domain.TextFormat;
import dev.pounce.domain.WatchedFolder;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Stream;

/** Keeps the Index in agreement with what is on disk in the Watched Folders. */
public final class Indexer {

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

    /** Walk every Watched Folder and make the whole Index agree with disk. */
    public void reconcile() {
        Set<Path> onDisk = new HashSet<>();
        for (WatchedFolder folder : folders) {
            try (Stream<Path> files = Files.walk(folder.path())) {
                files.filter(Files::isRegularFile).forEach(file -> {
                    refresh(file);
                    onDisk.add(realPath(file));
                });
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            }
        }
        for (Path document : index.documents()) {
            // Re-check the disk: the file may have been created (and indexed by a live refresh) after the walk.
            if (!onDisk.contains(document) && !Files.exists(document)) {
                index.remove(document);
            }
        }
    }

    private static Path realPath(Path file) {
        try {
            return file.toRealPath();
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /**
     * Make the Index agree with whatever is on disk at this path right now: a file, a folder (everything beneath
     * it), or a path that no longer exists (forget it and everything that was beneath it).
     * Calls are serialised, so a live change and the startup reconcile never interleave on the same file.
     */
    public synchronized void refresh(Path path) {
        try {
            if (!Files.exists(path)) {
                forget(lastKnownLocation(path));
            } else if (Files.isDirectory(path)) {
                try (Stream<Path> files = Files.walk(path)) {
                    files.filter(Files::isRegularFile).forEach(this::refreshFile);
                }
            } else {
                refreshFile(path);
            }
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private void forget(Path gone) {
        index.remove(gone);
        for (Path document : index.documents()) {
            if (document.startsWith(gone)) {
                index.remove(document);
            }
        }
    }

    private void refreshFile(Path path) {
        try {
            Path file = path.toRealPath();
            FileStamp stamp = new FileStamp(Files.size(file), Files.getLastModifiedTime(file).toMillis());
            if (exclusions.excludes(file, stamp.size())) {
                return;
            }
            Optional<FileStamp> stored = index.stamp(file);
            // Unchanged since it was indexed. Documents indexed before stamps existed count as unchanged too, so an
            // upgrade does not re-read every file; their next save refreshes them through the watcher.
            if (stored.filter(s -> s.equals(stamp) || s.equals(FileStamp.UNKNOWN)).isPresent()) {
                return;
            }
            String text = extractor.extract(file).orElse("");
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
