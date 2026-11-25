# Search panel: manual test plan

The panel (`src/Pounce.Launcher/LauncherWindow.*`) was written and built on Linux; it had not run on Windows when this plan was written. These are the checks to do on Windows, with what the right result looks like. Do them on Windows 11 22H2 or later first, then the fallback checks on Windows 10 or Windows 11 21H2 if you have one.

Before you start: run `Pounce.exe` with an Engine running (the bundled one, or `POUNCE_URL` pointing at one) and a few hundred indexed files. [Testing a build on Windows](../README.md#testing-a-build-on-windows) says how to get there. Keep `%LOCALAPPDATA%\Pounce\logs\launcher.log` open; the panel writes one line at startup saying whether it got the acrylic backdrop (`Panel: acrylic backdrop`) or not (`Panel: opaque background (...)`, with the reason).

## 1. First show

| Do | Right |
|---|---|
| Start Pounce, wait two seconds, press Alt+Space. | The panel appears at once (no fade or zoom), 680 px wide at 100 % scaling, only the search bar (56 px high). Horizontally centred on the monitor with the mouse, its top edge a quarter of the way down the work area. |
| Look at it. | Rounded corners, a soft shadow, a thin border. Behind the panel the desktop shows through, blurred (acrylic), under a light or dark tint. A search glyph on the left, "Search apps and files" in grey. |
| Type straight away, without clicking. | The letters go into the search field. The caret was already there. |
| Check the taskbar and Alt+Tab while the panel is open. | No Pounce button in the taskbar, no Pounce entry in Alt+Tab. |
| Press Alt+Space within two seconds of starting Pounce. | Still appears and takes the keys (it may take a little longer the very first time). It never shows the warm-up's sample rows ("Pounce" with "Open", "Pounce warm-up.txt"), not even for a frame. |
| Start Pounce, wait two seconds, open the panel and type one letter. | The first results come as fast as they do later on: no pause on the first letter while row templates or icons are set up (Pounce builds a sample row of each kind off-screen at startup). With the bundled Engine, wait for "Engine ready" in the log first (section 12). |

## 2. Typing and results

| Do | Right |
|---|---|
| Type a word that matches an app and some files, at full speed. | Results change with every letter, without visible lag; the letters never stall. Applications first under "Applications", then files under "Files". |
| Type a word that matches only files. | No section titles at all; just file rows. |
| Watch a file row. | 32 px icon, the name in semibold, the path in small grey text (relative to your user folder, e.g. `Documents\notes\a.md`, cut off with "…" at the end when long), then up to two lines of the matching passage with the Query's words in bold, darker text. |
| Watch the rows as you pause typing. | When the refined results arrive a few ms later, rows change in place. No blink, no rows disappearing and coming back. |
| Type a word that matches many files letter by letter at normal speed, watching the panel's bottom edge (film it if you can). | The panel feels steady: while you type, its bottom edge stays put or moves down, and never jumps up toward the search bar and back between letters. The first results for each letter may be fewer than the rows before; then the rows of the previous letters disappear at once and an empty band of the panel's background shows below the remaining rows for a moment, until the full results fill it. Rows that belong to an earlier letter never stay on screen next to the new ones once the new results have started to arrive. |
| Stop typing on a word with few results, after one with many. | Within a moment of the last results arriving, the panel shrinks to fit its rows. |
| Watch the right end of the search bar while typing normally. | No spinner. It only appears when a search takes longer than about 150 ms (stop the Engine's model, or search over a very large index, to see it). |
| Type something that matches nothing. | "No results for “…”" in grey instead of rows. |
| Stop the Engine and type. | A grey error message instead of file rows (matching apps still show); the panel keeps working. |
| Type until there are 3 apps and 8 files, on a small screen (1366×768 at 100 %). | The panel grows downward only; its top edge stays put. It stops above the bottom of the screen and the results scroll. |
| Delete everything with Backspace. | The panel keeps its height while letters remain and shrinks back to the search bar once the field is empty. |

## 3. Selection and keyboard

| Do | Right |
|---|---|
| With results, press ↓ and ↑. | The highlighted row (rounded, tinted in your accent colour) moves; the caret stays in the search field. It stops at the first and the last row. On an app row, "Open" shows at the right. |
| Move the mouse over the rows. | Nothing changes: hovering does not move the highlight. |
| Press Tab and Shift+Tab. | Nothing happens; the caret stays in the field. |
| Press Home, End, Ctrl+A, Ctrl+Backspace, Ctrl+←, Shift+→, Ctrl+Z, Ctrl+V. | They edit the text as in any text field. |
| Type with an input method (Japanese IME, or Win+. for emoji) and press Enter to commit. | Enter commits the composition; it does not open a result. |
| Press Alt+F4. | The panel hides (Pounce keeps running in the tray). Alt+Space shows it again. |

## 4. Open and reveal

| Do | Right |
|---|---|
| Select an app row and press Enter. | The panel disappears, the app starts and comes to the front with the keyboard focus (not just a flashing taskbar button). |
| Select a file row and press Enter. | The panel disappears and the file opens in its default app, in front. |
| Select a file row and press Ctrl+Enter. | The panel disappears and File Explorer opens with the file selected, in front. |
| Click a row. | Opens it, as Enter would. Pressing on one row and releasing on another opens nothing. |
| Press Alt+Space again after opening something, and again after closing with Esc. Watch closely, or film the screen. | The panel opens empty and small. After Esc there must be no flash of the previous search. After opening an item, a flash of the old search text for a single frame is a known possibility (the panel hides at once there, see the code comments); note whether you see it. |

## 5. Hiding

| Do | Right |
|---|---|
| Press Esc. | The panel hides. Next time it opens empty. |
| Click on another window or the desktop. | The panel hides. |
| Press Alt+Tab, or Win, while the panel is open. | The panel hides. |
| Press Alt+Space while the panel is open. | It hides. |
| Hold Alt+Space down for two seconds. | The panel opens once and stays open; it does not flicker on and off. |
| Right-click the tray icon while the panel is open. | The panel hides and the menu opens. |
| Choose "Search…" in the tray menu. | The panel opens and stays open with the caret in the field (it must not hide again as the menu closes). |
| Start `Pounce.exe` a second time while Pounce runs. | The running Pounce shows its panel, in front, with the caret in the field. If it shows but without the focus, check `launcher.log` for "did not let the panel take the foreground"; clicking the panel must then give it the focus, clicking another window must hide it within a quarter second, and Alt+Space must bring it to the front (not hide it). |
| Open the panel while an app run as administrator (e.g. an elevated Terminal) is in front, and while a hung app ("Not Responding") is in front. | The panel shows; Pounce itself never freezes. If it has no focus, the row above applies. |
| Lock the screen (Win+L) with the panel open, then unlock. | The panel is hidden. |

## 6. Several monitors and scaling

Best with two monitors at different scaling, e.g. a laptop at 150 % and an external one at 100 %, and with a monitor left of or above the main one.

| Do | Right |
|---|---|
| Put the mouse on each monitor in turn and press Alt+Space. | The panel opens on the monitor with the mouse, centred, top edge a quarter down. |
| Compare the panel on both monitors. | Same physical size relative to the text on each (680 "DIP" wide: 680 px at 100 %, 1020 px at 150 %). Text is sharp on both, not blurry or scaled. |
| Open it on one monitor, hide it, move the mouse to the other, open it again. | It appears directly in the right place and size. A one-frame jump at the first show on a monitor with a new scaling is acceptable; anything you can see for longer is a bug. |
| Change a monitor's scaling in Settings while Pounce runs, then open the panel there. | Right size and position. |
| Move the taskbar to the top or the left. | The panel stays inside the work area, a quarter down from the taskbar's edge. |

## 7. Light and dark

| Do | Right |
|---|---|
| Settings → Personalization → Colors → "Choose your app mode": Light, then open the panel. | Light tint, dark text. |
| Switch to Dark while the panel is hidden, then open it. | Dark tint, light text, dark border and shadow. Nothing left in light colours (except a scroll bar, if one shows). |
| Switch the mode while the panel is open. | It follows within a moment. |
| Change the accent colour. | The selected row's tint follows it the next time the panel opens. |
| Turn off Settings → Accessibility → Visual effects → Transparency effects. | The panel turns opaque (solid light or dark), still readable. |
| Turn on a contrast theme. | The panel is readable (it keeps its own colours; contrast themes are not specially handled yet). |

## 8. Fallback look (no backdrop)

On Windows 10, or Windows 11 21H2 (build 22000), over Remote Desktop, or with software rendering (a virtual machine without GPU acceleration):

| Do | Right |
|---|---|
| Open the panel. | An opaque light or dark background, never black and never see-through. Square corners on Windows 10. `launcher.log` says `Panel: opaque background (...)` with the reason: the Windows build, `Remote Desktop session`, or `software rendering (render tier 0)`. |
| Sign in over Remote Desktop, start Pounce there and open the panel. | Opaque, as above, with `Remote Desktop session` in the log. |
| Start Pounce at the machine, then connect to the same session over Remote Desktop and open the panel. | Readable, not black. The look is chosen once at startup, so it stays acrylic here; if it comes out black or garbled, note it (restarting Pounce in the remote session must fix it). |
| Do sections 2 to 5. | Everything works the same. |

## 9. Icons

| Do | Right |
|---|---|
| Search for apps and files you have not searched before. | A neutral grey rounded square for a moment at most, then the real icon. Never another row's icon. |
| Type fast so rows change before icons arrive. | Each row ends up with its own icon. |
| Search for a file whose icon cannot be read (a deleted file still in the index). | The grey square stays; nothing breaks. |

## 10. Accessibility

| Do | Right |
|---|---|
| Turn on Narrator and open the panel. | It reads "Search apps and files, edit". |
| Type and press ↓. | Narrator can read the results list ("Results") and each row by name and location (Caps Lock + arrow keys to explore). |
| Search for something with 8 results, then for something with 2, and explore the list with Narrator. | Narrator finds only the 2 rows and says "1 of 2", "2 of 2": never a row of the earlier search, never "of 8" or "of 11". |

## 11. Failures

`POUNCE_TEST_FAIL_STARTUP` makes the start fail on purpose at its very end, once the Engine, the tray icon, the hotkey and the single-instance lock all exist. Set it in a Command Prompt (`set POUNCE_TEST_FAIL_STARTUP=1`) and start `Pounce.exe` from there; quit any running Pounce first.

| Do | Right |
|---|---|
| Start Pounce with `POUNCE_TEST_FAIL_STARTUP=1`. | Within a few seconds one message box, in front of other windows, with an error icon: "Pounce couldn't start.", the reason ("POUNCE_TEST_FAIL_STARTUP is set, so this start fails on purpose."), and the full path of `launcher.log`. No tray icon stays behind (hover over the tray to be sure). |
| Close the message box, then check Task Manager. | No `Pounce.exe` and no Engine (`java.exe`/`javaw.exe` started by Pounce) left running. `echo %ERRORLEVEL%` after `start /wait Pounce.exe` says 1. |
| Open `launcher.log`. | "Pounce couldn't start" with the exception and its stack trace, after a "Starting Pounce" line. |
| Close that Command Prompt, start Pounce normally. | It starts as usual: the failed start left nothing that blocks it. |
| Start Pounce with both `POUNCE_TEST_FAIL_STARTUP=1` and `POUNCE_NO_DIALOGS=1`. | No message box; the process exits with code 1 at once; the log has the failure and a "No dialog" line with what the box would have said. |
| With Pounce running, run `Pounce.exe --quit`. | No message box ever, whatever happens; Pounce quits; the log has "--quit:" lines from the second process. |
| With Pounce running, start `Pounce.exe` again. | The panel shows (section 5); the log has "Pounce is already running; asking it to show its panel". |

## 12. The first half-minute

The bundled Engine needs about half a minute after Pounce starts it before it answers (it loads its model and opens its index first). The log says `Engine launched; waiting until it answers`, then `Engine ready after 27.4 s` (or whatever it took). Note that number in your report. Quit any running Pounce first, so that Pounce starts its own Engine.

| Do | Right |
|---|---|
| Start Pounce, press Alt+Space about 5 seconds later and type the start of an app's name, letter by letter. | Every letter updates the matching apps at once; the letters never stall. Below the apps, in grey: "Pounce is starting. Your results will appear in a moment.", and the spinner turns at the right of the search bar. Never "Pounce isn't running at …" and never "Can't read … bootstrap-secret". |
| Leave the panel open with the text in it until the Engine is ready. | As soon as `Engine ready` appears in the log (the Engine is asked every one to two seconds), the files for the text in the box appear by themselves, without typing again; the message and the spinner go. |
| Do the same as a first run: quit Pounce, then in a Command Prompt `set POUNCE_HOME=%TEMP%\pounce-first-run` and start `Pounce.exe` from there (a new, empty home, so the Engine has not written its `bootstrap-secret` yet). | The same as above; it takes longer only if the Engine does. Afterwards quit Pounce and delete that folder. |
| While it is starting, type, then delete everything. | The message goes with the text. |
| While it is starting, type, press Esc, and open the panel again after `Engine ready`. | An empty search field; searching works as usual. |
| Start Pounce with the Engine unable to start (rename the `runtime` folder next to `Pounce.exe`), open the panel and type. | No "starting" message: matching apps and, after a moment, "Pounce isn't running at http://127.0.0.1:7431. Start Pounce and try again." The tray menu names the problem. Rename the folder back afterwards. |
| Open the tray menu while it is starting. | It already says "Search engine running" (known: the menu does not tell starting from running yet). |

## When something is wrong

Note the Windows version (`winver`), the scaling of each monitor and what the log says, and send `launcher.log` (and `engine.log` if the Engine is involved) from `%LOCALAPPDATA%\Pounce\logs` with the report.
