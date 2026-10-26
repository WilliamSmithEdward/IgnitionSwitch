# Music engine API coverage

Last reconciled: **2026-10-26**, selected-track Quantise and optional recording-grid milestone. This is the IgnitionSwitch music report; reports for other applications belong in separate files. Review supported behavior, evidence, member counts, and remaining gaps with every implementation commit.

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
| `Sequencing.WorkstationEngine` | 3 | 24 | 14 | 42 |
| **Total: 7 types** | **8** | **30** | **34** | **79** |

`WorkstationEngine` therefore declares **41 non-type members**: 24 method overloads, 14 properties, and 3 constants. The full documented Core API has 72 non-type entries plus 7 type entries. `NoteRevision` tracks recorded note-content changes independently of mixer and transport state. The history API adds `CanUndo`, `CanRedo`, `Undo()`, `Redo()` and `CommitEdit()`. Quantization adds `RecordingQuantizationEnabled` and `QuantizeSelectedTrack()`.

Undo history is transient and does not change the version 1 JSON schema. Pad presentation and hardware feedback remain adapters around the same engine; animation and MIDI output do not drive sound generation.

## Implemented behavior and evidence

“Automated” describes executable assertions over implementation behavior. It does not establish physical playing quality, audible device output, or measured latency.

| Area | Status and supported behavior | Evidence | Remaining gaps |
| --- | --- | --- | --- |
| Built-in instruments | Implemented; keys, bass, atmosphere, synthesized drums, velocity, 64-voice limit | `AudioTests.Instruments`: each track produces nonzero finite samples; dense input stays bounded; channels align | Musical/physical listening review; richer presets and effects |
| Note ownership and release | Implemented; selected/explicit-track note methods, sustain, retrigger, velocity-zero release, 4 ms minimum smooth onset for short taps arriving between render callbacks, immediate all-notes-off | `AudioTests.Release` checks all three melodic instruments, pre-render/early releases, ordinary tails, retriggers, and panic; `StudioSessionTests.Ownership`, `.Sustain` | Physical pedal polarity, rapid scale/chord collisions, simultaneous input sources |
| Pitch bend | Implemented; normalized −1…1 maps to ±2 semitones for melodic voices | `ValidationTests.Controls` rejects invalid bend; formula in `SynthVoice`/`WorkstationEngine` | A frequency-response assertion and physical wheel pass; recorded bend automation |
| Transport and metronome | Implemented; sample-clock playback, pause/resume, stop/rewind, four bars, tempo 60–180 BPM, beat position, click | `SequencingTests.Transport`: stopped clock, exact frame-based advancement, loop wrap, reset, metronome samples; `SequencingTests.Pause`: frozen clock, precise resume, pending boundary onset, stop rewind | Variable meter/length, external clock, measured timing |
| Pause voice/recording policy | Implemented; pause finalizes held recorded durations, disables recording, clears sequenced voices/click, and preserves live playing/sustain. Resume does not chase interrupted loop notes; they retrigger at their next scheduled onset | `SequencingTests.Pause`: live note survives pause and accepts note-off, finalized duration remains unchanged, wrapped note is silent until its next onset and then releases correctly; `StudioSessionTests.GenericPlaybackPauseResume`: UI/mapped hardware equivalence, retained owners/pedal, saved pause revision | Recorded expression automation and broader physical transport/playing combinations |
| Loop recording and overdub | Implemented; optional sixteenth-note recording grid (on by default), expressive raw timing when off, four-track layering and same-onset/pitch replacement | `SequencingTests.Recording` and `QuantizationTests.RecordingPolicy`: grid/raw timing, per-note policy, replacement/replay, first-beat drum deduplication and superseded voice release | Retrospective capture and editable note grid |
| Selected-track Quantise | Implemented; explicit sixteenth-note onset correction, forward midpoint/wrap policy, expressive duration retention, deterministic same-pitch collision handling, one bounded undo step and no-op suppression | `QuantizationTests.SelectedTrack`; `QuantizeCommandTests.Commands` covers hardware/UI equivalence, revision and modal guards | Physical Quantise pass and musical review |
| Undo and Redo | Implemented; bounded delta history, recording-part groups split by mixer/clear edits, continuous scalar gesture coalescing, load resets, retained beat/live ownership, incremental note schedule restoration | `HistoryTests.Notes`, `.MixerAndLifecycle`, `.Bounds`; `PerformanceFeedbackTests.HistoryCommands` checks UI/hardware and modal/revision behavior | Interactive/physical Undo and Shift+Undo pass; user-facing history descriptions |
| Incremental schedule and input capacity | Implemented; preallocated sorted value-type events, binary-search replacement of one changed note pair, stable unrelated IDs, one full rebuild on project apply or a bulk Quantise edit, single-pass track clearing | `InputStressTests.OrderedInputs`: four-track chord/drum bursts match freshly loaded playback, 1,536 ordered retriggers/pedal release, full 2,048-note track still plays live hits, identical overdubs/mixer changes preserve `NoteRevision` | Note-on voice/record objects still allocate; broader sustained-load and physical-input measurements |
| Render slices and audio buffer policy | Implemented; 64-frame engine critical sections, driver-default-period shared/event output request, 30 ms compatibility fallback, reported playback failure triggers recovery | `AudioTests.RenderPartitions`: callback partition equivalence, metronome/loop continuity, offset guards and next-callback live input; `AudioBufferTests.SharedPeriod`: timing-unit conversion, positive timeouts, bounded fallback | Physical key-to-sound measurement; sub-default shared periods through a future `IAudioClient3` output path; driver glitch/compatibility testing |
| Loop-boundary duration | Implemented; note durations can extend across loop wrap | `SequencingTests.WrappingNote` checks sound across the boundary and scheduled release | Broader stress/timing measurements |
| Mixer and clear | Implemented; per-track mute/gain, master gain, selected-track clearing | `SequencingTests.Tracks`; finite-output assertions in `AudioTests.Instruments` | Pan, sends, effect processing, solo, automation |
| Mixer presentation | Implemented in App adapter; shared 100 ms cubic visual easing for all five volume controls, immediate native target values, coalesced hardware feedback on the UI tick, direct manipulation and reduced-motion behavior | `SliderMotion` and `MainWindow`; running-app checks covered native target updates, master zero/full endpoints, track gain changes, pointer takeover and pane resizing with aligned thumb/fill | Physical fast-pot-turn, keyboard takeover and reduced-motion verification on additional display configurations; screenshots do not measure animation cadence |
| Pad hit feedback | Implemented in adapters; note-addressed physical layout, velocity-sensitive app flash, minimum 80 ms hit visibility, held/retrigger/release handling, 120 ms app fade, coalesced worker-only hardware output and mode-aware base colors | `PadFeedbackTests.Layout`, `.Hits`, `.Context`; `PerformanceFeedbackTests.Activity` checks between-frame taps, 3,000 concurrent mailbox hits, matching UI/hardware requests and Stop reset; running build `8d64d95` displayed the Kick flash and return, aligned shortcut columns and all sixteen pads within a 1,348 × 834 logical window | Physical white-hit/restore and matching app-pad confirmation; reduced-motion and sustained-load visual pass |
| Session snapshots | Implemented; detached read-only collections, validated apply, version 1 schema | `PersistenceTests.Roundtrip` checks caller mutation and later edits cannot alter stored snapshots; `ValidationTests.Projects` | Schema migrations and variable track/loop capacity |
| JSON persistence | Implemented; 4 MiB file cap, four tracks/four bars, finite/ranged values, bounded names/events, atomic replacement | `ValidationTests.Projects`; `PersistenceTests.Roundtrip` checks roundtrip, cancellation, old-file preservation, temporary-file cleanup | Audio/MIDI export, additional schema versions |
| Input and lifecycle validation | Implemented; invalid track/note/gain/buffer controls rejected, disposal prevents further engine work | `ValidationTests.Controls` | Extended concurrency and long-session stress |
| Revision-aware saves and dialogs | Implemented in App adapter; captured revisions preserve newer unsaved edits, note completion advances content revision, modal operations finalize/block project mutations while preserving live performance, project replacement resets owners atomically | `StudioSessionTests.Revisions`, `.ModalIsolation`; `InputReliabilityTests.ProjectReplacement` | Full interactive picker/confirmation pass |
| Controller feedback and shutdown | Implemented in App adapter; mute/name/connection-aware feedback, callback guards and adapter disposal | `StudioSessionTests.Feedback`, `.Shutdown` | Physical unplug/contending-app tests and visible LED/LCD verification |
| Track arrows and Drum layout | Implemented in MIDI adapter; observed left/right mapping, mode-aware control-port Drum normalization, retained releases, blue/red Drum LEDs and Session-mode repaint | `ControlMappingTests.Commands`, `PerformanceRoutingTests.Routing`, `ProtocolTests.Feedback`; `StudioSessionTests.Ownership` checks raw-packet navigation boundaries and rendered drum routing. Local physical capture confirmed mode 1 and `99` Drum input on the control pair. On 2026-10-26, William confirmed build `3fe448d` displays blue Drum pads that make drum sounds, and both track-arrow directions are correct | Individual verification of all pads, muted red LEDs, Session-mode repaint, LCD behavior and broader playing quality |

