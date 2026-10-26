using System.Globalization;

namespace IgnitionSwitch.Core.Domain;

/// <summary>Cached parameter metadata and normalized-to-physical conversion.</summary>
public sealed class ParameterDescriptor
{
    private readonly bool logarithmic;
    private readonly double defaultPhysical;

    internal ParameterDescriptor(
        ParameterId id,
        string name,
        string unit,
        double minimum,
        double maximum,
        double nativeDefault,
        bool logarithmic = false
    )
    {
        Id = id;
        Name = name;
        Unit = unit;
        Minimum = minimum;
        Maximum = maximum;
        defaultPhysical = nativeDefault;
        this.logarithmic = logarithmic;
        DefaultNormalized = (float)Normalize(nativeDefault);
    }

    /// <summary>The parameter identity.</summary>
    public ParameterId Id { get; }

    /// <summary>The concise displayed name.</summary>
    public string Name { get; }

    /// <summary>The physical unit, or an empty string for a normalized amount.</summary>
    public string Unit { get; }

    /// <summary>The minimum physical value.</summary>
    public double Minimum { get; }

    /// <summary>The maximum physical value.</summary>
    public double Maximum { get; }

    /// <summary>The canonical normalized default, preserving the exact native physical default.</summary>
    public float DefaultNormalized { get; }

    /// <summary>Converts a finite normalized value from zero through one into physical units.</summary>
    public double ToPhysical(double normalized)
    {
        return !double.IsFinite(normalized)
            || normalized is < 0
                or > 1
                    ? throw new ArgumentOutOfRangeException(nameof(normalized))
                    : normalized == DefaultNormalized
                        ? defaultPhysical
                        : logarithmic
                            ? Minimum * Math.Pow(Maximum / Minimum, normalized)
                            : Minimum + ((Maximum - Minimum) * normalized);
    }

    /// <summary>Converts a finite physical value within the declared range into a normalized value.</summary>
    public double ToNormalized(double physical)
    {
        return !double.IsFinite(physical) || physical < Minimum || physical > Maximum
            ? throw new ArgumentOutOfRangeException(nameof(physical))
            : physical == defaultPhysical ? DefaultNormalized : Normalize(physical);
    }

    /// <summary>Formats a normalized value in physical units for presentation or controller feedback.</summary>
    public string Format(double normalized)
    {
        double physical = ToPhysical(normalized);

        return Id == ParameterId.TrackMute
            ? normalized >= 0.5 ? "Muted" : "On"
            : physical.ToString("0.###", CultureInfo.InvariantCulture)
                + (Unit.Length == 0 ? string.Empty : " " + Unit);
    }

    private double Normalize(double physical)
    {
        return logarithmic
            ? Math.Log(physical / Minimum) / Math.Log(Maximum / Minimum)
            : (physical - Minimum) / (Maximum - Minimum);
    }
}
