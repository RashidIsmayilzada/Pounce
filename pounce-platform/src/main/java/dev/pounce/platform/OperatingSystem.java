package dev.pounce.platform;

import java.nio.file.Path;
import java.util.Locale;
import java.util.regex.Pattern;

/**
 * The operating systems Pounce tells apart, named by their {@code os.name}. Everything that is not macOS or Windows
 * is treated like Linux.
 */
enum OperatingSystem {
    MACOS, WINDOWS, OTHER;

    /** {@code C:\} or {@code C:/}, or a {@code \\server\share} network path. */
    private static final Pattern WINDOWS_ABSOLUTE = Pattern.compile("^([A-Za-z]:[\\\\/]|\\\\\\\\).*");

    static OperatingSystem named(String osName) {
        String name = osName.toLowerCase(Locale.ROOT);
        if (name.startsWith("mac")) {
            return MACOS;
        }
        return name.startsWith("windows") ? WINDOWS : OTHER;
    }

    /**
     * Whether this is a full path by this system's rules, checked on the text rather than with {@link Path} so the
     * answer is the same whichever system the check runs on.
     */
    boolean isAbsolute(String path) {
        return this == WINDOWS ? WINDOWS_ABSOLUTE.matcher(path).matches() : path.startsWith("/");
    }

    /** An absolute path from an environment variable, or null when it is unset, blank or relative. */
    Path absolutePath(String value) {
        return value != null && !value.isBlank() && isAbsolute(value) ? Path.of(value) : null;
    }
}
