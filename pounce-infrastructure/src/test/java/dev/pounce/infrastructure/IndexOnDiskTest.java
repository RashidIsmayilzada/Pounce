package dev.pounce.infrastructure;

import org.apache.lucene.index.DirectoryReader;
import org.apache.lucene.index.SegmentInfos;
import org.apache.lucene.index.Term;
import org.apache.lucene.search.IndexSearcher;
import org.apache.lucene.search.TermQuery;
import org.apache.lucene.store.FSDirectory;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Executor;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * What a new start would find on disk if the Engine were stopped without warning right now (on Windows a child
 * process is ended without running any shutdown code). Read with a second reader while the Engine still has the
 * Index open.
 */
class IndexOnDiskTest {

    static final Duration PATIENCE = Duration.ofSeconds(5);

    @TempDir
    Path folder;
    @TempDir
    Path indexFolder;
    LucenePassageIndex index;

    @BeforeEach
    void start() throws IOException {
        folder = folder.toRealPath();
    }

    @AfterEach
    void stop() throws IOException {
        if (index != null) {
            index.close();
        }
    }

    @Test
    void a_reconciled_index_is_on_disk_before_the_engine_stops() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        write("plan.txt", "Shift ten percent of traffic first.");
        index = LucenePassageIndex.open(indexFolder);

        engineOver(index).indexer().reconcile();

        assertThat(documentsOnDisk()).isEqualTo(2);
    }

    @Test
    void live_changes_reach_the_disk_together_in_one_commit() throws IOException {
        write("notes.md", "The canary rollout went fine.");
        // Scheduled commits wait here until the test says their delay is over, however slow the machine is.
        List<Runnable> delayedCommits = new ArrayList<>();
        index = LucenePassageIndex.open(indexFolder, (Executor) delayedCommits::add);
        var engine = engineOver(index);
        engine.indexer().reconcile();
        runAll(delayedCommits);
        long reconciled = lastCommit();

        for (int i = 0; i < 10; i++) {
            write("note-" + i + ".md", "Live note " + i);
            engine.indexer().refresh(folder.resolve("note-" + i + ".md"));
        }

        assertThat(lastCommit()).as("not one commit per change").isEqualTo(reconciled);
        assertThat(delayedCommits).as("one commit waiting for the whole burst").hasSize(1);
        runAll(delayedCommits);
        assertThat(documentsOnDisk()).isEqualTo(11);
        assertThat(lastCommit()).as("one commit for the whole burst").isEqualTo(reconciled + 1);
    }

    @Test
    void a_live_change_reaches_the_disk_on_its_own_shortly_after() throws IOException {
        index = LucenePassageIndex.open(indexFolder, Duration.ofMillis(100));
        var engine = engineOver(index);
        engine.indexer().reconcile();

        write("notes.md", "The canary rollout went fine.");
        engine.indexer().refresh(folder.resolve("notes.md"));

        assertThat(eventuallyOnDisk(1)).isTrue();
    }

    private static void runAll(List<Runnable> tasks) {
        List<Runnable> due = List.copyOf(tasks);
        tasks.clear();
        due.forEach(Runnable::run);
    }

    private TestEngine engineOver(LucenePassageIndex index) {
        return TestEngine.over(List.of(folder), TestEngine.exclusionsFor(folder), new HashingEmbedder(), index);
    }

    /** Documents in the last commit, counted by their per-Document parent entries. */
    private int documentsOnDisk() throws IOException {
        try (var directory = FSDirectory.open(indexFolder); var reader = DirectoryReader.open(directory)) {
            return new IndexSearcher(reader).count(new TermQuery(new Term("kind", "document")));
        }
    }

    private long lastCommit() throws IOException {
        try (var directory = FSDirectory.open(indexFolder)) {
            return SegmentInfos.getLastCommitGeneration(directory);
        }
    }

    private boolean eventuallyOnDisk(int documents) throws IOException {
        Instant deadline = Instant.now().plus(PATIENCE);
        while (Instant.now().isBefore(deadline)) {
            if (documentsOnDisk() == documents) {
                return true;
            }
            try {
                Thread.sleep(50);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return false;
            }
        }
        return documentsOnDisk() == documents;
    }

    private void write(String name, String text) throws IOException {
        Files.writeString(folder.resolve(name), text);
    }
}
