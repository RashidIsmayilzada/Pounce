import Foundation
import Testing
@testable import PounceKit

/// Search-as-you-type, with a fake Engine the test drives by hand.
@MainActor
struct SearchModelTests {

    let engine = FakeEngine()

    @Test func typing_shows_early_results_then_replaces_them_with_final_results() async throws {
        let model = SearchModel(engine: engine)

        model.query = "canary"
        try await eventually { engine.isSearching("canary") }
        engine.send(.early([item("early-guess.md")]), to: "canary")
        try await eventually { model.results.map(\.name) == ["early-guess.md"] }
        engine.send(.final([item("rollout.md"), item("runbook.md")]), to: "canary")

        try await eventually { model.results.map(\.name) == ["rollout.md", "runbook.md"] }
        #expect(model.isFinal)
        #expect(model.shownQuery == "canary")
    }

    @Test func while_a_new_query_runs_the_shown_results_are_marked_as_belonging_to_the_old_one() async throws {
        let model = SearchModel(engine: engine)
        model.query = "can"
        try await eventually { engine.isSearching("can") }
        engine.send(.final([item("cannon.md")]), to: "can")
        try await eventually { model.isFinal }

        model.query = "canary"

        #expect(!model.isFinal)
        #expect(model.shownQuery == "can")
        #expect(model.results.map(\.name) == ["cannon.md"])
    }

    @Test func a_new_keystroke_drops_the_results_of_the_previous_query() async throws {
        let model = SearchModel(engine: engine)

        model.query = "can"
        try await eventually { engine.isSearching("can") }
        model.query = "canary"
        try await eventually { engine.isSearching("canary") }
        engine.send(.final([item("stale.md")]), to: "can")
        engine.send(.final([item("rollout.md")]), to: "canary")

        try await eventually { model.results.map(\.name) == ["rollout.md"] }
        try await Task.sleep(for: .milliseconds(50))
        #expect(model.results.map(\.name) == ["rollout.md"])
    }

    @Test func clearing_the_box_clears_the_results() async throws {
        let model = SearchModel(engine: engine)
        model.query = "canary"
        try await eventually { engine.isSearching("canary") }
        engine.send(.final([item("rollout.md")]), to: "canary")
        try await eventually { !model.results.isEmpty }

        model.query = "   "

        #expect(model.results.isEmpty)
        #expect(model.message == nil)
    }

    @Test func an_engine_problem_is_shown_as_a_message_instead_of_results() async throws {
        let model = SearchModel(engine: engine)

        model.query = "canary"
        try await eventually { engine.isSearching("canary") }
        engine.fail("canary", with: EngineError.notRunning(URL(string: "http://127.0.0.1:7431")!))

        try await eventually { model.message != nil }
        #expect(model.message == "Pounce isn't running at http://127.0.0.1:7431. Start Pounce and try again.")
        #expect(model.results.isEmpty)
    }

    @Test func a_search_with_no_matches_says_so() async throws {
        let model = SearchModel(engine: engine)

        model.query = "xyzzyplugh"
        try await eventually { engine.isSearching("xyzzyplugh") }
        engine.send(.final([]), to: "xyzzyplugh")

        try await eventually { model.message != nil }
        #expect(model.message == "No results for \u{201C}xyzzyplugh\u{201D}")
    }
}

// MARK: - helpers

func item(_ name: String) -> ResultItem {
    ResultItem(path: "/notes/\(name)", name: name, snippet: Snippet(text: "", highlights: []))
}

/// Polls until the condition holds, or fails after a second.
@MainActor
func eventually(_ condition: () -> Bool, sourceLocation: SourceLocation = #_sourceLocation) async throws {
    for _ in 0..<200 {
        if condition() { return }
        try await Task.sleep(for: .milliseconds(5))
    }
    Issue.record("condition never became true", sourceLocation: sourceLocation)
}

/// A SearchEngine whose streams the test feeds by hand.
final class FakeEngine: SearchEngine, @unchecked Sendable {
    private let lock = NSLock()
    private var streams: [String: AsyncThrowingStream<SearchPhase, Error>.Continuation] = [:]

    func search(_ query: String, limit: Int) -> AsyncThrowingStream<SearchPhase, Error> {
        AsyncThrowingStream { continuation in
            lock.withLock { streams[query] = continuation }
        }
    }

    func isSearching(_ query: String) -> Bool { lock.withLock { streams[query] != nil } }

    func send(_ phase: SearchPhase, to query: String) {
        let stream = lock.withLock { streams[query] }
        stream?.yield(phase)
        if case .final = phase { stream?.finish() }
    }

    func fail(_ query: String, with error: Error) {
        lock.withLock { streams[query] }?.finish(throwing: error)
    }
}
