import Testing
@testable import PounceKit

struct SnippetTests {

    @Test func highlight_offsets_count_utf16_units_like_the_java_engine() {
        // "🚀" is two UTF-16 units, so "canary" starts at offset 3 in Java, not at Swift character 2.
        let snippet = Snippet(text: "🚀 canary rollout", highlights: [.init(start: 3, end: 9)])

        #expect(snippet.runs.map(\.text) == ["🚀 ", "canary", " rollout"])
        #expect(snippet.runs.map(\.highlighted) == [false, true, false])
    }

    @Test func offsets_outside_the_text_are_ignored_instead_of_crashing() {
        let snippet = Snippet(text: "short", highlights: [.init(start: 3, end: 99), .init(start: 40, end: 50)])

        #expect(snippet.runs.map(\.text) == ["sho", "rt"])
    }
}
