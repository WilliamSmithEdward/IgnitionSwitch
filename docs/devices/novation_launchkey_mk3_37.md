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
| Sound/mixer milestone | Real instrument parameters, stereo pan, delay/room sends and returns, five native pot banks, actual Device Select/Lock and application pickup are implemented. The combined 66-group build/format/informational-analyzer verification passed. A running parameter build was resized independently in X/Y to 1,000 × 700 without horizontal scrolling; the Sound flyout retained aligned labels/values and parameter edit/Undo worked. Audio availability recovered after the initial no-default-output status. Physical pot/page/lock/LCD and listening checks remain separate evidence. |
| Earlier physical hit report | William confirmed that hardware pads flash and matching app pads react in the parameter preview, but reported intermittent missed registration and perceived latency. The steady Core retrigger route passes synthetic checks; a separate dense scheduled-chord case could evict a fresh live attack and now has a fix/regression. Received-input/velocity diagnostics support the counted follow-up below. The broader playing-quality concern is not declared resolved by one short trial. Physical pot testing remains deferred at William's request. |
| Counted physical input follow-up | William confirmed **10 two-finger strikes on different pads**, meaning **20 physical pad contacts**. The received-input counter showed **20**, last velocity **126**, and range **110–127**. This is consistent with one received drum note-on per contact in that trial; it does not prove delivery for every pad or playing pattern, one rendered sound per contact, or comprehensive absence of losses/duplicates. William kept **shared audio**, requesting **10 ms**, and said the updated build seemed better. No exclusive-mode or measured-latency claim follows. |
| Session scenes implementation | The Scene milestone passed **79 hardware-independent groups: 37 Core, 25 application, and 17 MIDI**, together with prettyCSharp and .NET style/analyzer checks at informational severity. Session clip launch, scene navigation, four lower-row contexts, immutable feedback and LED packet/state behavior are implemented. The new Scene workspace and physical Scene/Stop/Solo/Mute/pad/LCD behavior still require a running-app and controller pass. This verification predates the expression integration below and does not upgrade the limited counted Drum trial into Session-hardware evidence. |
| Expression milestone: local verification | The full WinUI **Release/x64** solution build and **87 groups (41 Core + 28 App + 18 MIDI)** passed through `scripts/verify.ps1`, including prettyCSharp and .NET style/analyzer checks at informational severity. Local evidence is `artifacts/expression-verification.log`, with preview output under `artifacts/expression-preview`. The live modulation/pressure response and ownership/lifecycle policy below are implemented and covered by rendered-PCM, routing and coordinator checks. Physical wheel, pressure, pressure-release and native Strum tests remain pending. The 20-contact Drum trial measured received note-ons, not expression. |
| Current output capabilities | A read-only CA DacMagic query found an active 48 kHz/stereo/32-bit float mix. IAudioClient3 shared minimum/fundamental/default/maximum and current periods were all 480 frames (10 ms). The separate exclusive minimum was 3 ms; native 48 kHz stereo float was unsupported in exclusive mode, while PCM16 was supported. No stream was initialized by these queries. The playback build opened shared output with requested 10 ms; exclusive testing is a separate explicit choice. These are device capabilities and requests, not measured MIDI-to-speaker latency. |
| Physical track arrows | William reported left/right reversed with the original mapping. The device profile now maps left to CC103 and right to CC102; this correction follows the physical report rather than the conflicting labels in the reference diagram. On 2026-10-26, William confirmed both physical directions work correctly in the build matching `3fe448d`. Releases and selection limits are covered by regression checks. |
| Physical Drum-mode input and sound | A read-only capture while William used Shift+Drum observed `BF 03 01` followed by `99 25 7E` and `99 25 00` on the second/control MIDI input: mode 1, note 37, channel 10, velocity-zero release. No output or audio ports were opened by the capture tool. After the correction in `3fe448d`, William confirmed that Shift+Drum lights the pads blue and pad strikes produce drum sounds. |
| Application pad hit presentation | Running build `8d64d95` at approximately 1,348 × 834 logical pixels showed the clicked Kick pad flash amber and settle after release. All sixteen pads and four bars remained visible; the shortcut flyout used aligned columns including Undo/Redo. This is application visual evidence, not physical-pad or timing evidence. |
| Quantise menu and compact scaling | The Quantise milestone's running build showed an aligned Loop options menu; the recording-grid checkmark switched off and back on without marking musical content unsaved. Native width and height resizing to approximately 1,000 × 700 logical pixels retained all tracks, four bars, sixteen pads and the full status line without scrollbars. The Kick flash filled the resized pad correctly. The Quantise action was disabled for an empty, non-recording track. |
| Repeat preference in the running app | The Loop milestone's running build displayed a checked Repeat loop menu item. Switching it off changed the header to `4 bars · once`, reopening showed the unchecked state, and switching it back restored `4 bars`. The session remained saved/unchanged. Audio was unavailable, so natural-end playback and physical Loop/LCD behavior remain separate checks. |
| Historical Loop-milestone output-unavailable observation | During the Loop milestone, read-only Windows endpoint enumeration reported CA DacMagic as NotPresent and no default render endpoint for Console, Multimedia or Communications (`0x80070490`). No audio stream was opened and no system setting was changed. This documents that earlier unavailable-output state; the later shared-output follow-up is recorded above. |
| Broader physical playing coverage | Drum-mode lighting/sound and both track-arrow directions are confirmed above. A complete keys, individual pads, pots, wheels and pedal pass, plus broader musical listening, remains incomplete. |
| Actual pad colors/LCD rendering | Blue Drum-mode lighting is confirmed by William. Muted red Drum lighting, Session colors and LCD appearance still need a visual hardware pass. |
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
| Modulation wheel | CC1 uses the last received channel value for smoothed live-only 5.5 Hz vibrato, up to ±30 cents. Hardware-generated Strum notes keep the normal note route. This sound mapping is an IgnitionSwitch choice, not a built-in Launchkey sound. | Physical wheel sweeps, musical listening and native Strum coexistence checks. |
| Pad channel/poly pressure | Owned default-channel Drum pressure routes from the control port: channel pressure requires an active Drum pad; poly pressure requires its matching active note. Zero pressure and retained-pad updates across layout changes are accepted. The live brightness/gain response and current-gate guards are implemented below. | Physical packet/channel/threshold/release/listening checks; no keybed-aftertouch or MPE claim. |
| Sustain pedal | CC64: values ≥64 sustain; lower values release sustained tracks. | Pedal polarity/custom mapping and cross-track playing checks. |
| Top Session pads 1–4 | Launch/relaunch the four clips in the selected scene. Track selection for live keys remains on the Track arrows. | Physical clip launch, queued transitions and LCD checks. |
| Bottom Session pads 1–4 | In Clips context, launch the next visible scene's clips. In Stop/Solo/Mute contexts, stop the corresponding sequenced lane or toggle its real solo/mute state. | Physical context-cycle and per-track behavior review. |
| Remaining Session pads | Columns 5–8 have no corresponding tracks and remain inactive/unlit in both rows. In Clips context, the lower row is also inactive when no next scene exists. | Additional tracks require a separate capacity/mapping change. |
| Drum pad mode | Notes 36–51 on the default drum channel route to the drum track from the performance input or verified Drum-mode control input. Returning to Session still releases held pads. Base colors are blue or muted red; hits request white for at least 80 ms and stay bright while held. Matching app pads use velocity-sensitive feedback and fade after release. William confirms hardware/app reactions, but reports intermittent registration and perceived delay. | Counted-hit/velocity/audio diagnosis, muted red-light verification, bank scrolling and individual-pad sound review. |
| Device pots 1–8 | Selected instrument: Tone, Attack, Release (Tail for drums), Drive, Pan, Delay send, Reverb send, Level. Echo: Time, Feedback, Damping, Return, then four track delay sends. Room: Decay, Damping, Pre-delay, Return, then four track reverb sends. Device mode is selected on connection. | Physical sweeps, listening and native Arp Control Lock interaction. |
| Volume pots 1–4 | Track gains: keys, bass, atmosphere, drums. Audio targets apply directly with dezippering; UI feedback uses visual easing. | Physical fast-turn review. |
| Volume pots 5–8 | Master, project tempo (60–180 BPM), delay return, reverb return. | Physical response and hardware arpeggiator clock synchronization. |
| Pan/Send A/Send B modes | First four pots control actual track pan/delay send/reverb send. Remaining pots have no corresponding tracks and remain unused. | Physical mapping and stereo/effect listening review. |
| Absolute-pot pickup and LCD | App takeover is keyed by bank and actual parameter address. A pot must approach/cross the target before controlling it. External edits invalidate pickup; Custom mode, modal dialogs and reconnects reset stale physical positions. LCD names/current physical values include a pickup-direction hint when needed. | Physical pickup/LCD cadence and rapid page-change review. |
| Track left/right | Left is CC103 and selects the previous track; right is CC102 and selects the next track. Both directions are physically confirmed by William. Selection clamps to the four-track range, and releases are ignored. | Banking if track capacity expands. |
| Play | Start, pause, or resume project playback; pausing preserves the transport position. | Verify the physical button and its interaction with held notes/recording. |
| Stop | Stop playback/recording, release held notes, and rewind the loop transport. | Verify with sustained notes and active recording. |
| Record | Toggle loop recording when audio is running. | Physical overdub review. |
| Click | Toggle the application metronome. | Physical confirmation. |
| Loop | CC118 toggles repeating versus single-pass playback; the Loop options menu has the same Repeat loop preference. Single pass ends at the four-bar boundary, finalizes recording and retains live notes/sustain. Play or Record from End starts at zero. | Physical button/LCD and recording review; variable loop length remains future work. |
| Capture MIDI | CC74 merges the latest retained four-bar live phrase across all tracks as one undoable edit. Track ownership, sustain and held gates are retained; capacity or relevant history loss leaves the project unchanged. The Loop options menu exposes the same action. | Physical button and audible phrase review. |
| Quantise | CC75 corrects selected-track onsets to sixteenths as one undoable edit, preserving durations. The Loop options menu provides the same action and an optional recording grid. | Physical button and musical review. |
| Undo / Shift+Undo | Undo / Redo the latest recording part or mixer edit. DAW channel 1 CC108 tracks Shift; channel 16 CC77 invokes the command on press. History is bounded and resets when loading another project. | Physical gesture/recording review and history descriptions. |
| Scene up/down; Scene Launch | In Session mode, ▲/▼ select the adjacent scene without launching it; the selected scene is the top pad row. Scene Launch `>` launches its four clips together. Navigation clamps at the available scenes. Device Select mode retains its existing device-navigation role; native Drum/Chord/Custom/menu functions remain owned by the device. | Physical arrows, Scene Launch, last-scene boundaries and queued timing review. |
| Stop/Solo/Mute button | In Session mode, cycle **Clips → Stop → Solo → Mute → Clips** on presses. The adapter sends a generic cycle command; the app owns this context. Releases and Shift-menu gestures do not cycle it. | Physical cycle/LED review; no invented long-press or release action. |
| Device Select / Device Lock | While Device Select is held (pad mode 9), ▲/▼ select Instrument/Echo/Room. Top pad 1 selects the one available parameter page; other pages are unlit/inactive. Device Lock retains the actual instrument/control bank across track selection; its white LED reflects the lock. Explicitly selecting another device moves the lock to that device. | Physical hold/release, page/arrow/LCD and lock checks. |
| Shift | Hardware handles mode selection and secondary functions. | Verify that every supported combination leaves the adapter in the expected mode. |
| Settings | Hardware settings menu remains native. | Read-back/configuration UI is not implemented. |
| Octave and Shift+Octave | Native hardware transposition changes the performed notes. | Physical note-range verification. |
| Fixed Chord, Scale, Arp | Native generated notes enter the normal performance path. | No application UI or synchronized state model for these features. |
| Navigation `...` | Hardware navigation can emit computer-keyboard navigation. No app-specific browser integration. | Focus/accessibility integration and a physical navigation test. |

