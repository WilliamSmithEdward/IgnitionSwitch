using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;

internal static class SessionFeedbackTests
{
    private static readonly ControllerClipFeedback Empty = new(
        true,
        false,
        false,
        false,
        false,
        false
    );
    private static readonly ControllerClipFeedback Stored = Empty with { HasNotes = true };

    public static void PadsAndButtons()
    {
        ControllerFeedback feedback = Feedback(SessionRowMode.Clips);
        ControllerSessionFeedback session = feedback.Session!;
        Check.That(
            LaunchkeySessionPresentation.Pad(feedback, 0).Color != 0
                && LaunchkeySessionPresentation.Pad(feedback, 1) == default
                && LaunchkeySessionPresentation.Pad(feedback, 8).Color != 0
                && LaunchkeySessionPresentation.Pad(feedback, 9) == default,
            "Stored clips must be visible while empty slots and unavailable scenes remain dark."
        );

        for (int column = 4; column < 8; column++)
        {
            Check.That(
                LaunchkeySessionPresentation.Pad(feedback, column) == default
                    && LaunchkeySessionPresentation.Pad(feedback, column + 8) == default,
                "Unused track columns must remain dark in both rows."
            );
        }

        (ControllerClipFeedback Clip, LaunchkeyLedFeedback Light)[] states =
        [
            (Stored with { Playing = true }, new(19, LaunchkeyLedMode.Pulsing)),
            (Empty with { Queued = true }, new(19, LaunchkeyLedMode.Flashing)),
            (Empty with { Recording = true }, new(5, LaunchkeyLedMode.Pulsing)),
            (
                Stored with
                {
                    Playing = true,
                    StopQueued = true
                },
                new(5, LaunchkeyLedMode.Flashing)
            ),
            (new(false, true, true, true, true, true), default)
        ];

        foreach ((ControllerClipFeedback clip, LaunchkeyLedFeedback light) in states)
        {
            ControllerClipFeedback[] upper = [clip, Empty, Empty, Empty];
            ControllerFeedback changed = feedback with
            {
                Session = session with { UpperClips = upper }
            };
            Check.That(
                LaunchkeySessionPresentation.Pad(changed, 0) == light,
                "Clip feedback must distinguish queued, playing, recording, stopping, and unavailable slots."
            );
            Check.That(
                LaunchkeySessionPresentation.Button(changed, 104) == light,
                "The Scene Launch indicator must retain actual pending, playback, and recording state in its top row."
            );
        }

        foreach (SessionRowMode mode in Enum.GetValues<SessionRowMode>())
        {
            ControllerFeedback changed = feedback with
            {
                Session = session with { RowMode = mode }
            };
            Check.That(
                LaunchkeySessionPresentation.Pad(changed, 0)
                    == LaunchkeySessionPresentation.Pad(feedback, 0),
                "Changing lower-row context must retain the upper clip row."
            );
            int modeColor = mode switch
            {
                SessionRowMode.Stop => 5,
                SessionRowMode.Solo => 45,
                SessionRowMode.Mute => 13,
                SessionRowMode.Clips => 0,
                _ => 0
            };
            Check.That(
                LaunchkeySessionPresentation.Button(changed, 105)
                    == new LaunchkeyLedFeedback(modeColor),
                "The mode button must identify all four app-owned row contexts."
            );

            foreach (bool playing in new[] { false, true })
            {
                foreach (bool looping in new[] { false, true })
                {
                    string detail = LaunchkeySessionPresentation.DisplayDetail(
                        changed with
                        {
                            Playing = playing,
                            LoopEnabled = looping
                        },
                        2
                    );
                    Check.That(
                        detail.Length <= 16
                            && detail.StartsWith(mode.ToString(), StringComparison.Ordinal),
                        "The LCD must retain row-mode and transport context within its16-character width."
                    );
                }
            }
        }

        ControllerFeedback stop = feedback with
        {
            Session = session with
            {
                RowMode = SessionRowMode.Stop,
                TrackActive = new[] { true, false, false, false },
                TrackStopQueued = new[] { false, true, false, false }
            }
        };
        Check.That(
            LaunchkeySessionPresentation.Pad(stop, 8) == new LaunchkeyLedFeedback(5)
                && LaunchkeySessionPresentation.Pad(stop, 9)
                    == new LaunchkeyLedFeedback(5, LaunchkeyLedMode.Flashing)
                && LaunchkeySessionPresentation.Pad(stop, 10) == new LaunchkeyLedFeedback(7),
            "Stop feedback must include active offscreen clips, pending stops, and inactive tracks."
        );
        ControllerFeedback solo = feedback with
        {
            Session = session with
            {
                RowMode = SessionRowMode.Solo,
                TrackSolo = new[] { true, false, false, false }
            }
        };
        Check.That(
            LaunchkeySessionPresentation.Pad(solo, 8).Color == 45
                && LaunchkeySessionPresentation.Pad(solo, 9).Color == 47,
            "Solo feedback must brighten enabled tracks and dim others."
        );
        ControllerFeedback mute = feedback with
        {
            TrackMuted = new[] { true, false, false, false },
            Session = session with { RowMode = SessionRowMode.Mute }
        };
        Check.That(
            LaunchkeySessionPresentation.Pad(mute, 8).Color == 15
                && LaunchkeySessionPresentation.Pad(mute, 9).Color == 13,
            "Mute feedback must be dim when muted and bright when audible."
        );
        Check.That(
            LaunchkeySessionPresentation.Button(feedback, 106).Color == 0
                && LaunchkeySessionPresentation.Button(feedback, 107).Color == 3
                && LaunchkeySessionPresentation.Button(feedback, 104).Color == 3,
            "Navigation and Scene Launch must reflect real availability."
        );
        Check.That(
            LaunchkeySessionPresentation.DisplayTitle(feedback, 2) == "Verse"
                && LaunchkeySessionPresentation.DisplayTitle(
                    feedback with { DeviceName = "Echo" },
                    9
                )
                    == "Echo"
                && LaunchkeySessionPresentation.DisplayDetail(feedback with { Recording = true }, 2)
                    == "Recording",
            "LCD layout context and recording priority must remain correct."
        );
        VerifyCodec();
    }

