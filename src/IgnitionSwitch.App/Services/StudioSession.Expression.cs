using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

namespace IgnitionSwitch.App.Services;

internal sealed partial class StudioSession
{
    private readonly LiveExpressionOwner[] expressionOwners = new LiveExpressionOwner[WorkstationEngine.TrackCount
        * 128];
    private readonly double[] channelModulation = new double[16];
    private readonly double[] channelPressure = new double[16];

    private void BeginExpressiveNote(int channel, int track, int note, int velocity)
    {
        // A finished or stolen gate cannot carry channel pressure into a new strike.
        UpdateChannelExpression(channel);
        ForgetExpressionOwner(track, note);
        LiveNoteHandle handle = Engine.BeginLiveNote(
            track,
            note,
            velocity,
            Audio.IsRunning,
            channelModulation[channel],
            channelPressure[channel]
        );
        expressionOwners[(track * 128) + note] = new(handle, channel);
    }

    private void SetChannelModulation(int channel, double amount)
    {
        channelModulation[channel] = amount;
        UpdateChannelExpression(channel);
    }

    private void SetChannelPressure(int channel, double amount)
    {
        // Pressure without an owned gate must not color a later, unrelated strike.
        channelPressure[channel] = HasExpressionOwner(channel) ? amount : 0;
        UpdateChannelExpression(channel);
    }

    private void SetPolyphonicPressure(int channel, int note, double amount)
    {
        for (int track = 0; track < WorkstationEngine.TrackCount; track++)
        {
            int index = (track * 128) + note;
            ref LiveExpressionOwner owner = ref expressionOwners[index];

            if (owner.Handle.IsValid && owner.Channel == channel && owner.KeyDown)
            {
                owner.Pressure = amount;
                UpdateExpression(index);
            }
        }
    }

    private void UpdateChannelExpression(int channel)
    {
        for (int index = 0; index < expressionOwners.Length; index++)
        {
            LiveExpressionOwner owner = expressionOwners[index];

            if (owner.Handle.IsValid && owner.Channel == channel)
            {
                UpdateExpression(index);
            }
        }
    }

    private void UpdateExpression(int index)
    {
        LiveExpressionOwner owner = expressionOwners[index];

        if (
            !Engine.SetLiveExpression(
                owner.Handle,
                channelModulation[owner.Channel],
                Math.Max(channelPressure[owner.Channel], owner.Pressure)
            )
        )
        {
            ForgetExpressionOwner(index / 128, index % 128);
        }
    }

    private void ReleaseExpressionOwner(int track, int note, int channel)
    {
        int index = (track * 128) + note;
        ref LiveExpressionOwner owner = ref expressionOwners[index];

        if (!owner.Handle.IsValid || owner.Channel != channel)
        {
            return;
        }

        if (pedalDown && sustainedTracks.Contains(track))
        {
            owner.KeyDown = false;
            owner.Pressure = 0;
            UpdateExpression(index);
        }

        else
        {
            ForgetExpressionOwner(track, note);
        }
    }

    private void ClearReleasedExpressionOwners()
    {
        for (int index = 0; index < expressionOwners.Length; index++)
        {
            LiveExpressionOwner owner = expressionOwners[index];

            if (owner.Handle.IsValid && !owner.KeyDown)
            {
                ForgetExpressionOwner(index / 128, index % 128);
            }
        }
    }

    private void ForgetExpressionOwner(int track, int note)
    {
        int index = (track * 128) + note;
        LiveExpressionOwner owner = expressionOwners[index];

        if (!owner.Handle.IsValid)
        {
            return;
        }

        _ = Engine.SetLiveExpression(owner.Handle, 0, 0);
        expressionOwners[index] = default;

        if (!HasExpressionOwner(owner.Channel))
        {
            channelPressure[owner.Channel] = 0;
        }
    }

    private bool HasExpressionOwner(int channel)
    {
        foreach (LiveExpressionOwner owner in expressionOwners)
        {
            if (owner.Handle.IsValid && owner.Channel == channel)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearExpressionState(bool resetModulation)
    {
        Array.Clear(expressionOwners);
        Array.Clear(channelPressure);

        if (resetModulation)
        {
            Array.Clear(channelModulation);
        }
    }

    private struct LiveExpressionOwner(LiveNoteHandle handle, int channel)
    {
        public LiveNoteHandle Handle { get; } = handle;
        public int Channel { get; } = channel;
        public bool KeyDown { get; set; } = true;
        public double Pressure { get; set; }
    }
}