Session pads now describe real clips and the app's lower-row context, as detailed below. Returning from another layout repaints cached Session state without forcing the player's mode selection. Feedback uses steady, flashing and pulsing hardware modes; project-to-hardware MIDI-clock output is still absent, so those animations are not claimed to follow the project tempo.

Play pauses a running transport and resumes from that position on the next press. Pause ends an active recording and silences sequenced voices and the click, while preserving live held-note and sustain state. Resume waits for the next scheduled note onset instead of reconstructing a sequenced note interrupted by Pause. Global Stop ends recording, releases notes/sustain, and rewinds to the start. The feedback contract has no separate paused flag: Session LCD context uses the appropriate row mode plus ready text while paused; other layouts retain Loop ready/One pass ready. A separate Paused LCD indication remains an improvement.

## Session scenes and clip control

### Manufacturer role versus application state

Novation's Ableton integration presents eight track columns and two scene rows. Its Scene Launch button launches the top row. The documented lower-row cycle includes Clips as its fourth state after Stop, Solo and Mute; Clips restores clip launching rather than switching the row off. Stop uses red, Solo blue, and Mute yellow. Muted pads are dim, while audible tracks remain bright. The MK3 guide does not document a momentary or long-press Session alternate. These are the manufacturer's integration roles, not an automatic firmware clip engine or a separate native pad-mode value for each context. [User Guide V6, pp.13–17](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=13)

