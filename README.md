# IgnitionSwitch

A standalone music app for the Novation Launchkey 37 MK3, built with C# and WinUI 3. Play built-in instruments, record an idea, and layer it into a four-bar loop without setting up another music application.

This is an early working foundation. The Launchkey 37 MK3 comes first; hardware integration sits behind shared contracts so other controllers can gain their own adapters later.

## The first workspace

- Four tracks: velvet keys, round bass, soft atmosphere, and synthesized drums.
- Velocity-sensitive playing, sustain, pitch bend, a metronome, and quantized loop recording.
- Track gain, mute, master volume, and tempo controls.
- A focused loop view, sixteen on-screen drum pads, and resize handles for both workspace splits.
- Play/pause that preserves position, with a separate stop/rewind action.
- Matching hardware/application drum-pad hit feedback and bounded Undo/Redo for recording and mixer edits.
- Retrospective Capture MIDI for recent playing across all four tracks.
- Save and reopen projects as `.ignition.json` files.
- Launchkey discovery, verified model identification, mapped transport and session controls, pad feedback, and LCD track labels.

The initial engine has four tracks, four bars, 60–180 BPM, 64 simultaneous voices, and up to 2,048 recorded notes per track. VST hosting, audio import/export, a song arranger, aftertouch synthesis, and synchronization with the hardware arpeggiator's MIDI clock are future work.

## Build and run

Develop on Windows x64 with the .NET SDK selected by [global.json](global.json), currently **10.0.402** with patch roll-forward. Visual Studio 2026 with the WinUI application development workload is the recommended IDE setup. A working Windows default audio output is required to hear the instruments.

Open **IgnitionSwitch.slnx**. Solution Explorer groups the three application projects under `src` and the three executable check projects under `tests`. Select **IgnitionSwitch.App** as the startup project and **x64** as the platform.

Import [.vsconfig](.vsconfig) through Visual Studio Installer to install the WinUI workload, Windows SDK 26100, and MSVC x64/x86 build tools. With the pinned WinUI SDK, the Visual Studio XAML task discovers MSVC tools even for this C# application. If `GetLatestMSVCVersion` reports a missing `VC\Tools\MSVC` folder, add **MSVC Build Tools for x64/x86** in Individual components, then reopen the solution. The Visual C++ Redistributable alone does not supply those build tools.

From PowerShell in the repository:

```powershell
dotnet build IgnitionSwitch.slnx -c Release
dotnet run --project src/IgnitionSwitch.App -c Release -p:Platform=x64 --no-build
```

The Windows App SDK and application dependencies are pinned in the project files and package lock files. The app builds with its Windows App SDK runtime included.

## Play

Connect the Launchkey by USB, select a track, and press **Record** or **R** to capture a phrase. Press Record again to keep it looping; select another track to add a layer. Drum mode plays the drum track. Recording uses a sixteenth-note grid by default; turn it off in **Loop options** to preserve your timing. The recording preference lasts for the current app run.

Press hardware **Quantise** or choose **Loop options → Quantise selected track** to align that track's note starts. Note lengths keep their expression, and Undo restores the original timing.

Forgot to press Record? Use hardware **Capture MIDI** or **Loop options → Capture recent playing** to add the latest four-bar phrase to the loop. Capture keeps track assignments, held notes and sustain, and the whole action has one Undo step. The captured notes join existing content; saved projects contain the result.

**Space** plays/pauses at the current position; **Esc** stops and rewinds. Pause ends recording and keeps live playing available. Choose **Save** to store the session. Connection/audio details are in **Devices**, and **?** opens shortcut help.

Use hardware **Loop**, or **Loop options → Repeat loop**, to switch between repeating playback and a single pass. A single pass finishes at the end of the four bars and keeps held live notes playable. Play or Record from **End** starts a fresh pass. Repeat is a preference for the current app run.

Use **Ctrl+Z** / **Ctrl+Shift+Z**, or hardware **Undo** / **Shift+Undo**, to reverse or restore recording parts and mixer edits. Drum hits flash the matching pad on the controller and in the app. The [production-polish goal](docs/production_polish.md) tracks the remaining controls and verification.

Drag the handle beside Tracks to change its width, or the handle between Loop and Drums to change their heights. Tab to a handle and use its arrow keys for keyboard resizing. The layout follows the window size without horizontal scrolling.

The app uses the Windows default audio output. In the supported hardware Device/Volume pot modes, pots 1–4 adjust track gains, pot 5 adjusts master gain, and pot 6 adjusts tempo. The Launchkey sends MIDI and has no sound engine of its own.

The connected Launchkey's model inquiry and firmware **0.2.1.7** were observed during development. Physical playing, pedal behavior, visible hardware feedback, and audio latency still need a hands-on verification pass. See the [living Launchkey reference](docs/devices/novation_launchkey_mk3_37.md) for exact mappings, evidence, and remaining gaps.

## Verify changes

Use Python 3.11–3.14 x64 and install the formatter in a local virtual environment. The requirements file pins the formatter and its dependencies with SHA-256 hashes.

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install --require-hashes -r requirements-dev.txt
.\scripts\verify.ps1 -Python .\.venv\Scripts\python.exe
```

The script restores locked packages, builds the solution, runs all three executable test suites, checks prettyCSharp formatting, and verifies .NET style and analyzer diagnostics at informational severity. Build warnings are treated as errors. These check runners execute through `dotnet run`; their individual cases are listed in each test project's `Program.cs`.

Keep [architecture](docs/architecture.md), the [music engine API coverage report](docs/in_memory_engine/music_api_coverage.md), and affected device references current in the same commit as implementation changes.

Licensed under [MIT](LICENSE).
