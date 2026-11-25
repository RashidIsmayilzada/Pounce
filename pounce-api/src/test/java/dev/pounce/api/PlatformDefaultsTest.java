package dev.pounce.api;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.core.env.StandardEnvironment;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Where the Engine keeps its data and what it watches when nobody says otherwise, and that every way of saying
 * otherwise wins: the real Spring Boot startup with a made-up environment and user home, so nothing real is read.
 */
class PlatformDefaultsTest {

    @TempDir
    Path userHome;

    @Test
    void keeps_its_data_in_the_usual_place_for_this_system() {
        String os = System.getProperty("os.name").toLowerCase(Locale.ROOT);
        Path expected = os.startsWith("mac") ? userHome.resolve("Library/Application Support/Pounce")
                : os.startsWith("windows") ? userHome.resolve("AppData/Local/Pounce")
                : userHome.resolve(".local/share/pounce");

        PounceProperties properties = start(Map.of(), Map.of());

        assertThat(properties.home()).isEqualTo(expected);
        assertThat(properties.modelDir()).isEqualTo(expected.resolve("models/bge-small-en-v1.5"));
    }

    @Test
    void pounce_home_in_the_environment_moves_it() {
        Path elsewhere = userHome.resolve("elsewhere");

        PounceProperties properties = start(Map.of("POUNCE_HOME", elsewhere.toString()), Map.of());

        assertThat(properties.home()).isEqualTo(elsewhere);
    }

    @Test
    void an_explicit_property_beats_the_environment() {
        Path chosen = userHome.resolve("chosen");

        PounceProperties properties = start(Map.of("POUNCE_HOME", userHome.resolve("elsewhere").toString()), Map.of(),
                "--pounce.home=" + chosen);

        assertThat(properties.home()).isEqualTo(chosen);
    }

    @Test
    void a_system_property_beats_the_default() {
        Path chosen = userHome.resolve("chosen");

        PounceProperties properties = start(Map.of(), Map.of("pounce.home", chosen.toString()));

        assertThat(properties.home()).isEqualTo(chosen);
    }

    @Test
    void watches_the_usual_folders_that_exist() throws IOException {
        Files.createDirectories(userHome.resolve("Documents"));

        PounceProperties properties = start(Map.of(), Map.of());

        assertThat(properties.watchedFolders()).containsExactly(userHome.resolve("Documents"));
    }

    @Test
    void watches_nothing_when_none_of_the_usual_folders_exist() {
        assertThat(start(Map.of(), Map.of()).watchedFolders()).isEmpty();
    }

    @Test
    void watched_folders_in_pounce_yml_replace_the_usual_ones() throws IOException {
        Files.createDirectories(userHome.resolve("Documents"));
        Path notes = Files.createDirectories(userHome.resolve("notes"));
        Path settings = Files.writeString(userHome.resolve("pounce.yml"),
                "pounce:\n  watched-folders: '" + notes + "'\n");

        // As the Launchers pass it, with "/" in place of "\" so the location is also a valid URL on Windows.
        PounceProperties properties = start(Map.of(), Map.of("spring.config.additional-location",
                "optional:file:" + settings.toString().replace('\\', '/')));

        assertThat(properties.watchedFolders()).containsExactly(notes);
    }

    /**
     * The Windows Launcher passes the home and the settings file in the environment, because java.exe reads its command
     * line in the ANSI code page and a user name outside it would arrive as "?".
     */
    @Test
    void the_windows_launcher_passes_the_home_and_the_settings_file_in_the_environment() throws IOException {
        Files.createDirectories(userHome.resolve("Documents"));
        Path home = Files.createDirectories(userHome.resolve("Рашид Əli 山田"));
        Path notes = Files.createDirectories(userHome.resolve("notes"));
        Path settings = Files.writeString(home.resolve("pounce.yml"), "pounce:\n  watched-folders: '" + notes + "'\n");

        PounceProperties properties = start(Map.of(
                "POUNCE_HOME", home.toString(),
                "SPRING_CONFIG_ADDITIONALLOCATION", "optional:file:" + settings.toString().replace('\\', '/')), Map.of());

        assertThat(properties.home()).isEqualTo(home);
        assertThat(properties.watchedFolders()).containsExactly(notes);
    }

    /** Only the settings, no Engine: a plain class, so component scanning in other tests never picks it up. */
    @EnableConfigurationProperties(PounceProperties.class)
    static class SettingsOnly {
    }

    /** Starts Spring Boot with these environment variables and extra system properties, and this test's user home. */
    private PounceProperties start(Map<String, Object> variables, Map<String, Object> systemProperties,
                                   String... args) {
        var properties = new HashMap<String, Object>();
        System.getProperties().forEach((name, value) -> properties.put((String) name, value));
        properties.put("user.home", userHome.toString());
        properties.putAll(systemProperties);
        var application = new SpringApplication(SettingsOnly.class);
        application.setWebApplicationType(WebApplicationType.NONE);
        application.setEnvironment(new StandardEnvironment() {
            @Override
            public Map<String, Object> getSystemEnvironment() {
                return variables;
            }

            @Override
            public Map<String, Object> getSystemProperties() {
                return properties;
            }
        });
        try (var context = application.run(args)) {
            return context.getBean(PounceProperties.class);
        }
    }
}
