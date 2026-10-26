using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;

internal static class FeedbackPresentationTests
{
    public static void Snapshots()
    {
        string[] names =
        [
            "Keys",
            null!
        ];
        bool[] muted = [false, true];
        ControllerKnobFeedback[] knobs =
        [
            new("Tone", "50%"),
            new(null!, null!),
            null!
        ];
        var source = new ControllerFeedback(0, true, false, names, muted, false, knobs, null, true);
        ControllerFeedback snapshot = LaunchkeyFeedbackPresentation.CreateSnapshot(source);
        names[0] = "Changed";
        muted[0] = true;
        knobs[0] = new("Changed", "0%");
        Check.That(
            snapshot.TrackNames[0] == "Keys" && !snapshot.TrackMuted[0],
            "Queued feedback retained mutable caller track collections."
        );
        Check.That(
            snapshot.TrackNames[1] == string.Empty && snapshot.DeviceName == string.Empty,
            "Nullable text was not normalized before queuing."
        );
        Check.That(
            snapshot.DeviceLocked && !snapshot.LoopEnabled && snapshot.Playing,
            "Snapshot cloning lost device or transport fields."
        );
        IReadOnlyList<ControllerKnobFeedback> copied = snapshot.Knobs
            ?? throw new InvalidOperationException("Snapshot omitted its knob slots.");
        Check.That(
            copied.Count == 8 && copied[0] == new ControllerKnobFeedback("Tone", "50%"),
            "Queued knob feedback was not detached and bounded to eight slots."
        );

        for (int index = 1; index < copied.Count; index++)
        {
            Check.That(
                copied[index].Name == string.Empty && copied[index].Value == string.Empty,
                "Null entries, fields, and omitted slots must normalize to blank display text."
            );
        }

        Check.Throws<NotSupportedException>(
            () => ((IList<string>)snapshot.TrackNames)[0] = "Changed"
        );
        Check.Throws<NotSupportedException>(
            () => ((IList<ControllerKnobFeedback>)copied)[0] = new("", "")
        );
        ControllerFeedback fallback = LaunchkeyFeedbackPresentation.CreateSnapshot(
            source with { Knobs = null }
        );
        Check.That(
            LaunchkeyFeedbackPresentation.KnobAt(fallback, 0).Name == "Tone"
                && LaunchkeyFeedbackPresentation.KnobAt(fallback, 7).Name == "Level",
            "Optional knob feedback must use the initial instrument parameter page."
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyFeedbackPresentation.KnobAt(snapshot, -1)
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyFeedbackPresentation.KnobAt(snapshot, 8)
        );
    }

    public static void Dedupe()
    {
        var source = new ControllerFeedback(
            0,
            false,
            false,
            ["Keys"],
            [false],
            DeviceName: "Synth"
        );
        ControllerFeedback previous = LaunchkeyFeedbackPresentation.CreateSnapshot(source);
        ControllerFeedback detached = LaunchkeyFeedbackPresentation.CreateSnapshot(previous);
        Check.That(
            LaunchkeyFeedbackPresentation.HasSameState(previous, detached),
            "Equal detached snapshots must not repaint hardware."
        );
        Check.That(
            !LaunchkeyFeedbackPresentation.HasSameState(
                previous,
                previous with { DeviceName = "Echo" }
            )
                && !LaunchkeyFeedbackPresentation.HasSameState(
                    previous,
                    previous with { DeviceLocked = true }
                ),
            "Device selection and lock state must invalidate feedback independently."
        );

        for (int index = 0; index < 8; index++)
        {
            ControllerKnobFeedback[] knobs = [.. previous.Knobs!];
            knobs[index] = knobs[index] with { Name = "New name" };
            ControllerFeedback changed = previous with { Knobs = knobs };
            Check.That(
                !LaunchkeyFeedbackPresentation.HasSameState(previous, changed),
                "Every knob name must participate in feedback deduplication."
            );
            Check.That(
                LaunchkeyFeedbackPresentation.HasSameTrackState(previous, changed),
                "A knob-only update must not require track LED repainting."
            );
            knobs[index] = previous.Knobs![index] with { Value = "New value" };
            Check.That(
                !LaunchkeyFeedbackPresentation.HasSameState(previous, changed),
                "Every knob value must participate in feedback deduplication."
            );
        }

        Check.That(
            !LaunchkeyFeedbackPresentation.HasSameTrackState(
                previous,
                previous with { TrackMuted = [true] }
            ),
            "Mute changes must repaint track state."
        );
        Check.That(
            !LaunchkeyFeedbackPresentation.HasSameState(null, previous),
            "Initial feedback must always paint."
        );
    }
}
