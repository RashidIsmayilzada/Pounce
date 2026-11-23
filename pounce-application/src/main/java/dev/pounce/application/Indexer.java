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
            if (!onDisk.contains(document)) {
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

    /** Make the Index agree with whatever is on disk at this path right now. */
    public void refresh(Path path) {
        try {
            if (!Files.exists(path)) {
                index.remove(lastKnownLocation(path));
                return;
            }
            Path file = path.toRealPath();
            if (exclusions.excludes(file, Files.size(file))) {
                return;
            }
            String text = extractor.extract(file).orElse("");
            List<Passage> passages = splitter.split(text, formatOf(file));
            List<float[]> vectors = embedder.embedPassages(passages.stream().map(Passage::text).toList());
            List<EmbeddedPassage> embedded = new ArrayList<>();
            for (int i = 0; i < passages.size(); i++) {
                embedded.add(new EmbeddedPassage(passages.get(i), vectors.get(i)));
            }
            index.replace(file, embedded);
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
