# Music engine API coverage

Last reconciled: **2026-10-26**, initial implementation. This is the IgnitionSwitch music report; reports for other applications belong in separate files. Review supported behavior, evidence, member counts, and remaining gaps with every implementation commit.

## Scope and inventory

The in-memory API is `IgnitionSwitch.Core`. Its implementation has no external package dependencies. Windows MIDI, physical controllers, WinUI, and the NAudio output driver are adapters around it, with separate evidence below.

Counts are taken from the generated Release XML documentation at `src/IgnitionSwitch.Core/bin/x64/Release/net10.0/IgnitionSwitch.Core.xml`. Count distinct documented `T:`, `F:`, `M:`, and `P:` entries. Include declared positional-record constructors/properties and enum values. Exclude internal helpers, implicit default constructors, and generated record equality, hashing, deconstruction, and printing conveniences. These counts inventory the declared API; they are not a test-coverage percentage.

| Public type | Constants/enum values | Constructors/method overloads | Properties | XML entries including type |
| --- | ---: | ---: | ---: | ---: |
| `Domain.InstrumentKind` | 4 | 0 | 0 | 5 |
| `Domain.LoopNote` | 0 | 1 | 4 | 6 |
| `Domain.SessionSnapshot` | 0 | 1 | 5 | 7 |
| `Domain.TrackSnapshot` | 0 | 1 | 5 | 7 |
| `Domain.TrackState` | 0 | 1 | 6 | 8 |
| `Persistence.SessionStore` | 1 | 2 | 0 | 4 |
| `Sequencing.WorkstationEngine` | 3 | 19 | 10 | 33 |
| **Total: 7 types** | **8** | **25** | **30** | **70** |

`WorkstationEngine` therefore declares **32 non-type members**: 19 method overloads, 10 properties, and 3 constants. The full documented Core API has 63 non-type entries plus 7 type entries.

## Implemented behavior and evidence

“Automated” describes executable assertions over implementation behavior. It does not establish physical playing quality, audible device output, or measured latency.

| Area | Status and supported behavior | Evidence | Remaining gaps |
| --- | --- | --- | --- |
| Built-in instruments | Implemented; keys, bass, atmosphere, synthesized drums, velocity, 64-voice limit | `AudioTests.Instruments`: each track produces nonzero finite samples; dense input stays bounded; channels align | Musical/physical listening review; richer presets and effects |
| Note ownership and release | Implemented; selected/explicit-track note methods, sustain, retrigger, velocity-zero release, immediate all-notes-off | `AudioTests.Release`; `StudioSessionTests.Ownership`, `.Sustain` | Physical pedal polarity, rapid scale/chord collisions, simultaneous input sources |
| Pitch bend | Implemented; normalized −1…1 maps to ±2 semitones for melodic voices | `ValidationTests.Controls` rejects invalid bend; formula in `SynthVoice`/`WorkstationEngine` | A frequency-response assertion and physical wheel pass; recorded bend automation |
| Transport and metronome | Implemented; sample-clock playback, stop/rewind, four bars, tempo 60–180 BPM, beat position, click | `SequencingTests.Transport`: stopped clock, exact frame-based advancement, loop wrap, reset, metronome samples | Variable meter/length, pause/resume, external clock, measured timing |
| Loop recording and overdub | Implemented; sixteenth-note onset/duration grid, four-track layering, same-grid/pitch replacement | `SequencingTests.Recording`: quantization, replacement/replay, first-beat drum deduplication, superseded voice release | Undo/history, retrospective capture, editable note grid |
| Loop-boundary duration | Implemented; note durations can extend across loop wrap | `SequencingTests.WrappingNote` checks sound across the boundary and scheduled release | Broader stress/timing measurements |
| Mixer and clear | Implemented; per-track mute/gain, master gain, selected-track clearing | `SequencingTests.Tracks`; finite-output assertions in `AudioTests.Instruments` | Pan, sends, effect processing, solo, automation |
| Session snapshots | Implemented; detached read-only collections, validated apply, version 1 schema | `PersistenceTests.Roundtrip` checks caller mutation and later edits cannot alter stored snapshots; `ValidationTests.Projects` | Schema migrations and variable track/loop capacity |
| JSON persistence | Implemented; 4 MiB file cap, four tracks/four bars, finite/ranged values, bounded names/events, atomic replacement | `ValidationTests.Projects`; `PersistenceTests.Roundtrip` checks roundtrip, cancellation, old-file preservation, temporary-file cleanup | Audio/MIDI export, additional schema versions |
| Input and lifecycle validation | Implemented; invalid track/note/gain/buffer controls rejected, disposal prevents further engine work | `ValidationTests.Controls` | Extended concurrency and long-session stress |
| Revision-aware saves and dialogs | Implemented in App adapter; captured revisions preserve newer unsaved edits, note completion advances content revision, modal operations finalize/block mutations | `StudioSessionTests.Revisions`, `.ModalIsolation` | Full interactive picker/confirmation pass |
| Controller feedback and shutdown | Implemented in App adapter; mute/name/connection-aware feedback, callback guards and adapter disposal | `StudioSessionTests.Feedback`, `.Shutdown` | Physical unplug/contending-app tests and visible LED/LCD verification |

## Test inventory and physical evidence

The executable runners currently define **9 Core groups**, **7 MIDI groups**, and **6 App groups**. Each group contains multiple assertions. Run the complete build, checks, formatting, and informational analyzer validation with:

```powershell
.\scripts\verify.ps1 -Python .\.venv\Scripts\python.exe
```

On **2026-10-26**, the complete verification script passed locally in Release/x64: **22/22 groups**, a build with **zero warnings and errors**, the pinned prettyCSharp check, and style/analyzer checks including informational diagnostics. This does not establish physical audio quality or complete hardware coverage.

The Core runner checks actual rendered buffers, musical state, and files. The MIDI runner uses pure protocol fixtures and malformed-input cases without opening ports. The App runner compiles the production coordinator with fake boundary adapters. An individual suite passing does not replace the full verification script after a change.

The launched app has observed the connected **Launchkey 37 MK3**, firmware **0.2.1.7**, through its identity handshake. Human-operated keys/buttons/pedal, actual LED/LCD appearance, audible playback through a selected physical output, and latency have not yet been recorded as completed physical checks. A working Windows default output is required for that pass. Detailed mappings and hardware evidence live in [the maintained device reference](../devices/novation_launchkey_mk3_37.md).

## Initial limits and unimplemented areas

| Limit | Current value |
| --- | --- |
| Audio format | 48 kHz, interleaved stereo floating-point samples |
| Tracks/instruments | Four tracks; keys, bass, atmosphere, drums |
| Loop | Four bars, sixteen beats |
| Tempo | 60–180 BPM |
| Quantization | 0.25 beat, a sixteenth note in the current meter |
| Simultaneous voices | 64, with oldest-voice replacement |
| Recorded notes | 2,048 per track |
| Stored note range | MIDI 0–127, velocity 1–127, onset 0 ≤ beat < 16, positive duration ≤16 beats |
| Project file | Version 1 JSON, at most 4 MiB |

No VST hosting, audio import/export, song arranger, undo/redo, synthesized aftertouch/modulation response, MIDI learn, external instrument routing, or project-to-hardware MIDI clock is implemented. Native Launchkey scale/chord/arp features can send ordinary performed notes, but the application does not mirror or synchronize their settings. Saved projects retain finalized notes and mixer state; metronome/selection and continuous pitch/sustain automation are not persisted.