    public static void SnapshotsAndDedupe()
    {
        ControllerFeedback source = Feedback(SessionRowMode.Clips);
        ControllerSessionFeedback session = source.Session!;
        ControllerClipFeedback[] upper =
        [
            Stored with { Playing = true },
            Empty,
            Empty,
            Empty
        ];
        ControllerClipFeedback[] lower =
        [
            Stored,
            Empty with { Queued = true },
            Empty,
            Empty
        ];
        bool[] solo = [true, false, false, false];
        source = source with
        {
            Session = session with
            {
                UpperClips = upper,
                LowerClips = lower,
                TrackSolo = solo
            }
        };
        ControllerFeedback copied = LaunchkeyFeedbackPresentation.CreateSnapshot(source);
        Check.That(
            ControllerSessionFeedback.HasSameState(source.Session, copied.Session),
            "Omitted global activity must compare equal to its normalized visible-clip fallback."
        );
        upper[0] = Empty;
        lower[1] = Empty;
        solo[0] = false;
        ControllerSessionFeedback snapshot = copied.Session!;
        Check.That(
            snapshot.UpperClips[0].Playing
                && snapshot.LowerClips[1].Queued
                && snapshot.TrackSolo[0]
                && snapshot.TrackActive![0]
                && snapshot.TrackActive[1],
            "Session feedback must detach collections and infer omitted global activity from visible clips."
        );
        Check.Throws<NotSupportedException>(
            () => ((IList<ControllerClipFeedback>)snapshot.UpperClips)[0] = Empty
        );
        bool[] active = [true, false, false, false];
        bool[] queued = [false, true, false, false];
        ControllerFeedback explicitActivity = LaunchkeyFeedbackPresentation.CreateSnapshot(
            copied with
            {
                Session = snapshot with
                {
                    TrackActive = active,
                    TrackStopQueued = queued
                }
            }
        );
        active[0] = false;
        queued[1] = false;
        Check.That(
            explicitActivity.Session!.TrackActive![0]
                && explicitActivity.Session.TrackStopQueued![1],
            "Explicit offscreen activity arrays must be copied before crossing threads."
        );
        Check.That(
            LaunchkeyFeedbackPresentation.HasSameState(
                copied,
                LaunchkeyFeedbackPresentation.CreateSnapshot(copied)
            ),
            "Detached equal Session snapshots must deduplicate by content."
        );
        ControllerSessionFeedback[] changes =
        [
            snapshot with { SceneName = "Chorus" },
            snapshot with { RowMode = SessionRowMode.Stop },
            snapshot with { CanMoveUp = true },
            snapshot with { CanMoveDown = false },
            snapshot with { TrackSolo = new[] { false, false, false, false } },
            snapshot with { TrackActive = new[] { false, true, false, false } },
            snapshot with { TrackStopQueued = new[] { true, false, false, false } },
            snapshot with { UpperClips = new[] { Stored, Empty, Empty, Empty } },
            snapshot with { LowerClips = new[] { Stored, Empty, Empty, Empty } }
        ];

        foreach (ControllerSessionFeedback changed in changes)
        {
            Check.That(
                !LaunchkeyFeedbackPresentation.HasSameState(
                    copied,
                    copied with { Session = changed }
                ),
                "Every Session field affecting hardware must invalidate cached feedback."
            );
        }

        ControllerSessionFeedback[] invalid =
        [
            session with { UpperClips = [] },
            session with { LowerClips = [] },
            session with { TrackSolo = [] },
            session with { TrackActive = [] },
            session with { TrackStopQueued = [] },
            session with { RowMode = (SessionRowMode)127 },
            session with { UpperClips = null! }
        ];

        foreach (ControllerSessionFeedback bad in invalid)
        {
            Check.Throws<ArgumentException>(
                () => LaunchkeyFeedbackPresentation.CreateSnapshot(source with { Session = bad })
            );
        }

        ControllerFeedback legacy = source with { Session = null };
        Check.That(
            LaunchkeyFeedbackPresentation.CreateSnapshot(legacy).Session is null
                && LaunchkeySessionPresentation.Pad(legacy, 8).Color == 19,
            "Omitted Session state must preserve legacy track feedback."
        );
    }

