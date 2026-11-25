package dev.pounce.infrastructure;

import dev.pounce.application.Phase;
import dev.pounce.domain.Exclusions;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import dev.pounce.domain.Snippet;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.DisabledOnOs;
import org.junit.jupiter.api.condition.OS;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.nio.file.attribute.BasicFileAttributes;
import java.time.Duration;
import java.util.List;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assertions.assertTimeoutPreemptively;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

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
    void the_index_is_not_indexed_when_it_lives_inside_a_watched_folder() throws IOException {
        Path pounceHome = folder.resolve("pounce-data");
        write("pounce-data/index/segments.txt", "canary");
        write("notes.md", "The canary rollout went fine.");
        engine = TestEngine.over(List.of(folder), Exclusions.defaults(folder.resolve("no-home"), pounceHome),
                new HashingEmbedder(), LucenePassageIndex.inMemory());

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void index_files_indexed_by_an_older_version_are_forgotten() throws IOException {
        write("pounce-data/index/segments.txt", "canary");
        write("notes.md", "The canary rollout went fine.");
        var index = LucenePassageIndex.inMemory();
        TestEngine.over(List.of(folder), TestEngine.exclusionsFor(folder), new HashingEmbedder(), index)
                .indexer().reconcile();

        engine = TestEngine.over(List.of(folder), Exclusions.defaults(folder.resolve("no-home"), folder.resolve("pounce-data")),
                new HashingEmbedder(), index);
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void a_file_that_grows_past_the_size_limit_is_forgotten() throws IOException {
        write("log.txt", "canary deploy log");
        engine.indexer().reconcile();

        try (var file = new java.io.RandomAccessFile(folder.resolve("log.txt").toFile(), "rw")) {
            file.setLength(50L * 1024 * 1024 + 1);
        }
        engine.indexer().refresh(folder.resolve("log.txt"));

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL)).isEmpty();
    }

    @Test
    void a_watched_folder_inside_a_hidden_folder_is_indexed_but_hidden_folders_inside_it_are_not() throws IOException {
        Path watched = folder.resolve(".local/notes");
        write(".local/notes/plan.md", "canary plan");
        write(".local/notes/.git/config", "canary config");
        engine = TestEngine.over(watched);

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(watched.resolve("plan.md"));
    }

    @Test
    void a_folder_that_cannot_be_read_does_not_stop_the_others() throws IOException {
        write("locked/secret.md", "canary secret");
        write("open/notes.md", "canary notes");
        Path locked = folder.resolve("locked");

        try (var _ = Unreadable.make(locked)) {
            engine.indexer().reconcile();
        }

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("open/notes.md"));
    }

    @Test
    void documents_in_a_folder_that_cannot_be_read_for_now_are_kept() throws IOException {
        write("locked/secret.md", "canary secret");
        engine.indexer().reconcile();

        try (var _ = Unreadable.make(folder.resolve("locked"))) {
            engine.indexer().reconcile();
        }

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("locked/secret.md"));
    }

    /** A USB disk that is unplugged, a mapped drive while off the VPN: its Documents wait for it to come back. */
    @Test
    void a_watched_folder_that_does_not_exist_for_now_is_skipped_and_keeps_its_documents() throws IOException {
        write("drive/plan.md", "The canary plan for the drive.");
        write("laptop/notes.md", "The canary rollout went fine.");
        var embedder = new HashingEmbedder();
        engine = TestEngine.over(List.of(folder.resolve("drive"), folder.resolve("laptop")),
                TestEngine.exclusionsFor(folder), embedder, LucenePassageIndex.inMemory());
        engine.indexer().reconcile();
        int embeddedAtFirst = embedder.passagesEmbedded;

        Files.move(folder.resolve("drive"), folder.resolve("unplugged"));
        engine.indexer().reconcile();

        assertThat(fileNamesFound("canary")).containsExactlyInAnyOrder("plan.md", "notes.md");

        Files.move(folder.resolve("unplugged"), folder.resolve("drive"));
        engine.indexer().reconcile();

        assertThat(fileNamesFound("canary")).containsExactlyInAnyOrder("plan.md", "notes.md");
        assertThat(embedder.passagesEmbedded).as("nothing read again").isEqualTo(embeddedAtFirst);
    }

    @Test
    void a_watched_folder_that_never_existed_is_skipped() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        engine = TestEngine.over(List.of(folder.resolve("unplugged-drive"), folder), TestEngine.exclusionsFor(folder),
                new HashingEmbedder(), LucenePassageIndex.inMemory());

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void a_file_that_could_not_be_read_is_read_again_at_the_next_reconcile() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        try (var _ = Unreadable.make(folder.resolve("notes.md"))) {
            engine.indexer().reconcile();
        }

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    /** On a case-insensitive disk (Windows, macOS) the old name still "exists" after the rename. */
    @Test
    void a_rename_that_only_changes_case_leaves_one_document() throws IOException {
        write("Notes.md", "The canary rollout went fine.");
        engine.indexer().reconcile();

        renameChangingOnlyCase("Notes.md", "notes.md");
        engine.indexer().refresh(folder.resolve("Notes.md"));
        engine.indexer().refresh(folder.resolve("notes.md"));

        assertThat(fileNamesFound("canary")).containsExactly("notes.md");
    }

    @Test
    void a_rename_that_only_changes_case_while_pounce_was_not_running_leaves_one_document() throws IOException {
        write("Notes.md", "The canary rollout went fine.");
        engine.indexer().reconcile();

        renameChangingOnlyCase("Notes.md", "notes.md");
        engine.indexer().reconcile();

        assertThat(fileNamesFound("canary")).containsExactly("notes.md");
    }

    @Test
    void a_note_saved_as_utf16_by_a_windows_tool_is_found_by_its_words() throws IOException {
        byte[] text = "Canary rollout checklist".getBytes(java.nio.charset.StandardCharsets.UTF_16LE);
        byte[] withBom = new byte[text.length + 2];
        withBom[0] = (byte) 0xFF;
        withBom[1] = (byte) 0xFE;
        System.arraycopy(text, 0, withBom, 2, text.length);
        Files.write(folder.resolve("checklist.txt"), withBom);

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("rollout"), Phase.EARLY))
                .extracting(Result::path)
                .containsExactly(folder.resolve("checklist.txt"));
    }

    @Test
    void a_note_saved_in_the_windows_ansi_code_page_is_found_by_its_words() throws IOException {
        byte[] text = "Café rollout checklist".getBytes(java.nio.charset.Charset.forName("windows-1252"));
        Files.write(folder.resolve("checklist.txt"), text);

        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("café"), Phase.EARLY))
                .extracting(Result::path)
                .containsExactly(folder.resolve("checklist.txt"));
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
        try {
            Files.createSymbolicLink(folder.resolve("shortcut.md"), folder.resolve("notes.md"));
        } catch (IOException | UnsupportedOperationException e) {
            assumeTrue(false, "cannot create a symbolic link here (on Windows it needs Developer Mode or admin): " + e);
        }
        engine.indexer().reconcile();

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("notes.md"));
    }

    @Test
    void a_link_to_a_folder_above_is_not_followed() throws IOException {
        write("projects/notes.md", "The canary rollout went fine.");
        try {
            Files.createSymbolicLink(folder.resolve("projects/back-up"), folder);
        } catch (IOException | UnsupportedOperationException e) {
            assumeTrue(false, "cannot create a symbolic link here (on Windows it needs Developer Mode or admin): " + e);
        }

        assertTimeoutPreemptively(Duration.ofSeconds(10), () -> engine.indexer().reconcile());

        assertThat(engine.searcher().search(Query.of("canary"), Phase.FINAL))
                .extracting(Result::path)
                .containsExactly(folder.resolve("projects/notes.md"));
    }

    /**
     * Opening a named pipe waits for a writer, forever: it must never be read, however it is reached.
     * Windows has no named pipes inside folders (its pipes live under {@code \\.\pipe\}), and the {@code mkfifo} that
     * Git for Windows brings along leaves an ordinary file, which is rightly indexed; so there is nothing to test there.
     */
    @Test
    @DisabledOnOs(value = OS.WINDOWS, disabledReason = "a folder on Windows cannot hold a named pipe")
    void a_named_pipe_in_a_watched_folder_is_never_read_or_indexed() throws IOException, InterruptedException {
        write("notes.md", "The canary rollout went fine.");
        Path pipe = folder.resolve("canary-pipe.txt");
        makeNamedPipe(pipe);
        try {
            Files.createSymbolicLink(folder.resolve("canary-link.txt"), pipe);
        } catch (IOException | UnsupportedOperationException e) {
            assumeTrue(false, "cannot create a symbolic link here: " + e);
        }

        assertTimeoutPreemptively(Duration.ofSeconds(10), () -> {
            engine.indexer().reconcile();
            engine.indexer().refresh(pipe);
            engine.indexer().refresh(folder.resolve("canary-link.txt"));
        });

        assertThat(fileNamesFound("canary")).containsExactly("notes.md");
    }

    /** Skips the test, rather than failing it or waiting long, where no working {@code mkfifo} makes a real pipe. */
    private static void makeNamedPipe(Path path) throws InterruptedException {
        boolean made;
        try {
            Process mkfifo = new ProcessBuilder("mkfifo", path.toString()).inheritIO().start();
            made = mkfifo.waitFor(5, TimeUnit.SECONDS) && mkfifo.exitValue() == 0;
            mkfifo.destroyForcibly();
        } catch (IOException noMkfifo) {
            made = false;
        }
        assumeTrue(made, "cannot make a named pipe here (no mkfifo, or it failed)");
        assumeTrue(isSpecialFile(path), "mkfifo made something other than a named pipe here");
    }

    /** Neither a regular file, a folder nor a link: on Linux and macOS, a named pipe. */
    private static boolean isSpecialFile(Path path) {
        try {
            return Files.readAttributes(path, BasicFileAttributes.class, LinkOption.NOFOLLOW_LINKS).isOther();
        } catch (IOException e) {
            return false;
        }
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

    /** What the file watcher asks for when the system dropped its events (a sync burst, an unzip, a checkout). */
    @Test
    void catching_up_on_a_folder_after_lost_events_forgets_files_deleted_meanwhile() throws IOException {
        write("project/notes.md", "canary one");
        write("project/deep/plan.md", "canary two");
        write("keep.md", "canary three");
        engine.indexer().reconcile();

        Files.delete(folder.resolve("project/notes.md"));
        Files.move(folder.resolve("project/deep/plan.md"), folder.resolve("project/deep/renamed.md"));
        write("project/new.md", "canary four");
        engine.indexer().reconcile(folder);

        assertThat(fileNamesFound("canary")).containsExactlyInAnyOrder("renamed.md", "new.md", "keep.md");
    }

    @Test
    void catching_up_on_a_folder_leaves_documents_outside_it_alone() throws IOException {
        write("project/notes.md", "canary one");
        write("other/plan.md", "canary two");
        engine.indexer().reconcile();

        Files.delete(folder.resolve("project/notes.md"));
        Files.delete(folder.resolve("other/plan.md"));
        engine.indexer().reconcile(folder.resolve("project"));

        assertThat(fileNamesFound("canary")).containsExactly("plan.md");
    }

    @Test
    void catching_up_on_a_folder_that_is_gone_forgets_everything_in_it() throws IOException {
        write("project/notes.md", "canary one");
        write("keep.md", "canary two");
        engine.indexer().reconcile();

        Files.delete(folder.resolve("project/notes.md"));
        Files.delete(folder.resolve("project"));
        engine.indexer().reconcile(folder.resolve("project"));

        assertThat(fileNamesFound("canary")).containsExactly("keep.md");
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

    /**
     * Through a third name: on Windows {@code Files.move} onto a name that differs only in case does nothing, since
     * the target "is" the file already (Explorer can rename in one step; Java cannot). The Index sees the same end
     * result either way.
     */
    private void renameChangingOnlyCase(String from, String to) throws IOException {
        Path between = Files.move(folder.resolve(from), folder.resolve(from + ".renaming"));
        Files.move(between, folder.resolve(to));
    }

    /** Compared as text: on Windows two paths that differ only in case are equal. */
    private List<String> fileNamesFound(String query) {
        return engine.searcher().search(Query.of(query), Phase.FINAL).stream()
                .map(result -> result.path().getFileName().toString())
                .toList();
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
