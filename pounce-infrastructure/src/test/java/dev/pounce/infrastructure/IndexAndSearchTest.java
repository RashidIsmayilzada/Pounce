package dev.pounce.infrastructure;

import dev.pounce.application.Phase;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import dev.pounce.domain.Snippet;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

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
    void reconciling_again_skips_files_that_have_not_changed() throws IOException {
        var embedder = new HashingEmbedder();
        engine = TestEngine.over(folder, embedder);
        write("notes.md", "# Deploys\nThe canary rollout went fine.");
        write("plan.txt", "Shift ten percent of traffic first.");
        engine.indexer().reconcile();
        int afterFirstScan = embedder.passagesEmbedded;

        engine.indexer().reconcile();

        assertThat(afterFirstScan).isPositive();
        assertThat(embedder.passagesEmbedded).isEqualTo(afterFirstScan);
        assertThat(engine.searcher().search(Query.of("canary"), Phase.EARLY)).hasSize(1);
    }

    @Test
    void a_file_changed_while_pounce_was_not_running_is_indexed_again() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        engine.indexer().reconcile();

        write("notes.md", "The falcon landed safely after the long flight.");
        Files.setLastModifiedTime(folder.resolve("notes.md"),
                java.nio.file.attribute.FileTime.fromMillis(System.currentTimeMillis() + 5_000));
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("falcon"), Phase.EARLY)).hasSize(1);
        assertThat(engine.searcher().search(Query.of("canary"), Phase.EARLY)).isEmpty();
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

    @Test
    void a_pdf_is_found_by_the_words_inside_it() throws IOException {
        TestPdfs.write(folder.resolve("cluster.pdf"), "Kubernetes upgrade checklist for the production cluster");
        engine.indexer().reconcile();

        Result top = engine.searcher().search(Query.of("kubernetes upgrade"), Phase.FINAL).getFirst();

        assertThat(top.path()).isEqualTo(folder.resolve("cluster.pdf"));
        assertThat(highlighted(top.snippet())).containsExactly("Kubernetes", "upgrade");
    }

    @Test
    void deleting_a_folder_forgets_every_file_inside_it() throws IOException {
        write("project/notes.md", "canary one");
        write("project/deep/plan.md", "canary two");
        write("keep.md", "canary three");
        engine.indexer().reconcile();

        try (var paths = Files.walk(folder.resolve("project"))) {
            paths.sorted(java.util.Comparator.reverseOrder()).forEach(p -> p.toFile().delete());
        }
        engine.indexer().refresh(folder.resolve("project"));

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("keep.md"));
    }

    @Test
    void a_folder_moved_in_has_every_file_inside_it_indexed() throws IOException {
        engine.indexer().reconcile();

        write("incoming/notes.md", "canary one");
        write("incoming/deep/plan.md", "canary two");
        engine.indexer().refresh(folder.resolve("incoming"));

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactlyInAnyOrder(folder.resolve("incoming/notes.md"), folder.resolve("incoming/deep/plan.md"));
    }

    @Test
    void a_result_shows_the_passage_that_matched_with_the_query_words_highlighted() throws IOException {
        write("runbook.md", "# Deploy\nShip the canary first.\n# Rollback\nIf errors rise, revert the helm release.");
        engine.indexer().reconcile();

        Snippet snippet = engine.searcher().search(Query.of("revert helm"), Phase.EARLY).getFirst().snippet();

        assertThat(snippet.text()).contains("If errors rise, revert the helm release.").doesNotContain("canary");
        assertThat(highlighted(snippet)).containsExactly("revert", "helm");
    }

    @Test
    void a_long_passage_is_cut_down_to_the_part_around_the_match() throws IOException {
        String filler = "lorem ipsum dolor sit amet ".repeat(25); // ~675 characters, no sentence breaks
        write("log.txt", filler + "then the kubernetes node restarted " + filler);
        engine.indexer().reconcile();

        Snippet snippet = engine.searcher().search(Query.of("kubernetes"), Phase.EARLY).getFirst().snippet();

        assertThat(snippet.text().length()).isLessThanOrEqualTo(Snippets.MAX_LENGTH);
        assertThat(snippet.text()).contains("then the kubernetes node restarted");
        assertThat(highlighted(snippet)).containsExactly("kubernetes");
    }

    @Test
    void a_document_found_by_its_name_shows_the_start_of_its_text() throws IOException {
        write("canary-plan.md", "Shift ten percent of traffic first, then watch the error rate.");
        engine.indexer().reconcile();

        Snippet snippet = engine.searcher().search(Query.of("canary"), Phase.EARLY).getFirst().snippet();

        assertThat(snippet.text()).isEqualTo("Shift ten percent of traffic first, then watch the error rate.");
        assertThat(snippet.highlights()).isEmpty();
    }

    @Test
    void a_document_without_readable_text_has_an_empty_snippet() throws IOException {
        Files.write(folder.resolve("scanned-passport.pdf"), new byte[]{(byte) 0xFF, (byte) 0xFE, 0, 1, 2});
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("passport"), Phase.EARLY).getFirst().snippet())
                .isEqualTo(Snippet.NONE);
    }

    private static List<String> highlighted(Snippet snippet) {
        return snippet.highlights().stream().map(h -> snippet.text().substring(h.start(), h.end())).toList();
    }

    private void write(String name, String text) throws IOException {
        Path file = folder.resolve(name);
        Files.createDirectories(file.getParent());
        Files.writeString(file, text);
    }
}
