import Foundation
import Testing
@testable import PounceKit

/// Apps come first, like Spotlight: matched by name, word starts and initials, at most three, before any files.
@MainActor
struct AppSearchTests {

    let engine = FakeEngine()
    let apps = FixedApps(["Safari", "Slack", "Calculator", "Visual Studio Code", "Notes", "Notes Exporter",
                          "Sticky Notes", "System Settings", "Spotify", "Screenshot"])

    func model() async -> SearchModel {
        let model = SearchModel(engine: engine, apps: apps)
        await model.reloadApps()
        return model
    }

    @Test func typing_the_start_of_an_app_name_puts_that_app_first() async throws {
        let model = await model()

        model.query = "saf"

        #expect(model.apps.map(\.name) == ["Safari"])
        #expect(model.selected == .app(apps.named("Safari")))
    }

    @Test func the_start_of_any_word_and_initials_also_match() async {
        let model = await model()

        model.query = "code"
        #expect(model.apps.map(\.name) == ["Visual Studio Code"])

        model.query = "vsc"
        #expect(model.apps.map(\.name) == ["Visual Studio Code"])
    }

    @Test func an_exact_name_beats_a_name_start_which_beats_a_word_start() async {
        let model = await model()

        model.query = "notes"

        #expect(model.apps.map(\.name) == ["Notes", "Notes Exporter", "Sticky Notes"])
    }

    @Test func a_single_letter_matches_only_the_start_of_names_and_at_most_three_apps_show() async {
        let model = await model()

        model.query = "s"

        #expect(model.apps.count == 3)
        #expect(model.apps.allSatisfy { $0.name.lowercased().hasPrefix("s") })
    }

    @Test func files_follow_the_apps_and_selection_moves_from_apps_into_files() async throws {
        let model = await model()
        model.query = "notes"
        try await eventually { engine.isSearching("notes") }
        engine.send(.final([item("meeting-notes.md")]), to: "notes")
        try await eventually { model.isFinal }

        #expect(model.items.count == 4)
        model.moveSelection(by: 3)
        #expect(model.selected == .file(item("meeting-notes.md")))
    }

    @Test func a_matching_app_means_no_no_results_message() async throws {
        let model = await model()
        model.query = "spotify"
        try await eventually { engine.isSearching("spotify") }
        engine.send(.final([]), to: "spotify")
        try await eventually { model.isFinal }

        #expect(model.apps.map(\.name) == ["Spotify"])
        #expect(model.message == nil)
    }

    @Test func the_catalog_finds_apps_in_folders_and_one_level_of_subfolders_only() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("pounce-apps-\(UUID().uuidString)")
        for path in ["Safari.app/Contents", "Utilities/Terminal.app/Contents", "Xcode.app/Contents/Developer/Inner.app",
                     ".Hidden.app", "Deep/Deeper/Lost.app"] {
            try FileManager.default.createDirectory(at: root.appendingPathComponent(path), withIntermediateDirectories: true)
        }

        let names = FolderAppSource(folders: [root]).apps().map(\.name).sorted()

        #expect(names == ["Safari", "Terminal", "Xcode"])
    }
}

/// A fixed list of apps, for tests.
struct FixedApps: AppSource {
    let entries: [AppEntry]

    init(_ names: [String]) {
        entries = names.map { AppEntry(name: $0, path: "/Applications/\($0).app") }
    }

    func apps() -> [AppEntry] { entries }
    func named(_ name: String) -> AppEntry { entries.first { $0.name == name }! }
}
