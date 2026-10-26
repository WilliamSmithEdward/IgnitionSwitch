using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App.Services;

internal sealed class AbsoluteKnobPickup
{
    private const double Tolerance = 1.0 / 127;
    private readonly State[] states = new State[8];

    public bool Accept(
        int knob,
        ControllerControlBank bank,
        ParameterAddress address,
        double physical,
        double target
    )
    {
        ref State state = ref states[knob];

        if (
            !state.HasTarget
            || state.Address != address
            || state.Bank != bank
            || state.Target != target
        )
        {
            state.Latched = false;
        }

        state.Address = address;
        state.Bank = bank;
        state.HasTarget = true;
        state.Target = target;

        if (
            Math.Abs(physical - target) <= Tolerance
            || (state.HasPhysical
                && target >= Math.Min(state.Physical, physical) - Tolerance
                && target <= Math.Max(state.Physical, physical) + Tolerance)
        )
        {
            state.Latched = true;
        }

        state.Physical = physical;
        state.HasPhysical = true;
        return state.Latched;
    }

    public void Applied(int knob, double target)
    {
        states[knob].Target = target;
    }

    public void Observe(int knob, double physical)
    {
        states[knob].Physical = physical;
        states[knob].HasPhysical = true;
    }

    public string Direction(int knob, double target)
    {
        State state = states[knob];
        return state.HasTarget && state.HasPhysical && (!state.Latched || state.Target != target)
            ? state.Physical < target ? "Pickup > " : "Pickup < "
            : string.Empty;
    }

    public void Invalidate()
    {
        for (int index = 0; index < states.Length; index++)
        {
            states[index].HasTarget = false;
            states[index].Latched = false;
        }
    }

    public void Reset()
    {
        Array.Clear(states);
    }

    private struct State
    {
        public ParameterAddress Address;
        public ControllerControlBank Bank;
        public double Target;
        public double Physical;
        public bool HasTarget;
        public bool HasPhysical;
        public bool Latched;
    }
}
