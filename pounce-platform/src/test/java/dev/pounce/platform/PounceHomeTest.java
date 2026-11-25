package dev.pounce.platform;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvFileSource;

import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

class PounceHomeTest {

    /** The shared table: the same cases the Windows Launcher's tests read. */
    @ParameterizedTest(name = "{0}, home {1}, POUNCE_HOME={2}, LOCALAPPDATA={3}, XDG_DATA_HOME={4}")
    @CsvFileSource(resources = "/pounce-home-cases.csv")
    void finds_the_home_for_each_operating_system(String osName, String userHome, String pounceHome,
                                                  String localAppData, String xdgDataHome, String expected) {
        var environment = new HashMap<String, String>();
        put(environment, "POUNCE_HOME", pounceHome);
        put(environment, "LOCALAPPDATA", localAppData);
        put(environment, "XDG_DATA_HOME", xdgDataHome);

        Path home = PounceHome.resolve(osName, environment, Path.of(userHome)).folder();

        assertThat(slashes(home.toString())).isEqualTo(slashes(expected));
    }

    @Test
    void a_relative_pounce_home_is_made_absolute() {
        Path home = PounceHome.resolve("Linux", Map.of("POUNCE_HOME", "relative/pounce"), Path.of("/home/me")).folder();

        assertThat(home).isEqualTo(Path.of("relative/pounce").toAbsolutePath());
    }

    @Test
    void names_the_files_and_folders_inside_the_home() {
        var home = new PounceHome(Path.of("pounce-home"));

        assertThat(home.index()).isEqualTo(Path.of("pounce-home", "index"));
        assertThat(home.bootstrapSecret()).isEqualTo(Path.of("pounce-home", "bootstrap-secret"));
        assertThat(home.signingKey()).isEqualTo(Path.of("pounce-home", "signing-key"));
        assertThat(home.settings()).isEqualTo(Path.of("pounce-home", "pounce.yml"));
        assertThat(home.logs()).isEqualTo(Path.of("pounce-home", "logs"));
    }

    @Test
    void reads_the_real_environment_when_asked_without_arguments() {
        assertThat(PounceHome.resolve()).isEqualTo(PounceHome.resolve(
                System.getProperty("os.name"), System.getenv(), Path.of(System.getProperty("user.home"))));
    }

    private static void put(Map<String, String> environment, String name, String field) {
        if (field != null) {
            environment.put(name, field.equals("<blank>") ? " " : field);
        }
    }

    private static String slashes(String path) {
        return path.replace('\\', '/');
    }
}