Keep that handling confined to Session pad mode 2. Native User Chord mode assigns held Stop/Solo/Mute plus a pad to deleting a stored chord; app cycling must not consume that gesture. Shift-menu gestures also remain native. [User Guide V6, p.30](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=30)

IgnitionSwitch implements up to **eight scenes**, each containing four four-bar MIDI clips, one per track. The selected editing scene occupies the top Session row; the next scene occupies the lower row in Clips context. A lower-row clip launch leaves that scene window in place. Track arrows select the live-keyboard track independently of scene selection and clip playback. Columns 5–8 remain unused instead of acquiring unrelated actions.

While playing, clip launches/relaunches and track-clip stops queue for the next bar. Launching from a stopped transport starts the requested clip or scene immediately; a launch queued while paused waits for resumed playback. Scene Launch queues all four lanes for the same boundary. An empty clip stops its lane unless recording is active, when it can become a recording destination. A lower-row Stop affects the sequenced lane and preserves live notes and shared effect tails; global Stop retains its separate transport-reset behavior. Solo is a transient monitoring state, permits multiple soloed tracks, and does not rewrite persistent mute/history; previously fed shared effect tails can finish. Mute changes the actual track mute parameter.

### Implemented Session feedback

| Surface state | Current output |
| --- | --- |
| Stored, non-playing clip with notes | Steady track palette: cyan 37, blue 45, purple 49, pink 57 for the four tracks. |
| Playing clip | Green 19, pulsing. |
| Queued launch | Green 19, flashing; an empty slot may light while queued. |
| Recording clip | Red 5, pulsing, including a recording destination that has no completed notes yet. |
| Pending clip stop | Red 5, flashing. |
| Empty/unavailable clip or unused column | Off, unless the real slot is queued or recording. |
| Lower row: Stop | Bright red 5 for active/queued lanes, dim red 7 when inactive; a queued stop flashes red. Activity includes clips outside the two visible scenes. |
| Lower row: Solo | Bright blue 45 when soloed, dim blue 47 otherwise. |
| Lower row: Mute | Bright yellow 13 when audible, dim yellow 15 when muted. |
| Stop/Solo/Mute button | Off in Clips context; red 5 / blue 45 / yellow 13 in Stop / Solo / Mute. |
| Scene up/down buttons | White 3 when an adjacent scene is available, otherwise off. |
| Scene Launch button | Reflects the top row: recording red pulse, pending stop red flash, queued launch green flash, playing green pulse, otherwise white if it contains notes or off if empty. |

