package dev.pounce.infrastructure;

import dev.pounce.domain.Exclusions;
import dev.pounce.domain.LinkedFolders;
import io.methvin.watcher.DirectoryChangeEvent;
import io.methvin.watcher.DirectoryChangeEvent.EventType;
import io.methvin.watcher.DirectoryChangeListener;
import io.methvin.watcher.DirectoryWatcher;
import io.methvin.watcher.visitor.FileTreeVisitor;

import java.io.IOException;
import java.nio.file.FileVisitResult;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.nio.file.SimpleFileVisitor;
import java.nio.file.attribute.BasicFileAttributes;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/**
 * Watches folders through the operating system's file events (via directory-watcher: FSEvents on macOS, one recursive
 * registration per Watched Folder on Windows, one inotify watch per folder on Linux) and reports each changed path
 * once it has been quiet for {@link #QUIET}, so an editor's burst of writes becomes one change. Changes are reported
 * one at a time, on one thread, in the order they settle. Starts listening from now: changes made while not
 * watching are the startup reconcile's job.
 * <p>
 * Excluded folders are never entered, so on Linux they cost no inotify watch, and changes inside them are not
 * reported. Neither is a folder whose own details changed (Windows reports a folder as modified whenever something
 * inside it changes, and that change has an event of its own); a folder that appears or disappears is reported.
 * <p>
 * If watching cannot start (on Linux typically because the inotify watch limit is reached), that is logged as an
 * error and the Engine runs without live updates: searching still works and the next start catches up. Failing the
 * whole Engine instead would leave the user with no search at all.
 */
public final class FolderWatcher implements AutoCloseable {

    static final Duration QUIET = Duration.ofMillis(300);
    private static final Duration START_PATIENCE = Duration.ofSeconds(10);
    private static final System.Logger LOG = System.getLogger(FolderWatcher.class.getName());

    private final List<Path> roots;
    private final Exclusions exclusions;
    private final Consumer<Path> onChange;
    private final Consumer<Path> onEventsLost;
    private final ScheduledExecutorService settle;
    private final Map<Path, ScheduledFuture<?>> pendingChanges = new ConcurrentHashMap<>();
    private final Map<Path, ScheduledFuture<?>> pendingCatchUps = new ConcurrentHashMap<>();
    /** Released once the watcher has subscribed to file events, or has failed to; changes before that are missed. */
    private final CountDownLatch listening = new CountDownLatch(1);
    private volatile boolean closed;
    /** Null when watching could not start. */
    private volatile DirectoryWatcher watcher;

    private FolderWatcher(List<Path> roots, Exclusions exclusions, Consumer<Path> onChange,
                          Consumer<Path> onEventsLost) {
        this.roots = roots;
        this.exclusions = exclusions;
        this.onChange = onChange;
        this.onEventsLost = onEventsLost;
        this.settle = Executors.newSingleThreadScheduledExecutor(Thread.ofPlatform().name("watch-settle").daemon().factory());
    }

    /**
     * Starts watching the folders that exist; {@code onChange} receives every changed path, file or folder, outside
     * the excluded folders. Returns once file events are flowing, so a change made right after is never lost.
     * <p>
     * When the system drops events (its buffer overflowed: on Windows one 16 KB buffer serves a whole Watched Folder,
     * so a OneDrive sync, an unzip or a {@code git checkout} can overflow it), {@code onEventsLost} receives that
     * Watched Folder instead, to bring it up to date, deletions included. It runs on the same thread as the changes,
     * never on the thread that receives events, and repeated losses while it waits or runs lead to one more call.
     */
    public static FolderWatcher start(List<Path> folders, Exclusions exclusions, Consumer<Path> onChange,
                                      Consumer<Path> onEventsLost) {
        var folderWatcher = new FolderWatcher(realFolders(folders), exclusions, onChange, onEventsLost);
        folderWatcher.listen();
        return folderWatcher;
    }

    /** Real paths, so events and exclusions see the same paths the Index stores. */
    private static List<Path> realFolders(List<Path> folders) {
        List<Path> real = new ArrayList<>();
        for (Path folder : folders) {
            try {
                if (Files.isDirectory(folder)) {
                    real.add(folder.toRealPath());
                }
            } catch (IOException gone) {
                // A folder that does not exist is not watched; the reconcile reports it.
            }
        }
        return real;
    }

