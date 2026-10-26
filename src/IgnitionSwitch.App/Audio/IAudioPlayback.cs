namespace IgnitionSwitch.App.Audio;

/// <summary>The audio-output lifecycle used by a studio session, independent of its playback driver.</summary>
internal interface IAudioPlayback : IDisposable
{
    string Status { get; }
    bool IsRunning { get; }
    AudioOutputMode Mode { get; set; }

    void Start();

    void EnsureRunning();
}