## Test inventory and physical evidence

The executable runners currently define **17 Core groups**, **11 MIDI groups**, and **14 App groups**: **42 groups** in total. Each group contains multiple assertions. Run the complete build, checks, formatting, and informational analyzer validation with:

```powershell
.\scripts\verify.ps1 -Python .\.venv\Scripts\python.exe
```

The combined verification script passed all 42 groups for this milestone, with zero build warnings/errors, clean prettyCSharp formatting and no outstanding style/analyzer diagnostics at informational severity. Automated checks do not establish physical audio quality or complete hardware coverage.

The running Quantise build was resized separately in width and height to approximately 1,000 × 700 logical pixels. All four tracks, four bars, sixteen pads and the complete status line remained visible without scrollbars; the pad flash matched its resized face. The Loop menu aligned its labels, disabled Quantise on an empty non-recording track, and toggled the transient recording grid without marking content unsaved. Sound checks were unavailable during this pass because Windows reported no default playback output.

The local formatter runtime is Python 3.12. GitHub CI pins Python **3.13.17**, whose Windows x64 artifact is listed in the Actions runtime manifest. This tooling pin does not change the API inventory or supported music behavior.

The Core runner checks actual rendered buffers, musical state, and files. The MIDI runner uses pure protocol fixtures and malformed-input cases without opening ports. The App runner compiles the production coordinator with fake boundary adapters. An individual suite passing does not replace the full verification script after a change.

