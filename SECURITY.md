# Security policy

Pounce reads the files on your computer, so security problems matter. Thank you for reporting them responsibly.

## Reporting a vulnerability

**Please don't open a public issue.** Report it privately through GitHub instead: on the repository page go to **Security → Report a vulnerability**. Include what an attacker could do, the steps to reproduce it, and the Pounce version (`pounce --version` or the app's version).

You'll get an answer within a week. Once a fix is released, the report can be made public, with credit to you if you'd like.

## Supported versions

Only the latest release gets security fixes.

## What's in scope

- Another app or user on the same computer reading indexed content through the Engine's API (`127.0.0.1:7431`), for example by getting a token without the bootstrap secret.
- The bootstrap secret or signing key being readable by anyone but the user. On macOS and Linux they should be permission `600` in a Pounce home with permission `700`; on Windows the Pounce home (`%LOCALAPPDATA%\Pounce`) and both files should have an access list with a single entry, for the user.
- Pounce sending any data off the computer.
- Code execution through a crafted file in a Watched Folder (for example a malicious PDF).
