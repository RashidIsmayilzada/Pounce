package dev.pounce.platform;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * The Watched Folders Pounce starts with when the user has not chosen any: where people keep their own files on
 * each system.
 * <ul>
 *     <li>macOS: {@code ~/Documents}, {@code ~/Desktop}, {@code ~/Downloads} and {@code ~/projects};</li>
 *     <li>Windows: {@code Documents}, {@code Desktop} and {@code Downloads} in the user home, and {@code Documents}
 *     and {@code Desktop} in each OneDrive folder ({@code OneDrive}, {@code OneDriveCommercial},
 *     {@code OneDriveConsumer}), because Windows often moves them there;</li>
 *     <li>anything else: the documents, desktop and download folders named in the XDG
 *     {@code ${XDG_CONFIG_HOME:-~/.config}/user-dirs.dirs}, else {@code ~/Documents}, {@code ~/Desktop} and
 *     {@code ~/Downloads}.</li>
 * </ul>
 * Only folders that exist are returned, and a folder reached by two of these paths only once.
 */
public final class DefaultWatchedFolders {

    private static final List<String> ONEDRIVE_VARIABLES = List.of("OneDrive", "OneDriveCommercial", "OneDriveConsumer");
    /** The XDG user dirs Pounce watches, with the folder used when {@code user-dirs.dirs} does not name it. */
    private static final List<String[]> XDG_DIRS = List.of(
            new String[] {"DOCUMENTS", "Documents"}, new String[] {"DESKTOP", "Desktop"},
            new String[] {"DOWNLOAD", "Downloads"});
    /** {@code XDG_DOCUMENTS_DIR="$HOME/Documents"}, the form {@code xdg-user-dirs-update} writes. */
    private static final Pattern USER_DIR = Pattern.compile("XDG_(\\w+)_DIR\\s*=\\s*\"?(.*?)\"?\\s*");

    private DefaultWatchedFolders() {
    }

    /** The default Watched Folders for the user running this program. */
    public static List<Path> find() {
        return find(System.getProperty("os.name"), System.getenv(), Path.of(System.getProperty("user.home")));
    }

    /**
     * The default Watched Folders for this system, environment and user home.
     *
     * @param osName      the {@code os.name} system property
     * @param environment the environment variables
     * @param userHome    the {@code user.home} system property
     */
    public static List<Path> find(String osName, Map<String, String> environment, Path userHome) {
        var system = OperatingSystem.named(osName);
        List<Path> candidates = switch (system) {
            case MACOS -> List.of(userHome.resolve("Documents"), userHome.resolve("Desktop"),
                    userHome.resolve("Downloads"), userHome.resolve("projects"));
            case WINDOWS -> windows(environment, userHome);
            case OTHER -> xdg(system, environment, userHome);
        };
        return existingOnce(candidates);
    }

    private static List<Path> windows(Map<String, String> environment, Path userHome) {
        var folders = new ArrayList<>(List.of(
                userHome.resolve("Documents"), userHome.resolve("Desktop"), userHome.resolve("Downloads")));
        for (String variable : ONEDRIVE_VARIABLES) {
            String oneDrive = environment.get(variable);
            if (oneDrive != null && !oneDrive.isBlank()) {
                folders.add(Path.of(oneDrive, "Documents"));
                folders.add(Path.of(oneDrive, "Desktop"));
            }
        }
        return folders;
    }

    private static List<Path> xdg(OperatingSystem system, Map<String, String> environment, Path userHome) {
        Path config = system.absolutePath(environment.get("XDG_CONFIG_HOME"));
        Map<String, Path> named = userDirs((config != null ? config : userHome.resolve(".config"))
                .resolve("user-dirs.dirs"), userHome);
        var folders = new ArrayList<Path>();
        for (String[] dir : XDG_DIRS) {
            Path folder = named.getOrDefault(dir[0], userHome.resolve(dir[1]));
            // Pointing a user dir at the home itself is how it is switched off; watching the whole home is not meant.
            if (!folder.equals(userHome)) {
                folders.add(folder);
            }
        }
        return folders;
    }

    /** The folders named in an XDG {@code user-dirs.dirs} file, by name ({@code DOCUMENTS}); empty when it is absent. */
    private static Map<String, Path> userDirs(Path file, Path userHome) {
        var dirs = new HashMap<String, Path>();
        List<String> lines;
        try {
            lines = Files.readAllLines(file);
        } catch (IOException e) {
            return dirs;
        }
        for (String line : lines) {
            Matcher matcher = USER_DIR.matcher(line.strip());
            if (!line.strip().startsWith("#") && matcher.matches()) {
                String value = matcher.group(2).replaceAll("\\\\(.)", "$1");
                if (value.startsWith("$HOME")) {
                    dirs.put(matcher.group(1), userHome.resolve(value.substring("$HOME".length()).replaceFirst("^/+", "")));
                } else if (value.startsWith("/")) {
                    dirs.put(matcher.group(1), Path.of(value));
                }
            }
        }
        return dirs;
    }

    private static List<Path> existingOnce(List<Path> candidates) {
        var seen = new HashSet<Path>();
        var folders = new ArrayList<Path>();
        for (Path candidate : candidates) {
            if (Files.isDirectory(candidate) && seen.add(realPath(candidate))) {
                folders.add(candidate);
            }
        }
        return List.copyOf(folders);
    }

    private static Path realPath(Path folder) {
        try {
            return folder.toRealPath();
        } catch (IOException e) {
            return folder.toAbsolutePath().normalize();
        }
    }
}
