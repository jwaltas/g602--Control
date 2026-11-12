Historical investigation notes; earlier status statements are superseded by the root README.

# G602 Control: Windows read-only investigation

## Version 0.2.0: persistent installation and quiet startup

Release packages now contain a checksum-verified installer/updater, a launcher,
App payload and an initial profile seed. The permanent location is
Documents/Codex/G602-Control with separate App and Data directories. Updates
replace only App, retain its previous version, and preserve Data byte-for-byte.
The startup entry is opt-in, per-user, and points to the permanent WinForms
executable with --background; it never invokes a shell or console helper at
login. Both active and paused startup modes are tray-only. The hidden startup
uses ApplicationContext with no MainForm so Windows Forms does not auto-show
the editor. IPC opens an already running tray instance when manually launched.

Saved profiles are atomic Data/default-profile.json bundles containing macro
events, editor text/options, and both timings. ProfileStore migrates legacy
files once without deleting them. Import/export validate the whole bundle and
cannot modify startup preferences, install paths, or hardware assignments.
Save macros remains explicit. Advanced holds backup, startup and folder access.

Tests cover full profile roundtrip, invalid import preservation, timing and
version validation, opt-in startup command quoting, flags, install/update data
preservation, checksums, path traversal, running-app refusal, and unrecognized
directories. UI checks cover hidden launch without taskbar/window flash and
import timing/pause preservation. Actual Windows login remains a user trial;
tests do not write startup registry entries or send live keystrokes.

## Automatic default profile

`G602-Control-Default` removes Start and the main playback/setup buttons.
Successful read-only startup verification activates the saved default profile.
Editing suspends playback; Save/Revert and timing changes resume automatically
unless an explicit tray/emergency pause is latched. ProfileActivation separates
user intent from readiness, dirty edits, setup, shutdown and critical faults.
Saving cannot override explicit pause or readiness failures. Setup/restore and
read-only recheck are in Advanced. The tray is available while the window is
open and offers Pause/Resume. Additional profiles and Windows-login startup
remain out of scope. Startup performs no mouse writes.

## Simplified editor

`G602-Control-Clean` removes all user-facing Test buttons, countdown playback,
and the associated execution path. The freed space widens each macro field.
Playback is now reached only from mouse input while enabled. Save, revert,
timing controls, tap recognition, stop/cancel, and setup/restore remain.
Packaging carries saved user macros, their backup, and timing settings from
the previous editor folder without overwriting that working version. The
automated verification suite remains separate from the product interface.

## Macro editor update

`G602-Control-Editor` provides one card per G4–G9 with stacked single- and
double-tap text fields, per-field Enter options, and timed tests. Save validates
all twelve fields before an atomic user-macros.json replacement; .bak retains
the previous save. Unchanged fields retain their exact original events,
including the original overlapping presses. The base control-macros.json is
not changed by the editor. Startup reloads user-macros.json; invalid saved data
leaves that file untouched and shows defaults with an error message.

Blank fields disable their mapping. Only buttons with a double mapping wait
for the tap window. Editing, saving, or reverting pauses playback and clears
pending taps. Tests save current valid edits and arm a three-second countdown.
Start saves pending valid edits before enabling. Unsaved edits should be saved
before closing; Revert edits reloads the current saved assignments in memory.

The editor supports English command text, multiline Enter, common US keyboard
punctuation, and explicit key tokens such as {ESC}, {TAB}, {CTRL+Z}. Left Ctrl,
Shift and Alt and F1–F12 are mapped in the Windows adapter. Uncheck Enter for
pure shortcuts. Unknown tokens/characters are rejected instead of dropped.
The existing fast typing and G7 double-tap defaults remain; no profile writes
are required for changing macros. The previous working version is preserved.

## Faster typing update

`G602-Control-Fast` caps playback pauses at 5 ms by default, with a 1–25 ms
Key pause control. It wraps the cancellable delay provider, preserving original
macro data, keyboard event ordering, and any shorter original delay. At 25 ms,
the original recorded timings are restored. The double-tap window remains
300 ms by default and is independent of typing speed. Both settings persist
in tap-settings.json; editing either pauses playback and clears pending taps.
No mouse writes or continuous high-resolution timer are needed. Real Windows
wait resolution and Rhino throughput still determine observed playback speed.
The prior confirmed double-tap build remains untouched in its own folder.

