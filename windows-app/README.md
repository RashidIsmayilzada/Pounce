# Pounce for Windows

The Windows launcher: a C# WPF app on .NET 10 that talks to the Engine's localhost API, like `macos-app/` does on macOS.

| Project | What it holds |
|---|---|
| `src/Pounce.Core` | Testable logic. Plain `net10.0`, no Windows APIs, so its tests run on Linux too. |
| `src/Pounce.Launcher` | The thin WPF shell (`net10.0-windows`). Builds `Pounce.exe`. |
| `tests/Pounce.Core.Tests` | xUnit v3 tests for `Pounce.Core`. |

You need the .NET 10 SDK (see `global.json`). Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` so the SDK sends nothing. Run everything from this folder.

Linux or WSL (the Launcher is cross-compiled; it only runs on Windows). The SDK needs ICU: install `libicu`, or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet publish src/Pounce.Launcher -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Windows: the same commands in PowerShell, and `dotnet run --project src/Pounce.Launcher` to start it.

`publish/win-x64` is self-contained: about 155 MB in 252 files, no .NET install needed. Release builds keep English resources only, precompile the app with ReadyToRun and embed the debug symbols. Add `-p:PublishSingleFile=true` for `Pounce.exe` plus 5 native WPF DLLs instead. Trimming is not supported for WPF.

The version is 0.1.0 unless you pass `-p:Version=1.2.3` or set `POUNCE_VERSION`.

The icon, manifest and version info are embedded in `Pounce.exe` on Linux and on Windows alike.
