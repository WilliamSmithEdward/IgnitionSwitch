# Music engine API coverage

Last reconciled: **2026-10-26**, responsive input and incremental sequencing update. This is the IgnitionSwitch music report; reports for other applications belong in separate files. Review supported behavior, evidence, member counts, and remaining gaps with every implementation commit.

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
| `Sequencing.WorkstationEngine` | 3 | 20 | 11 | 35 |
| **Total: 7 types** | **8** | **26** | **31** | **72** |

`WorkstationEngine` therefore declares **34 non-type members**: 20 method overloads, 11 properties, and 3 constants. The full documented Core API has 65 non-type entries plus 7 type entries. `NoteRevision` tracks recorded note-content changes independently of mixer and transport state.

## Implemented behavior and evidence

“Automated” describes executable assertions over implementation behavior. It does not establish physical playing quality, audible device output, or measured latency.

| Area | Status and supported behavior | Evidence | Remaining gaps |
| --- | --- | --- | --- |
| Built-in instruments | Implemented; keys, bass, atmosphere, synthesized drums, velocity, 64-voice limit | `AudioTests.Instruments`: each track produces nonzero finite samples; dense input stays bounded; channels align | Musical/physical listening review; richer presets and effects |
| Note ownership and release | Implemented; selected/explicit-track note methods, sustain, retrigger, velocity-zero release, 4 ms minimum smooth onset for short taps arriving between render callbacks, immediate all-notes-off | `AudioTests.Release` checks all three melodic instruments, pre-render/early releases, ordinary tails, retriggers, and panic; `StudioSessionTests.Ownership`, `.Sustain` | Physical pedal polarity, rapid scale/chord collisions, simultaneous input sources |
| Pitch bend | Implemented; normalized −1…1 maps to ±2 semitones for melodic voices | `ValidationTests.Controls` rejects invalid bend; formula in `SynthVoice`/`WorkstationEngine` | A frequency-response assertion and physical wheel pass; recorded bend automation |
| Transport and metronome | Implemented; sample-clock playback, pause/resume, stop/rewind, four bars, tempo 60–180 BPM, beat position, click | `SequencingTests.Transport`: stopped clock, exact frame-based advancement, loop wrap, reset, metronome samples; `SequencingTests.Pause`: frozen clock, precise resume, pending boundary onset, stop rewind | Variable meter/length, external clock, measured timing |
| Pause voice/recording policy | Implemented; pause finalizes held recorded durations, disables recording, clears sequenced voices/click, and preserves live playing/sustain. Resume does not chase interrupted loop notes; they retrigger at their next scheduled onset | `SequencingTests.Pause`: live note survives pause and accepts note-off, finalized duration remains unchanged, wrapped note is silent until its next onset and then releases correctly; `StudioSessionTests.GenericPlaybackPauseResume`: UI/mapped hardware equivalence, retained owners/pedal, saved pause revision | Recorded expression automation and broader physical transport/playing combinations |
| Loop recording and overdub | Implemented; sixteenth-note onset/duration grid, four-track layering, same-grid/pitch replacement | `SequencingTests.Recording`: quantization, replacement/replay, first-beat drum deduplication, superseded voice release | Undo/history, retrospective capture, editable note grid |
| Incremental schedule and input capacity | Implemented; preallocated sorted value-type events, binary-search replacement of one changed note pair, stable unrelated IDs, one full rebuild on project apply, single-pass track clearing | `InputStressTests.OrderedInputs`: four-track chord/drum bursts match freshly loaded playback, 1,536 ordered retriggers/pedal release, full 2,048-note track still plays live hits, identical overdubs/mixer changes preserve `NoteRevision` | Note-on voice/record objects still allocate; broader sustained-load and physical-input measurements |
| Render slices and audio buffer policy | Implemented; 64-frame engine critical sections, driver-default-period shared/event output request, 30 ms compatibility fallback, reported playback failure triggers recovery | `AudioTests.RenderPartitions`: callback partition equivalence, metronome/loop continuity, offset guards and next-callback live input; `AudioBufferTests.SharedPeriod`: timing-unit conversion, positive timeouts, bounded fallback | Physical key-to-sound measurement; sub-default shared periods through a future `IAudioClient3` output path; driver glitch/compatibility testing |
| Loop-boundary duration | Implemented; note durations can extend across loop wrap | `SequencingTests.WrappingNote` checks sound across the boundary and scheduled release | Broader stress/timing measurements |
| Mixer and clear | Implemented; per-track mute/gain, master gain, selected-track clearing | `SequencingTests.Tracks`; finite-output assertions in `AudioTests.Instruments` | Pan, sends, effect processing, solo, automation |
| Session snapshots | Implemented; detached read-only collections, validated apply, version 1 schema | `PersistenceTests.Roundtrip` checks caller mutation and later edits cannot alter stored snapshots; `ValidationTests.Projects` | Schema migrations and variable track/loop capacity |
| JSON persistence | Implemented; 4 MiB file cap, four tracks/four bars, finite/ranged values, bounded names/events, atomic replacement | `ValidationTests.Projects`; `PersistenceTests.Roundtrip` checks roundtrip, cancellation, old-file preservation, temporary-file cleanup | Audio/MIDI export, additional schema versions |
| Input and lifecycle validation | Implemented; invalid track/note/gain/buffer controls rejected, disposal prevents further engine work | `ValidationTests.Controls` | Extended concurrency and long-session stress |
| Revision-aware saves and dialogs | Implemented in App adapter; captured revisions preserve newer unsaved edits, note completion advances content revision, modal operations finalize/block project mutations while preserving live performance, project replacement resets owners atomically | `StudioSessionTests.Revisions`, `.ModalIsolation`; `InputReliabilityTests.ProjectReplacement` | Full interactive picker/confirmation pass |
| Controller feedback and shutdown | Implemented in App adapter; mute/name/connection-aware feedback, callback guards and adapter disposal | `StudioSessionTests.Feedback`, `.Shutdown` | Physical unplug/contending-app tests and visible LED/LCD verification |

