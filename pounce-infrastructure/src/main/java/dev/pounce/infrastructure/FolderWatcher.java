package dev.pounce.infrastructure;

import io.methvin.watcher.DirectoryChangeEvent;
import io.methvin.watcher.DirectoryChangeListener;
import io.methvin.watcher.DirectoryWatcher;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/**
 * Watches folders through native macOS file events (FSEvents, via directory-watcher) and reports each changed path
 * once it has been quiet for {@link #QUIET}, so an editor's burst of writes becomes one change. Changes are reported
 * one at a time, on one thread, in the order they settle. Starts listening from now: changes made while not
 * watching are the startup reconcile's job.
 */
public final class FolderWatcher implements AutoCloseable {

    static final Duration QUIET = Duration.ofMillis(300);
    private static final System.Logger LOG = System.getLogger(FolderWatcher.class.getName());

    private final DirectoryWatcher watcher;
    private final ScheduledExecutorService settle;
    private final Map<Path, ScheduledFuture<?>> pending = new ConcurrentHashMap<>();
    private final Consumer<Path> onChange;
    /** Released once the watcher has subscribed to file events; changes before that would be missed. */
    private final CountDownLatch listening = new CountDownLatch(1);

    private FolderWatcher(List<Path> folders, Consumer<Path> onChange) throws IOException {
        this.onChange = onChange;
        this.settle = Executors.newSingleThreadScheduledExecutor(Thread.ofPlatform().name("watch-settle").daemon().factory());
        this.watcher = DirectoryWatcher.builder()
                .paths(folders)
                .fileHashing(false)
                .listener(new DirectoryChangeListener() {
                    @Override
                    public void onEvent(DirectoryChangeEvent event) {
                        FolderWatcher.this.onEvent(event);
                    }

                    /** The event loop asks this on every turn, and only after registering the folders. */
                    @Override
                    public boolean isWatching() {
                        listening.countDown();
                        return !watcherClosed();
                    }
                })
                .build();
    }

    private volatile boolean closed;

    private boolean watcherClosed() {
        return closed;
    }

    /** Starts watching the folders that exist; {@code onChange} receives every changed path, file or folder. */
    public static FolderWatcher start(List<Path> folders, Consumer<Path> onChange) {
        try {
            var watcher = new FolderWatcher(folders.stream().filter(Files::isDirectory).toList(), onChange);
            watcher.watcher.watchAsync(Executors.newSingleThreadExecutor(
                    Thread.ofPlatform().name("watch-events").daemon().factory()));
            // Return only once file events are flowing, so a change made right after start is never lost.
            if (!watcher.listening.await(10, TimeUnit.SECONDS)) {
                LOG.log(System.Logger.Level.WARNING, "File watcher took more than 10 s to start listening");
            }
            return watcher;
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Interrupted while starting the file watcher", e);
        }
    }

    private void onEvent(DirectoryChangeEvent event) {
        // OVERFLOW means events were lost: refresh the whole watched folder.
        Path changed = event.eventType() == DirectoryChangeEvent.EventType.OVERFLOW ? event.rootPath() : event.path();
        if (changed == null) {
            return;
        }
        pending.compute(changed, (path, previous) -> {
            if (previous != null) {
                previous.cancel(false);
            }
            return settle.schedule(() -> report(path), QUIET.toMillis(), TimeUnit.MILLISECONDS);
        });
    }

    private void report(Path path) {
        pending.remove(path);
        try {
            onChange.accept(path);
        } catch (RuntimeException e) {
            LOG.log(System.Logger.Level.WARNING, "Could not refresh " + path, e);
        }
    }

    @Override
    public void close() {
        closed = true;
        try {
            watcher.close();
        } catch (IOException e) {
            LOG.log(System.Logger.Level.WARNING, "Could not stop watching", e);
        } finally {
            settle.shutdownNow();
        }
    }
}
