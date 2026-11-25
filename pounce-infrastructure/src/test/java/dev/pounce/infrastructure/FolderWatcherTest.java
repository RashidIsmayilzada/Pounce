package dev.pounce.infrastructure;

import dev.pounce.application.Phase;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.function.BooleanSupplier;
import java.util.function.Predicate;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Live watching on the operating system's real file events (FSEvents on macOS, inotify on Linux, ReadDirectoryChangesW
 * on Windows): changes on disk show up in search within a few seconds.
 */
class FolderWatcherTest {

    static final Duration PATIENCE = Duration.ofSeconds(5);

    @TempDir
    Path folder;
    @TempDir
    Path elsewhere;
    TestEngine engine;
    FolderWatcher watcher;

    @BeforeEach
    void start() throws IOException {
        folder = folder.toRealPath();
        elsewhere = elsewhere.toRealPath();
        engine = TestEngine.over(folder);
        engine.indexer().reconcile();
        watcher = FolderWatcher.start(List.of(folder), engine.exclusions(), engine.indexer()::refresh);
    }

    @AfterEach
    void stop() {
        watcher.close();
    }

    @Test
    void a_new_file_becomes_searchable() throws IOException {
        Files.writeString(folder.resolve("notes.md"), "The canary rollout went fine.");

        assertThat(eventually("canary", paths -> paths.contains(folder.resolve("notes.md")))).isTrue();
    }

    @Test
    void an_edited_file_is_found_by_its_new_words_only() throws IOException {
        Files.writeString(folder.resolve("notes.md"), "The canary rollout went fine.");
        assertThat(eventually("canary", paths -> !paths.isEmpty())).isTrue();

        Files.writeString(folder.resolve("notes.md"), "The falcon landed safely.");

        assertThat(eventually("falcon", paths -> paths.contains(folder.resolve("notes.md")))).isTrue();
        assertThat(search("canary")).isEmpty();
    }

    @Test
    void a_deleted_file_disappears() throws IOException {
        Files.writeString(folder.resolve("notes.md"), "The canary rollout went fine.");
        assertThat(eventually("canary", paths -> !paths.isEmpty())).isTrue();

        Files.delete(folder.resolve("notes.md"));

        assertThat(eventually("canary", List::isEmpty)).isTrue();
    }

    @Test
    void a_renamed_file_is_found_under_its_new_name_only() throws IOException {
        Files.writeString(folder.resolve("draft.md"), "The canary rollout went fine.");
        assertThat(eventually("canary", paths -> !paths.isEmpty())).isTrue();

        Files.move(folder.resolve("draft.md"), folder.resolve("final.md"));

        assertThat(eventually("canary", paths -> paths.equals(List.of(folder.resolve("final.md"))))).isTrue();
    }

    @Test
    void a_folder_moved_in_becomes_searchable() throws IOException {
        Files.createDirectories(elsewhere.resolve("project/deep"));
        Files.writeString(elsewhere.resolve("project/deep/plan.md"), "The canary rollout plan.");

        Files.move(elsewhere.resolve("project"), folder.resolve("project"));

        assertThat(eventually("canary", paths -> paths.equals(List.of(folder.resolve("project/deep/plan.md"))))).isTrue();
    }

    @Test
    void a_folder_moved_out_disappears() throws IOException {
        Files.createDirectories(folder.resolve("project"));
        Files.writeString(folder.resolve("project/plan.md"), "The canary rollout plan.");
        assertThat(eventually("canary", paths -> !paths.isEmpty())).isTrue();

        Files.move(folder.resolve("project"), elsewhere.resolve("project"));

        assertThat(eventually("canary", List::isEmpty)).isTrue();
    }

    @Test
    void changes_inside_an_excluded_folder_are_not_reported_but_the_others_are() throws IOException {
        Files.createDirectories(folder.resolve("web/node_modules/react"));
        var reported = restartRecording();

        Files.writeString(folder.resolve("web/node_modules/react/index.js"), "module.exports = 1;");
        Files.createDirectories(folder.resolve("web/node_modules/vue"));
        Files.writeString(folder.resolve("web/notes.md"), "The canary rollout went fine.");

        assertThat(eventually(() -> reported.contains(folder.resolve("web/notes.md")))).isTrue();
        settle();
        assertThat(reported).noneMatch(path -> path.startsWith(folder.resolve("web/node_modules")));
    }

    @Test
    void a_folder_that_only_changed_its_own_details_is_not_walked_again() throws IOException {
        Files.createDirectories(folder.resolve("docs"));
        var reported = restartRecording();

        Files.setLastModifiedTime(folder.resolve("docs"), FileTime.fromMillis(System.currentTimeMillis() - 60_000));
        Files.writeString(folder.resolve("notes.md"), "The canary rollout went fine.");

        assertThat(eventually(() -> reported.contains(folder.resolve("notes.md")))).isTrue();
        settle();
        assertThat(reported).doesNotContain(folder.resolve("docs"));
    }

    /** Restarts the watcher so it records what it reports instead of refreshing the Index. */
    private List<Path> restartRecording() {
        watcher.close();
        List<Path> reported = new CopyOnWriteArrayList<>();
        watcher = FolderWatcher.start(List.of(folder), engine.exclusions(), reported::add);
        return reported;
    }

    /** Waits long enough for every change made so far to have been reported. */
    private static void settle() {
        sleep(FolderWatcher.QUIET.multipliedBy(3));
    }

    private static void sleep(Duration duration) {
        try {
            Thread.sleep(duration);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private static boolean eventually(BooleanSupplier condition) {
        Instant deadline = Instant.now().plus(PATIENCE);
        while (Instant.now().isBefore(deadline) && !condition.getAsBoolean()) {
            sleep(Duration.ofMillis(50));
        }
        return condition.getAsBoolean();
    }

    private List<Path> search(String query) {
        return engine.searcher().search(Query.of(query), Phase.FINAL).stream().map(Result::path).toList();
    }

    /** Polls until the Results for the Query satisfy the condition, or gives up after {@link #PATIENCE}. */
    private boolean eventually(String query, Predicate<List<Path>> condition) {
        Instant deadline = Instant.now().plus(PATIENCE);
        while (Instant.now().isBefore(deadline)) {
            if (condition.test(search(query))) {
                return true;
            }
            try {
                Thread.sleep(50);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return false;
            }
        }
        return condition.test(search(query));
    }
}
