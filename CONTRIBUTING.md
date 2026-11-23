# Contributing to Pounce

Thanks for wanting to help. Pounce is a small project, so the process is simple: open an issue to discuss anything bigger than a bug fix, then send a pull request from your fork. Nobody pushes to `main` directly; every change goes through a reviewed pull request with passing CI.

## Ways to help

- **Report a bug** with the bug report template: macOS version, Mac model, what you searched for and what happened. If a search went wrong, say which kind of file you expected (PDF, Markdown, code…) without sharing private content.
- **Suggest a feature** with the feature request template. Check the [roadmap](README.md#roadmap) first.
- **Fix or build something.** Issues labelled `good first issue` are a good start. Comment on the issue before starting, so two people don't do the same work.

## Development setup

You need macOS 14 or later on Apple Silicon, **JDK 25**, **Maven 3.9+**, and **Xcode 16+** (for the Swift Launcher).

```bash
git clone https://github.com/<you>/Pounce.git && cd Pounce
scripts/fetch-model.sh          # downloads the embedding model into models/ (not in git)
mvn verify                      # Engine and CLI tests
(cd macos-app && swift test)    # Launcher tests
```

To try your changes end to end:

```bash
scripts/build-app.sh            # builds build/Pounce.app
scripts/smoke-app.sh            # starts it on a temporary folder and checks a search works
```

`smoke-app.sh` needs port 7431 free, so quit any running Pounce first.

## How the code is organised

Read these before a larger change:

- [`docs/design/engine-modules.md`](docs/design/engine-modules.md): the Engine's modules, its `Indexer` and `Searcher`, the ports, and the terms used in the code (Document, Passage, Snippet…).
- [`docs/adr/`](docs/adr/): why things are the way they are (Java over Rust, Lucene only, JWT on localhost, the bundled Engine, the onion layers). If your change reverses one of these, add a new decision record explaining why.
- [`docs/research/`](docs/research/): measured speed, memory and library facts.

The Engine is an onion of Maven modules: `pounce-domain` → `pounce-application` → `pounce-infrastructure` → `pounce-api`. Dependencies only point inward; the compiler and the ArchUnit tests in `pounce-api` enforce it. Spring, Lucene, ONNX Runtime and PDFBox stay in their outer modules. The `pounce-cli` and the Swift Launcher (`macos-app/`) talk to the Engine over HTTP only.

## Rules for changes

- **Tests first.** Behaviour is tested through the public interfaces (`Indexer` and `Searcher`, the REST API, the Launcher's `SearchModel`), with real temporary folders and a real in-memory Lucene index rather than mocks. Put a failing test up before the fix.
- **Nothing leaves the Mac.** The Engine and the Launcher make no network calls, and pull requests that add telemetry, analytics or cloud services won't be merged.
- **Measure performance claims.** If a change is meant to be faster or lighter, include before/after numbers and how you measured them.
- **Keep the ranking honest.** Changes to ranking must keep `RankingTest` green; add labelled queries for the case you're improving.
- **Small, focused pull requests.** One change per pull request is much easier to review than a bundle.
- **Code style:** follow the surrounding code. Java uses the standard conventions with 4-space indents; Swift follows the Swift API Design Guidelines.

## Pull requests

1. Fork the repository and create a branch from `main` (`fix/pdf-snippets`, `feature/settings-window`).
2. Make your change with its tests, and run `mvn verify` and `swift test` locally.
3. Add a line to the `## [Unreleased]` section of [`CHANGELOG.md`](CHANGELOG.md) for anything a user would notice.
4. Open the pull request and fill in the template. CI runs every test on macOS; it must be green before review. First-time contributors' CI runs start after a maintainer approves them.
5. A maintainer reviews it. Expect questions; once it's approved, the maintainer merges it (usually squashed into one commit).

## Commit messages

Write the subject as a short summary of the change in the imperative ("Skip unchanged files on restart"), and use the body to explain why, not what.

## Security

Please don't open public issues for security problems. See [SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
