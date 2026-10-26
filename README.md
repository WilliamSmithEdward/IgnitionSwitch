# IgnitionSwitch

A standalone music app for the Novation Launchkey 37 MK3, built with C# and WinUI 3. Play built-in instruments, record ideas into clips, and organize them into four-bar scenes without setting up another music application.

This is an early working foundation. The Launchkey 37 MK3 comes first; hardware integration sits behind shared contracts so other controllers can gain their own adapters later.

## The first workspace

- Four tracks: velvet keys, round bass, soft atmosphere, and synthesized drums.
- Velocity-sensitive playing, sustain, pitch bend, a metronome, and quantized loop recording.
- Track gain, mute, stereo pan, master volume, and tempo controls.
- Eight instrument controls, real echo and room sends, and a compact Sound inspector.
- A focused loop view, sixteen on-screen drum pads, and resize handles for both workspace splits.
- Up to eight real scenes, with independent track clips, quantized launches, and selected-track Stop/Solo/Mute actions.
- Play/pause that preserves position, with a separate stop/rewind action.
- Matching hardware/application drum-pad hit feedback and bounded Undo/Redo for recording, mixer and scene edits.
- Retrospective Capture MIDI for recent playing across all four tracks.
- Save and reopen projects as `.ignition.json` files.
- Launchkey discovery, verified model identification, mapped transport and session controls, pad feedback, and LCD track labels.

The engine has four tracks, four bars per clip, 60–180 BPM, 64 simultaneous voices, up to eight scenes, and limits of 2,048 notes per clip and 8,192 notes across the project. VST hosting, audio import/export, a song arranger, aftertouch synthesis, and synchronization with the hardware arpeggiator's MIDI clock are future work.

The scene/clip milestone passes the Release/x64 solution build, 79 automated groups, formatting and informational analyzer checks. Runtime visual checks and physical Launchkey scene tests are still pending; automated checks alone do not establish a verified production workflow.

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

Connect the Launchkey by USB, choose a scene in the Loop header, and select a track. The scene selector chooses what you edit; it does not replace clips already playing. **Launch** starts or queues that scene across all four tracks. Each track card also has its own small Launch button, so clips from different scenes can play together. When recording is off, launching an empty clip stops that track's sequence. Cards show their actual playing, queued or stopping state, and each visible playing clip has its own timeline position.

Press **Record** or **R** to capture a phrase. From stopped playback, recording starts the selected scene. During playback, recording writes to the clips actually playing, which can differ from the scene being edited. Press Record again to finish recording. Drum mode plays the drum track. Recording uses a sixteenth-note grid by default; turn it off in **Loop options** to preserve your timing. The recording preference lasts for the current app run.

Use **Loop options → Scene** to create, duplicate, rename or delete a scene. The last scene cannot be deleted. **Clear selected clip** removes only that track's clip in the scene being edited. The selected-track controls provide track-wide **Mute** and **Solo**, plus a Stop icon for that track's clip; the transport Stop button and **Esc** stop the whole performance. Clip launches and stops use the engine's bar boundaries while playing.

Press hardware **Quantise** or choose **Loop options → Quantise selected clip** to align note starts in the selected track's clip in the scene being edited. Note lengths keep their expression, and Undo restores the original timing.

Forgot to press Record? Use hardware **Capture MIDI** or **Loop options → Capture recent playing** to add the latest four-bar phrase to the scene being edited. Capture keeps track assignments, held notes and sustain, and the whole action has one Undo step. The captured notes join existing content; saved projects contain the result.

**Space** plays/pauses at the current position; **Esc** stops and rewinds. Pause ends recording and keeps live playing available. Choose **Save** to store all scenes and their clips together. Playback queues, monitoring solo and the scene selected for editing are transient rather than saved launch choices. Connection/audio details are in **Devices**, and **?** opens shortcut help.

Use hardware **Loop**, or **Loop options → Repeat loop**, to switch between repeating playback and a single pass. A single pass finishes at the end of the four bars and keeps held live notes playable. Play or Record from **End** starts a fresh pass. Repeat is a preference for the current app run.

Use **Ctrl+Z** / **Ctrl+Shift+Z**, or hardware **Undo** / **Shift+Undo**, to reverse or restore recording parts, mixer changes and scene edits. Drum hits flash the matching pad on the controller and in the app. The [production-polish goal](docs/production_polish.md) tracks the remaining controls and verification.

Drag the handle beside Tracks to change its width, or the handle between Loop and Drums to change their heights. Tab to a handle and use its arrow keys for keyboard resizing. The layout follows the window size without horizontal scrolling.

Open **Sound** to adjust the selected instrument, Echo or Room. Instrument controls are Tone, Attack, Release (Tail for drums), Drive, Pan, Delay send, Reverb send and Level. Echo and Room each have four effect controls followed by the four track sends. These settings are saved with the project; older version 1 projects open with their original dry sound.

The Launchkey's **Device** pots follow the same Sound target. Hold **Device Select** and use ▲/▼ to choose Instrument, Echo or Room; the first top-row page pad is lit because each currently has one page. **Device Lock** retains the actual instrument across track changes. **Volume** maps pots 1–4 to track levels, 5 to master, 6 to tempo, and 7–8 to effect returns. **Pan**, **Send A** and **Send B** map the first four pots to the corresponding track controls. Absolute pots use pickup: turn through the existing value before taking over; the LCD indicates the required direction. Custom modes remain hardware-owned.

The app uses the Windows default audio output. The Launchkey sends MIDI and has no sound engine of its own.

**Devices → Playback mode** offers shared output or an explicit low-latency exclusive option. Exclusive output requests the driver's minimum period and can prevent other apps from playing sound; unsupported/busy devices fall back to shared output. Buffer requests describe configuration, rather than measured key-to-sound latency. **Devices → Input diagnostics** shows received drum hits and velocity statistics for troubleshooting without changing the music.

The connected Launchkey's model inquiry and firmware **0.2.1.7** were observed during development. Shared output is currently selected, and physical knob testing is deferred while intermittent missed hits and perceived delay are investigated. Physical scene/context controls, pedal behavior, recovery and measured audio latency still need a hands-on verification pass. See the [living Launchkey reference](docs/devices/novation_launchkey_mk3_37.md) for exact mappings, evidence, and remaining gaps.

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
