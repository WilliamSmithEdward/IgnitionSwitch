using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;

internal static class EndpointMatchingTests
{
    public static void Names()
    {
        foreach (string name in new[]
        {
            "LAUNCHKEY", "Launchkey MK3 37", "MIDIIN2 (Launchkey MK3 37)", "Launchkey 37 MK3",
            "LKMK3 MIDI", "LKMK3 DAW"
        })
        {
            Check.That(
                LaunchkeyMk3Profile.IsCandidateName(name),
                $"Candidate {name} was overlooked."
            );
        }

        foreach (string name in new[]
        {
            "Launchpad Pro MK3", "Launchkey Mini MK3", "Launchkey 37 MK4",
            "Microsoft GS Wavetable Synth"
        })
        {
            Check.That(
                !LaunchkeyMk3Profile.IsCandidateName(name),
                $"Other product {name} was selected."
            );
        }

        Check.That(
            LaunchkeyMk3Profile.IsDawName("MIDIIN2 (LAUNCHKEY)")
                && LaunchkeyMk3Profile.IsDawName("MIDIOUT2 (LAUNCHKEY)"),
            "Windows second ports were not recognized."
        );
        Check.That(
            LaunchkeyMk3Profile.IsDawName("LKMK3 DAW")
                && LaunchkeyMk3Profile.IsDawName("Launchkey (Port 2)"),
            "Named DAW ports were not recognized."
        );
        Check.That(
            !LaunchkeyMk3Profile.IsDawName("Launchkey MK3 37"),
            "Performance port was mistaken for DAW."
        );
    }

    public static void Pairing()
    {
        MidiEndpointInfo input = Endpoint("input", MidiEndpointDirection.Input, "one", false);
        MidiEndpointInfo output = Endpoint("output", MidiEndpointDirection.Output, "one", false);
        MidiEndpointInfo dawInput = Endpoint("daw-input", MidiEndpointDirection.Input, "one", true);
        MidiEndpointInfo dawOutput = Endpoint(
            "daw-output",
            MidiEndpointDirection.Output,
            "one",
            true
        );
        Check.That(
            LaunchkeyEndpointMatcher.TryMatch(
                [dawOutput, input, dawInput, output],
                out LaunchkeyEndpointSet? set
            )
                && set?.PerformanceInput.Id
                == "input"
                && set.DawOutput?.Id
                == "daw-output",
            "Same-device pair failed independent of enumeration order."
        );
        Check.That(
            LaunchkeyEndpointMatcher.TryMatch([input, output], out set) && set?.DawInput is null,
            "Performance-only discovery should be usable."
        );
        Check.That(
            LaunchkeyEndpointMatcher.TryMatch(
                [
                    input with { ContainerId = null },
                    output with { ContainerId = null }
                ],
                out _
            ),
            "Single unnamed-container pair failed."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch(
                [
                    input with { ContainerId = null },
                    output with { ContainerId = null },
                    dawInput with { ContainerId = null },
                    dawOutput with { ContainerId = null }
                ],
                out _
            ),
            "Control ports without device-container metadata were paired."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch(
                [
                    input with { ContainerId = null },
                    output with { ContainerId = null },
                    dawOutput with { ContainerId = null }
                ],
                out _
            ),
            "An additional unidentified control port was accepted."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch(
                [
                    input with { ContainerId = "" },
                    output with { ContainerId = "" },
                    dawInput with { ContainerId = "" },
                    dawOutput with { ContainerId = "" }
                ],
                out _
            ),
            "Empty device-container metadata was treated as a known device."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch(
                [
                    input,
                    output with { ContainerId = "two" }
                ],
                out _
            ),
            "Input/output crossed physical containers."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch(
                [
                    input,
                    output,
                    input with { Id = "second-input" }
                ],
                out _
            ),
            "Ambiguous performance input was guessed."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch(
                [
                    input,
                    output,
                    dawInput,
                    dawInput with { Id = "second-daw" }
                ],
                out _
            ),
            "Ambiguous control input was guessed."
        );
        Check.That(
            !LaunchkeyEndpointMatcher.TryMatch([input], out _),
            "Missing output was accepted."
        );
        Check.That(!LaunchkeyEndpointMatcher.TryMatch([], out _), "Empty inventory was accepted.");
        MidiEndpointInfo unrelated = Endpoint(
            "unrelated",
            MidiEndpointDirection.Input,
            "two",
            false
        ) with
        {
            IsLaunchkeyCandidate = false
        };
        Check.That(
            LaunchkeyEndpointMatcher.TryMatch([unrelated, input, output], out _),
            "Unrelated devices interfered with pairing."
        );
    }

    private static MidiEndpointInfo Endpoint(
        string id,
        MidiEndpointDirection direction,
        string container,
        bool daw
    )
    {
        return new(id, "Launchkey MK3 37", direction, container, true, daw);
    }
}
