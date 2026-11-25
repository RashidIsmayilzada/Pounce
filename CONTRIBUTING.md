# Contributing to Pounce

Thanks for wanting to help. Pounce is a small project, so the process is simple: open an issue to discuss anything bigger than a bug fix, then send a pull request from your fork. Nobody pushes to `main` directly; every change goes through a reviewed pull request with passing CI.

## Ways to help

- **Report a bug** with the bug report template: your operating system and its version, your computer, what you searched for and what happened. If a search went wrong, say which kind of file you expected (PDF, Markdown, code…) without sharing private content.
- **Suggest a feature** with the feature request template. Check the [roadmap](README.md#roadmap) first.
- **Fix or build something.** Issues labelled `good first issue` are a good start. Comment on the issue before starting, so two people don't do the same work.

## Development setup

The Engine and the `pounce` command build and test on macOS, Windows and Linux. Each launcher builds on its own system; the Windows one also cross-builds on Linux and WSL.

### Engine and CLI (any system)

You need **JDK 25** and **Maven 3.9+**, and on Windows Git Bash for the scripts.

```bash
git clone https://github.com/<you>/Pounce.git && cd Pounce
scripts/fetch-model.sh          # downloads the embedding model into models/ (not in git)
mvn verify                      # Engine and CLI tests
```

On Windows the tests that need symbolic links skip themselves (creating links there needs Developer Mode or administrator rights), and the tokenizer's golden test skips itself because its reference library has no Windows build (a frozen fixture still checks the tokenizer). On Linux, if the file watcher logs that the inotify watch limit is reached, raise it with `sudo sysctl fs.inotify.max_user_watches=524288`.

### macOS launcher

You need macOS 14 or later on Apple Silicon and **Xcode 16+**.

```bash
(cd macos-app && swift test)    # Launcher tests
scripts/build-app.sh            # builds build/Pounce.app
scripts/smoke-app.sh            # starts it on a temporary folder and checks a search works
```

### Windows launcher

You need the **.NET 10 SDK** (see `windows-app/global.json`). Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` so the SDK sends nothing. On Linux or WSL the SDK also needs ICU (install `libicu`, or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`).

```bash
cd windows-app
dotnet build
dotnet test                            # Pounce.Core tests, on Linux too
dotnet format --verify-no-changes
cd ..
scripts/build-windows.sh               # builds build/Pounce-<version>-windows-x64.zip, on Windows or Linux/WSL
```

Then, on Windows, in PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-windows.ps1   # starts it on a temporary folder and checks a search, quit and forced quit
```

Logic goes in `Pounce.Core`, which uses no Windows APIs, so its tests run anywhere; `Pounce.Launcher` is the thin WPF shell and only runs on Windows. [`windows-app/README.md`](windows-app/README.md) describes the layout and how to try a build on Windows; [`windows-app/docs/panel-test-plan.md`](windows-app/docs/panel-test-plan.md) lists the manual checks for the search panel.

Both smoke checks need port 7431 free, so quit any running Pounce first.

## How the code is organised

Read these before a larger change:

- [`docs/design/engine-modules.md`](docs/design/engine-modules.md): the Engine's modules, its `Indexer` and `Searcher`, the ports, and the terms used in the code (Document, Passage, Snippet…).
- [`docs/adr/`](docs/adr/): why things are the way they are (Java over Rust, Lucene only, JWT on localhost, the bundled Engine, the onion layers, the pure-Java tokenizer, the WPF launcher, the Engine's lifetime on Windows). If your change reverses one of these, add a new decision record explaining why.
- [`docs/research/`](docs/research/): measured speed, memory and library facts.

The Engine is an onion of Maven modules: `pounce-domain` → `pounce-application` → `pounce-infrastructure` → `pounce-api`. Dependencies only point inward; the compiler and the ArchUnit tests in `pounce-api` enforce it. Spring, Lucene, ONNX Runtime and PDFBox stay in their outer modules. `pounce-platform` (the Pounce home and the default Watched Folders on each OS) uses only the JDK and is shared with the CLI; the domain and application layers may not use it. The `pounce-cli` and the launchers (`macos-app/`, `windows-app/`) talk to the Engine over HTTP only.

## Rules for changes

- **Tests first.** Behaviour is tested through the public interfaces (`Indexer` and `Searcher`, the REST API, each launcher's `SearchModel`), with real temporary folders and a real in-memory Lucene index rather than mocks. Put a failing test up before the fix.
- **Nothing leaves the computer.** On every system, the Engine, the launchers and the CLI make no network calls except to the Engine on `127.0.0.1`, and pull requests that add telemetry, analytics or cloud services won't be merged. That includes libraries that report usage on their own.
- **Measure performance claims.** If a change is meant to be faster or lighter, include before/after numbers and how you measured them.
- **Keep the ranking honest.** Changes to ranking must keep `RankingTest` green; add labelled queries for the case you're improving.
- **Small, focused pull requests.** One change per pull request is much easier to review than a bundle.
- **Code style:** follow the surrounding code. Java uses the standard conventions with 4-space indents; Swift follows the Swift API Design Guidelines; C# follows `dotnet format`, which CI checks.

## Pull requests

1. Fork the repository and create a branch from `main` (`fix/pdf-snippets`, `feature/settings-window`).
2. Make your change with its tests, and run `mvn verify` and the tests of any launcher you changed (`swift test` or `dotnet test`) locally.
3. Add a line to the `## [Unreleased]` section of [`CHANGELOG.md`](CHANGELOG.md) for anything a user would notice.
4. Open the pull request and fill in the template. CI runs the Engine tests on macOS, Windows and Linux, the macOS launcher's tests, and the Windows launcher's tests, formatting check, build and smoke check; it must be green before review. First-time contributors' CI runs start after a maintainer approves them.
5. A maintainer reviews it. Expect questions; once it's approved, the maintainer merges it (usually squashed into one commit).

## Commit messages

Write the subject as a short summary of the change in the imperative ("Skip unchanged files on restart"), and use the body to explain why, not what.

## Security

Please don't open public issues for security problems. See [SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