## Test inventory and physical evidence

The executable runners currently define **12 Core groups**, **7 MIDI groups**, and **11 App groups**: **30 groups** in total. Each group contains multiple assertions. Run the complete build, checks, formatting, and informational analyzer validation with:

```powershell
.\scripts\verify.ps1 -Python .\.venv\Scripts\python.exe
```

The combined change passes **12/12 Core**, **7/7 MIDI**, and **11/11 App groups** in Release/x64. Automated checks do not establish physical audio quality or complete hardware coverage.

The local formatter runtime is Python 3.12. GitHub CI pins Python **3.13.17**, whose Windows x64 artifact is listed in the Actions runtime manifest. This tooling pin does not change the API inventory or supported music behavior.

The Core runner checks actual rendered buffers, musical state, and files. The MIDI runner uses pure protocol fixtures and malformed-input cases without opening ports. The App runner compiles the production coordinator with fake boundary adapters. An individual suite passing does not replace the full verification script after a change.

The launched app has observed the connected **Launchkey 37 MK3**, firmware **0.2.1.7**, through its identity handshake. A UI-driven session recorded one keyboard note and one drum-pad hit into the four-bar loop. After restoring a Windows default physical output, the app recovered playback; its process-specific WASAPI session meter showed active, nonzero output, reaching approximately **0.07** over a **12-second** observation window. This is output-path evidence, not a latency measurement or a human listening assessment.

Human-operated hardware keys/buttons/pedal, actual LED/LCD appearance, subjective audible quality, and latency have not yet been recorded as completed physical checks. A working Windows default output is required for that pass. Detailed mappings and hardware evidence live in [the maintained device reference](../devices/novation_launchkey_mk3_37.md).

## Responsiveness and measured compute cost

Performance MIDI reaches the engine from the MIDI callback, independently of the UI refresh timer. Rendering releases its state lock after at most 64 frames, representing 1.33 ms of musical time at 48 kHz. This is a work-slice size, not a bound on thread scheduling or physical latency. Recorded note edits update only their scheduled event pair; they no longer allocate/sort a complete project schedule for every input. `NoteRevision` lets presentation code refresh note geometry only when musical content changes.

Output remains shared, event-driven WASAPI using the pinned NAudio version. It requests the reported shared default device period, rounded up to a positive whole millisecond; initialization/reportable playback errors can select the established 30 ms compatibility request. Device invalidation retries the preferred request. A read-only development-endpoint query reported a **10 ms shared default**, so the normal request changes from 30 ms to **10 ms**. Its separate 3 ms minimum is an exclusive-mode capability and is not used. These are requested buffer durations; neither is a measured MIDI-to-speaker delay. [Microsoft device-period semantics](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getdeviceperiod), [pinned NAudio WASAPI implementation](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiOut.cs).

At commit `d00c464`, a short isolated Release/x64 benchmark on the development host (.NET 10.0.13, 16 logical processors) warmed each fixed-voice engine for 128 callbacks and measured 512 callbacks of 480 stereo frames. It opened no audio stream. All sampled output remained finite. These figures precede the subsequent short-tap release correction.

| Active voices | Mean render CPU time per 10 ms audio callback | 95th percentile | Allocated bytes across 512 measured callbacks |
| --- | ---: | ---: | ---: |
| 1 | 0.0249 ms | 0.0255 ms | 0 |
| 16 | 0.2668 ms | 0.2879 ms | 24 |
| 64 | 1.0384 ms | 1.0672 ms | 0 |

At the full 8,192-note/16,384-event project capacity, 512 incremental recorded drum replacements averaged **0.0304 ms CPU time** and **176 allocated bytes per update**. All tracks remained within capacity. These host-specific compute/allocation observations exclude physical MIDI transport, Windows/device buffering, audible onset, and glitch testing; they are not timing guarantees. Fixed-voice render measurements also exclude allocations when new voices are created.

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
