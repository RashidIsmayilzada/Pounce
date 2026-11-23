import Foundation

/// An installed application.
public struct AppEntry: Equatable, Hashable, Sendable, Identifiable {
    public let name: String
    public let path: String

    public var id: String { path }

    public init(name: String, path: String) {
        self.name = name
        self.path = path
    }
}

/// Lists the installed applications.
public protocol AppSource: Sendable {
    func apps() -> [AppEntry]
}

/// No applications, for when app search is off.
public struct NoApps: AppSource {
    public init() {}
    public func apps() -> [AppEntry] { [] }
}

/// Finds `.app` bundles in the given folders and one level of subfolders (such as `Utilities` or a vendor folder),
/// without looking inside bundles. The name is the bundle's file name without `.app`.
public struct FolderAppSource: AppSource {
    public let folders: [URL]

    public init(folders: [URL]) {
        self.folders = folders
    }

    /// Where macOS keeps applications: the same places Spotlight lists apps from.
    public static let standard = FolderAppSource(folders: [
        URL(fileURLWithPath: "/Applications"),
        URL(fileURLWithPath: "/System/Applications"),
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications"),
    ])

    public func apps() -> [AppEntry] {
        var found: [String: AppEntry] = [:]
        for folder in folders {
            for entry in children(of: folder) {
                if entry.pathExtension == "app" {
                    add(entry, to: &found)
                } else if isFolder(entry) {
                    children(of: entry).filter { $0.pathExtension == "app" }.forEach { add($0, to: &found) }
                }
            }
        }
        return Array(found.values)
    }

    private func add(_ bundle: URL, to found: inout [String: AppEntry]) {
        let path = bundle.resolvingSymlinksInPath().path
        found[path] = AppEntry(name: bundle.deletingPathExtension().lastPathComponent, path: path)
    }

    private func children(of folder: URL) -> [URL] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? []
        return names.filter { !$0.hasPrefix(".") }.map { folder.appendingPathComponent($0) }
    }

    private func isFolder(_ url: URL) -> Bool {
        var folder: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &folder) && folder.boolValue
    }
}

/// Ranks apps against a Query the way a launcher should: exact name, then the start of the name, then the start of
/// any word, then initials, then anywhere in the name. One letter only matches the start of a name.
public enum AppMatcher {

    public static let limit = 3

    public static func rank(_ query: String, in apps: [AppEntry]) -> [AppEntry] {
        let q = normalised(query.trimmingCharacters(in: .whitespacesAndNewlines))
        guard !q.isEmpty else { return [] }
        return apps
            .compactMap { app in score(q, against: app.name).map { (app, $0) } }
            .sorted { ($0.1, $0.0.name.count, $0.0.name) < ($1.1, $1.0.name.count, $1.0.name) }
            .prefix(limit)
            .map(\.0)
    }

    private static func score(_ q: String, against name: String) -> Int? {
        let n = normalised(name)
        if n == q { return 0 }
        if n.hasPrefix(q) { return 1 }
        guard q.count > 1 else { return nil }
        let words = n.split { !$0.isLetter && !$0.isNumber }.map(String.init)
        let queryWords = q.split(separator: " ").map(String.init)
        if queryWords.allSatisfy({ part in words.contains { $0.hasPrefix(part) } }) { return 2 }
        if String(words.compactMap(\.first)).hasPrefix(q) { return 3 }
        if q.count >= 3 && n.contains(q) { return 4 }
        return nil
    }

    private static func normalised(_ text: String) -> String {
        text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: nil)
    }
}
