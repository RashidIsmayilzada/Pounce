package dev.pounce.domain;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/** Where a folder that leads elsewhere (a Windows junction, a OneDrive folder) may take a walk. */
class LinkedFoldersTest {

    private static final Path DOCUMENTS = Path.of("/Users/me/Documents").toAbsolutePath();

    private final LinkedFolders links = new LinkedFolders();

    @Test
    void a_folder_leading_to_a_folder_above_it_is_not_entered() {
        assertThat(links.mayEnter(DOCUMENTS.resolve("projects/app"), DOCUMENTS)).isFalse();
    }

    @Test
    void a_folder_leading_to_the_folder_it_sits_in_is_not_entered() {
        assertThat(links.mayEnter(DOCUMENTS.resolve("projects"), DOCUMENTS.resolve("projects"))).isFalse();
    }

    @Test
    void a_folder_leading_to_itself_is_entered() {
        // A OneDrive folder: a reparse point, but it leads nowhere else.
        assertThat(links.mayEnter(DOCUMENTS, DOCUMENTS.resolve("projects"))).isTrue();
    }

    @Test
    void a_folder_leading_elsewhere_is_entered_once() {
        Path elsewhere = Path.of("/Volumes/archive/2024").toAbsolutePath();

        assertThat(links.mayEnter(DOCUMENTS, elsewhere)).isTrue();
        assertThat(links.mayEnter(DOCUMENTS.resolve("projects"), elsewhere)).isFalse();
    }

    @Test
    void a_folder_leading_next_to_a_folder_above_it_is_entered() {
        assertThat(links.mayEnter(DOCUMENTS.resolve("projects/app"), DOCUMENTS.resolve("notes"))).isTrue();
    }

    @Test
    void on_disk_a_folder_leading_back_up_is_not_entered_and_an_ordinary_one_is(@TempDir Path folder)
            throws IOException {
        folder = folder.toRealPath();
        Path ordinary = Files.createDirectories(folder.resolve("projects/app"));
        Path loop = folder.resolve("projects/app/back-up");
        try {
            Files.createSymbolicLink(loop, folder);
        } catch (IOException | UnsupportedOperationException e) {
            assumeTrue(false, "cannot create a symbolic link here (on Windows it needs Developer Mode or admin): " + e);
        }

        assertThat(links.mayEnter(ordinary)).isTrue();
        assertThat(links.mayEnter(loop)).isFalse();
        assertThat(links.mayEnter(folder.resolve("projects/app/broken"))).isFalse();
    }
}
