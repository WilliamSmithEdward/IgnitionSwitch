# Music engine API coverage

Last reconciled: **2026-10-26**, owner-safe live wheel/pressure expression, alongside scenes and schema 3 persistence. This is the IgnitionSwitch music report; reports for other applications belong in separate files. Review supported behavior, evidence, member counts, and remaining gaps with every implementation commit.

## Scope and inventory

The in-memory API is `IgnitionSwitch.Core`. Its implementation has no external package dependencies. Windows MIDI, physical controllers, WinUI, and the NAudio output driver are adapters around it, with separate evidence below.

Counts are taken from the generated Release XML documentation, verified for this milestone at `artifacts/expression-preview/bin/IgnitionSwitch.Core/release/IgnitionSwitch.Core.xml`. Count distinct documented `T:`, `F:`, `M:`, and `P:` entries. Include declared positional-record constructors/properties and enum values. Exclude internal helpers, implicit default constructors, and generated record equality, hashing, deconstruction, and printing conveniences. Explicit source-compatible snapshot `Deconstruct` overloads are declared methods and are included. These counts inventory the declared API; they are not a test-coverage percentage.

| Public type | Constants/enum values | Constructors/method overloads | Properties | XML entries including type |
| --- | ---: | ---: | ---: | ---: |
| `Domain.CaptureResult` | 0 | 1 | 2 | 4 |
| `Domain.CaptureStatus` | 5 | 0 | 0 | 6 |
| `Domain.ClipAddress` | 0 | 1 | 2 | 4 |
| `Domain.ClipSnapshot` | 0 | 1 | 2 | 4 |
| `Domain.ClipState` | 0 | 1 | 2 | 4 |
| `Domain.DelaySettings` | 0 | 1 | 4 | 6 |
| `Domain.InstrumentKind` | 4 | 0 | 0 | 5 |
| `Domain.LiveNoteHandle` | 0 | 0 | 1 | 2 |
| `Domain.LoopNote` | 0 | 1 | 4 | 6 |
| `Domain.ParameterAddress` | 0 | 1 | 2 | 4 |
| `Domain.ParameterDescriptor` | 0 | 3 | 6 | 10 |
| `Domain.ParameterId` | 19 | 0 | 0 | 20 |
| `Domain.ReverbSettings` | 0 | 1 | 4 | 6 |
| `Domain.SceneState` | 0 | 1 | 2 | 4 |
| `Domain.SessionSnapshot` | 0 | 2 | 8 | 11 |
| `Domain.TrackPlaybackState` | 0 | 1 | 4 | 6 |
| `Domain.TrackSnapshot` | 0 | 2 | 7 | 10 |
| `Domain.TrackSoundSettings` | 0 | 1 | 7 | 9 |
| `Domain.TrackState` | 0 | 1 | 6 | 8 |
| `Persistence.SessionStore` | 1 | 2 | 0 | 4 |
| `Sequencing.WorkstationEngine` | 5 | 47 | 19 | 72 |
| **Total: 21 types** | **34** | **68** | **82** | **205** |

`WorkstationEngine` declares **71 non-type members**: 47 method overloads, 19 properties and 5 constants. The full documented Core API has 184 non-type entries plus 21 type entries. `NoteRevision` tracks notes and scene metadata/structure independently of mixer targets and transient playback choices; `LaunchRevision` tracks editing/playing/queued choices and solo. `GetClipNotes` returns one detached clip, while `CopyTrackPlaybackStates` fills four value-type states under one lock without allocation. `TrackState.NoteCount` describes the current editing clip. The legacy `TrackSnapshot.Notes` constructor/init/projection targets scene 0 and is empty when that scene is absent; canonical notes live only in `TrackSnapshot.Clips`. Explicit legacy snapshot deconstruction remains source compatible.

Scene APIs create, duplicate, rename and delete bounded rows, address clips through `ClipAddress`, queue per-lane or whole-row launches/stops, cancel queued actions and provide transient solo monitoring. Existing selected-track clear and Quantise target the editing scene; `ClearClip` addresses an exact clip independently of selection. History, Quantise, repeat/single-pass transport, idempotent `EndRecording`, Capture and generic parameter methods retain their earlier contracts with scene-aware note ownership.

