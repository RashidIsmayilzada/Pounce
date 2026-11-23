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
    public private(set) var isFinal = false
    public private(set) var message: String?
    public var selection: Int = 0

    private let engine: SearchEngine
    private let limit: Int
    private var current: Task<Void, Never>?

    public init(engine: SearchEngine, limit: Int = 8) {
        self.engine = engine
        self.limit = limit
    }

    public var selected: ResultItem? { results.indices.contains(selection) ? results[selection] : nil }

    public func moveSelection(by step: Int) {
        guard !results.isEmpty else { return }
        selection = min(max(selection + step, 0), results.count - 1)
    }

    private func start() {
        current?.cancel()
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else {
            show([], final: true, message: nil)
            return
        }
        let stream = engine.search(text, limit: limit)
        current = Task { [weak self] in
            do {
                for try await phase in stream {
                    guard !Task.isCancelled else { return }
                    switch phase {
                    case .early(let items):
                        self?.show(items, final: false, message: nil)
                    case .final(let items):
                        self?.show(items, final: true, message: items.isEmpty ? "No results for \u{201C}\(text)\u{201D}" : nil)
                    }
                }
            } catch {
                guard !Task.isCancelled else { return }
                self?.show([], final: true, message: error.localizedDescription)
            }
        }
    }

    private func show(_ items: [ResultItem], final: Bool, message: String?) {
        results = items
        isFinal = final
        self.message = message
        selection = 0
    }
}