These are implemented host-selected palette/state rules, verified through pure presentation and packet tests. Physical Session colors, animations, press/release transitions and LCD appearance remain unverified. Recording feedback takes priority over stop/launch/play states. Novation's hardware uses MIDI-clock timing for flashing/pulsing, falling back to **120 BPM or its last received clock** when no clock is provided. IgnitionSwitch does not currently send project MIDI clock; an animation's cadence is not a playback- or latency-measurement result. [Programmer's Reference V1, pp.10–13](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=10)

The default Session LCD title is the selected scene name. Its second row includes the lower-row mode and loop/one-pass/ready context, with `Recording` taking priority. Device Select retains its device title; other layouts retain track/transport context. The adapter copies and validates Session collections before queuing them, and deduplicates pad/button, pot-name/value and default-LCD updates. Knob-only changes do not replace the default LCD rows or repaint Session LEDs. Output remains on the demand-driven feedback worker, separate from the direct live-note input path; its approximately 8 ms scheduling interval is not an end-to-end timing guarantee.

The optional generic Session feedback field preserves compatibility with simpler callers: if omitted, the adapter retains the earlier selected-track/top-row and mute/bottom-row colors and track/transport LCD text. The current production coordinator supplies real Session state.

## Modulation and pressure

### Verified hardware facts

The native Settings menu offers **Pad Aftertouch: Off / Channel / Poly**, with **Poly** listed as the default. Pad AT Threshold offers **Low / Medium / High**; the manual's default column prints **Normal**. Those are manufacturer defaults/labels, not observations of William's current setting. Drum MIDI channel is configurable from 1–16, default 10; the listed key and chord defaults are channels 1 and 2. Aftertouch mode and threshold remain native settings; the application does not invent a remote configuration command for them. [User Guide V6, p.41](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=41)

The programmer guide describes poly pressure for Session/Device Select on human channel 1 and Drum pads on human channel 10. Its Drum example prints `AA`, repeating the off-by-one status inconsistency in its `9A` note example. The observed note packets were `99`; the current default-channel profile therefore accepts pressure as **`A9 <note> <pressure>`** and **`D9 <pressure>`**, without accepting `AA`/`DA` aliases. These pressure packets have **not** been captured on William's unit. Channel-pressure support is documented in the Settings table, but the programmer guide does not supply a `D9` example or guarantee pressure emission after a layout change. [Programmer's Reference V1, pp.8–9](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=8)