## G7 double-tap update

The user confirmed the six original mouse macros and trigger setup work. The
new package `G602-Control-DoubleTap` adds G7 double -> DimAligned+Enter, while
G7 single remains DIM+Enter. A 300 ms default tap window is adjustable from
150–600 ms and saved separately as tap-settings.json. Only assigned double-tap
buttons incur single-tap delay; the other five remain immediate. The new
version reuses the existing trigger profile and leaves the known-working
package intact. Close the old app before launching the new version.

TapClassifier uses monotonic deadlines. The UI arms a timer only while a tap
is pending; it does not poll at idle. Stop, focus changes, settings edits, and
closing clear pending taps. Each pending action retains its original target
window and process. Busy macro input remains ignored, not queued. Tests cover
deadline boundaries, mutually exclusive single/double output, context changes,
triple-tap classification, cancellation, and the full new command encoding.
Actual G7 double-tap behavior still needs the user's Rhino trial.

## Runnable prototype

The project now includes `agent/G602Control.cs`, a compiled Windows app with
all six full macros, timed live tests, receiver input, pause/cancel controls,
foreground-change cancellation, and an explicit six-button setup/restore tool.
The user has approved expanding the G7 test to G4–G9 -> F13–F18. Only these
side-button bindings may change. `diagnose.py` remains strictly read-only;
authorized writes are isolated in `setup_profile.py` and require `--apply`.

