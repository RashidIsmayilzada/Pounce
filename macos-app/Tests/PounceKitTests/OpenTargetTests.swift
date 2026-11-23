import Foundation
import Testing
@testable import PounceKit

/// Markdown inside an Obsidian vault opens in Obsidian; everything else opens in its default app.
struct OpenTargetTests {

    let root: URL

    init() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("pounce-open-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root.appendingPathComponent("Vault/.obsidian"), withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: root.appendingPathComponent("Vault/wiki/my notes"), withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: root.appendingPathComponent("Elsewhere"), withIntermediateDirectories: true)
    }

    @Test func markdown_inside_a_vault_opens_through_obsidian() {
        let note = root.appendingPathComponent("Vault/wiki/my notes/TLS renewal.md").path

        let url = OpenTarget.url(for: note, obsidianInstalled: true)

        #expect(url.scheme == "obsidian")
        #expect(url.host() == "open")
        #expect(URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems == [URLQueryItem(name: "path", value: note)])
    }

    @Test func markdown_outside_any_vault_opens_in_the_default_app() {
        let note = root.appendingPathComponent("Elsewhere/readme.md").path

        #expect(OpenTarget.url(for: note, obsidianInstalled: true) == URL(fileURLWithPath: note))
    }

    @Test func other_files_inside_a_vault_open_in_the_default_app() {
        let pdf = root.appendingPathComponent("Vault/wiki/paper.pdf").path

        #expect(OpenTarget.url(for: pdf, obsidianInstalled: true) == URL(fileURLWithPath: pdf))
    }

    @Test func without_obsidian_installed_markdown_opens_in_the_default_app() {
        let note = root.appendingPathComponent("Vault/wiki/note.md").path

        #expect(OpenTarget.url(for: note, obsidianInstalled: false) == URL(fileURLWithPath: note))
    }
}