Live expression adds an opaque engine-scoped `LiveNoteHandle` and two methods:

```csharp
LiveNoteHandle BeginLiveNote(int track, int note, int velocity,
    bool retainForCapture = true, double modulation = 0, double pressure = 0);
bool SetLiveExpression(LiveNoteHandle handle, double modulation, double pressure);
```

`IsValid` describes an issued identity, not current liveness. `SetLiveExpression` rejects default, foreign-engine, superseded, released, finished or stolen gates without creating voices. Initial expression is atomic with note-on; velocity zero follows existing note-off behavior and returns default. Existing void `NoteOn` overloads retain neutral expression. Amounts are finite normalized 0–1 values; these controls do not add persistent parameters, schema fields, Capture automation or history edits.

Project schema 3 saves every scene and its clip notes once, with shared track sound/mixer targets and global effects. Strict version 1 and 2 documents import into scene 0; version 1 receives native dry defaults. Editing/active clip choices, queues, phase, solo, live expression/handles, history, smoothing and DSP buffers remain transient. Pad presentation and hardware feedback remain adapters around the engine; animation and MIDI output do not drive sound generation.

## Implemented behavior and evidence

“Automated” describes executable assertions over implementation behavior. It does not establish physical playing quality, audible device output, or measured latency.

| Area | Status and supported behavior | Evidence | Remaining gaps |
| --- | --- | --- | --- |
| Scene content | Implemented; one initial scene, up to eight ordered stable slot IDs, create/duplicate/rename/delete, four owned clips per row, final-scene protection and total-budget preflight | `SceneTests.ContentAndHistory`, `.PersistenceAndCapacity` | Physical scene workflow; scene reordering and variable clip length |
| Quantized clip launches | Implemented; four bounded next-bar action slots, atomic row launch, per-lane replacement/cancel/stop, independent local-zero origins and generation-aware eligibility. Empty unarmed clips stop lanes; armed empty clips are recording destinations | `SceneTests.QueuesAndTransport`, `.EmptyRecordingWorkflow`, `.AudioAndPartitions` cover boundaries, lane independence, relaunch, pause/resume, one-pass cancellation, actual audio and callback partitions | Physical Session/scene verification and selectable launch quantization |
| Live wheel and pressure DSP | Implemented in Core; melodic live wheel vibrato at 5.5 Hz up to ±30 cents, live pressure for all instruments including drums, exact neutral bypass and 5 ms ramps. Pressure moves Tone halfway through its remaining headroom and adds at most 15% gain | `ExpressionTests.MusicalResponse` measures rendered vibrato rate/depth, harmonic brightening, exact pressure ramp/gain endpoint, drum response and unaffected neighboring/loop audio; `.NeutralAndValidation` checks exact neutral output and transient state | Physical wheel/pressure listening and packet traces; expression automation and configurable modulation routing |
| Expression gate ownership | Implemented in Core; one current engine-scoped live identity per shared track/pitch gate, sustained eligibility through Pause/clip/Undo, rejection after real release/retrigger/finish/steal/reset | `ExpressionTests.OwnershipAndLifecycle`, `.PartitionsAndAllocation` check lifecycle, callback equality, finite audio and warm allocation-free rendering/retargeting | Independent same-pitch source voices and MPE remain outside the current final-owner model |
| Expression adapters | App uses 512 latest track/pitch owner slots and 16-channel wheel/pressure caches, combines channel/poly pressure by maximum, and uses original channel/handle ownership. MIDI routes retained Drum pressure and zero values across pad-mode changes | `PressureRoutingTests.Routing`; `ExpressionCommandTests.OwnershipAndAudio`, `.LifecycleAndGuards`, `.HotPath` check original/latest ownership, resets, disconnected callbacks, pressure pruning and allocation | Physical pressure mode/channel/zero-release traces and listening |
| Built-in instruments | Implemented; keys, bass, atmosphere, synthesized drums, velocity, 64-voice limit, Tone/Attack/Release/Drive and drum Tail. Scheduled notes prefer replacing loop voices and preserve fresh live attacks | `AudioTests.Instruments`; `SoundTests.Instruments`; `VoicePriorityTests.LiveAttacks` proves live output under dense scheduled pressure, deterministic replacement and the 64-voice bound | Sparse hardware-miss diagnosis, musical listening and richer presets; more than 64 fresh live attacks still replaces the oldest live voice |
| Note ownership and release | Implemented; selected/explicit-track note methods, sustain, retrigger, velocity-zero release, 4 ms minimum smooth onset for short taps arriving between render callbacks, immediate all-notes-off | `AudioTests.Release` checks all three melodic instruments, pre-render/early releases, ordinary tails, retriggers, and panic; `StudioSessionTests.Ownership`, `.Sustain` | Physical pedal polarity, rapid scale/chord collisions, simultaneous input sources |
| Pitch bend | Implemented; normalized −1…1 maps to ±2 semitones for melodic voices | `ValidationTests.Controls` rejects invalid bend; formula in `SynthVoice`/`WorkstationEngine` | A frequency-response assertion and physical wheel pass; recorded bend automation |
| Transport and metronome | Implemented; sample-clock playback, repeat/single-pass mode, pause/resume, stop/rewind, four bars, tempo 60–180 BPM, retained terminal beat and click | `LoopTests.Transport`, `.RecordingAndPartitions` cover completion/restart/live ownership/recording/partition behavior; `SequencingTests.Transport` covers frame-based advancement, loop wrap, reset and click; `SequencingTests.Pause`: frozen clock, precise resume, pending boundary onset, stop rewind | Variable meter/length, external clock, measured timing |
| Pause voice/recording policy | Implemented; pause finalizes held recorded durations, disables recording, clears sequenced voices/click, and preserves live playing/sustain. Resume does not chase interrupted loop notes; they retrigger at their next scheduled onset | `SequencingTests.Pause`: live note survives pause and accepts note-off, finalized duration remains unchanged, wrapped note is silent until its next onset and then releases correctly; `StudioSessionTests.GenericPlaybackPauseResume`: UI/mapped hardware equivalence, retained owners/pedal, saved pause revision | Recorded expression automation and broader physical transport/playing combinations |
| Clip recording and overdub | Implemented; optional sixteenth-note grid, raw timing and same-onset/pitch replacement. Record retains active clips and arms stopped lanes into the editing row. Launches split already-recorded held gates without retriggering live voices | `SequencingTests.Recording`; `QuantizationTests.RecordingPolicy`; `SceneTests.RecordingOwnership`, `.EmptyRecordingWorkflow` cover active/edit separation, sustain, held segments, stopped lanes and first-hit retention | Editable note grid and expression automation |
| Selected-track Quantise | Implemented for the editing scene's selected clip; explicit sixteenth-note onset correction, forward midpoint/wrap policy, expressive duration retention, deterministic same-pitch collision handling, one bounded undo step and no-op suppression | `QuantizationTests.SelectedTrack`; `QuantizeCommandTests.Commands` covers hardware/UI equivalence, revision and modal guards | Physical Quantise pass and musical review |
| Retrospective Capture MIDI | Implemented; independent rendered-frame/performance-beat clock while stopped or paused, all-track live-source history, sustain and held gates, shared four-bar phrase window, active destination clip-origin alignment, inactive-row common anchor, atomic merge/capacity/truncation outcomes, one undo step and repeated-capture no-op handling | `CaptureTests.MergeAndHistory`, `.BoundsAndLifecycle`, `.ClocksAndPartitions`; `CaptureAlignmentTests.FreePhraseAndBoundary`, `.ProjectPhaseAndEpoch`; `CaptureCommandTests.Commands`, `.AudioAvailabilityAndLifecycle`; `SceneTests.CaptureAlignment` checks local-onset audio, paused epoch adoption, idle no-op and addressed Undo | Physical Capture button and audible phrase review; pitch/modulation automation is not retained |
| Undo and Redo | Implemented; bounded delta history, recording-part groups split by mixer/clear edits, continuous scalar gesture coalescing, load resets, retained beat/live ownership, addressed clip restoration and reversible scene creation/duplication/rename/deletion | `HistoryTests.Notes`, `.MixerAndLifecycle`, `.Bounds`; `PerformanceFeedbackTests.HistoryCommands` checks UI/hardware and modal/revision behavior; `SceneTests.ContentAndHistory` checks structural edits | Interactive/physical Undo and Shift+Undo pass; user-facing history descriptions |
| Incremental schedule and input capacity | Implemented; sorted value-type schedules prepared per clip, incremental event-pair changes, stable IDs and one batched rebuild/alignment for bulk edits or simultaneous launch recording handoffs | `InputStressTests.OrderedInputs`: chord/drum bursts, 1,536 retriggers/pedal releases, full 2,048-note clip still plays live hits and no-op revisions; `SceneTests.AudioAndPartitions` | Voice and finalized-note/history objects still allocate at event boundaries; broader sustained-load and physical-input measurements |
| Render slices and audio buffer policy | Implemented; 64-frame engine critical sections, default shared/event output, 30 ms compatibility fallback, explicit minimum-period exclusive option, direct native float/PCM16 validation, render-thread MMCSS lifecycle and reported-failure recovery | `AudioTests.RenderPartitions`; `AudioBufferTests.SharedPeriod`; `ExclusiveAudioBufferTests.MinimumPeriod`; running playback build opened shared output with requested 10 ms. Read-only DAC query found shared min/default/max 480 frames and exclusive minimum 3 ms, with native 48 kHz stereo PCM16 support | Exclusive stream and fallback/recovery interactive checks; physical key-to-sound measurement and driver glitch/compatibility testing. Requested periods are not measured latency |
| Loop-boundary duration | Implemented; note durations can extend across loop wrap | `SequencingTests.WrappingNote` checks sound across the boundary and scheduled release | Broader stress/timing measurements |
| Mixer, solo and clear | Implemented; shared track mute/gain/pan, master gain, transient multi-solo, selected editing-clip or exact-address clear, unity-center routing and 5 ms gain/pan/send/return ramps. Mute/solo block dry/new effect feeds while shared tails may finish | `SequencingTests.Tracks`; `SoundTests.Mixer`; `SceneTests.ContentAndHistory`, `.AudioAndPartitions` cover addressed clear, no-op, solo and retained live audio | Automation and physical stereo listening |
| Echo and Room | Implemented; post-fader/mute/pan parallel sends, damped stereo feedback delay and diffuse stereo reverb, bounded preallocated storage, crossfaded time/predelay changes, independent live/sequenced lanes | `SoundTests.EffectsAndLanes` checks real wet timing/decay, routing/mute/pan, partition equivalence and lane resets. Isolated source-linked DSP harness checks sample rates/extremes, diffusion/damping, retargets, reset and warm zero allocation | Listening and preset tuning. Shared already-fed tails may spill after a track is muted; no cross-effect feedback or recorded automation |
| Parameter controls | Implemented; nineteen IDs, validated track/global addresses, normalized targets, physical conversion/defaults, target-based undo, all eight Instrument/Echo/Room slots, actual Device Lock, semantic mixer banks and absolute-pot pickup | `SoundTests.Mixer`; `SoundControlTests.PagesAndLifecycle`, `.HardwareBanks`, `.Pickup`; `ControlMappingTests.SoundModes`; `FeedbackPresentationTests.Snapshots`, `.Dedupe` | Physical pot/Device Select/Lock/LCD verification and broader parameter pages |
| Mixer presentation | Implemented in App adapter; shared 100 ms cubic visual easing for all five volume controls, immediate native target values, coalesced hardware feedback on the UI tick, direct manipulation and reduced-motion behavior | `SliderMotion` and `MainWindow`; running-app checks covered native target updates, master zero/full endpoints, track gain changes, pointer takeover and pane resizing with aligned thumb/fill | Physical fast-pot-turn, keyboard takeover and reduced-motion verification on additional display configurations; screenshots do not measure animation cadence |
| Pad hit feedback | Implemented in adapters; note-addressed physical layout, velocity-sensitive app flash, minimum 80 ms hit visibility, held/retrigger/release handling, 120 ms app fade, coalesced worker-only hardware output and mode-aware base colors | `PadFeedbackTests.Layout`, `.Hits`, `.Context`; `PerformanceFeedbackTests.Activity`; William confirms physical/app reactions in the parameter preview, while reporting intermittent registration and perceived delay | Diagnose the reported sparse misses and perceived delay; sustained-load/reduced-motion pass. Flashes intentionally coalesce and cannot count every rapid hit |
| Received-input diagnostics | Implemented in App; bounded received drum count and last/min/max velocity, collapsed Devices UI, noncontent reset, no per-hit allocation | `InputDiagnosticsTests.CountingAndReset`: 1,024 retriggers, releases/exclusions, modal/offline input, held-owner/history/revision preservation and allocation checks; running playback build displayed aligned zero-state counters | Broader counted-hit and velocity runs; the observed 20-message sequence matches 20 reported contacts. Counter does not measure rendered sound or latency |
| Session snapshots | Implemented; canonical clip-owned notes, ordered scenes, detached collections, shared mixer/sound targets, scene-0 compatibility and apply that prepares the first scene | `PersistenceTests.Roundtrip`; `SchemaTests`; `SceneTests.ContentAndHistory`, `.PersistenceAndCapacity` check compatibility, all-scene roundtrip, transient reset and capacity rejection | Variable track/loop capacity and expression automation |
| JSON persistence | Implemented; 4 MiB cap, four tracks/four bars, 2,048 notes per clip and 8,192 total, finite/ranged values, bounded names, strict v1/v2 import and v3 documents, atomic replacement | `ValidationTests.Projects`; `PersistenceTests.Roundtrip`; `SchemaTests`; `SceneTests.PersistenceAndCapacity` checks migration, clip-only saves and rejected null/unknown fields | Audio/MIDI export and future schema migrations |
| Input and lifecycle validation | Implemented; invalid track/note/gain/buffer controls rejected, disposal prevents further engine work | `ValidationTests.Controls` | Extended concurrency and long-session stress |
| Revision-aware saves and dialogs | Implemented in App adapter; captured revisions preserve newer unsaved edits, idempotent recording finalization cannot restart a naturally completed pass, note completion advances content revision, modal operations finalize/block project mutations while preserving live performance, project replacement resets owners atomically | `StudioSessionTests.Revisions`, `.ModalIsolation`; `InputReliabilityTests.ProjectReplacement` | Full interactive picker/confirmation pass |
| Controller feedback and shutdown | Implemented in App adapter; mute/name/connection/loop-aware feedback, callback guards and adapter disposal | `StudioSessionTests.Feedback`, `.Shutdown` | Physical unplug/contending-app tests and visible LED/LCD verification |
| Track arrows and Drum layout | Implemented in MIDI adapter; observed left/right mapping, mode-aware control-port Drum normalization, retained releases, blue/red Drum LEDs and Session-mode repaint | `ControlMappingTests.Commands`, `PerformanceRoutingTests.Routing`, `ProtocolTests.Feedback`; `StudioSessionTests.Ownership` checks raw-packet navigation boundaries and rendered drum routing. Local physical capture confirmed mode 1 and `99` Drum input on the control pair. On 2026-10-26, William confirmed build `3fe448d` displays blue Drum pads that make drum sounds, and both track-arrow directions are correct | Individual verification of all pads, muted red LEDs, Session-mode repaint, LCD behavior and broader playing quality |

