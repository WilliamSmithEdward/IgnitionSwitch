using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;

internal static class PadFeedbackTests
{
    public static void Layout()
    {
        ControllerPadLayout layout = LaunchkeyMk3Profile.Model37.DrumPadLayout;
        int[] expected = [40, 41, 42, 43, 48, 49, 50, 51, 36, 37, 38, 39, 44, 45, 46, 47];
        Check.That(
            layout.Columns == 8 && layout.Notes.SequenceEqual(expected),
            "The shared layout must match the MK3's physical top and bottom pad rows."
        );
        int[] callerNotes = [36, 37, 38, 39];
        var detached = new ControllerPadLayout(2, callerNotes);
        callerNotes[0] = 99;
        Check.That(
            detached.Notes[0] == 36,
            "A caller must not mutate the layout after construction."
        );
        Check.Throws<NotSupportedException>(() => ((IList<int>)detached.Notes)[0] = 99);
        Check.Throws<ArgumentNullException>(() => _ = new ControllerPadLayout(2, null!));
        Check.Throws<ArgumentOutOfRangeException>(() => _ = new ControllerPadLayout(0, [36]));
        Check.Throws<ArgumentOutOfRangeException>(() => _ = new ControllerPadLayout(129, [36]));
        Check.Throws<ArgumentException>(() => _ = new ControllerPadLayout(2, [36, 37, 38]));
        Check.Throws<ArgumentException>(() => _ = new ControllerPadLayout(2, [36, 36]));
        Check.Throws<ArgumentException>(() => _ = new ControllerPadLayout(1, [-1]));
        Check.Throws<ArgumentException>(() => _ = new ControllerPadLayout(1, [128]));
        Check.Throws<ArgumentException>(() => _ = new ControllerPadLayout(1, []));
    }

    public static void Hits()
    {
        PadHitState state = Create();
        Check.That(!state.HasPendingRelease(0), "Idle feedback must need no timer work.");
        Check.That(state.Update(new(37, 126, true), 0), "A valid hit must be accepted.");
        Color(state, 37, 0, 3);
        Color(state, 37, 10_000, 3);
        Check.That(
            !state.HasPendingRelease(10_000),
            "An unchanged held pad must not need polling."
        );
        _ = state.Update(new(37, 0, false), 10_001);
        Color(state, 37, 10_001, 45);
        Check.That(
            !state.HasPendingRelease(10_001),
            "A long-held release can restore immediately."
        );

        _ = state.Update(new(36, 100, true), 20_000);
        _ = state.Update(new(36, 0, false), 20_001);
        Color(state, 36, 20_008, 3);
        Color(state, 36, 20_087, 3);
        Check.That(state.HasPendingRelease(20_087), "A rapid tap must retain its visible pulse.");
        Color(state, 36, 20_088, 45);
        Check.That(!state.HasPendingRelease(20_088), "An expired pulse must stop timer work.");

        _ = state.Update(new(38, 80, true), 30_000);
        _ = state.Update(new(38, 0, false), 30_010);
        Color(state, 38, 30_011, 3);
        _ = state.Update(new(38, 127, true), 30_070);
        _ = state.Update(new(38, 0, false), 30_071);
        Color(state, 38, 30_078, 3);
        Color(state, 38, 30_157, 3);
        Color(state, 38, 30_158, 45);

        for (int burst = 0; burst < 64; burst++)
        {
            long now = 40_000 + burst;

            foreach (int note in LaunchkeyMk3Profile.Model37.DrumPadLayout.Notes)
            {
                _ = state.Update(new(note, 127, true), now);
                _ = state.Update(new(note, 0, false), now);
            }
        }

        foreach (int note in LaunchkeyMk3Profile.Model37.DrumPadLayout.Notes)
        {
            Color(state, note, 40_142, 3);
            Color(state, note, 40_221, 3);
            Color(state, note, 40_222, 45);
        }

        Check.That(!state.HasPendingRelease(40_222), "A coalesced chord burst must fully settle.");

        _ = state.Update(new(37, 127, true), 50_000);
        _ = state.Update(new(37, 0, false), 50_001);
        Color(state, 37, 51_000, 3);
        Color(state, 37, 51_079, 3);
        Color(state, 37, 51_080, 45);

        _ = state.Update(new(39, 127, true), 60_000);
        _ = state.Update(new(39, 0, false), 60_001);
        Color(state, 39, 60_008, 3);
        Color(state, 39, 60_087, 3);
        Check.That(
            state.HasPendingRelease(60_100),
            "Crossing the pulse deadline during output must still schedule the base-color frame."
        );
        Check.That(
            state.TryGetColor(39, 60_100, true, false, out int restored) && restored == 45,
            "The overdue release frame must restore the base color."
        );
        Check.That(
            state.HasPendingRelease(60_101),
            "Planning the base color must not finish work before its output is acknowledged."
        );
        state.MarkPainted(39, false, 60_102);
        Check.That(
            !state.HasPendingRelease(60_102),
            "Acknowledging the base-color frame must stop polling."
        );
    }

