namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Hardware-independent banks for eight absolute surface controls.</summary>
public enum ControllerControlBank
{
    /// <summary>Track levels, master, tempo, and effect returns.</summary>
    Volume,
    /// <summary>The selected instrument or effect parameter page.</summary>
    Device,
    /// <summary>Track stereo position.</summary>
    Pan,
    /// <summary>Track delay sends.</summary>
    SendA,
    /// <summary>Track reverb sends.</summary>
    SendB,
    /// <summary>Controls owned by the hardware's custom configuration.</summary>
    Custom
}
