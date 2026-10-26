using IgnitionSwitch.Core.Sequencing;
using NAudio.Wave;

namespace IgnitionSwitch.App.Audio;

internal sealed class EngineSampleProvider(WorkstationEngine engine) : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(
        WorkstationEngine.SampleRate,
        2
    );

    public int Read(float[] buffer, int offset, int count)
    {
        engine.Render(buffer, offset, count);
        return count;
    }
}