The wheels remain on the performance USB pair. Native Strum uses the modulation wheel to generate performed notes; a fixed vibrato response is a software sound-design decision. The app does not infer a Strum on/off state or suppress its normal note route. `9F 0B 7F` enables continuous pot/fader touch reports and is unrelated to Pad Aftertouch; it is not a pad-pressure fix. [Programmer's Reference V1, p.6](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=6), [User Guide V6, p.42](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=42)

### Implemented router policy and musical response

The completed router slice leaves performance-port input unchanged. Control-port pressure requires zero-based channel 9 and a value from 0–127. Poly pressure additionally requires note 36–51 with retained Drum-pad ownership. Channel pressure has no note number and requires at least one retained active Drum pad. That eligibility continues across pad-mode exit until the matching note release; this is an application ownership policy, not a verified firmware promise. Pressure never creates a note owner, retriggers a hit, or counts as a new drum note-on. Orphan pressure, invalid data, other channels and Session/Device Select surface pressure are rejected. Coordinator ownership checks remain necessary after Stop, panic or project replacement can clear live gates before physical pad release.

The integrated App/Core response passed local Release/x64 verification and rendered-audio/lifecycle checks:

| Input/state | Implemented behavior |
| --- | --- |
| Modulation wheel CC1 | Smoothed, live-only 5.5 Hz vibrato with maximum ±30-cent pitch depth. Zero depth retains the baseline sound. Recorded clip playback is not modulated by this live expression path. |
| Channel and poly pressure | Live brightness plus at most 15% additional gain, using the maximum of channel and per-note pressure rather than adding them. Base Tone and other persistent sound parameters remain unchanged. |
| Target ownership | Address the current live-note gate and its original MIDI channel. Track/scene selection alone does not retarget already-held expression. |
| Key release and sustain | Poly pressure is ignored after key-up, even while the pedal sustains that gate. Channel pressure and wheel changes may continue to affect the sustained current gate. |
| Stop, modal stop, panic and project load | Clear pressure and live owners. Keep the last received wheel value per channel so later notes follow that retained control position; this does not read an unreported physical position. |
| Disconnect and disposal | Reset cached wheel state as well as pressure/ownership. |
| Expired or replaced gate | Ignore stale expression handles. A finished voice's cached channel pressure cannot leak into a fresh drum strike. |
| Scope | Existing global pitch bend, pedal handling and shared track/pitch release behavior remain unchanged. This is not MPE. No new expression UI, project-schema change or expression-automation recording is part of this slice. |

Physical wheel sweeps, pressure in both native modes, threshold/zero-release behavior, mode changes while holding pads, sustained gates and native Strum coexistence remain pending. The earlier counted 20-contact trial does not verify any of these expression behaviors.

The coordinator rejects late performance and control callbacks once `Controller.IsConnected` is false; a callback captured before disconnection cannot revive expression, start notes or mutate project controls afterward. Reconnection begins with neutral wheel/pressure state. These disconnected-callback and finished-voice stale-pressure cases are covered by integration checks.

`ExpressionCommandTests.HotPath` measures **60,000 reused expression messages** after warming the exact batch and reports **0 additional managed bytes on the coordinator thread**. It also checks that those updates produce no note-activity events, hardware-feedback requests or drum-hit counts, and leave project content/schema, revision and history unchanged. Parsing, message construction, rendering and assertions are outside that allocation window; this is neither a whole MIDI-stack allocation claim nor a hardware timing measurement. The other expression checks compare coordinator-rendered PCM with a reference engine across original-channel ownership, replacement/retrigger, sustain, reset, offline and disconnected-callback cases.

Live keys, drum notes, pitch bend and sustain continue through file dialogs without a UI-thread hop or a queued input path. Opening a modal operation first stops recording/transport and blocks project-changing surface commands, so auditioned notes do not alter the recorded project. Accepting a replacement project atomically resets the coordinator's old note and pedal ownership with the engine state; cancelling a dialog leaves subsequently played live notes available until their normal releases. The direct callback path is covered by deterministic input tests, while actual hardware delivery under load still requires measurement.

Transport buttons such as Play, Stop, Record, Loop, Capture MIDI, Quantise, Click and Undo have no host-controllable LED to illuminate. A dark Play button is not a connection failure. Host parameter names and physical-value text are synchronized for the eight pots through the coalesced feedback worker. [Programmer's Reference V1, surface coloring and LCD sections](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=10)

## MIDI conventions and exact application assignments

Human MIDI channels are **1–16**. The C# contracts use **0–15**. This difference matters for both control-surface buttons and the drum channel. Novation calls MIDI note 60 C3; some software calls it C4. Compare note numbers when debugging octave differences.

| Input | Human channel / C# channel | Meaning in this application |
| --- | --- | --- |
| Performance note-on/off | Configurable / parsed as 0–15 | Default channel 10 / 9 selects drums; other performance notes play the selected track. |
| Drum-mode control-input notes 36–51 | 10 / 9 | While pad mode is 1, the adapter normalizes these as performance for the drum track; owned releases remain valid after a mode change. |
| Modulation CC1 | Performance channel | Live vibrato amount `value / 127`, scoped to current live gates owned by that original channel; cache the last received value for subsequent notes. |
| Owned Drum poly pressure `A9` | 10 / 9, control port | Route note 36–51 pressure 0–127 only for that retained pad; preserve pressure kind/value across layout changes until release. |
| Owned Drum channel pressure `D9` | 10 / 9, control port | Route pressure 0–127 only while at least one Drum pad remains owned; the parsed event's note/number field is zero. |
| Sustain | Performance channel, CC64 | Sustain on at 64–127, off at 0–63. |
| Pitch bend | Performance channel | 0 minimum, 8192 center, 16383 maximum. |
| Pot CC21–28 | 16 / 15 | Absolute slot `CC - 21`, normalized `value / 127`; the reported Device/Volume/Pan/Send bank and actual parameter address determine the target. Pickup precedes mutation. |
| Pot mode CC9 | 16 / 15 | 1 Volume, 2 Device, 3 Pan, 4 Send A, 5 Send B; Custom/unknown layouts do not control project parameters. |
| Device Select page notes 64–71 | 1 / 0, control port | In pad mode 9, request top-row page `note - 64`; only page 0 exists. Lower-row roles are not invented. |
| Device navigation CC106/107 | 16 / 15 | In pad mode 9 only, previous/next sound device on press. |
| Device Lock CC52 | 16 / 15 | Retain/release the actual sound target on press. LED uses `BF 34 7F` / `BF 34 00`. |
| Track left CC103 / right CC102 | 16 / 15 | Selection offset −1 / +1 on press; no action on release. |
| Play/Stop/Record CC115/116/117 | 16 / 15 | Start/pause/resume toggle / stop and rewind / recording toggle on press. |
| Capture MIDI CC74 | 16 / 15 | Request retrospective capture of recent live playing on press; Shift retains the primary action. |
| Quantise CC75 | 16 / 15 | Correct selected-track note onsets on press; Shift retains the same action. |
| Loop CC118 | 16 / 15 | Toggle repeat/single-pass on press; Shift retains the same primary action. |
| Click CC76 | 16 / 15 | Metronome toggle on press. |
| Session notes 96–99 | 1 / 0, control port | In pad mode 2, trigger track column `note - 96`, visible row 0. The app launches that selected-scene clip. |
| Session notes 112–115 | 1 / 0, control port | In pad mode 2, trigger column `note - 112`, row 1. The app applies its Clips/Stop/Solo/Mute context. |
| Scene Launch CC104 | 1 / 0, control port | In pad mode 2, launch the selected/top scene on press. |
| Stop/Solo/Mute CC105 | 1 / 0, control port | In pad mode 2, cycle the lower-row context on press. |
| Scene up/down CC106/107 | 16 / 15, control port | In pad mode 2, select the previous/next scene; in pad mode 9, retain device navigation. Other layouts keep native ownership. |
| Legacy navigation CC104/105 | 16 / 15, control port | Ignored aliases of the up/down arrows, preventing duplicate navigation. They are distinct from channel-1 Scene Launch/mode commands. |

The application adapter ignores button releases and wrong-channel messages. It suppresses Session pad, scene-navigation, Scene Launch and row-cycle gestures while Shift is held, while retaining the supported primary transport/Undo combinations. Pot values of zero remain valid gain/tempo commands. Unsupported pad/pot modes produce no invented controls. A channel configured differently on the hardware is not silently inferred: changing the drum channel from its default currently requires a future configurable performance-routing profile. The button/channel distinction and navigation aliases are shown in [Programmer's Reference V1, p.6](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=6).

The observed Drum input above resolves an inconsistency in Programmer's Reference V1: its incoming Drum example prints `9A` despite describing human channel 10. William's unit sent `99` on the control input. Drum LED steady output uses `99 <note> <palette>` on that control output (`9A` flashes, `9B` pulses); blue is palette 45, red is 5, and zero turns a pad off. For example, bottom-left blue is `99 24 2D`. Input normalization remains limited to the verified layout, channel and note range.

`MidiMessageParser` also accepts program change, timing clock, Start, Continue and Stop; these are not connected to musical program selection or external-clock transport. Pressure decoding, owned Drum routing and the live App/Core expression response are implemented as described above. SysEx goes through the identity path instead of the ordinary note/CC parser. Messages above 64 bytes are ignored by the receive boundary because no implemented incoming packet requires more.

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

The User Guide's Device Select example lights top-row page pads. Its up/down controls choose the previous/next device, while pads select a parameter page. The programmer reference defines both rows' raw notes but does not establish the lower row as pages 9–16. Do not infer that assignment. Device Lock retains the actual device/control bank across track selection; it is more than freezing the interface's selected-track highlight. These are manufacturer roles; the application implementation remains listed in the support table above. [User Guide V6, p.22](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/Launchkey%20MK3%20User%20Guide%20v6%20-%20EN.pdf#page=22)

The same pot CC21–28 data are reused across Device/Volume/Pan/Send modes; the mode report determines their meaning. Continuous-control touch (`9F 0B 7F/00`) and pickup (`9F 0A 7F/00`) are optional hardware features and are not enabled by the app. When touch events are enabled, touch/release use channel 15 CC with values 127/0.

Both features default to disabled on DAW-mode entry. Programmer's Reference V1 does not document a host command that sets a pot's raw pickup target; echoing CC21–28 is therefore unverified. SysEx `08` supplies arbitrary LCD value text, rather than a numeric pickup threshold. IgnitionSwitch implements application takeover using actual parameter addresses, bank identity and observed absolute positions; native pickup remains disabled. Native Arp Control Lock and Custom modes retain their hardware ownership. [Programmer's Reference V1, pp.6, 9 and 15](https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf#page=6)

### LED reference

For Session, note/CC channel 1 sets a steady palette color, channel 2 flashes, and channel 3 pulses. Drum color channels are 10/11/12. Velocity/CC value selects a palette entry; value zero clears it. The complete palette and grayscale-only/no-LED controls are in the manufacturer's coloring table. Examples verified against the manual:

```text
Lower-left Session pad, steady red: 90 70 05
Upper-left Session pad, flash green: 91 60 13
Lower-right Session pad, pulse blue: 92 77 2D
Lower-right Session pad, off:        90 77 00
```

CC-associated RGB buttons use the same animation channels. For example, a steady white Scene Launch indicator is `B0 68 03`, a steady red Stop-context indicator is `B0 69 05`, and white scene-up availability is `B0 6A 03`. These are output packets, distinct from the channel-16 up/down input reports. Flashing/pulsing follows the hardware clock or the documented 120 BPM/last-clock fallback; application-tempo synchronization remains unfinished.

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
| A drum strike seems absent or delayed | Open Devices → Input diagnostics and compare received drum note-ons with a known number of pad contacts. A two-finger strike is two contacts. Note the last/range velocities; the counter excludes releases and on-screen previews and measures MIDI delivery into the coordinator, not rendered sounds, listening quality or latency. Shared audio remains William's selected mode. |
| Session pads no longer select tracks directly | Use Track left/right for the live instrument. The Session grid now launches clips; use scene up/down for the top scene and Stop/Solo/Mute for the lower-row role. |
| Session animations differ from project tempo | No project MIDI clock is sent. Hardware flashing/pulsing can use its 120 BPM/last-clock fallback. |
| Pots appear inactive | Turn through the existing value; the LCD gives a pickup direction. Pan/Send pages use only four track slots. Custom modes and native Arp controls retain hardware ownership. |
| A parameter jumps when a pot moves | Pots have absolute positions. The app resets pickup on reconnect/modal/Custom transitions and reacquires after external edits. Record the bank, actual target and movement if a jump remains reproducible. |
| LCD does not immediately show scene/device context | A native settings menu or temporary parameter display can cover default text. Exit the hardware menu and retry a supported app action. Session normally shows the scene; Device Select shows the sound device. |
| Sustain does not work | Check pedal connection/polarity and Components assignments; the app expects CC64. |
| Tempo differs from the native arpeggiator | External MIDI-clock synchronization is still a gap. The app tempo pot changes the project, not the native arp's clock. |
| Notes remain after a change | Use Stop. Record the active channel, layout and sustain state for diagnosis; unplug handling should release app notes and requires physical testing. |
| More than one Launchkey connected | Current discovery refuses ambiguous pairing. Use one device until an explicit selector is implemented. |

## Implementation, tests, and next verification

| Area | Source/evidence |
| --- | --- |
| Generic adapter contract | `src/IgnitionSwitch.Midi/Contracts/IMusicController.cs`, `MusicControlEvent.cs`, `ControllerFeedback.cs`, `ControllerSessionFeedback.cs`, `ControllerClipFeedback.cs`, `SessionRowMode.cs` |
| MK3 37 capabilities and endpoint naming | `src/IgnitionSwitch.Midi/Profiles/LaunchkeyMk3Profile.cs` |
| Conservative endpoint pairing | `src/IgnitionSwitch.Midi/Profiles/LaunchkeyEndpointMatcher.cs` |
| Mode-aware application command mapping | `src/IgnitionSwitch.Midi/Profiles/LaunchkeyControlMapper.cs` |
| Pure Session LED/LCD state and immutable comparison | `src/IgnitionSwitch.Midi/Profiles/LaunchkeySessionPresentation.cs`, `LaunchkeyFeedbackPresentation.cs`, `src/IgnitionSwitch.Midi/Contracts/ControllerSessionFeedback.cs` |
| Packet creation and identity validation | `src/IgnitionSwitch.Midi/Protocol/LaunchkeyMk3Protocol.cs` |
| Bounded input decoding | `src/IgnitionSwitch.Midi/Protocol/MidiMessageParser.cs` |
| Windows port lifecycle and feedback | `src/IgnitionSwitch.Midi/Devices/LaunchkeyController.cs`, `ControllerSession.cs` |
| Generic command execution, notes and Session state | `src/IgnitionSwitch.App/Services/StudioSession.cs`, `StudioSession.Scenes.cs`; real scene/clip playback resides in `src/IgnitionSwitch.Core/Sequencing/WorkstationEngine.Scenes.cs`. |
| Automated hardware-independent checks | `tests/IgnitionSwitch.Midi.Tests`: **18 groups** passed, including the new `PressureRoutingTests.Routing` fixtures for owned channel/poly pressure, zero values, retained mode-exit ownership, all 16 note slots, duplicate press/release safety and wrong-channel/range/alias rejection. Existing checks cover malformed input, identity, packets, pairing, pots/devices, transport/history, Session feedback and Drum hit feedback. These are software fixtures, not captured pressure packets. |
| Application input, scene and expression integration checks | **28 App groups** passed, including rendered-PCM expression comparisons, lifecycle/invalid-input guards, 60,000 reused-message coordinator updates with 0 measured bytes after warmup, disconnected callbacks and finished-gate pressure cleanup. Existing coverage includes input/ownership, modal audition, Capture, history, sound/pickup, input diagnostics, buffer policies and Session coordination. The full local verification passed **87 groups (41 Core + 28 App + 18 MIDI)** and informational analyzer/format checks. |
| Core API inventory | The current generated Core XML inventory has **205 public entries: 21 types, 34 fields, 68 methods and 82 properties**. Supported musical behavior, evidence and remaining gaps are maintained in the [engine report](../in_memory_engine/music_api_coverage.md). |

Run `dotnet run --project tests/IgnitionSwitch.Midi.Tests --no-restore` after the project has been restored. These checks do not open MIDI ports. The initial library/tests passed strict .NET builds with zero diagnostics; rerun verification after relevant changes.

Next physical evidence should cover Session clip/scene launches, all four lower-row contexts, navigation boundaries, queued changes, mode returns, LEDs and LCD priorities; the new Scene workspace also needs its running-app visual pass. Expression's software integration is verified locally; physical wheel/pressure/native Strum and listening checks remain pending. Continue broader counted playing checks beyond the single 20-contact trial, including simultaneous notes, retriggers, sustain and track changes. Physical pot/page/lock testing remains deferred. Keys, wheels/pedal, native scale/chord/arp/strum, Launchkey USB removal/reconnection and MIDI-port contention remain incomplete. Native Save As completion/reopening, human listening and timing measurements are separate checks. Record the actual firmware, app revision and outcome without unique device identifiers. Timing/latency claims require measurements through the chosen audio output.

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
