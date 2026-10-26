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
| Drum hit feedback and physical layout | Implemented: coalesced hardware white flashes, matching app feedback, minimum visible duration, adapter-provided note order; running app flash/restore and aligned shortcut menu checked | Physical feedback verification |
| Undo/Redo | Implemented: bounded note/mixer delta history, recording-part grouping, gesture coalescing, hardware and keyboard commands | Interactive gestures, save/dirty state and physical Shift+Undo verification |
| Quantise | Implemented: selected-track onset correction with preserved durations, deterministic collisions, one undo step, optional recording grid and compact Loop options | 42 combined automated groups, build, formatting and informational analyzers pass; physical Quantise and musical review remain |
| Loop | Implemented: repeating/single-pass transport, exact terminal position, retained live playing, hardware command, LCD context and compact menu; 45 automated groups and full informational checks pass | Physical Loop/LCD/recording review; audible terminal-position check |
| Capture MIDI | Pending | Retrospective sample-clocked performance history and a clear undoable capture window |
| Scene and clip controls | Pending | Selectable scene content, launch semantics and Stop/Solo/Mute contextual row actions |
| All parameter pages | Pending | Eight useful parameters per applicable page, pan/effects sends, Device Select/Lock, pickup and LCD values |
| Native feature integration | Partially verified | Scale/chord/arp/strum and transposition coverage; optional clock synchronization through the performance output |
| Production readiness | In progress | Recovery/persistence, accessibility/DPI, extended input/audio stress and measured physical latency; installer/release preparation separately from publication |

This goal stays active until the remaining implementation and validation are complete. Shipping an individual milestone does not establish complete hardware support or measured latency.
