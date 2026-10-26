# Novation Launchkey 37 MK3

Maintained device reference for IgnitionSwitch. Last reconciled: **2026-10-26**.

This file separates manufacturer-documented hardware behavior, behavior implemented in this repository, and observations from William's physical controller. Keep it current whenever discovery, mapping, feedback, settings, or support claims change. Update the evidence and remaining gaps in the same commit as the relevant implementation. A protocol unit test is not evidence that a physical button has been exercised.

## Device identity and boundaries

The first supported device is the **full-size Novation Launchkey 37 [MK3]**. It is distinct from Launchkey Mini MK3, Launchkey 37 MK4, and other full-size MK3 sizes. The MK3 37 has 37 velocity-sensitive synth-action keys, 16 RGB velocity-sensitive pads in two rows of eight, eight rotary pots, pitch/modulation wheels, and a two-row, 16-character LCD. The pads support channel/polyphonic pressure; key aftertouch is not an implemented input capability here. There are **no faders, fader buttons, or Arm/Select button** on this model. MK4 endless-encoder banks, OLED behavior, and its different scale/chord feature set must not be assumed. [Novation comparison](https://support.novationmusic.com/hc/en-gb/articles/360017267699-Launchkey-Comparison-Chart)

Launchkey is a MIDI controller: its USB and DIN messages contain performance/control data, not sound. IgnitionSwitch provides the instruments and computer audio output. No external DAW is required. The name **DAW mode** below is Novation's hardware protocol terminology; IgnitionSwitch uses that protocol as a standalone music application. [Novation's explanation of sound generation](https://support.novationmusic.com/hc/de/articles/360001346265-I-don-t-hear-any-sound-from-my-Launchkey)

Published physical specifications for the 37-key MK3 are 555 × 258 × 77 mm (width × depth × height, including pots), 62 mm high excluding pots, and 2.7 kg in the V6 manual. The supplied connection is USB Type A to Type B. [User Guide V6, physical characteristics and connection diagrams](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=54)

## Observed device and application evidence

The following observations come from development smoke tests on 2026-10-26. The virtual-keyboard interaction was exercised in an earlier development layout, before the requested workspace refinement. It remains evidence for the recording path and does not imply a permanent on-screen piano in the workspace.

| Observation | Evidence as of 2026-10-26 |
| --- | --- |
| Windows sees the connected unit | The development machine exposed a Launchkey device; generic PnP naming alone was not used as model identification. |
| Application discovery and model verification | A launched WinUI application reported `Launchkey 37 MK3 connected · firmware 0.2.1.7 · keys and controls ready`. The connection path opened both performance and control pairs and accepted the expected Novation model inquiry reply. |
| Firmware of William's connected unit | **0.2.1.7**, as formatted from the four application-version bytes in that reply. This is an observation, not a claim that this is Novation's latest firmware. |
| Recording through the running WinUI application | The earlier on-screen keyboard recorded one melodic note, and an on-screen drum pad recorded one drum note on a separate track in the four-bar loop. This exercised application input, recording and track separation; it did not exercise physical Launchkey keys or pads. |
| Audio-output recovery | The audio output was disconnected during the running-app test and then reconnected. The application recovered its output without a manual reconnect action. This was an audio-device recovery test, not a Launchkey USB unplug test. |
| Signal in the application's actual Windows render session | After the default physical output was reconnected, Windows WASAPI metering for the running **IgnitionSwitch process's own render session** was active and reported a nonzero peak of about **0.07** during a **12-second** observation. This establishes signal flow into that render session; it is not a human listening test or a latency measurement. |
| Native Save As interaction | The Windows Save As dialog opened with the corrected JSON file filter and a suggested filename ending `.ignition.json`; Cancel worked. Completing a native save and reopening that saved file remain manual checks because automation did not expose the picker host. |
| Pause and reset semantics | Hardware-independent core checks passed (12/12 groups in the confirmed run): Pause preserves the beat and live held-note/sustain state; Stop rewinds. This verifies engine behavior, not a physical press of the Launchkey transport buttons. |
| Direct input acceptance and ownership | The application coordinator passed 11/11 hardware-independent check groups, including 4,736 note-on/off events in 64 rapid 37-key chord bursts, cross-channel overlapping-pitch releases, repeated track-changing retriggers, modal live playing and project replacement. Inputs were injected through a test controller into the actual production coordinator; these are not physical MIDI-delivery, listening, or latency measurements. |
| Responsive workspace | The launched layout displayed all four bars and all 16 pads without horizontal scrolling at approximately 1,348 × 834 and 1,000 × 700 logical window sizes. Both pane handles were dragged successfully; native window width and height were resized separately. Shortcut labels use aligned grid columns in the compact window. |
| Lower buffering request | A read-only query of the development playback endpoint reported a 10 ms shared default period. Shared/event playback now requests that period, with a 30 ms compatibility fallback. This is a requested duration, not measured key-to-sound latency; render work and recording capacity are covered by the [engine report](../in_memory_engine/music_api_coverage.md#responsiveness-and-measured-compute-cost). |
| Gain display feedback | All five gain controls share 100 ms compositor easing while their native values and audio targets apply immediately. Running-app automation checked master zero/full endpoints, track target updates, direct pointer takeover and resizing with aligned fill/thumb positions. Physical rapid-pot sweeps and animation cadence remain separate checks. |
| Keys/pads/pots played by a human | Not yet recorded as a completed physical test. |
| Actual pad colors/LCD rendering | Feedback packets are implemented and tested against documented bytes; a visual hardware pass is still required. |
| Audio latency and device timing | No measured hardware latency or timing benchmark yet. |
| Launchkey USB unplug/reconnect and MIDI-port contention | Teardown and removal handling are implemented; a physical controller unplug/replug and another-app MIDI contention test remains required. The successful audio-output recovery above does not establish either result. |

Do not publish endpoint IDs, container IDs, unique serial numbers, or raw photos merely to demonstrate discovery. None are needed in this reference.

## Connectors, power, and endpoint routing

| Connector/interface | Purpose | IgnitionSwitch behavior |
| --- | --- | --- |
| USB-B | Bus power and two bidirectional MIDI interfaces | Windows MIDI discovery opens only candidate Launchkey endpoints, then verifies the 37 MK3 model. |
| Performance USB MIDI pair | Keys, wheels, sustain, drum/chord performance, and custom controls | Receives notes, pitch bend, CC, program and pressure data. Sends the universal identity inquiry. |
| Control-surface USB MIDI pair | Buttons, session/device-select pads, pot-mode control, LEDs, LCD | Opened for MK3 control after the model handshake. Sends DAW-mode entry and initializes the supported surface layout. |
| Five-pin DIN MIDI **OUT** | Performance output to external MIDI equipment | No DIN routing or external-instrument management UI is implemented. Do not treat this as a MIDI input or general USB-to-DIN thru service. |
| Quarter-inch sustain input | Pedal, normally CC64 | CC64 is interpreted as sustain; remapping the pedal in Components changes that behavior. |

Windows names vary with the driver. Recognized candidate examples include `LAUNCHKEY`, `Launchkey MK3 37`, and `LKMK3 MIDI`. A second interface can appear as `MIDIIN2 (...)`, `MIDIOUT2 (...)`, a `DAW` name, or `Port 2`. These names are discovery hints. The identity reply is authoritative.

`LaunchkeyEndpointMatcher` groups candidates by Windows device container and requires one unambiguous performance input/output pair. It does not join ports from different containers. Without container metadata, only exactly two endpoints forming one performance input/output pair are accepted; additional unidentified endpoints are declined. Control-surface pairing requires shared device-container metadata. Multiple indistinguishable ports or devices require a future explicit device picker. An unrelated MIDI keyboard or Windows software synth is left alone. A missing control pair permits verified performance-only operation, with a status explaining the limitation.

The keyboard can also be powered without a computer using the supply specified in the user guide (5 V DC, at least 500 mA) to control external instruments through DIN. This does not make it an audio instrument. [User Guide V6, p.10](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=10)

## Physical controls and current application support

Statuses below describe code currently present. The identity/connection and application recording evidence above do not replace a physical playing pass for the hardware mappings.

| Control | Current IgnitionSwitch behavior | Remaining work |
| --- | --- | --- |
| 37 keys and velocity | Play the selected instrument; record incoming notes into the loop. Velocity-zero note-on releases a note. Releases retain their original track ownership; overlapping same-pitch owners on different MIDI channels remain held until the final owner releases. Short taps that arrive entirely between render callbacks retain a smoothly rendered onset. | Physical velocity/rapid-retrigger checks and musical tuning. |
| Pitch wheel | Routes the unsigned 14-bit MIDI value to the engine's pitch bend control. | Confirm range, center, and response on hardware. |
| Modulation wheel | Ordinary modulation CC input is parsed but does not modulate an engine parameter. Hardware-generated Strum notes use the normal note path. | Musical modulation mapping and physical Strum verification. |
| Sustain pedal | CC64: values ≥64 sustain; lower values release sustained tracks. | Pedal polarity/custom mapping and cross-track playing checks. |
| Top session pads 1–4 | Select tracks 1–4. | More tracks/scenes are not implemented. |
| Bottom session pads 1–4 | Toggle mute for tracks 1–4. | Stop/Solo/Mute mode cycling and per-clip launch behavior. |
| Remaining session pads | Unassigned and unlit. | Additional track/clip functions. |
| Drum pad mode | Performance notes on the default drum channel route to the drum track. | Bank scrolling, drum-pad selection, per-drum colors and all-pad sound review. |
| Pots 1–4 | Track gains: keys, bass, pad, drums. Audio targets apply directly; the on-screen gain controls use visual easing and coalesce feedback on the next UI tick. | Pickup and value synchronization; physical fast-turn review. |
| Pot 5 | Master gain, with the same visual feedback behavior as track gains. | Physical response check. |
| Pot 6 | Project tempo, 60–180 BPM. | Hardware arpeggiator clock synchronization. |
| Pots 7–8 | Deliberately unassigned. | No effect parameters are advertised. |
| Device/Volume pot modes | The six mappings above are enabled. Device mode is selected on connection. | A richer device parameter page may replace this initial mapping later. |
| Pan/Send pot modes | Ignored by the application mapper. | Pan/send processing and matching feedback. |
| Track left/right | Move selected track one step, clamped to the four-track range. | Banking if track capacity expands. |
| Play | Start, pause, or resume project playback; pausing preserves the transport position. | Verify the physical button and its interaction with held notes/recording. |
| Stop | Stop playback/recording, release held notes, and rewind the loop transport. | Verify with sustained notes and active recording. |
| Record | Toggle loop recording when audio is running. | Physical overdub review. |
| Click | Toggle the application metronome. | Physical confirmation. |
| Loop | No button command yet; the engine currently uses a fixed four-bar loop. | An explicit loop behavior/UI and button mapping. |
| Capture MIDI | No retrospective capture command. | Rolling note history and capture semantics. |
| Quantise | No button command. Recording already uses the engine's current grid. | A dedicated quantise operation and feedback. |
| Undo / Shift+Undo | No undo/redo command. | Session edit history. |
| Scene up/down; Scene Launch | No application scene command. Native chord/menu functions remain device-owned. | Scene/bank navigation and launching. |
| Stop/Solo/Mute button | No application mode-cycle command; lower session row currently remains mute. | Implement the hardware's contextual modes. |
| Device Select / Device Lock | Alternate pad mode reports are recognized; no device browser/lock command. | Device selection, parameter pages and lock semantics. |
| Shift | Hardware handles mode selection and secondary functions. | Verify that every supported combination leaves the adapter in the expected mode. |
| Settings | Hardware settings menu remains native. | Read-back/configuration UI is not implemented. |
| Octave and Shift+Octave | Native hardware transposition changes the performed notes. | Physical note-range verification. |
| Fixed Chord, Scale, Arp | Native generated notes enter the normal performance path. | No application UI or synchronized state model for these features. |
| Navigation `...` | Hardware navigation can emit computer-keyboard navigation. No app-specific browser integration. | Focus/accessibility integration and a physical navigation test. |

The selected track uses blue session feedback, changing to red while recording; other existing tracks use a different steady palette color. Lower pads use green for audible and red for muted. Empty columns are off. The LCD shows the selected track and Ready/Playing/Recording. Returning from another pad layout to Session repaints cached application feedback without forcing the player's mode selection. LEDs are steady because project-to-hardware MIDI-clock synchronization is not implemented.

Play pauses a running transport and resumes from that position on the next press. Pause ends an active recording and silences sequenced voices and the click, while preserving live held-note and sustain state. Resume waits for the next scheduled note onset instead of reconstructing a sequenced note interrupted by Pause. Stop ends recording, releases notes/sustain, and rewinds to the start. The current feedback contract has no separate paused flag, so the hardware LCD uses Ready when the transport is paused; an explicit Paused LCD state remains a feedback improvement.

Live keys, drum notes, pitch bend and sustain continue through file dialogs without a UI-thread hop or a queued input path. Opening a modal operation first stops recording/transport and blocks project-changing surface commands, so auditioned notes do not alter the recorded project. Accepting a replacement project atomically resets the coordinator's old note and pedal ownership with the engine state; cancelling a dialog leaves subsequently played live notes available until their normal releases. The direct callback path is covered by deterministic input tests, while actual hardware delivery under load still requires measurement.

Transport buttons such as Play, Stop, Record, Loop, Capture MIDI, Quantise, Click and Undo have no host-controllable LED to illuminate. A dark Play button is not a connection failure. Host parameter names are assigned to the eight pots; current-value text is not yet synchronized, so native raw 0–127 values may appear. [Programmer's Reference V1, surface coloring and LCD sections](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=10)

## MIDI conventions and exact application assignments

Human MIDI channels are **1–16**. The C# contracts use **0–15**. This difference matters for both control-surface buttons and the drum channel. Novation calls MIDI note 60 C3; some software calls it C4. Compare note numbers when debugging octave differences.

| Input | Human channel / C# channel | Meaning in this application |
| --- | --- | --- |
| Performance note-on/off | Configurable / parsed as 0–15 | Default channel 10 / 9 selects drums; other performance notes play the selected track. |
| Sustain | Performance channel, CC64 | Sustain on at 64–127, off at 0–63. |
| Pitch bend | Performance channel | 0 minimum, 8192 center, 16383 maximum. |
| Pot CC21–24 | 16 / 15 | Track index `CC - 21`, gain `value / 127`. |
| Pot CC25 | 16 / 15 | Master gain `value / 127`. |
| Pot CC26 | 16 / 15 | Tempo `60 + round(value × 120 / 127)` BPM. |
| Track CC102 / CC103 | 16 / 15 | Selection offset −1 / +1. |
| Play/Stop/Record CC115/116/117 | 16 / 15 | Start/pause/resume toggle / stop and rewind / recording toggle on press. |
| Click CC76 | 16 / 15 | Metronome toggle on press. |
| Session notes 96–99 | 1 / 0, control port | Select track index `note - 96`. |
| Session notes 112–115 | 1 / 0, control port | Toggle mute at index `note - 112`. |

The application adapter ignores button releases and wrong-channel messages. Pot values of zero remain valid gain/tempo commands. Unsupported pad/pot modes produce no invented controls. A channel configured differently on the hardware is not silently inferred: changing the drum channel from its default currently requires a future configurable performance-routing profile.

`MidiMessageParser` also accepts program change, polyphonic/channel pressure, timing clock, Start, Continue and Stop. Parsing is distinct from musical support: those extra events are not currently connected to engine parameters or external-clock transport. SysEx goes through the identity path instead of the ordinary note/CC parser. Messages above 64 bytes are ignored by the receive boundary because no implemented incoming packet requires more.

## Protocol packet reference

The authoritative packet source is [Novation's MK3 Programmer's Reference V1](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf). Values below are hexadecimal unless explicitly marked decimal. These are hardware-interface facts, not a claim that every command is implemented.

### Identity and session lifetime

```text
Universal inquiry (performance output):
F0 7E 7F 06 01 F7

Expected application reply, full-size 37 MK3:
F0 7E <device-id> 06 02 00 20 29 35 01 00 00 <v0> <v1> <v2> <v3> F7

Enter control-surface mode (control output): 9F 0C 7F
Exit control-surface mode:                  9F 0C 00
Select Session pad layout:                 BF 03 02
Select Device pot layout:                  BF 09 02
```

The identity parser requires the complete 17-byte reply, Novation manufacturer `00 20 29`, model byte `35`, application family bytes `01 00 00`, and four version digits 0–9. Other model bytes, bootloader replies, truncation and trailing data are rejected. Bootloader family byte `11` is not an application identity. No device-specific SysEx or control-mode command is sent before this verification. Device-specific SysEx starts `F0 00 20 29 02 0F`; the 88-key MK3 uses a different product header and needs its own profile.

### LCD and configuration

```text
Default LCD row: F0 00 20 29 02 0F 04 <row:00|01> <text> F7
Clear LCD:       F0 00 20 29 02 0F 06 F7
Pot name:        F0 00 20 29 02 0F 07 <38..3F> <text> F7
Pot value:       F0 00 20 29 02 0F 08 <38..3F> <text> F7
Velocity curve:  F0 00 20 29 02 0F 02 <target> <curve> F7
Startup pattern: F0 00 20 29 02 0F 78 <interval> <RGB triplets> F7
```

| Field | Hardware range |
| --- | --- |
| `row` | 0–1 |
| Text | 16 characters per row; app emits printable ASCII only and replaces other characters with `?` |
| `target` | 0 keys, 1 pads |
| `curve` | 0 soft, 1 medium, 2 hard, 3 fixed |
| Startup `interval` | Units of 2 ms |
| Startup RGB | 0–127/component; up to 56 steps |

LCD priority is menu, temporary parameter display, then default display. Temporary values normally appear for five seconds. A menu can therefore cover app text even after a successful send. The codec implements row/clear/pot-name/pot-value packets; velocity-curve and startup-pattern commands above are reference-only and are not sent by the application.

### Pad and pot modes

Mode changes/reporting use CC3 for pads and CC9 for pots on human channel 16. Decimal values:

| Pad value | Layout | Pot value | Layout |
| --- | --- | --- | --- |
| 0 | Custom 0, legacy value | 0 | Custom 0, legacy value |
| 1 | Drum | 1 | Volume |
| 2 | Session | 2 | Device |
| 3 | Scale Chords | 3 | Pan |
| 4 | User Chords | 4 | Send A |
| 5 | Custom 0 | 5 | Send B |
| 6 | Custom 1 | 6 | Custom 0 |
| 7 | Custom 2 | 7 | Custom 1 |
| 8 | Custom 3 | 8 | Custom 2 |
| 9 | Device Select | 9 | Custom 3 |
| 10 | Navigation | — | — |

Session note matrix, decimal:

```text
top:     96  97  98  99 100 101 102 103
bottom: 112 113 114 115 116 117 118 119
```

Drum matrix, decimal (default drum channel 10):

```text
top:    40 41 42 43 48 49 50 51
bottom: 36 37 38 39 44 45 46 47
```

Device Select matrix, decimal:

```text
top:    64 65 66 67 68 69 70 71
bottom: 80 81 82 83 84 85 86 87
```

The same pot CC21–28 data are reused across Device/Volume/Pan/Send modes; the mode report determines their meaning. Continuous-control touch (`9F 0B 7F/00`) and pickup (`9F 0A 7F/00`) are optional hardware features and are not enabled by the app. When touch events are enabled, touch/release use channel 15 CC with values 127/0.

### LED reference

For Session, note/CC channel 1 sets a steady palette color, channel 2 flashes, and channel 3 pulses. Drum color channels are 10/11/12. Velocity/CC value selects a palette entry; value zero clears it. The complete palette and grayscale-only/no-LED controls are in the manufacturer's coloring table. Examples verified against the manual:

```text
Lower-left Session pad, steady red: 90 70 05
Upper-left Session pad, flash green: 91 60 13
Lower-right Session pad, pulse blue: 92 77 2D
Lower-right Session pad, off:        90 77 00
```

Flashing/pulsing can follow MIDI beat clock. The app's feedback currently uses steady colors; its codec exposes a pulsing Session-pad packet for future synchronization work.

### Arpeggiator and scale control catalog

These controls are documented for the control USB interface but are not yet exposed as synchronized application settings.

| Feature | Human channel | CC, decimal | Values |
| --- | --- | --- | --- |
| Arp enable | 1 | 110 | 0 off; nonzero on |
| Arp type | 1 | 85 | 0–6: Up, Down, Up/Down, As Played, Random, Chord, Mutate |
| Arp rate | 1 | 86 | 0–7: quarter, quarter-triplet, eighth, eighth-triplet, sixteenth, sixteenth-triplet, thirty-second, thirty-second-triplet |
| Arp octaves | 1 | 87 | 0–3 → 1–4 octaves |
| Arp latch | 1 | 88 | 0 off; nonzero on |
| Arp gate | 1 | 89 | 0–99 → 0–198% |
| Arp swing | 1 | 90 | 34–94 → −47% to +47% in this protocol |
| Arp rhythm | 1 | 91 | 0–4: Note, Note–Rest–Note, Note–Rest–Rest–Note, Random, Deviate |
| Mutate / Deviate | 1 | 92 / 93 | 0–127 |
| Scale enable | 16 | 14 | 0 off; nonzero on |
| Scale type | 16 | 15 | 0–7, ordered below |
| Scale root | 16 | 16 | 0–11, C upwards by semitone |

Do not substitute physical knob percentages from a user-guide illustration for the documented MIDI-control ranges. Firmware and physical-control descriptions can differ from these encoded CC ranges; preserve the source and verify actual behavior before adding a setting.

## Native creative features

Hardware features generate normal performance notes; IgnitionSwitch does not need to recreate their note transformations. The application also does not currently read back enough hardware state to present synchronized on/off indicators for them.

| Feature | Native role | App boundary |
| --- | --- | --- |
| Scale | Eight scales: Minor, Major, Dorian, Mixolydian, Phrygian, Harmonic Minor, Minor Pentatonic, Major Pentatonic | The default is C minor. The hardware changes performed notes; app scale selection/state is not implemented. |
| Fixed Chord | Store a chord shape and transpose it with the keys | Generated note messages use the performance route. |
| Scale Chord | Predefined chord banks based on the selected scale | Generated chord notes are playable; chord-bank UI is not implemented. |
| User Chord | Store user-defined pad chords in hardware | No chord-memory editor/synchronization in the app. |
| Arp | Seven ordering types, rate/triplet/octave/rhythm, swing, gate, mutate, deviate, latch | Generated notes are playable, but application tempo does not yet drive hardware clock. |
| Arp Control Lock | Keeps native arp controls accessible | Hardware-owned; its pots must not be mistaken for app gain controls. |
| Strum | Uses the modulation wheel to perform the native arp notes | Firmware-dependent native behavior; no app-side strum engine. |

For Scale, press Scale to enable it; Shift+Scale opens its hardware settings. A played note outside the selected scale can be shifted to another note. Several physical keys can therefore produce the same MIDI note. Novation documents a repeated-note behavior in this case; note ownership/retrigger handling must be tested with these collisions, not just one key at a time. [Official MK3 Scale explanation](https://support.novationmusic.com/hc/de/articles/360016062800-Launchkey-MK3-Scale-Mode-explanation)

The complete native interaction instructions, chord-programming gestures, Arp Control Lock/Strum behavior, octave/transposition controls, and settings defaults are in [User Guide V6, pp.28–43](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=28). These remain hardware features even when a corresponding application UI is absent.

## Custom modes, firmware, and setup

[Novation Components](https://components.novationmusic.com/) manages MK3 custom assignments, firmware, wheel calibration and resets. The 37 has four pad Custom Modes and four pot Custom Modes; no fader modes apply. Custom pads can use different message types, channels and momentary/toggle behavior. Pots can use custom CC assignments. The sustain default is CC64; changing it can stop normal sustain recognition. Components can also send computer keystrokes from configured pads, so not every custom-pad action is MIDI. [Official MK3 Components guide](https://support.novationmusic.com/hc/en-gb/articles/360014619639-Launchkey-MK3-Components-Guide)

IgnitionSwitch does not upload custom templates, overwrite chord memories, reinstall firmware or factory-reset the device. Custom performance note messages can play through the ordinary note path, but a general MIDI-learn editor and arbitrary CC/program/keystroke assignments are not implemented.

Start with one connected Launchkey and a USB data cable. If another MIDI application or a Components browser tab is holding a port, close that connection before retrying. Use the full-size **Launchkey 37 [MK3]** download page rather than a similarly named Mini/MK4 page. At the source check on 2026-10-26, it listed **Novation USB Driver 2.30.0.84**, User Guide V6, and Programmer's Reference V1. Check that page and Components for subsequent driver/firmware changes. The app does not require the bundled Ableton software. [Official downloads](https://downloads.novationmusic.com/novation/launchkey-mk3/launchkey-37-mk3)

Easy Start can expose a Novation onboarding USB drive. That drive is not a MIDI endpoint and is not proof of the specific model/firmware. The documented bootloader gesture is holding both Octave buttons while reconnecting USB; it is a maintenance operation, not the normal app connection procedure. Bootloader mode will not pass the app's application-identity check.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No sound, despite a connected keyboard | Confirm computer audio output and master/track gain. The controller itself has no sound engine. Check mute and whether the selected instrument is the expected one. |
| No MIDI ports | Confirm a USB data connection, device power and the official Windows driver. A visible onboarding drive alone is insufficient. Retry Connect after the device finishes enumerating. |
| Model verification timeout | Close other port-owning software, leave bootloader mode, verify the model, and reconnect. The application intentionally declines an unverified or other-model controller. |
| Keys work but buttons/feedback do not | Look for the second USB pair; the status reports missing control ports. Reconnect after closing competing MIDI applications. |
| Pads play unexpected notes | Check Drum, Session, Scale Chord, User Chord or Custom mode. These modes have different meanings and note layouts. |
| Drum pads play the selected melodic instrument | Restore the hardware drum channel to 10 for the current routing profile. Channels are user-configurable. |
| Pots appear inactive | Pan/Send/Custom modes are not mapped to gain. Native Arp controls may have ownership. Hardware pickup can withhold output until a stored value is crossed. |
| A parameter jumps when a pot moves | Pots have absolute positions. Host pickup/value synchronization is unfinished; do not assume an endless encoder. |
| LCD does not immediately show a track name | A native settings menu or temporary parameter display can cover default text. Exit the hardware menu and retry a supported app action. |
| Sustain does not work | Check pedal connection/polarity and Components assignments; the app expects CC64. |
| Tempo differs from the native arpeggiator | External MIDI-clock synchronization is still a gap. The app tempo pot changes the project, not the native arp's clock. |
| Notes remain after a change | Use Stop. Record the active channel, layout and sustain state for diagnosis; unplug handling should release app notes and requires physical testing. |
| More than one Launchkey connected | Current discovery refuses ambiguous pairing. Use one device until an explicit selector is implemented. |

## Implementation, tests, and next verification

| Area | Source/evidence |
| --- | --- |
| Generic adapter contract | `src/IgnitionSwitch.Midi/Contracts/IMusicController.cs`, `MusicControlEvent.cs`, `ControllerFeedback.cs` |
| MK3 37 capabilities and endpoint naming | `src/IgnitionSwitch.Midi/Profiles/LaunchkeyMk3Profile.cs` |
| Conservative endpoint pairing | `src/IgnitionSwitch.Midi/Profiles/LaunchkeyEndpointMatcher.cs` |
| Mode-aware application command mapping | `src/IgnitionSwitch.Midi/Profiles/LaunchkeyControlMapper.cs` |
| Packet creation and identity validation | `src/IgnitionSwitch.Midi/Protocol/LaunchkeyMk3Protocol.cs` |
| Bounded input decoding | `src/IgnitionSwitch.Midi/Protocol/MidiMessageParser.cs` |
| Windows port lifecycle and feedback | `src/IgnitionSwitch.Midi/Devices/LaunchkeyController.cs`, `ControllerSession.cs` |
| Generic command execution and note routing | `src/IgnitionSwitch.App/Services/StudioSession.cs` |
| Automated hardware-independent checks | `tests/IgnitionSwitch.Midi.Tests`: seven groups covering channel semantics, 20,000 seeded malformed messages, identity rejection, exact packet fixtures/ASCII/ranges, naming, pairing, and mode-aware normalized commands. |
| Application input checks | `tests/IgnitionSwitch.App.Tests`: 11 groups in the confirmed run, retaining shared-audio buffer, transport, save-revision, feedback and shutdown checks alongside chord-burst acceptance, overlapping-channel ownership, modal audition and atomic project replacement. No timing thresholds or physical-device I/O are used by these checks. |

Run `dotnet run --project tests/IgnitionSwitch.Midi.Tests --no-restore` after the project has been restored. These checks do not open MIDI ports. The initial library/tests passed strict .NET builds with zero diagnostics; rerun verification after relevant changes.

Next physical evidence should cover each key/wheel/pedal, every mapped session pad/pot/button, mode changes and returning to Session, native scale/chord/arp/strum output, simultaneous notes across track changes, sustain/retrigger collisions, mute feedback and LCD priorities, Launchkey USB removal/reconnection, and MIDI-port contention. Native Save As completion/reopening, human listening and timing measurements remain separate checks. Record the actual device firmware, app revision and outcome without including unique device identifiers. Timing/latency claims require measurements through the chosen audio output.

The seven original HEIC references remain local under `F:\GitHub\IgnitionSwitch\hardware_images` (`IMG_0960(2)` through `IMG_0966(1)` filenames). The folder is gitignored. Photos are visual references, not a protocol source; add reviewed, privacy-safe diagrams only if the project needs a public hardware illustration.

## Future hardware

Keep manufacturer-specific packet bytes, channels, modes and surface layouts inside the MIDI adapter/profile. The application consumes generic music commands and sends generic feedback. Other Launchkey sizes/generations and other hardware types need independently verified identity, capabilities, port behavior and mappings; the MK3 37 header and pad maps must not become global assumptions. No speculative driver or unsupported-device compatibility is claimed by the shared interface.

When adding another device, create its own maintained reference in this directory, link its authoritative manual, and distinguish simulated/protocol tests from physical evidence. Do not silently broaden the MK3 37 profile to accept a new identity.

## Source register

Sources checked on **2026-10-26**. Vendor content can change; preserve the document version and relevant page/section when revising a fact.

| Source | Used for |
| --- | --- |
| [Launchkey 37 MK3 downloads](https://downloads.novationmusic.com/novation/launchkey-mk3/launchkey-37-mk3) | Correct product family, driver listing and current manual links |
| [Programmer's Reference V1](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf) | USB pairs, identity, surface/mode messages, note matrices, LEDs, LCD, configuration and feature commands |
| [User Guide V6](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf) | Physical controls, connectors, native playing features, settings and measurements |
| [Launchkey comparison](https://support.novationmusic.com/hc/en-gb/articles/360017267699-Launchkey-Comparison-Chart) | MK3/Mini/MK4 distinctions and model-specific controls |
| [MK3 Components guide](https://support.novationmusic.com/hc/en-gb/articles/360014619639-Launchkey-MK3-Components-Guide) | Custom assignments, pedal CC, firmware/calibration/reset tooling |
| [MK3 Scale explanation](https://support.novationmusic.com/hc/de/articles/360016062800-Launchkey-MK3-Scale-Mode-explanation) | Native scale transformation and repeated-note behavior |
| [Why Launchkey produces no sound itself](https://support.novationmusic.com/hc/de/articles/360001346265-I-don-t-hear-any-sound-from-my-Launchkey) | MIDI versus audio responsibilities |
