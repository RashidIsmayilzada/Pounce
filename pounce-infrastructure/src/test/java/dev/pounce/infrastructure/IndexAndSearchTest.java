package dev.pounce.infrastructure;

import dev.pounce.application.Phase;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.assertj.core.api.Assertions.assertThat;

/** The Indexer + Searcher seam: real files, real Lucene (in memory), fake embedder. */
class IndexAndSearchTest {

    @TempDir
    Path folder;
    TestEngine engine;

    @BeforeEach
    void start() throws IOException {
        folder = folder.toRealPath();
        engine = TestEngine.over(folder);
    }

    @Test
    void finds_a_document_by_a_word_it_contains() throws IOException {
        write("notes.md", "# Deploys\nThe canary rollout went fine.");
        write("groceries.txt", "milk, eggs, bread");

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.EARLY))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void a_deleted_file_disappears_once_its_path_is_refreshed() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        engine.indexer().reconcile();

        Files.delete(folder.resolve("notes.md"));
        engine.indexer().refresh(folder.resolve("notes.md"));

        assertThat(engine.searcher().search(Query.of("canary"), Phase.EARLY)).isEmpty();
    }

    @Test
    void final_results_show_each_document_once_however_many_passages_match() throws IOException {
        write("runbook.md", "# One\ncanary rollout step one\n# Two\ncanary rollout step two\n# Three\ncanary rollout step three");
        write("notes.txt", "canary rollout notes");
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary rollout"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactlyInAnyOrder(folder.resolve("runbook.md"), folder.resolve("notes.txt"));
    }

    @Test
    void files_matching_an_exclusion_are_never_found() throws IOException {
        write("app/venv/lib/site-packages/requests/adapters.py", "canary = True");
        write("app/main.py", "canary = False");
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("app/main.py"));
    }

    @Test
    void a_document_without_readable_text_is_still_found_by_its_name() throws IOException {
        Files.write(folder.resolve("scanned-passport.pdf"), new byte[]{(byte) 0xFF, (byte) 0xFE, 0, 1, 2});
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("passport"), Phase.EARLY))
                .extracting(Result::path)
                .containsExactly(folder.resolve("scanned-passport.pdf"));
    }

    @Test
    void a_file_reached_through_a_link_is_one_document() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        Files.createSymbolicLink(folder.resolve("shortcut.md"), folder.resolve("notes.md"));
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void typing_part_of_the_last_word_already_matches() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary roll"), Phase.EARLY))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void reconcile_forgets_files_deleted_while_pounce_was_not_watching() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        engine.indexer().reconcile();

        Files.delete(folder.resolve("notes.md"));
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.EARLY)).isEmpty();
    }

    private void write(String name, String text) throws IOException {
        Path file = folder.resolve(name);
        Files.createDirectories(file.getParent());
        Files.writeString(file, text);
    }
}
