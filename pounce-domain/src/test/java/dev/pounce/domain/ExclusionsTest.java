package dev.pounce.domain;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.nio.file.Path;

import static org.assertj.core.api.Assertions.assertThat;

class ExclusionsTest {

    private static final long SMALL = 4_000;
    /** An absolute home on every OS: {@code /Users/me} here, {@code C:\Users\me} on Windows. */
    private static final Path HOME = Path.of("/Users/me").toAbsolutePath();
    private static final Path POUNCE_HOME = HOME.resolve("pounce-home");
    private final Exclusions defaults = Exclusions.defaults(HOME, POUNCE_HOME);

    @Test
    void keeps_an_ordinary_document() {
        assertThat(excludes(HOME, "Documents/plans/canary-rollout.md")).isFalse();
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "projects/app/venv/lib/python3.13/site-packages/requests/adapters.py",
            "projects/app/.venv/lib/x.py",
            "projects/app/lib/site-packages/x.py",
            "projects/app/__pycache__/x.pyc",
            "projects/web/node_modules/react/index.js",
            "projects/pounce/.git/config",
            "projects/pounce/target/classes/A.class",
            "projects/app/build/out.txt",
            "Documents/.hidden/notes.md",
            "Library/Caches/foo.txt",
            "AppData/Local/Microsoft/Edge/notes.txt",
            "snap/firefox/common/notes.txt"
    })
    void skips_dependency_build_hidden_and_system_folders(String path) {
        assertThat(excludes(HOME, path)).isTrue();
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "$Recycle.Bin/S-1-5-21/report.docx",
            "$RECYCLE.BIN/report.docx",
            "System Volume Information/tracking.log",
            "system volume information/tracking.log",
            "$WinREAgent/Scratch/update.log",
            "$SysReset/Logs/setupact.log",
            "$Windows.~BT/Sources/setup.log",
            "$WINDOWS.~WS/Sources/setup.log",
            "Pictures/desktop.ini",
            "Pictures/Desktop.ini",
            "Pictures/Thumbs.db",
            "Pictures/thumbs.db",
            "Documents/~$report.docx",
            "Documents/~$budget.xlsx"
    })
    void skips_windows_system_folders_thumbnail_caches_and_office_lock_files(String path) {
        assertThat(excludes(HOME, path)).isTrue();
    }

    @ParameterizedTest
    @ValueSource(strings = {"Documents/report.docx", "Documents/~notes.md", "Documents/$5 budget.txt", "Documents/thumbs.md",
            "$archive/notes.md", "Documents/$Recycle/notes.md", "projects/$templates/plan.md"})
    void keeps_files_that_only_look_like_windows_system_files(String path) {
        assertThat(excludes(HOME, path)).isFalse();
    }

    @Test
    void skips_files_larger_than_50_megabytes() {
        Path video = HOME.resolve("Downloads/talk.txt");
        assertThat(defaults.excludes(HOME, video, 50L * 1024 * 1024)).isFalse();
        assertThat(defaults.excludes(HOME, video, 50L * 1024 * 1024 + 1)).isTrue();
    }

    @Test
    void the_pounce_home_is_always_skipped_so_the_index_never_indexes_itself() {
        Path home = HOME.resolve("Documents/pounce-data");
        var exclusions = Exclusions.defaults(HOME, home);

        assertThat(exclusions.excludesFolder(HOME.resolve("Documents"), home)).isTrue();
        assertThat(exclusions.excludes(HOME.resolve("Documents"), home.resolve("index/_0.cfs"), SMALL)).isTrue();
        assertThat(exclusions.excludesFolder(home, home.resolve("index"))).isTrue();
    }

    @Test
    void a_watched_folder_inside_a_hidden_or_build_folder_is_still_indexed() {
        Path watched = HOME.resolve(".local/share/notes");
        Path tempDir = HOME.resolve("AppData/Local/Temp/junit123");

        assertThat(excludes(watched, ".local/share/notes/plan.md")).isFalse();
        assertThat(excludes(tempDir, "AppData/Local/Temp/junit123/plan.md")).isFalse();
        assertThat(excludes(HOME.resolve("projects/app/build"), "projects/app/build/report.md")).isFalse();
    }

    @Test
    void below_the_watched_folder_every_rule_applies_again() {
        Path watched = HOME.resolve("AppData/Local/Temp/junit123");

        assertThat(excludes(watched, "AppData/Local/Temp/junit123/.git/config")).isTrue();
        assertThat(excludes(watched, "AppData/Local/Temp/junit123/web/node_modules/react/index.js")).isTrue();
    }

    @Test
    void a_watched_folder_chosen_inside_appdata_or_snap_is_indexed_but_the_home_folder_skips_them() {
        assertThat(excludes(HOME.resolve("AppData/Roaming/Notes"), "AppData/Roaming/Notes/notes.md")).isFalse();
        assertThat(excludes(HOME, "AppData/Roaming/Notes/notes.md")).isTrue();
        assertThat(excludes(HOME.resolve("snap/notes/common"), "snap/notes/common/notes.md")).isFalse();
        assertThat(excludes(HOME, "snap/notes/common/notes.md")).isTrue();
    }

    /** iCloud Drive lives in ~/Library/Mobile Documents: reading its files would download every one of them. */
    @Test
    void library_is_skipped_even_for_a_watched_folder_chosen_inside_it() {
        Path iCloudDrive = HOME.resolve("Library/Mobile Documents");

        assertThat(excludes(iCloudDrive, "Library/Mobile Documents/notes.md")).isTrue();
        assertThat(defaults.excludesFolder(iCloudDrive, iCloudDrive)).isTrue();
        assertThat(excludes(HOME, "Library/Mobile Documents/notes.md")).isTrue();
    }

    @Test
    void whole_folders_can_be_skipped_before_walking_into_them() {
        assertThat(defaults.excludesFolder(HOME, HOME.resolve("projects/web/node_modules"))).isTrue();
        assertThat(defaults.excludesFolder(HOME, HOME.resolve("projects/web/node_modules/react"))).isTrue();
        assertThat(defaults.excludesFolder(HOME, HOME.resolve("AppData"))).isTrue();
        assertThat(defaults.excludesFolder(HOME, HOME.resolve("$Recycle.Bin"))).isTrue();
        assertThat(defaults.excludesFolder(HOME, HOME.resolve("projects/web"))).isFalse();
        assertThat(defaults.excludesFolder(HOME, HOME)).isFalse();
    }

    @Test
    void a_file_outside_the_watched_folder_is_judged_by_its_whole_path() {
        assertThat(excludes(HOME.resolve("Documents"), "projects/.git/config")).isTrue();
        assertThat(excludes(HOME.resolve("Documents"), "projects/notes.md")).isFalse();
    }

    @Test
    void cloud_only_files_and_windows_system_files_are_recognised_by_their_attributes() {
        int archive = 0x20;
        assertThat(Exclusions.isCloudOnlyOrSystem(archive)).isFalse();
        assertThat(Exclusions.isCloudOnlyOrSystem(0)).isFalse();
        assertThat(Exclusions.isCloudOnlyOrSystem(archive | 0x400000)).as("recall on data access").isTrue();
        assertThat(Exclusions.isCloudOnlyOrSystem(archive | 0x40000)).as("recall on open").isTrue();
        assertThat(Exclusions.isCloudOnlyOrSystem(archive | 0x1000)).as("offline").isTrue();
        assertThat(Exclusions.isCloudOnlyOrSystem(archive | 0x4)).as("system").isTrue();
        assertThat(Exclusions.isCloudOnlyOrSystem(archive | 0x2 | 0x1)).as("hidden, read-only").isFalse();
        assertThat(Exclusions.isCloudOnlyOrSystem(0x80000 | 0x100000)).as("pinned / unpinned, but local").isFalse();
    }

    private boolean excludes(Path watchedFolder, String below) {
        return defaults.excludes(watchedFolder, HOME.resolve(below), SMALL);
    }
}
