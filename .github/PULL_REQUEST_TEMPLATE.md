## What and why

<!-- What does this change, and why? Link the issue: "Fixes #123". -->

## How it's tested

<!-- Which tests did you add or change? For speed or memory changes, include before/after numbers. -->

## Checklist

- [ ] `mvn verify` passes
- [ ] `(cd macos-app && swift test)` passes (if the macOS Launcher changed)
- [ ] `(cd windows-app && dotnet test)` passes (if the Windows Launcher changed)
- [ ] Tests were written for the change
- [ ] `CHANGELOG.md` has a line under `## [Unreleased]` (if users would notice)
- [ ] Nothing is sent off the computer
