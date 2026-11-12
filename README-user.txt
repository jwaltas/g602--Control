G602 CONTROL 0.3.1

Close the old app and use Open G602 Control.exe. Your saved default profile
becomes active automatically after receiver and readiness checks succeed.
Existing mouse setup is reused; no new onboard changes are required. Your
saved macros and timings are preserved on update. New installs use defaults;
export/import your profile to transfer customizations from another computer.

PERMANENT LOCATION AND UPDATES
Install or Update G602 Control.exe installs under:
Documents\Codex\G602-Control
App contains replaceable program files. Data contains your saved profile,
backups, and logs. Updates preserve Data instead of replacing your settings.
Close the running permanent app from its tray menu before applying an update.
The prior program folder is retained for recovery. Windows startup preferences
are not changed by installation or updates. The former prototype folders remain.

PROFILE BACKUP
Advanced -> Export profile saves all single/double macros and both timings to
one JSON file. Advanced -> Import profile validates a file before replacing
the saved default profile. Import keeps a backup of the previous saved profile.
Save or revert unsaved edits before importing/exporting. Profiles do not change
Windows startup, application paths, or the mouse's onboard assignments.

QUIET WINDOWS STARTUP (OPTIONAL)
Advanced -> Start with Windows -> Active — tray only starts the saved profile
silently at login. No editor window or taskbar window is shown. Double-click
the tray icon, choose Open, or use Open G602 Control to edit settings later.
Paused — tray only also starts hidden but waits for Resume profile.
Off disables this app's Windows startup entry. Startup is off until you choose
an option. G HUB must remain closed, as with the existing prototype.

Startup readiness performs only read operations. Brief bounded retries handle
a receiver that is not ready immediately; there is no constant polling at idle.
Startup errors remain in the tray status/logs rather than opening a dialog.

EDITING
Each G4–G9 card has a single-tap field and a double-tap field underneath.
Enter command text and click Save macros. Saved changes become active
automatically; there is no Start button or extra profile selector.
The Enter checkbox adds a final Enter; uncheck it for shortcuts or partial
command input. A newline presses Enter between commands. Blank disables that
action. Only buttons with a double-tap macro delay their single-tap action.

There are no Test buttons, preview playback, or typing countdowns. Macros run
only from the mouse while playback is enabled and Rhino 8 has focus.

Editing temporarily suspends playback until Save or Revert edits. Saving,
reverting, and timing changes automatically return to the saved default
profile unless you explicitly paused it from the tray or emergency shortcut.
Save before closing to keep changes. Revert edits discards unsaved changes. Saved macros
are in Data\default-profile.json, and .bak keeps the previous save. Macros and
timings commit together. Invalid edits do not replace saved settings. Unchanged
original macros retain their exact events. Legacy files remain untouched.

EXAMPLES
DimAligned       command text; Enter submits it when the checkbox is checked
{ESC}Line        Escape followed by Line and Enter
{CTRL+Z}         Ctrl+Z; uncheck Enter for this shortcut
{{ and }}        literal braces

Supported text: English letters, numbers, spaces, common punctuation at US
keyboard positions, and {ENTER}, {ESC}, {TAB}, {BACKSPACE}, {SPACE}, {F1}–{F12}.
Chords use CTRL, SHIFT, ALT, e.g. {CTRL+SHIFT+S}. Unsupported input is rejected.

TIMING
Key pause ranges from 1–25 ms, default 5 ms. Lower values request faster typing;
25 restores the original recorded delays. Shorter original delays stay shorter.
Windows scheduling affects actual speed. Double-tap window ranges from
150–600 ms, default 300 ms, independently of typing speed. Both settings are
saved together in the default profile and take effect automatically.

STOPPING
Ctrl+Alt+Shift+F12 pauses playback and cancels pending tap recognition.
The tray icon offers Pause profile / Resume profile. An explicit pause remains
in effect even when you save edits, until you choose Resume profile.
Focus loss cancels the current sequence and attempts held-key cleanup. Input
during an already running macro is ignored, not queued. The window X hides the editor and keeps macros running. Use tray -> Exit to quit;
minimizing keeps the app in the tray. Startup at login is optional as above.

MOUSE SETUP
Setup and restore are now inside the Advanced menu. Only use Set up mouse
triggers if the app reports setup is required. It writes
the authorized G4–G9 -> F13–F18 assignments, backs up a freshly read profile,
preserves unrelated bytes, and verifies the entire readback. A mismatch or
failed read stops the operation. The app never resets or flashes the mouse.
Restore original buttons is separate and explicit and only accepts the exact
reviewed trigger profile. It is never attempted automatically after an error.
Advanced also offers a read-only Recheck receiver action. Starting the app
never writes to the mouse automatically.

DEFAULT PROFILE
There is one saved default profile. Additional profiles are not included in
this version. The profile activates when you open the app, in Rhino 8 only.
It does not eliminate the need to keep the app running. Critical input/output faults lock playback until
the app is reopened; saving cannot bypass a failed readiness check.

This package targets Windows 11 x64 with its built-in .NET Framework.
Python 3.13.17 is bundled; no separate Python installation is needed.
The bundled HID library is used briefly for profile operations. Normal macro
playback is in the compiled app, with no extra Python process left running.
No new driver, browser runtime, global keyboard suppression, or download is
installed. Logs hold operation results and macro outcomes, not unrelated typing.
Windows input delivery cannot atomically target a window despite the app's
focus checks, so stop playback if anything unexpected occurs.
