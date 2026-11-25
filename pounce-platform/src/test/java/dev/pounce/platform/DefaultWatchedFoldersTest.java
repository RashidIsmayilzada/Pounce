package dev.pounce.platform;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.DisabledOnOs;
import org.junit.jupiter.api.condition.OS;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

class DefaultWatchedFoldersTest {

    @TempDir
    Path home;

    @Test
    void on_macos_watches_documents_desktop_downloads_and_projects() throws IOException {
        folders("Documents", "Desktop", "Downloads", "projects", "Music");

        assertThat(DefaultWatchedFolders.find("Mac OS X", Map.of(), home)).containsExactly(
                home.resolve("Documents"), home.resolve("Desktop"), home.resolve("Downloads"), home.resolve("projects"));
    }

    @Test
    void leaves_out_folders_that_do_not_exist() throws IOException {
        folders("Documents");

        assertThat(DefaultWatchedFolders.find("Mac OS X", Map.of(), home)).containsExactly(home.resolve("Documents"));
    }

    @Test
    void finds_nothing_in_an_empty_home() {
        assertThat(DefaultWatchedFolders.find("Linux", Map.of(), home)).isEmpty();
    }

    @Test
    void on_windows_also_watches_documents_and_desktop_in_onedrive() throws IOException {
        folders("Documents", "Desktop", "Downloads", "projects",
                "OneDrive/Documents", "OneDrive/Desktop", "OneDrive/Pictures",
                "OneDrive - Contoso/Documents");
        var environment = Map.of(
                "OneDrive", home.resolve("OneDrive").toString(),
                "OneDriveConsumer", home.resolve("OneDrive").toString(),
                "OneDriveCommercial", home.resolve("OneDrive - Contoso").toString());

        assertThat(DefaultWatchedFolders.find("Windows 11", environment, home)).containsExactly(
                home.resolve("Documents"), home.resolve("Desktop"), home.resolve("Downloads"),
                home.resolve("OneDrive/Documents"), home.resolve("OneDrive/Desktop"),
                home.resolve("OneDrive - Contoso/Documents"));
    }

    @Test
    void on_windows_a_documents_folder_moved_into_onedrive_is_still_watched() throws IOException {
        folders("Downloads", "OneDrive/Documents", "OneDrive/Desktop");

        assertThat(DefaultWatchedFolders.find("Windows 11", Map.of("OneDrive", home.resolve("OneDrive").toString()),
                home)).containsExactly(
                home.resolve("Downloads"), home.resolve("OneDrive/Documents"), home.resolve("OneDrive/Desktop"));
    }

    @Test
    void on_linux_without_user_dirs_watches_documents_desktop_and_downloads() throws IOException {
        folders("Documents", "Desktop", "Downloads", "projects");

        assertThat(DefaultWatchedFolders.find("Linux", Map.of(), home)).containsExactly(
                home.resolve("Documents"), home.resolve("Desktop"), home.resolve("Downloads"));
    }

    @Test
    void on_linux_follows_the_xdg_user_dirs() throws IOException {
        Path elsewhere = folders("Dokumente", "Schreibtisch", "elsewhere/Downloads").resolve("elsewhere");
        folders("Documents", "Desktop", "Downloads", ".config");
        Files.writeString(home.resolve(".config/user-dirs.dirs"), """
                # This file is written by xdg-user-dirs-update
                XDG_DESKTOP_DIR="$HOME/Schreibtisch"
                XDG_DOCUMENTS_DIR="$HOME/Dokumente"
                XDG_DOWNLOAD_DIR="%s"
                XDG_MUSIC_DIR="$HOME/Musik"
                """.formatted(elsewhere.resolve("Downloads")));

        assertThat(DefaultWatchedFolders.find("Linux", Map.of(), home)).containsExactly(
                home.resolve("Dokumente"), home.resolve("Schreibtisch"), elsewhere.resolve("Downloads"));
    }

    @Test
    void on_linux_reads_user_dirs_from_xdg_config_home() throws IOException {
        Path config = folders("Dokumente", "Desktop", "my-config").resolve("my-config");
        Files.writeString(config.resolve("user-dirs.dirs"), "XDG_DOCUMENTS_DIR=\"$HOME/Dokumente\"\n");

        assertThat(DefaultWatchedFolders.find("Linux", Map.of("XDG_CONFIG_HOME", config.toString()), home))
                .containsExactly(home.resolve("Dokumente"), home.resolve("Desktop"));
    }

    @Test
    void on_linux_skips_a_user_dir_that_is_the_home_itself() throws IOException {
        folders("Documents", "Desktop", ".config");
        Files.writeString(home.resolve(".config/user-dirs.dirs"), "XDG_DESKTOP_DIR=\"$HOME/\"\n");

        assertThat(DefaultWatchedFolders.find("Linux", Map.of(), home)).containsExactly(home.resolve("Documents"));
    }

    @Test
    void lists_a_folder_reached_twice_once() throws IOException {
        folders("Documents", ".config");
        Files.writeString(home.resolve(".config/user-dirs.dirs"), """
                XDG_DOCUMENTS_DIR="$HOME/Documents"
                XDG_DESKTOP_DIR="$HOME/Documents"
                """);

        assertThat(DefaultWatchedFolders.find("Linux", Map.of(), home)).containsExactly(home.resolve("Documents"));
    }

    @Test
    @DisabledOnOs(value = OS.WINDOWS, disabledReason = "creating symbolic links needs extra rights on Windows")
    void lists_a_folder_reached_through_a_link_once() throws IOException {
        folders("Documents");
        Files.createSymbolicLink(home.resolve("Desktop"), home.resolve("Documents"));

        assertThat(DefaultWatchedFolders.find("Mac OS X", Map.of(), home)).containsExactly(home.resolve("Documents"));
    }

    @Test
    void reads_the_real_environment_when_asked_without_arguments() {
        assertThat(DefaultWatchedFolders.find()).isEqualTo(DefaultWatchedFolders.find(
                System.getProperty("os.name"), System.getenv(), Path.of(System.getProperty("user.home"))));
    }

    private Path folders(String... names) throws IOException {
        for (String name : names) {
            Files.createDirectories(home.resolve(name));
        }
        return home;
    }
}
