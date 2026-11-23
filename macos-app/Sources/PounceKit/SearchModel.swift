import Foundation
import Observation

/// Search-as-you-type: every change to `query` cancels the previous search and starts a new one, whose
/// Early Results are replaced by its Final Results. Problems and empty searches become a `message`.
@MainActor
@Observable
public final class SearchModel {

    public var query: String = "" {
        didSet { if query != oldValue { start() } }
    }
    public private(set) var results: [ResultItem] = []
    /// True once the Final Results for the current `query` are shown; false while a newer search is running.
    public private(set) var isFinal = false
    /// The Query the shown Results belong to (lags behind `query` while a new search runs).
    public private(set) var shownQuery = ""
    public private(set) var message: String?
    /// Applications whose names match the Query, best first; shown before the Results, like Spotlight.
    public private(set) var apps: [AppEntry] = []
    public var selection: Int = 0

    /// One row of the panel: an application, or a Result.
    public enum Item: Equatable, Hashable, Sendable {
        case app(AppEntry)
        case file(ResultItem)
    }

    private let engine: SearchEngine
    private let appSource: AppSource
    private var catalog: [AppEntry] = []
    private let limit: Int
    private var current: Task<Void, Never>?

    public init(engine: SearchEngine, apps: AppSource = NoApps(), limit: Int = 8) {
        self.engine = engine
        self.appSource = apps
        self.limit = limit
    }

    /// Re-reads the installed applications (off the main thread); call when the panel opens.
    public func reloadApps() async {
        let source = appSource
        catalog = await Task.detached { source.apps() }.value
        apps = AppMatcher.rank(query, in: catalog)
    }

    /// Apps first, then Results: the order of the rows and of the selection.
    public var items: [Item] { apps.map(Item.app) + results.map(Item.file) }

    public var selected: Item? { items.indices.contains(selection) ? items[selection] : nil }

    public func moveSelection(by step: Int) {
        guard !items.isEmpty else { return }
        selection = min(max(selection + step, 0), items.count - 1)
    }

    private func start() {
        current?.cancel()
        apps = AppMatcher.rank(query, in: catalog)
        selection = 0
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else {
            show([], for: "", final: true, message: nil)
            return
        }
        isFinal = false
        let stream = engine.search(text, limit: limit)
        current = Task { [weak self] in
            do {
                for try await phase in stream {
                    guard !Task.isCancelled else { return }
                    switch phase {
                    case .early(let items):
                        self?.show(items, for: text, final: false, message: nil)
                    case .final(let items):
                        self?.show(items, for: text, final: true, message: items.isEmpty && (self?.apps.isEmpty ?? true)
                            ? "No results for \u{201C}\(text)\u{201D}" : nil)
                    }
                }
            } catch {
                guard !Task.isCancelled else { return }
                self?.show([], for: text, final: true, message: error.localizedDescription)
            }
        }
    }

    private func show(_ items: [ResultItem], for query: String, final: Bool, message: String?) {
        results = items
        shownQuery = query
        isFinal = final
        self.message = message
        selection = 0
    }
}
