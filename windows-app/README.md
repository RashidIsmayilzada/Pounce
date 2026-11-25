# Pounce for Windows

The Windows launcher: a C# WPF app on .NET 10 that talks to the Engine's localhost API, like `macos-app/` does on macOS. Why WPF: [ADR 0007](../docs/adr/0007-windows-launcher-in-wpf.md). How it stops the Engine: [ADR 0008](../docs/adr/0008-engine-stops-itself-on-windows.md).

| Project | What it holds |
|---|---|
| `src/Pounce.Core` | Testable logic. Plain `net10.0`, no Windows APIs, so its tests run on Linux too. |
| `src/Pounce.Launcher` | The thin WPF shell (`net10.0-windows`). Builds `Pounce.exe`. |
| `tests/Pounce.Core.Tests` | xUnit v3 tests for `Pounce.Core`. |
| `docs/panel-test-plan.md` | The manual checks for the search panel on Windows. |

You need the .NET 10 SDK (see `global.json`). Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` so the SDK sends nothing. Run everything from this folder.

Linux or WSL (the Launcher is cross-compiled; it only runs on Windows). The SDK needs ICU: install `libicu`, or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet publish src/Pounce.Launcher -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Windows: the same commands in PowerShell, and `dotnet run --project src/Pounce.Launcher` to start it. Started that way, it finds no bundled Engine next to it and starts none: run an Engine yourself, or point `POUNCE_URL` at one. The tray menu then says "Search engine started by hand".

`publish/win-x64` is self-contained: about 155 MB in 252 files, no .NET install needed. Release builds keep English resources only, precompile the app with ReadyToRun and embed the debug symbols. Add `-p:PublishSingleFile=true` for `Pounce.exe` plus 5 native WPF DLLs instead. Trimming is not supported for WPF.

The version is 0.1.0 unless you pass `-p:Version=1.2.3` or set `POUNCE_VERSION`.

The icon, manifest and version info are embedded in `Pounce.exe` on Linux and on Windows alike.

`Pounce.exe` reads three environment variables: `POUNCE_URL` (the Engine's address, `http://127.0.0.1:7431` by default), `POUNCE_HOME` (the data folder, `%LOCALAPPDATA%\Pounce` by default) and `POUNCE_HOTKEY` (the shortcut, such as `control-alt-space`; `alt-space` by default).

## The whole bundle

`scripts/build-windows.sh`, run from the repository root, builds what users download: `Pounce.exe` with the Engine, a trimmed Java runtime, the model and ONNX Runtime's DLLs. It runs on Windows under Git Bash, and on Linux, WSL or macOS by cross-building, and needs JDK 25, Maven, the .NET 10 SDK and curl (and unzip when cross-building).

```text
build/Pounce-windows-x64/Pounce/           the unpacked bundle
  Pounce.exe                               this Launcher, with the .NET runtime beside it
  engine/pounce-engine.jar                 the Engine
  runtime/                                 the trimmed Java runtime
  models/bge-small-en-v1.5/                the embedding model
  native/onnxruntime/                      ONNX Runtime's DLLs
build/Pounce-<version>-windows-x64.zip     the same Pounce folder, zipped, with a .sha256 file
```

## Testing a build on Windows

1. **Get the zip.** Run `scripts/build-windows.sh`; it writes `build/Pounce-<version>-windows-x64.zip` (`Pounce-0.1.0-windows-x64.zip` unless `POUNCE_VERSION` is set). Or download the `Pounce-windows-x64` artifact of a CI run (kept for 7 days); it holds the same zip.
2. **Copy it to a local folder.** From WSL, File Explorer reaches the build folder as `\\wsl.localhost\<distro>\<path to the repository>\build`, for example `\\wsl.localhost\Ubuntu\home\you\Pounce\build`. Copy the zip to a folder on a Windows drive, such as `C:\Users\you\pounce-test`, and unzip it there. Don't run Pounce from the `\\wsl.localhost` path: Windows treats it as a network share.
3. **Start `Pounce\Pounce.exe`.** The build is not signed, so SmartScreen may say "Windows protected your PC": click **More info** → **Run anyway**.
4. **The first 30 to 60 seconds.**
   - The Pounce icon appears in the notification area at once (it may be hidden under the ^ arrow). Its menu shows the shortcut it got and the search engine's state.
   - On the first run Pounce writes `%LOCALAPPDATA%\Pounce\pounce.yml` with your Documents, Desktop and Downloads folders. To test on a small folder instead, choose **Settings file…**, change the list, then **Quit Pounce** and start it again.
   - The Engine needs some time to start (about 30 seconds on a Mac). Until it answers, a search shows "Pounce isn't running at http://127.0.0.1:7431. Start Pounce and try again." Applications are found from the start.
   - Then it indexes the Watched Folders in the background, and file results grow as it goes. `engine.log` says `Index reconciled in … ms` when the first pass is done.
5. **When something is wrong**, look in `%LOCALAPPDATA%\Pounce\logs`:
   - `launcher.log`: the shortcut that was registered, the Engine's process id and exit code, whether the panel got its backdrop, and any exception.
   - `engine.log`: everything the Engine printed, including why it stopped.
   - If the tray menu says "Pounce needs the Microsoft Visual C++ Redistributable (x64)", install that and start Pounce again.
6. **Run the smoke check.** `scripts\smoke-windows.ps1` starts the built `Pounce.exe` on a temporary home and Watched Folder (never your own), waits for a search to find its test file, then checks that `Pounce.exe --quit` and a forced kill each stop the Engine. It expects the repository layout: the script in `scripts\` and the bundle in `build\Pounce-windows-x64\Pounce\` beside it. Run it from a Windows checkout, or copy those two folders to a local folder as they are. Quit any running Pounce first (it needs port 7431), then in PowerShell, from the folder that holds `scripts`:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-windows.ps1
   ```

   It prints an `ok` line per check and ends with `PASS`, or with `FAIL` and the last lines of the Engine log.
7. **Quit.** Choose **Quit Pounce** in the tray icon's menu, or run `Pounce.exe --quit`. No `java.exe` of Pounce should be left in Task Manager. To start over, delete `%LOCALAPPDATA%\Pounce`; if you turned on **Launch at login**, turn it off first.

Then go through [`docs/panel-test-plan.md`](docs/panel-test-plan.md) for the search panel itself.