The launched app has observed the connected **Launchkey 37 MK3**, firmware **0.2.1.7**, through its identity handshake. A UI-driven session recorded one keyboard note and one drum-pad hit into the four-bar loop. After restoring a Windows default physical output, the app recovered playback; its process-specific WASAPI session meter showed active, nonzero output, reaching approximately **0.07** over a **12-second** observation window. This is output-path evidence, not a latency measurement or a human listening assessment.

A limited physical capture confirmed William's Shift+Drum mode change and pad notes on the control pair. On **2026-10-26**, William tested updated build `3fe448d` and confirmed that the pads light blue and make drum sounds, and that both track-arrow directions are correct. This establishes basic physical Drum lighting/sound and corrected navigation; it does not establish individual coverage of all pads, muted red LEDs, LCD behavior, broader musical quality or latency. A full hardware controls and pedal pass is still pending. Detailed mappings and hardware evidence live in [the maintained device reference](../devices/novation_launchkey_mk3_37.md).

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

During the Quantise milestone, an isolated Release/x64 benchmark loaded 8,192 notes and 16,384 scheduled events, with 2,048 off-grid notes on the selected track and no quantization collisions. Four warmups preceded 24 measured actions. Rebuilding the schedule once per batch replaced repeated event-pair shifts for Quantise and note-history operations. All 8,192 notes were preserved.

| Operation | Mean before batching | Mean after batching | Final p95 | Final maximum |
| --- | ---: | ---: | ---: | ---: |
| Quantise | 17.54 ms | 4.96 ms | 7.40 ms | 11.04 ms |
| Undo | 16.07 ms | 2.96 ms | 8.47 ms | 8.73 ms |
| Redo | 15.75 ms | 3.51 ms | 13.12 ms | 15.88 ms |

These are host-specific lock-held compute costs on .NET 10.0.13, including allocation and scheduling variation. No audio stream or MIDI port was opened. They establish lower bulk-edit cost, not a maximum input wait, glitch-free output or physical latency guarantee. Bulk actions still hold the engine lock; reducing their tail latency further remains production work.

## Initial limits and unimplemented areas

| Limit | Current value |
| --- | --- |
| Audio format | 48 kHz, interleaved stereo floating-point samples |
| Tracks/instruments | Four tracks; keys, bass, atmosphere, drums |
| Loop | Four bars, sixteen beats |
| Tempo | 60–180 BPM |
| Quantization | Optional 0.25-beat recording grid, on by default; explicit selected-track onset correction |
| Simultaneous voices | 64, with oldest-voice replacement |
| Recorded notes | 2,048 per track |
| Stored note range | MIDI 0–127, velocity 1–127, onset 0 ≤ beat < 16, positive duration ≤16 beats |
| Project file | Version 1 JSON, at most 4 MiB |
| Undo history | At most 64 edit steps and 32,768 retained note deltas; not persisted |

No VST hosting, audio import/export, song arranger, synthesized aftertouch/modulation response, MIDI learn, external instrument routing, or project-to-hardware MIDI clock is implemented. Native Launchkey scale/chord/arp features can send ordinary performed notes, but the application does not mirror or synchronize their settings. Saved projects retain finalized notes and mixer state; metronome/selection, recording-grid preference, history and continuous pitch/sustain automation are not persisted. Remaining controller work is tracked in [the production-polish goal](../production_polish.md).
