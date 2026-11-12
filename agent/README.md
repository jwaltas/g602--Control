# G602 Control application sources

Current behavior and installation instructions are in ../README.md and
../README-user.txt. Build with ../Build-Prototype.ps1.

G602Control.cs is the editor and tray app. RuntimeContext.cs implements quiet
login startup. RawInputWindow.cs receives mouse events. TapClassifier.cs handles
single/double taps. SequenceRunner.cs and WindowsKeyboardSink.cs handle bounded,
cancellable playback with Rhino foreground checks. ProfileStore.cs and
MacroEditor.cs validate and save macros. StartupSettings.cs handles opt-in login
startup. InstallEngine.cs validates and stages application updates independently
of saved Data. G602DryRun.cs is a historical observer and is not shipped.

Check executables use simulated keyboard output; they do not send real keys.
AppChecks also registers read-only raw input and verifies hidden window behavior.
