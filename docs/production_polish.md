# Production polish

Active development goal, started **2026-10-26**. The first hardware target is William's Launchkey 37 MK3; controller-specific behavior stays behind shared adapters.

## Completion criteria

- Hardware drum hits and matching application pads react promptly, retain very short taps, and settle after release, mode changes, Stop and shutdown.
- Every physical knob/button has an implemented standalone purpose or a verified native hardware role. Unsupported host functions are not counted as complete because an input packet is recognized.
- Editing, capture, quantization, loop and scene behavior are real engine operations with bounded state, clear undo semantics and safe project persistence.
- Device, Volume, Pan and Send pages have meaningful controls, pickup and useful LCD feedback. Native Scale, Chord, Arp and transposition are consumed once as performed notes.
- Input/audio remain independent of UI animation and hardware output. Keyboard navigation, reduced motion, resizing, device removal/recovery and project dialogs receive interactive checks.
- Automated checks pass, physical evidence is recorded precisely, and changes are pushed with the [API coverage report](in_memory_engine/music_api_coverage.md) and [device reference](devices/novation_launchkey_mk3_37.md) kept current.

## Work sequence

| Milestone | Current state | Remaining validation/work |
| --- | --- | --- |
| Hardware audit | Complete for documented MK3 control messages and native roles | Confirm ambiguous firmware behavior on William's unit rather than guessing from diagrams |
| Drum hit feedback and physical layout | Implemented: coalesced hardware white flashes, matching app feedback, minimum visible duration, adapter-provided note order. William confirms both react in the parameter preview, but reports occasional missed registration and perceived delay | Diagnose sparse hardware/audio delivery; verify counter/velocity evidence and lower-buffer mode. Dense scheduled-note eviction of fresh live attacks is fixed separately |
| Undo/Redo | Implemented: bounded note/mixer delta history, recording-part grouping, gesture coalescing, hardware and keyboard commands | Interactive gestures, save/dirty state and physical Shift+Undo verification |
| Quantise | Implemented: selected-track onset correction with preserved durations, deterministic collisions, one undo step, optional recording grid and compact Loop options | 42 combined automated groups, build, formatting and informational analyzers pass; physical Quantise and musical review remain |
| Loop | Implemented: repeating/single-pass transport, exact terminal position, retained live playing, hardware command, LCD context and compact menu; 45 automated groups and full informational checks pass | Physical Loop/LCD/recording review; audible terminal-position check |
| Capture MIDI | Implemented: bounded all-track live history, phrase alignment, held/sustained gates, atomic one-step capture and explicit failure results | 52 combined automated groups and full informational checks pass; physical button and audible phrase review remain |
| Scene and clip controls | Implemented: one to eight scenes with stable identities, independent track clips, bar-quantized scene/clip launches and stops, real mixed-scene playback, transient solo, undoable scene management, and persistence of all clips. Compact UI adds a scene selector/Launch action, separate track launch buttons, state subtitles and selected-track Mute/Solo/Stop; editor-row snapshots and independent lane playheads keep editing distinct from playback. Release/x64 solution build, 79 automated groups (37 Core, 25 App, 17 MIDI), prettyCSharp and informational style/analyzer checks pass | Runtime visual/accessibility/DPI checks and physical Session/Scene/context tests remain pending; automated results do not establish native UI or hardware verification |
| All parameter pages | Implemented: eight instrument controls; real stereo pan and parallel echo/room sends; Device/Volume/Pan/Send banks; Device Select/Lock, target-aware pickup, LCD names/values and compact Sound inspector; 66 combined groups and informational checks pass; app sizing and Sound edit/Undo checked at 1,000 × 700 | Physical knob testing deferred at William's request while missed hits and perceived delay are diagnosed; broader device chains/presets remain future work |
| Playing reliability and output latency | Fresh live attacks survive dense scheduled-note pressure; received drum count/velocity diagnostics; optional minimum-period exclusive output with shared fallback and render-thread scheduling. William currently selects shared output | Continue counted-hit diagnosis, longer playing and recovery checks; short observed contact checks do not establish sustained reliability or measured latency. The reported sparse misses are not yet explained |
| Native feature integration | Partially verified | Scale/chord/arp/strum and transposition coverage; optional clock synchronization through the performance output |
| Production readiness | In progress | Recovery/persistence, accessibility/DPI, extended input/audio stress and measured physical latency; installer/release preparation separately from publication |

This goal stays active until the remaining implementation and validation are complete. Shipping an individual milestone does not establish complete hardware support or measured latency.

## Current scene workflow and verification boundary

The scene selector changes the editing and Capture target. Launch actions choose audible clips separately; changing the editing scene must not swap the project or interrupt an existing mix. Recording from stopped playback starts the selected scene, while recording during playback follows each track's actual active clip. The four lane playheads therefore use individual clip positions and appear only for clips belonging to the displayed scene.

Scene creation, duplication, renaming, deletion and clip clearing use guarded session operations. The final scene is retained, names and note counts are bounded, and all scene content is saved together. A copy that exceeds capacity shows a concise status message. Launch queues, selected editing scene and monitoring solo remain transient. Clear/Delete/Rename use the existing modal lifecycle guards, and delayed UI work must not refresh a disposed window.

The current UI keeps the existing workspace height and resize handles: scene actions live in the Loop header and options flyout, clip launches sit beside track selection, and the bottom selected-track actions are Mute, Solo and Stop clip. The full WinUI build and all 79 automated groups pass. Interactive checks for this revision remain **pending**, including the 1,000 × 700 layout, long names, empty/full scene limits, mixed launches, queued stops, recording targets, Undo/Redo, project round trips, keyboard access, reduced motion and modal closure. Physical Launchkey Session pads, scene navigation/launch and Stop/Solo/Mute context/feedback also remain **pending**. Prior Sound-inspector sizing checks do not validate this new scene layout.

Physical knob testing remains deferred at William's request. Shared output stays selected; neither a short contact-counter observation nor an output buffer request is a measured end-to-end latency result.
