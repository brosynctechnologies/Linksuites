using System.Globalization;
using Saas.Domain.Care;

namespace Saas.Application.Care;

/// <summary>
/// Wave 4: the lab's pure rules. Which reference range applies to a patient (sex and age band), how a value is flagged against it, the
/// age in whole years on a day, and the plain-text result summary the record and portal show.
/// </summary>
public static class LabRules
{
    /// <summary>
    /// The range for this patient: the first range whose sex matches (or is for both) and whose age band holds <paramref name="ageYears"/>,
    /// preferring a range for the patient's own sex over one for both. Null when none applies.
    /// </summary>
    public static LabRange? Pick(IEnumerable<LabRange> ranges, PatientSex sex, int ageYears)
    {
        bool InBand(LabRange r) => (r.MinAgeYears is not { } min || ageYears >= min) && (r.MaxAgeYears is not { } max || ageYears < max);
        var list = ranges.Where(InBand).ToList();
        return list.FirstOrDefault(r => r.Sex == sex) ?? list.FirstOrDefault(r => r.Sex is null);
    }

    /// <summary>
    /// The flag for a numeric value: critical when at or beyond a critical limit, low or high outside the reference limits, else normal.
    /// <see cref="LabFlag.None"/> when there is no value or no range with a limit.
    /// </summary>
    public static LabFlag Flag(decimal? value, LabRange? range)
    {
        if (value is not { } v || range is null) return LabFlag.None;
        if (range.Low is null && range.High is null && range.CriticalLow is null && range.CriticalHigh is null) return LabFlag.None;
        if (range.CriticalLow is { } cl && v <= cl) return LabFlag.CriticalLow;
        if (range.CriticalHigh is { } ch && v >= ch) return LabFlag.CriticalHigh;
        if (range.Low is { } lo && v < lo) return LabFlag.Low;
        if (range.High is { } hi && v > hi) return LabFlag.High;
        return LabFlag.Normal;
    }

    public static bool IsCritical(LabFlag flag) => flag is LabFlag.CriticalLow or LabFlag.CriticalHigh;

    public static bool IsAbnormal(LabFlag flag) => flag is LabFlag.Low or LabFlag.High or LabFlag.CriticalLow or LabFlag.CriticalHigh;

    /// <summary>Whole years from <paramref name="dateOfBirth"/> to <paramref name="on"/> (never negative).</summary>
    public static int AgeYears(DateOnly dateOfBirth, DateOnly on)
    {
        var age = on.Year - dateOfBirth.Year - (on < dateOfBirth.AddYears(on.Year - dateOfBirth.Year) ? 1 : 0);
        return Math.Max(0, age);
    }

    /// <summary>"12-16", "&gt;= 5", "&lt;= 3.5" or "" for the reference limits.</summary>
    public static string RangeText(decimal? low, decimal? high) => (low, high) switch
    {
        ({ } l, { } h) => $"{Number(l)}-{Number(h)}",
        ({ } l, null) => $">= {Number(l)}",
        (null, { } h) => $"<= {Number(h)}",
        _ => "",
    };

    /// <summary>A decimal without trailing zeros ("5.0" → "5", "3.50" → "3.5").</summary>
    public static string Number(decimal value) => (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    public static string FlagWord(LabFlag flag) => flag switch
    {
        LabFlag.Low => "low",
        LabFlag.High => "high",
        LabFlag.CriticalLow => "critically low",
        LabFlag.CriticalHigh => "critically high",
        _ => "",
    };

    /// <summary>
    /// One line per value ("Haemoglobin: 9.1 g/dL (low; ref 12-16)" or "Malaria parasites: Not seen"), then the comment. This is the result
    /// text the record, the review and the portal show.
    /// </summary>
    public static string Summary(IEnumerable<(string Name, string? Unit, string Value, LabFlag Flag, decimal? Low, decimal? High)> values, string? comment)
    {
        var lines = values.Select(v =>
        {
            var line = $"{v.Name}: {v.Value}{(string.IsNullOrEmpty(v.Unit) ? "" : " " + v.Unit)}";
            var notes = new List<string>();
            if (FlagWord(v.Flag) is { Length: > 0 } word) notes.Add(word);
            if (RangeText(v.Low, v.High) is { Length: > 0 } range) notes.Add($"ref {range}");
            return notes.Count == 0 ? line : $"{line} ({string.Join("; ", notes)})";
        }).ToList();
        if (!string.IsNullOrWhiteSpace(comment)) lines.Add($"Comment: {comment.Trim()}");
        return string.Join("\n", lines);
    }
}