    private void listen() {
        try {
            watcher = DirectoryWatcher.builder()
                    .paths(roots)
                    .fileHashing(false)
                    .fileTreeVisitor(new SkipExcludedFolders())
                    .listener(new DirectoryChangeListener() {
                        @Override
                        public void onEvent(DirectoryChangeEvent event) {
                            FolderWatcher.this.onEvent(event);
                        }

                        /** The event loop asks this on every turn, and only after registering the folders. */
                        @Override
                        public boolean isWatching() {
                            listening.countDown();
                            return !closed;
                        }
                    })
                    .build();
            // Registering the folders happens inside watchAsync; if it fails, the future comes back already failed.
            CompletableFuture<Void> events = watcher.watchAsync(Executors.newSingleThreadExecutor(
                    Thread.ofPlatform().name("watch-events").daemon().factory()));
            events.whenComplete((done, failure) -> {
                if (!closed) {
                    cannotWatch(failure != null ? failure : new IOException("the Watched Folders are gone"));
                }
                listening.countDown();
            });
            if (!listening.await(START_PATIENCE.toMillis(), TimeUnit.MILLISECONDS)) {
                LOG.log(System.Logger.Level.WARNING, "File watcher took more than {0} s to start listening",
                        START_PATIENCE.toSeconds());
            }
        } catch (IOException | RuntimeException e) {
            cannotWatch(e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Interrupted while starting the file watcher", e);
        }
    }

    private void cannotWatch(Throwable cause) {
        String hint = cause.toString().contains("inotify")
                ? " Raise the Linux limit, for example: sudo sysctl fs.inotify.max_user_watches=524288" : "";
        LOG.log(System.Logger.Level.ERROR, "Live updates are off: cannot watch " + roots + " (" + cause + "). "
                + "Searching still works, and changes are picked up at the next start." + hint, cause);
    }

    private void onEvent(DirectoryChangeEvent event) {
        Path root = event.rootPath();
        if (event.eventType() == EventType.OVERFLOW) {
            // Events were lost, deletions among them: bring the whole Watched Folder up to date.
            LOG.log(System.Logger.Level.INFO, "Missed some file changes in {0}; catching up", root);
            schedule(root, pendingCatchUps, onEventsLost);
            return;
        }
        Path changed = event.path();
        if (changed == null || isExcluded(root, changed, event.isDirectory())) {
            return;
        }
        boolean isFolder = event.isDirectory() || Files.isDirectory(changed, LinkOption.NOFOLLOW_LINKS);
        if (event.eventType() == EventType.MODIFY && isFolder) {
            return;
        }
        schedule(changed, pendingChanges, onChange);
    }

    private boolean isExcluded(Path root, Path changed, boolean isDirectory) {
        Path parent = changed.getParent();
        return parent != null && parent.startsWith(root) && exclusions.excludesFolder(root, parent)
                || isDirectory && exclusions.excludesFolder(root, changed);
    }

    /**
     * Reports the path to {@code action} once it has been quiet for {@link #QUIET}, on the settle thread. A path
     * already waiting is reported once; one being reported right now is reported again after.
     */
    private void schedule(Path changed, Map<Path, ScheduledFuture<?>> pending, Consumer<Path> action) {
        pending.compute(changed, (path, previous) -> {
            if (previous != null) {
                previous.cancel(false);
            }
            return settle.schedule(() -> report(path, pending, action), QUIET.toMillis(), TimeUnit.MILLISECONDS);
        });
    }

    private void report(Path path, Map<Path, ScheduledFuture<?>> pending, Consumer<Path> action) {
        pending.remove(path);
        try {
            action.accept(path);
        } catch (RuntimeException e) {
            LOG.log(System.Logger.Level.WARNING, "Could not refresh " + path, e);
        }
    }

    @Override
    public void close() {
        closed = true;
        try {
            if (watcher != null) {
                watcher.close();
            }
        } catch (IOException e) {
            LOG.log(System.Logger.Level.WARNING, "Could not stop watching", e);
        } finally {
            settle.shutdownNow();
        }
    }

    /**
     * Walks a folder the way the library's default visitor does (to register folders and to list a folder that
     * appeared), but never enters an excluded folder: on Linux each folder entered costs an inotify watch, and a
     * {@code node_modules} or {@code .git} can hold thousands. Nor does it loop through a Windows junction that leads
     * back up ({@link LinkedFolders}). Folders that cannot be read are skipped, as before.
     */
    private final class SkipExcludedFolders implements FileTreeVisitor {

        @Override
        public void recursiveVisitFiles(Path start, Callback onDirectory, Callback onFile) throws IOException {
            Path root = rootOf(start);
            Files.walkFileTree(start, new SimpleFileVisitor<>() {
                private final LinkedFolders links = new LinkedFolders();

                @Override
                public FileVisitResult preVisitDirectory(Path dir, BasicFileAttributes attrs) throws IOException {
                    if (exclusions.excludesFolder(root, dir)) {
                        return FileVisitResult.SKIP_SUBTREE;
                    }
                    // "Other": a Windows junction, mount point or OneDrive folder, which may lead elsewhere.
                    if (attrs.isOther() && !links.mayEnter(dir)) {
                        return FileVisitResult.SKIP_SUBTREE;
                    }
                    onDirectory.call(dir);
                    return FileVisitResult.CONTINUE;
                }

                @Override
                public FileVisitResult visitFile(Path file, BasicFileAttributes attrs) throws IOException {
                    onFile.call(file);
                    return FileVisitResult.CONTINUE;
                }

                @Override
                public FileVisitResult visitFileFailed(Path file, IOException e) {
                    return FileVisitResult.CONTINUE;
                }

                @Override
                public FileVisitResult postVisitDirectory(Path dir, IOException e) {
                    return FileVisitResult.CONTINUE;
                }
            });
        }

        /** The innermost watched folder holding this path; outside all of them, every folder on the path counts. */
        private Path rootOf(Path path) {
            Path best = path.getRoot();
            for (Path root : roots) {
                if (path.startsWith(root) && (best == null || root.getNameCount() >= best.getNameCount())) {
                    best = root;
                }
            }
            return best;
        }
    }
}