    public static void Context()
    {
        PadHitState state = Create();
        _ = state.Update(new(37, 100, true), 0);
        Check.That(
            !state.TryGetColor(37, 1, false, false, out int color) && color == 0,
            "Drum activity must never produce a Session-layout LED packet."
        );
        Color(state, 37, 1, 3, true);
        _ = state.Update(new(37, 0, false), 2);
        Color(state, 37, 79, 3, true);
        Color(state, 37, 81, 5, true);
        Color(state, 37, 81, 45);

        _ = state.Update(new(40, 127, true), 100);
        Check.That(
            !state.TryGetColor(40, 110, false, true, out _),
            "Leaving Drum mode must suppress activity writes without stranding held state."
        );
        Color(state, 40, 120, 3, true);
        _ = state.Update(new(40, 0, false), 121);
        Color(state, 40, 200, 5, true);

        _ = state.Update(new(51, 127, true), 200);
        _ = state.Update(new(36, 127, true), 201);
        _ = state.Update(new(36, 0, false), 202);
        state.Clear();
        Color(state, 51, 203, 45);
        Color(state, 36, 203, 5, true);
        Check.That(
            !state.HasPendingRelease(203),
            "Stop/reset must clear held and transient state."
        );

        _ = state.Update(new(37, 127, true), 204);
        _ = state.Update(new(37, 0, false), 205);
        Check.That(
            state.HasPendingRelease(10_000),
            "An unpainted tap remains pending when no output has acknowledged it."
        );
        state.Clear();
        Check.That(
            !state.HasPendingRelease(10_000),
            "Discarding unpaintable feedback must stop polling on a performance-only connection."
        );
        Color(state, 37, 10_000, 45);

        _ = state.Update(new(51, 100, true), 10_004);
        _ = state.Update(new(51, 0, true), 10_005);
        Color(state, 51, 10_010, 3);
        Color(state, 51, 10_090, 45);

        foreach (PadFeedback invalid in new[]
        {
            new PadFeedback(-1, 127, true),
            new PadFeedback(128, 127, true),
            new PadFeedback(60, 127, true),
            new PadFeedback(36, -1, true),
            new PadFeedback(36, 128, true)
        })
        {
            Check.That(
                !state.Update(invalid, 10_100),
                "Invalid or unmapped feedback must be ignored."
            );
        }

        Check.That(
            !state.TryGetColor(60, 10_100, true, false, out _),
            "A note outside the hardware layout must not produce a color."
        );
        Check.Throws<ArgumentOutOfRangeException>(() => state.Update(new(36, 127, true), -1));
        Check.Throws<ArgumentOutOfRangeException>(() => state.HasPendingRelease(-1));
        Check.Throws<ArgumentNullException>(() => _ = new PadHitState(null!));
    }

    private static PadHitState Create()
    {
        return new(LaunchkeyMk3Profile.Model37.DrumPadLayout);
    }

    private static void Color(
        PadHitState state,
        int note,
        long now,
        int expected,
        bool muted = false
    )
    {
        bool valid = state.TryGetColor(note, now, true, muted, out int actual);
        Check.That(
            valid && actual == expected,
            $"Pad {note} at {now}ms must have palette {expected}."
        );
        state.MarkPainted(note, actual == 3, now);
    }
}
