# G602 Control

A small Windows 11 x64 background app for Logitech G602 macros in Rhino 8.
Version 0.3.1 includes its own Python runtime for brief receiver/profile checks.
Normal playback runs in the compiled app without a Python process.

## Install

Download and fully extract the release ZIP. Close any running G602 Control,
then run **Install or Update G602 Control.exe**. Use **Open G602 Control.exe**
to open the permanent installation. No administrator rights or separate Python
installation are required. The package is unsigned; Windows may show an
unknown-publisher warning.

Installation uses Documents/Codex/G602-Control, with separate App and Data
folders. Updates preserve Data and retain the previous App folder. Public
packages contain default macros, never a personal settings database/profile.
Use Advanced -> Export/Import profile to transfer your customized settings.

Keep G HUB closed. If you use the same already-configured mouse, its F13-F18
triggers travel with it. Only use Advanced -> Set up mouse triggers when
needed. Setup accepts only the exact reviewed G602 profile, backs it up before
changing the six bindings, and verifies the complete readback. An unfamiliar
profile stops setup. There is no reset or firmware flashing.

## Features

- G4-G9 single and double tap editors, with explicit Save macros.
- Default single commands: Line, Move, Copy, Dim, ROT, Trim, each with Enter.
- Default G7 double tap: DimAligned followed by Enter.
- Adjustable typing speed and double tap window.
- Playback only with Rhino 8 focused; foreground loss cancels playback.
- Tray pause/resume and Ctrl+Alt+Shift+F12 emergency pause.
- Optional Start with Windows -> Active - tray only, without an editor at login.
- Validated profile import/export and backups on save.

Full instructions: [README-user.txt](README-user.txt).

## Verification

The owner confirmed live macro editing/playback and double tap operation.
On November 12, 2026, the owner also confirmed recovery after sleep, receiver
unplug/reconnect, mouse power cycling, and PC restart on the development PC.
Automated verification covers 32 Python tests, 90 compiled checks, hidden
startup/UI checks and playback simulation using a sanitized receiver capture.
These checks send no real keystrokes or mouse configuration writes.

The bundled runtime is checked for diagnostic/native HID imports. A second-PC
hardware test is still required. Command text assumes US keyboard positions.
Windows cannot atomically guarantee foreground input delivery; stop playback
if you observe unexpected input.

## Build

On Windows 11 x64 with Python 3.13 available for the build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Prototype.ps1
```

The build downloads pinned, SHA-256-verified Python 3.13.17 embeddable and
hidapi 0.15.0 dependencies from python.org/PyPI. It uses the system .NET
Framework compiler and runs checks before producing the final ZIP.
Outputs and caches use sibling outputs/ and work/ folders. No settings.zip
or attached mouse is required to build.

Original importer: `Importer`. Read-only diagnostic: `diagnose.py`.
Guarded explicit setup: `setup_profile.py`. Earlier research is preserved in
[history](docs/INVESTIGATION-HISTORY.md); earlier status statements there are
superseded by this README. G HUB's onboardable flag remains unverified; no
general onboard sequence encoding has been established.