Scene launch choices are transient. Play from stopped starts the editing row; an explicit stopped clip launch starts only that lane. Paused queues wait for resume, Stop clears them, and nonrepeat completion cancels remaining actions without extending the current global pass. Record preserves active clip phases and arms stopped lanes into the editing row; an explicit stop while already recording stays unrecorded until launch/rearm. Pre-Record held keys remain live-only. See the [scene and clip model](../architecture.md#scene-and-clip-model) for ownership and scheduling details.

Capture uses one shared phrase window. Active editing clips align against their own current origins; inactive/new-row clips retain the common project/free anchor. Capturing into a mixed-origin audible row preserves that mix. Relaunching the entire row intentionally resets every clip to local zero and can change its relative groove; saved launch offsets are not implemented.

Expression retains the existing shared track/pitch final-owner and global-pedal behavior. Core has no MIDI channels; the adapter associates the latest gate with its original channel and combines channel/poly pressure by maximum. Its 512 owner slots and 16-channel caches are bounded. Wheel values remain cached through Stop, modal playing, panic and project load, then reset on disconnect/disposal. Pressure without an owned gate is discarded; new-strike pruning and real-release cleanup prevent stale pressure carrying into unrelated input. Disconnected input/control callbacks are rejected before they can repopulate caches or mutate state. This is live expression, not MPE or independently owned same-pitch voices.

## Test inventory and physical evidence

The expression milestone passed **87 groups**: **41 Core**, **28 App** and **18 MIDI**. The four new Core groups are `ExpressionTests.NeutralAndValidation`, `.MusicalResponse`, `.OwnershipAndLifecycle` and `.PartitionsAndAllocation`; all 37 prior Core groups also pass. App expression evidence is `ExpressionCommandTests.OwnershipAndAudio`, `.LifecycleAndGuards` and `.HotPath`. MIDI pressure ownership/routing is covered by `PressureRoutingTests.Routing`.

The full `scripts/verify.ps1` run passed locked restore, the WinUI Release/x64 build, all 87 groups, prettyCSharp and style/analyzer validation at informational severity. Local evidence is `artifacts/expression-verification.log`, with output under `artifacts/expression-preview`. The new generated Core XML was independently counted as 205 entries. Run verification with:

```powershell
.\scripts\verify.ps1 -Python .\.venv\Scripts\python.exe
```

Physical expression controls and musical listening remain unverified. Earlier UI and hardware observations below apply to their named builds, and do not establish pressure packets or end-to-end latency.

The received-input allocation check warms the identical non-inlined batch before measuring 100,000 further hits. Its zero-byte assertion remains strict. This separates steady counter work from a reproduced tiered-JIT/on-stack-replacement first-loop allocation on a two-processor runtime; five fresh constrained processes passed after the warm-up correction. The counter implementation and runtime/CI settings are unchanged. The current Core inventory is 205 documented entries.

The running Quantise build was resized separately in width and height to approximately 1,000 × 700 logical pixels. All four tracks, four bars, sixteen pads and the complete status line remained visible without scrollbars; the pad flash matched its resized face. The Loop menu aligned its labels, disabled Quantise on an empty non-recording track, and toggled the transient recording grid without marking content unsaved. Sound checks were unavailable during this pass because Windows reported no default playback output.

The running Loop build's Repeat menu was toggled off and back on. Its checkmark and the `4 bars · once` header agreed, without a phantom unsaved-content change. Natural completion, finalized save contents and retained live-note release were checked through the engine/App regression suites; audible endpoint behavior and the physical button/LCD still need confirmation after restoring audio output.

The parameter build was launched after Windows was unlocked and the empty Loop preview closed. It identified the Launchkey and recovered audio availability (transport became enabled). Width and height were resized separately to 1,000 × 700; all four tracks, four bars and sixteen pads fit without horizontal scrolling. The Sound flyout fit within the window with aligned label/value columns, correct Instrument/Echo controls, and working parameter edit/Undo. Physical feedback and all-bank playing verification remain separate human checks. Capture's menu action is implemented and covered by production coordinator checks; its live action still needs review.

William chose to keep shared audio and reported that playing seems better on the playback-reliability build. The scenes preview showed **20 received drum messages**, last velocity **126**, and range **110–127**. William clarified that ten two-finger strikes on different pads meant twenty physical contacts. The observed count is consistent with one message per contact for that sequence; it does not verify every pad, complete reliability, rendered sound or latency. Knob testing remains deferred.

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

The Loop milestone also batches simultaneous held-note finalization at transport/recording boundaries; individual note-off remains incremental. With a full 8,192-note project, two warmups and eight natural-completion samples, the final-source callback averaged **2.08 ms** with 384 melodic owners and **1.86 ms** with a schema-valid all-melodic 512-owner configuration. Observed maxima were **7.08 ms** and **2.71 ms** respectively, and all project notes were retained. An earlier prototype's 384-owner sample reached **13.31 ms**, demonstrating host variation. These short no-stream measurements do not establish a deadline or physical playback quality.

Capture uses a lazy bounded workspace and value candidates. In a warmed Release/x64 benchmark on the same host, all 8,192 source notes were retained through a complete append or replacement capture. Capturing unchanged content allocated **24 bytes** for its result, rather than the initial prototype's approximately **2.05 MB**. The initial full append prototype averaged **23.02 ms** before scratch reuse and indexed/batched application.

| Capture case | Mean compute time | Observed maximum |
| --- | ---: | ---: |
| Append 8,192 notes | 8.52 ms | 10.18 ms |
| Replace 8,192 matching slots | 8.68 ms | 10.86 ms |
| Already-matching 8,192 notes | 2.07 ms | Not recorded here |
| Append a 37-note phrase | 0.0434 ms | 0.154 ms |
| Already-matching 37-note phrase | 0.0160 ms | Not recorded here |

At the earlier Capture milestone, retained and explicitly unretained live note-on/off pairs both allocated **136 bytes**, so the value-type history ring added no input allocation in that comparison. That historical count predates the inline expression fields; the expression benchmark below excludes new voice construction. A fixed-voice run of 256 callbacks of 480 stereo frames allocated **zero bytes** and produced finite samples. These are isolated Core compute/allocation observations, with warm scratch storage, no audio stream and no MIDI port. Cold initialization, host scheduling, bulk-operation tails, physical input delivery and audible latency remain separate concerns.

The sound-parameter milestone compared default audio against the frozen Loop-build Core DLL in an isolated assembly load context. Maximum sample differences across held and released audio were **0** for one voice, **8.94 × 10⁻⁸** for sixteen voices and **2.68 × 10⁻⁷** for sixty-four voices, consistent with changed summation order and below a 10⁻⁶ tolerance. This compares actual earlier synthesis rather than two configurations of the new implementation.

A warmed Release/x64 run measured 256 callbacks of 480 stereo frames per case. All output was finite and each case allocated zero bytes during measured rendering:

| Voices | Dry mean CPU per 10 ms audio | Delay, room and full drive mean CPU |
| --- | ---: | ---: |
| 1 | 0.0356 ms | 0.2011 ms |
| 16 | 0.2452 ms | 0.3827 ms |
| 64 | 0.9752 ms | 1.1151 ms |

Development provenance is in the ignored `artifacts/sound-foundation-benchmark/Program.cs` and `SoundBenchmark.csproj`; its comparison loaded `artifacts/loop-mode/bin/IgnitionSwitch.Core/release/IgnitionSwitch.Core.dll`. The isolated source-linked effects harness is `artifacts/fx-dsp-benchmark/Program.cs`. No output stream or hardware port was opened for these measurements. They describe host compute cost and allocation, rather than physical latency or a scheduling guarantee.

The scene milestone's ignored `artifacts/scene-benchmark/Program.cs` and `SceneBenchmark.csproj` measured the engine on .NET 10.0.13 without opening audio or MIDI ports. After 1,024 warm callbacks per case, the second pass of 256 callbacks measured:

| Held voices | Mean CPU per 10 ms audio | Allocated bytes |
| --- | ---: | ---: |
| 1 | 0.03557 ms | 0 |
| 16 | 0.22379 ms | 0 |
| 64 | 0.97006 ms | 0 |

One thousand `CopyTrackPlaybackStates` calls also allocated zero bytes. These are warmed compute observations, not end-to-end latency measurements.

An 8,192-note project benchmark used two warmups and eight 64-frame callbacks crossing a scene-launch boundary. Without held recording, mean/max CPU was **0.1379/0.1682 ms**, allocating **57,344 bytes** for 512 scheduled voice starts. With 384 held recording replacements, mean/max was **1.9366/8.703 ms**, allocating **122,216 bytes** for starts and finalized note/history data. Launches batch schedule work once; scheduled voice creation and recording finalization still allocate. These historical scene-build allocations predate inline expression fields. Warm fixed-voice zero allocation does not describe every onset or recording boundary, and these observed maxima are not deadlines.

The expression benchmark compared actual held/released default audio against the frozen Scenes Core DLL at `artifacts/scenes-preview/bin/IgnitionSwitch.Core/release/IgnitionSwitch.Core.dll`. Maximum sample difference was **zero** at 1, 16 and 64 voices. On .NET 10.0.13, 1,024 warm callbacks preceded 256 measured callbacks of 480 stereo frames per case:

| Voices | Neutral mean CPU per 10 ms audio | Full wheel + pressure mean CPU | Allocated bytes in each case |
| --- | ---: | ---: | ---: |
| 1 | 0.04629 ms | 0.05290 ms | 0 |
| 16 | 0.25555 ms | 0.28071 ms | 0 |
| 64 | 1.02705 ms | 1.16687 ms | 0 |

All six fixed-voice cases produced finite samples. After three warm passes of the identical update batch, 64,000 `SetLiveExpression` calls took **2.5034 ms total** and allocated **zero bytes**; no timing threshold was asserted. Provenance is the ignored `artifacts/expression-benchmark/Program.cs`, project file and `result.log`. No audio stream or MIDI port was opened. These measurements exclude new voice construction and recording/history events and do not establish physical key-to-sound latency.

`ExpressionCommandTests.HotPath` exercises the production coordinator with six reused wheel/channel/poly-pressure message objects in a 60,000-message batch. Three identical batches warm the path before measurement. The measured batch allocated **zero bytes**, emitted no performance activity, feedback writes or drum-diagnostic updates, and left content/revision/history unchanged. Parsing, message construction, rendering and assertions are outside that allocation measurement. Lifecycle tests also verify stale/finished-owner pressure pruning, cache reset/retention rules and rejection of queued callbacks after disconnect.

## Initial limits and unimplemented areas

| Limit | Current value |
| --- | --- |
| Audio format | 48 kHz, interleaved stereo floating-point samples |
| Tracks/instruments | Four tracks; keys, bass, atmosphere, drums |
| Scenes/clips | One initial scene; up to eight stable scene slots, four track clips each |
| Clip length | Four bars, sixteen beats per clip; next-bar launch, local-zero restart |
| Tempo | 60–180 BPM |
| Quantization | Optional 0.25-beat recording grid, on by default; explicit selected editing-clip onset correction |
| Simultaneous voices | 64; loop-first replacement with 4 ms fresh-live-attack protection; two inline expression ramps per voice |
| Live expression | Normalized wheel/pressure per issued gate, 5 ms ramps; melodic 5.5 Hz vibrato up to ±30 cents; pressure brightness and ≤15% gain; transient |
| Recorded notes | 2,048 per track/scene clip; 8,192 total. `MaximumNotesPerTrack` retains its legacy name for the per-clip limit |
| Stored note range | MIDI 0–127, velocity 1–127, onset 0 ≤ beat < 16, positive duration ≤16 beats |
| Project file | Writes version 3 JSON; strict version 1/2 import into scene 0; at most 4 MiB |
| Undo history | At most 64 edit steps and 32,768 retained note deltas; not persisted |
| Retrospective history | At most 8,192 completed performance notes and 512 held gates; current phrase, four-bar window; not persisted |

No VST hosting, audio import/export, song arranger, expression automation, MPE, configurable modulation matrix, MIDI learn, external instrument routing, or project-to-hardware MIDI clock is implemented. Native Launchkey scale/chord/arp features can send ordinary performed notes, but the application does not mirror or synchronize their settings. Saved projects retain all scene metadata and clip notes, shared mixer/sound targets and effect settings. Editing scene, active/queued clips, local phase, solo, device/track selection and lock, metronome, recording-grid/repeat preferences, retrospective buffers, live expression/handles, history, DSP buffers and continuous pitch/sustain automation are not persisted. Remaining controller work is tracked in [the production-polish goal](../production_polish.md).
