import Foundation

/// One Document returned for a Query, as the Engine sends it.
public struct ResultItem: Codable, Equatable, Hashable, Sendable, Identifiable {
    public let path: String
    public let name: String
    public let snippet: Snippet

    public var id: String { path }

    public init(path: String, name: String, snippet: Snippet) {
        self.path = path
        self.name = name
        self.snippet = snippet
    }
}

/// The excerpt shown under a Result. Highlight offsets count UTF-16 code units, as Java strings do.
public struct Snippet: Codable, Equatable, Hashable, Sendable {
    public struct Highlight: Codable, Equatable, Hashable, Sendable {
        public let start: Int
        public let end: Int

        public init(start: Int, end: Int) {
            self.start = start
            self.end = end
        }
    }

    public let text: String
    public let highlights: [Highlight]

    public init(text: String, highlights: [Highlight]) {
        self.text = text
        self.highlights = highlights
    }

    /// The text split into runs, each marked as highlighted or not; safe against out-of-range offsets.
    public var runs: [(text: String, highlighted: Bool)] {
        let utf16 = Array(text.utf16)
        var runs: [(String, Bool)] = []
        var position = 0
        for h in highlights.sorted(by: { $0.start < $1.start }) {
            let start = min(max(h.start, position), utf16.count)
            let end = min(max(h.end, start), utf16.count)
            if start > position { runs.append((String(decoding: utf16[position..<start], as: UTF16.self), false)) }
            if end > start { runs.append((String(decoding: utf16[start..<end], as: UTF16.self), true)) }
            position = end
        }
        if position < utf16.count { runs.append((String(decoding: utf16[position...], as: UTF16.self), false)) }
        return runs
    }
}

/// One step of a streamed search: Early Results first, then Final Results.
public enum SearchPhase: Equatable, Sendable {
    case early([ResultItem])
    case final([ResultItem])
}
