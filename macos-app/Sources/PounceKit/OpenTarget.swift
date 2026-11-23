import Foundation

/// What opening a Result should open: Markdown inside an Obsidian vault goes through Obsidian's own link, so it
/// opens in the right vault instead of whatever app is the default for `.md`; everything else opens as a file.
public enum OpenTarget {

    public static func url(for path: String, obsidianInstalled: Bool, fileManager: FileManager = .default) -> URL {
        let file = URL(fileURLWithPath: path)
        guard obsidianInstalled,
              ["md", "markdown"].contains(file.pathExtension.lowercased()),
              isInsideVault(file, fileManager: fileManager)
        else { return file }
        var link = URLComponents()
        link.scheme = "obsidian"
        link.host = "open"
        link.queryItems = [URLQueryItem(name: "path", value: path)]
        return link.url ?? file
    }

    /// A vault is any folder above the file that holds a `.obsidian` folder.
    private static func isInsideVault(_ file: URL, fileManager: FileManager) -> Bool {
        var folder = file.deletingLastPathComponent()
        while folder.path != "/" && !folder.path.isEmpty {
            var isFolder: ObjCBool = false
            if fileManager.fileExists(atPath: folder.appendingPathComponent(".obsidian").path, isDirectory: &isFolder),
               isFolder.boolValue {
                return true
            }
            folder = folder.deletingLastPathComponent()
        }
        return false
    }
}
