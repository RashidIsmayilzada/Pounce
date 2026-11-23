package dev.pounce.domain;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.nio.file.Path;

import static org.assertj.core.api.Assertions.assertThat;

class ExclusionsTest {

    private static final long SMALL = 4_000;
    private final Exclusions defaults = Exclusions.defaults(Path.of("/Users/me"));

    @Test
    void keeps_an_ordinary_document() {
        assertThat(defaults.excludes(Path.of("/Users/me/Documents/plans/canary-rollout.md"), SMALL)).isFalse();
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "/Users/me/projects/app/venv/lib/python3.13/site-packages/requests/adapters.py",
            "/Users/me/projects/app/.venv/lib/x.py",
            "/Users/me/projects/app/lib/site-packages/x.py",
            "/Users/me/projects/app/__pycache__/x.pyc",
            "/Users/me/projects/web/node_modules/react/index.js",
            "/Users/me/projects/pounce/.git/config",
            "/Users/me/projects/pounce/target/classes/A.class",
            "/Users/me/projects/app/build/out.txt",
            "/Users/me/Documents/.hidden/notes.md",
            "/Users/me/Library/Caches/foo.txt"
    })
    void skips_dependency_build_hidden_and_system_folders(String path) {
        assertThat(defaults.excludes(Path.of(path), SMALL)).isTrue();
    }

    @Test
    void skips_files_larger_than_50_megabytes() {
        Path video = Path.of("/Users/me/Downloads/talk.txt");
        assertThat(defaults.excludes(video, 50L * 1024 * 1024)).isFalse();
        assertThat(defaults.excludes(video, 50L * 1024 * 1024 + 1)).isTrue();
    }
}