Build the app from the repository root:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /platform:x64 /target:winexe /out:../outputs/G602Control.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll agent/G602Control.cs agent/RawInputWindow.cs agent/WindowsKeyboardSink.cs agent/SequenceRunner.cs
python package_prototype.py
```

The package uses this PC's existing Python 3.13 only for profile operations,
with the bundled matching HID library. Normal playback is in the compiled app;
there is no external Python process left running for macros. No auto-start,
new driver, browser runtime, or global keyboard suppression is installed.

Current validation: 32 Python tests; 14 compiled scheduler checks; 14 compiled
Windows-output boundary checks; a rendered UI layout and live passive input
registration smoke check. Real SendInput into Rhino and profile writes/restore
remain untested on hardware because direct HID access is restricted in this
session. The executable is ready for a local trial, not claimed hardware-proven.

Earlier notes below describe prior investigation milestones and the old
dry-run-only observer; they do not describe the new live app's capabilities.

The original `Importer` is retained. New tools neither configure the mouse nor
install a background service. Python 3.10+ is required; HID access uses the
`hidapi` package (not the similarly named `hid` package).

```
python -m pip install -r requirements-diagnostic.txt
python diagnose.py --output hardware-diagnostic.json
python analyze_settings.py path/to/settings.zip --output macro-analysis.json
python assess_onboard.py hardware-diagnostic.json macro-analysis.json --output onboard-assessment.json
python capture_input.py --seconds 40 --output passive-input-capture.json
python analyze_input.py passive-input-capture.json --output passive-input-analysis.json
python -m unittest discover -p "test_*.py" -v
```

For a workspace-local HID library, add `--hid-library path/to/hidapi` to the
diagnostic command. The completed desktop investigation used a CPython 3.13
Windows wheel in the parent workspace's `work/hidapi` directory. No global
Python package or driver was installed. Windows device access must be available
to the process; restricted execution may return an empty device list.

## Hardware access policy

Only receiver VID/PID 046D:C537 and mouse device index 1 are targeted. Root,
feature enumeration, firmware, name, and profile getters are allowlisted.
The legacy 8080 profile operation requires the exact read-mode payload 80 2A 00
and only the three short read-chunk requests; payload-bearing long transfers,
write mode 81, and commit are unavailable. Starting a read transaction selects
a temporary read transfer, not a configuration change. No reset, flash,
diversion, mode switch, or profile save command is implemented. Receiver
firmware uses fixed HID++ 1.0 get-register F1 requests only.

Raw request/reply traces accompany every hardware capture. Missing features or
failed reads must not be interpreted as proof of corruption or no onboard
support. The read profile is the exposed onboard block; active execution mode
and power-cycle persistence are not independently established by this reader.

`analyze_settings.py` reads only the exact settings.db ZIP member, opens a
temporary database copy read-only, records hashes, and preserves original macro
objects and event order. G HUB's onboardable field remains unverified metadata.
User settings and capture files are not included in Git by default by this work.

`assess_onboard.py` works entirely offline against the saved hardware capture
and analyzed macros. It decodes fixed binding slots conservatively, retains
unknown values and all original macro events, and never treats an onboardable
flag or presence of feature 8100 alone as proof that a sequence can be stored.

## Follow-up capability decision

Logitech's G602-specific onboard configuration documentation offers mouse
functions and keystrokes with modifiers, not multi-key playback. Combined with
the captured legacy interface, this establishes that the documented G602
onboard configuration path does not meet the six sequence requirements.
No verified alternative sequence format was found. Undocumented firmware
capabilities remain unproven; speculative writes are not a suitable next test.

The libratbag onboard macro PR #1850 was tested on the G604 and uses modern
profile sectors. Its success cannot be transferred to this G602, which lacks
8100. The G602-specific PR #1837 concerns legacy DPI/profile support and does
not establish sequence support. G HUB's internal type string SEQUENCE also
must not be equated automatically with its similarly named UI macro mode.

Next engineering step: a passive Windows input capture scoped to this receiver
to determine whether all six physical inputs can be distinguished while
Logitech software is closed. No replacement runtime should be claimed usable
until input identity, suppression of fallback keys, exact sequence playback,
and Rhino 8 behavior have been verified. Such a capture does not require
profile writes, diversion, or firmware changes.

## Evidence and limitations

A compiled Windows dry-run handler now lives in `agent/G602DryRun.cs`; see
`agent/README.md`. It replays the saved physical capture into six correct macro
candidates and preserves the original key event order. It has no playback API.
The live smoke run verifies registration and clean timed exit only. Exact G HUB
default-delay semantics and suppression/remapping are unresolved. Correction to
the earlier timing summary: LINE has defaultDelay=50 and stored 1 ms delays;
the other macros have defaultDelay=25. Both interpretations are retained.

Follow-up passive input test: the receiver emitted six distinct report-80
button masks (0008, 0010, 0020, 0040, 0080, 0100) in the requested G4–G9
order, with releases. No device reconfiguration was needed. Physical labels
are order-based candidates. The bounded recorder exited after 40 seconds.
Raw Input cannot by itself suppress the fallback keys in another application;
safe device-specific suppression and Rhino playback remain unverified. This
new result establishes input availability for a native fallback investigation.

The Windows hardware run on November 12, 2026 read mouse firmware
RQM 47.00.B0010, receiver firmware 34.00.B0002, 22 non-root HID++ features,
and the legacy 45-byte onboard profile. Feature 8080 is present; 8100 is absent.
Rhino.exe was running and no lghub/LCore process appeared in the capture.
This was a protocol diagnostic, not a live button playback test in Rhino.

The uploaded settings contain six sequences: G4 LINE+Enter, G5 MOVE+Enter,
G6 COPY+Enter, G7 DIM+Enter, G8 ROT+Enter, G9 TRIM+Enter. LINE and MOVE contain
overlapping key-down events; those were preserved. Only a desktop default
profile was present, not a separate Rhino-specific application profile.

The captured onboard block contains fixed mouse/action bindings and three
single-key bindings for 1/2/3, rather than these six sequences. The known
legacy format provides no verified sequence encoding. This supports continued
research, not a categorical claim that all firmware macro execution is
impossible. The later passive test found six distinct input candidates without
device writes. No service, startup task, or replacement runtime exists.

## Protocol references

- [libratbag G602 identity and device index](https://github.com/libratbag/libratbag/blob/master/data/devices/logitech-g602.device)
- [libratbag HID++ profile getters](https://github.com/libratbag/libratbag/blob/master/src/hidpp20.c)
- [Solaar firmware readers](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)
- [Solaar receiver firmware reader](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py)
- [G602 legacy read transaction and layout research](https://github.com/shelterx/g602-onboard-memory-recovery/blob/master/PROTOCOL.md)
- [Logitech G602-specific onboard assignments](https://support.logi.com/hc/en-ch/articles/360023258354-Configuring-G602-gaming-mouse-on-board-memory)
- [G602 legacy DPI/profile work](https://github.com/libratbag/libratbag/pull/1837)
- [Modern macro work tested on G604](https://github.com/libratbag/libratbag/pull/1850)

The last project's recovery program was reviewed as source only, never executed.
Its physical-button labels are inconsistent, so captured binding slots are
reported by index; physical mapping needs independent validation.
