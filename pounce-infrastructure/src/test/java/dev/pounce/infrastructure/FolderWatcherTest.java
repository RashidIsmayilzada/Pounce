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
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.function.Predicate;

import static org.assertj.core.api.Assertions.assertThat;

/** Live watching on real macOS file events: changes on disk show up in search within a few seconds. */
class FolderWatcherTest {

    static final Duration PATIENCE = Duration.ofSeconds(5);

    @TempDir
    Path folder;
    TestEngine engine;
    FolderWatcher watcher;

    @BeforeEach
    void start() throws IOException {
        folder = folder.toRealPath();
        engine = TestEngine.over(folder);
        engine.indexer().reconcile();
        watcher = FolderWatcher.start(List.of(folder), engine.indexer()::refresh);
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
