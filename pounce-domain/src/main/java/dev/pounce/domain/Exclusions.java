package dev.pounce.domain;

import java.io.IOException;
import java.nio.file.Path;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Rules that keep paths inside a Watched Folder out of the Index. Every check is made relative to the Watched
 * Folder the path was reached through, because the user chose that folder and nothing above it:
 * <ul>
 *   <li><b>Name rules</b> look only at the folders below the Watched Folder (and the file's own name): hidden
 *       folders ({@code .git}), dependency and build folders ({@code node_modules}, {@code target}…), Windows system
 *       folders ({@code $Recycle.Bin}, {@code System Volume Information}, {@code $WinREAgent}, {@code $SysReset},
 *       {@code $Windows.~BT}, {@code $Windows.~WS}; other folders starting with {@code $} are the user's), and the
 *       files {@code desktop.ini}, {@code Thumbs.db} and Office lock files ({@code ~$report.docx}). The Watched
 *       Folder's own path is never judged by name, so a Watched Folder inside a hidden folder or under
 *       {@code AppData\Local\Temp} is indexed.</li>
 *   <li><b>Location rules</b> skip the places where programs keep their own files: {@code ~/AppData} and
 *       {@code ~/snap}. They apply when the walk reaches such a place from a Watched Folder above it (for example
 *       the whole home folder); a Watched Folder chosen inside one of them is indexed.</li>
 *   <li><b>Always skipped</b>, even when the Watched Folder is inside: {@code ~/Library}, since on macOS it holds
 *       iCloud Drive ({@code ~/Library/Mobile Documents}), whose files would all be downloaded by reading them; and
 *       the Pounce home (where the Index itself lives), since indexing the Index would feed its own writes back
 *       into it.</li>
 *   <li><b>Size:</b> files over 50 MB.</li>
 *   <li><b>Windows file attributes</b> ({@link #isCloudOnlyOrSystem}): files that are only in the cloud, and system
 *       files.</li>
 * </ul>
 * A path that is not below the given Watched Folder (a link pointing elsewhere) is judged by every folder on it.
 * Names are compared exactly, except the Windows names, which ignore case as Windows does.
 */
public final class Exclusions {

    private static final long MAX_SIZE_BYTES = 50L * 1024 * 1024;
    private static final int SYSTEM = 0x4;
    private static final int OFFLINE = 0x1000;
    private static final int RECALL_ON_OPEN = 0x4_0000;
    private static final int RECALL_ON_DATA_ACCESS = 0x40_0000;
    private static final Set<String> SKIPPED_FOLDERS = Set.of(
            "node_modules", "venv", "site-packages", "__pycache__", "target", "build");
    /** Folders Windows keeps for itself at the top of a drive, in lower case: Windows ignores case. */
    private static final Set<String> WINDOWS_SYSTEM_FOLDERS = Set.of(
            "$recycle.bin", "system volume information", "$winreagent", "$sysreset", "$windows.~bt", "$windows.~ws");

    private final List<Path> programLocations;
    private final List<Path> alwaysSkipped;

    private Exclusions(List<Path> programLocations, List<Path> alwaysSkipped) {
        this.programLocations = programLocations;
        this.alwaysSkipped = alwaysSkipped;
    }

    /**
     * The standard rules for this user. Both folders are resolved to their real location when they exist, since
     * the paths being judged are real paths.
     */
    public static Exclusions defaults(Path userHome, Path pounceHome) {
        Path home = real(userHome);
        return new Exclusions(
                List.of(home.resolve("AppData"), home.resolve("snap")), List.of(home.resolve("Library"), real(pounceHome)));
    }

    /** Whether this file, reached through this Watched Folder, stays out of the Index. */
    public boolean excludes(Path watchedFolder, Path file, long sizeBytes) {
        if (sizeBytes > MAX_SIZE_BYTES || isSkippedFileName(file.getFileName().toString())) {
            return true;
        }
        Path folder = file.getParent();
        return folder != null && excludesFolder(watchedFolder, folder);
    }

    /**
     * Whether this folder, reached through this Watched Folder, stays out of the Index with everything inside it.
     * Lets a walk skip a whole subtree without looking inside.
     */
    public boolean excludesFolder(Path watchedFolder, Path folder) {
        for (Path location : alwaysSkipped) {
            if (folder.startsWith(location)) {
                return true;
            }
        }
        for (Path location : programLocations) {
            if (folder.startsWith(location) && !watchedFolder.startsWith(location)) {
                return true;
            }
        }
        Path below = folder.startsWith(watchedFolder) ? watchedFolder.relativize(folder) : folder;
        for (Path part : below) {
            if (isSkippedFolderName(part.toString())) {
                return true;
            }
        }
        return false;
    }

    /**
     * Whether a file with these Windows file attributes ({@code dos:attributes}) stays out of the Index: a cloud
     * placeholder (OneDrive and other cloud drives mark files that are only in the cloud as recall-on-access, or
     * offline), since reading it would download it, and Windows system files.
     */
    public static boolean isCloudOnlyOrSystem(int windowsAttributes) {
        return (windowsAttributes & (RECALL_ON_DATA_ACCESS | RECALL_ON_OPEN | OFFLINE | SYSTEM)) != 0;
    }

    private static boolean isSkippedFolderName(String name) {
        return name.startsWith(".") || SKIPPED_FOLDERS.contains(name)
                || WINDOWS_SYSTEM_FOLDERS.contains(name.toLowerCase(Locale.ROOT));
    }

    private static boolean isSkippedFileName(String name) {
        return name.equalsIgnoreCase("desktop.ini") || name.equalsIgnoreCase("Thumbs.db") || name.startsWith("~$");
    }

    private static Path real(Path path) {
        Path absolute = path.toAbsolutePath().normalize();
        try {
            return absolute.toRealPath();
        } catch (IOException doesNotExistYet) {
            return absolute;
        }
    }
}
