package dev.pounce.platform;

import java.nio.file.Path;
import java.util.Map;

/**
 * The folder where Pounce keeps its data: the Index, the bootstrap secret, the signing key, the user's settings and
 * the logs. The Engine, the CLI and the Launchers all find it the same way, so they always agree on it:
 * <ol>
 *     <li>{@code POUNCE_HOME}, when it is set and not blank;</li>
 *     <li>macOS: {@code ~/Library/Application Support/Pounce};</li>
 *     <li>Windows: {@code %LOCALAPPDATA%\Pounce}, or {@code ~\AppData\Local\Pounce} when that variable is unset or
 *     not an absolute path;</li>
 *     <li>anything else: {@code $XDG_DATA_HOME/pounce}, or {@code ~/.local/share/pounce} when that variable is unset
 *     or not an absolute path.</li>
 * </ol>
 *
 * @param folder the home folder itself
 */
public record PounceHome(Path folder) {

    /** The home for the user running this program. */
    public static PounceHome resolve() {
        return resolve(System.getProperty("os.name"), System.getenv(), Path.of(System.getProperty("user.home")));
    }

    /**
     * The home for this system, environment and user home; touches no files.
     *
     * @param osName      the {@code os.name} system property
     * @param environment the environment variables
     * @param userHome    the {@code user.home} system property
     */
    public static PounceHome resolve(String osName, Map<String, String> environment, Path userHome) {
        var system = OperatingSystem.named(osName);
        String chosen = environment.get("POUNCE_HOME");
        if (chosen != null && !chosen.isBlank()) {
            return new PounceHome(system.isAbsolute(chosen) ? Path.of(chosen) : Path.of(chosen).toAbsolutePath());
        }
        return new PounceHome(switch (system) {
            case MACOS -> userHome.resolve("Library").resolve("Application Support").resolve("Pounce");
            case WINDOWS -> orElse(system.absolutePath(environment.get("LOCALAPPDATA")),
                    userHome.resolve("AppData").resolve("Local")).resolve("Pounce");
            case OTHER -> orElse(system.absolutePath(environment.get("XDG_DATA_HOME")),
                    userHome.resolve(".local").resolve("share")).resolve("pounce");
        });
    }

    /** The Index. */
    public Path index() {
        return folder.resolve("index");
    }

    /** The secret clients exchange for a JWT. */
    public Path bootstrapSecret() {
        return folder.resolve("bootstrap-secret");
    }

    /** The key the Engine signs JWTs with. */
    public Path signingKey() {
        return folder.resolve("signing-key");
    }

    /** The user's settings, such as the Watched Folders; Launchers pass it to the Engine. */
    public Path settings() {
        return folder.resolve("pounce.yml");
    }

    /** Where Launchers write the Engine's log. */
    public Path logs() {
        return folder.resolve("logs");
    }

    private static Path orElse(Path path, Path otherwise) {
        return path != null ? path : otherwise;
    }
}
