# On Windows the Engine stops itself when the Launcher exits

On macOS the Launcher stops the Engine with SIGTERM, and Spring shuts it down cleanly. Windows has no such signal for a windowless child process, and killing it (`TerminateProcess`) stops it mid-write, before it has committed its Index. So the Windows Launcher never stops the Engine itself. It starts the Engine with its own process id (`-Dpounce.parent-pid`), and the Engine's `ParentProcessWatch` shuts the Engine down through Spring as soon as that process ends, whether Pounce was quit or killed. On quit, `EngineSupervisor.Stop` only arms a 10-second deadline: the Launcher exits, the deadline dies with it, and the Engine closes on its own. If the Launcher is still running when the deadline passes, it kills the Engine and anything it started, so no Engine is left running.

A clean stop is still not guaranteed on Windows, so the Index no longer relies on one: it is committed at the end of every reconcile and 10 seconds after the first write that is not on disk yet, instead of only when it is closed.

## Consequences

- An Engine that ends abruptly loses at most about 10 seconds of indexing. The next start reads those files again, because their stamps were not committed either.
- A Stop followed by a new start in the same Launcher would find the old Engine on port 7431 for up to 10 seconds. Nothing does that today: Stop is only called on quit.
- `scripts/smoke-windows.ps1` checks both paths: `Pounce.exe --quit` and a forced kill of `Pounce.exe` must each end the Engine.
- The Engine's lifetime on Windows depends on `ParentProcessWatch` working. A Launcher that starts the Engine without `-Dpounce.parent-pid` would leave it running after quit.
