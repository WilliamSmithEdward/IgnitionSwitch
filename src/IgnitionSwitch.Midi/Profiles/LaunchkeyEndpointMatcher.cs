using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Conservative, side-effect-free pairing of Windows MIDI endpoints.</summary>
public static class LaunchkeyEndpointMatcher
{
    /// <summary>Accepts one unambiguous Launchkey device, never mixing different Windows containers.</summary>
    public static bool TryMatch(
        IReadOnlyList<MidiEndpointInfo> inventory,
        out LaunchkeyEndpointSet? result
    )
    {
        ArgumentNullException.ThrowIfNull(inventory);
        result = null;
        MidiEndpointInfo[][] groups =
        [
            .. inventory
                .Where(endpoint => endpoint.IsLaunchkeyCandidate)
                .GroupBy(
                    endpoint => string.IsNullOrWhiteSpace(endpoint.ContainerId)
                        ? "unknown"
                        : endpoint.ContainerId
                )
                .Select(group => group.ToArray())
        ];

        // Multiple candidates require an explicit user device picker in a future version.
        if (groups.Length != 1)
        {
            return false;
        }

        MidiEndpointInfo[] group = groups[0];

        // Without device container metadata, support only one performance pair.
        if (string.IsNullOrWhiteSpace(group[0].ContainerId) && group.Length != 2)
        {
            return false;
        }

        MidiEndpointInfo[] performanceInputs =
        [
            .. group.Where(
                endpoint => endpoint.Direction == MidiEndpointDirection.Input && !endpoint.IsDaw
            )
        ];
        MidiEndpointInfo[] performanceOutputs =
        [
            .. group.Where(
                endpoint => endpoint.Direction == MidiEndpointDirection.Output && !endpoint.IsDaw
            )
        ];
        MidiEndpointInfo[] dawInputs =
        [
            .. group.Where(
                endpoint => endpoint.Direction == MidiEndpointDirection.Input && endpoint.IsDaw
            )
        ];
        MidiEndpointInfo[] dawOutputs =
        [
            .. group.Where(
                endpoint => endpoint.Direction == MidiEndpointDirection.Output && endpoint.IsDaw
            )
        ];

        if (
            performanceInputs.Length != 1
            || performanceOutputs.Length != 1
            || dawInputs.Length > 1
            || dawOutputs.Length > 1
        )
        {
            return false;
        }

        result = new(
            performanceInputs[0],
            performanceOutputs[0],
            dawInputs.SingleOrDefault(),
            dawOutputs.SingleOrDefault()
        );
        return true;
    }
}