    private static ControllerFeedback Feedback(SessionRowMode mode)
    {
        return new(
            0,
            false,
            false,
            ["Keys", "Bass", "Pad", "Drums"],
            [false, false, false, false],
            Session: new(
                "Verse",
                mode,
                [Stored, Empty, Empty, Empty],
                [
                    Stored,
                    default,
                    Empty,
                    Empty
                ],
                [false, false, false, false],
                false,
                true
            )
        );
    }

    private static void VerifyCodec()
    {
        Check.Bytes(
            [0x91, 96, 19],
            LaunchkeyMk3Protocol.SetSessionPad(
                0,
                new LaunchkeyLedFeedback(19, LaunchkeyLedMode.Flashing)
            )
        );
        Check.Bytes(
            [0x92, 112, 5],
            LaunchkeyMk3Protocol.SetSessionPad(
                8,
                new LaunchkeyLedFeedback(5, LaunchkeyLedMode.Pulsing)
            )
        );
        Check.Bytes(
            [0xB0, 104, 3],
            LaunchkeyMk3Protocol.SetRgbControl(104, new LaunchkeyLedFeedback(3))
        );
        Check.Bytes(
            [0xB1, 105, 5],
            LaunchkeyMk3Protocol.SetRgbControl(
                105,
                new LaunchkeyLedFeedback(5, LaunchkeyLedMode.Flashing)
            )
        );
        Check.Bytes(
            [0xB2, 106, 19],
            LaunchkeyMk3Protocol.SetRgbControl(
                106,
                new LaunchkeyLedFeedback(19, LaunchkeyLedMode.Pulsing)
            )
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetRgbControl(-1, default)
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetRgbControl(128, default)
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetRgbControl(104, new LaunchkeyLedFeedback(-1))
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetRgbControl(104, new LaunchkeyLedFeedback(128))
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetSessionPad(
                0,
                new LaunchkeyLedFeedback(3, (LaunchkeyLedMode)3)
            )
        );
    }
}
