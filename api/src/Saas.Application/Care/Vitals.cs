using Saas.Domain.Care;

namespace Saas.Application.Care;

/// <summary>Where a reading falls against its range.</summary>
public enum VitalLevel { Normal, Low, High, Critical }

/// <summary>
/// One vital sign's range. Normal is <see cref="NormalMin"/>–<see cref="NormalMax"/> inclusive (a null bound is open). Critical is
/// below <see cref="CriticalBelow"/>, or above <see cref="CriticalHigh"/> (at or above it when <see cref="CriticalHighInclusive"/>).
/// </summary>
public sealed record VitalRange(
    string Label, decimal? NormalMin, decimal? NormalMax, decimal? CriticalBelow, decimal? CriticalHigh, bool CriticalHighInclusive)
{
    public VitalLevel Classify(decimal value)
    {
        if (CriticalBelow is { } low && value < low) return VitalLevel.Critical;
        if (CriticalHigh is { } high && (CriticalHighInclusive ? value >= high : value > high)) return VitalLevel.Critical;
        if (NormalMin is { } min && value < min) return VitalLevel.Low;
        if (NormalMax is { } max && value > max) return VitalLevel.High;
        return VitalLevel.Normal;
    }

    /// <summary>The flag for a reading ("High temperature", "Critical SpO2"), or null when it's normal.</summary>
    public string? FlagFor(decimal value) => Classify(value) switch
    {
        VitalLevel.Normal => null,
        var level => $"{level} {Label}",
    };
}

/// <summary>
/// The clinic's vital-sign range table: the one place the ranges live (triage here, ward charting in Task 9). Flags read like
/// "High temperature" and "Critical SpO2"; a critical flag notifies the patient's doctor.
/// </summary>
public static class Vitals
{
    /// <summary>Temperature: normal 36.1–37.8 °C; critical below 35 or at or above 40.</summary>
    public static readonly VitalRange Temperature = new("temperature", 36.1m, 37.8m, 35m, 40m, CriticalHighInclusive: true);

    /// <summary>Systolic: normal 90–139; critical below 80 or at or above 180.</summary>
    public static readonly VitalRange Systolic = new("systolic pressure", 90m, 139m, 80m, 180m, CriticalHighInclusive: true);

    /// <summary>Diastolic: normal 60–89; critical at or above 120.</summary>
    public static readonly VitalRange Diastolic = new("diastolic pressure", 60m, 89m, null, 120m, CriticalHighInclusive: true);

    /// <summary>Pulse: normal 60–100; critical below 40 or above 140.</summary>
    public static readonly VitalRange Pulse = new("pulse", 60m, 100m, 40m, 140m, CriticalHighInclusive: false);

    /// <summary>Respiratory rate: normal 12–20; critical below 8 or above 30.</summary>
    public static readonly VitalRange RespRate = new("respiratory rate", 12m, 20m, 8m, 30m, CriticalHighInclusive: false);

    /// <summary>SpO2: normal 95 or above; critical below 90.</summary>
    public static readonly VitalRange SpO2 = new("SpO2", 95m, null, 90m, null, CriticalHighInclusive: false);

    private const string CriticalPrefix = "Critical ";

    /// <summary>The flags for a set of readings, in table order (temperature, systolic, diastolic, pulse, respiratory rate, SpO2).</summary>
    public static List<string> Flag(VitalSigns v)
    {
        var readings = new (VitalRange Range, decimal? Value)[]
        {
            (Temperature, v.TemperatureC), (Systolic, v.Systolic), (Diastolic, v.Diastolic), (Pulse, v.Pulse), (RespRate, v.RespRate), (SpO2, v.SpO2),
        };
        return readings.Where(r => r.Value is not null).Select(r => r.Range.FlagFor(r.Value!.Value)).OfType<string>().ToList();
    }

    /// <summary>True when any flag is critical.</summary>
    public static bool IsCritical(IEnumerable<string> flags) => flags.Any(f => f.StartsWith(CriticalPrefix, StringComparison.Ordinal));

    /// <summary>Body-mass index (kg/m², one decimal) when both weight and height are given.</summary>
    public static decimal? Bmi(decimal? weightKg, decimal? heightCm)
    {
        if (weightKg is not { } w || heightCm is not { } h || w <= 0 || h <= 0) return null;
        var metres = h / 100m;
        return Math.Round(w / (metres * metres), 1, MidpointRounding.AwayFromZero);
    }
}
