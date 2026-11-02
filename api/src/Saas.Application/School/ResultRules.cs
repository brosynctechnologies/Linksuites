using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>Pure result rules: grade lookup, competition ranking and averages.</summary>
public static class ResultRules
{
    /// <summary>
    /// The band a total falls in. A band runs from its minimum up to the next band's minimum, so 74.5 belongs to the 70 to 74 band;
    /// the top band takes everything above it (100 included) and a total below the lowest minimum takes the lowest band.
    /// </summary>
    public static GradingBand Grade(decimal total, IReadOnlyList<GradingBand> bands)
    {
        if (bands.Count == 0) throw new ArgumentException("A grading scale needs at least one band.", nameof(bands));
        var ordered = bands.OrderByDescending(b => b.Min).ToList();
        return ordered.FirstOrDefault(b => total >= b.Min) ?? ordered[^1];
    }

    /// <summary>Competition ranking, highest score first: 90, 85, 85, 70 gives 1, 2, 2, 4.</summary>
    public static IReadOnlyDictionary<Guid, int> Positions(IReadOnlyList<(Guid Id, decimal Score)> scores)
    {
        var result = new Dictionary<Guid, int>();
        var ordered = scores.OrderByDescending(s => s.Score).ToList();
        for (var i = 0; i < ordered.Count; i++)
            result[ordered[i].Id] = i > 0 && ordered[i].Score == ordered[i - 1].Score ? result[ordered[i - 1].Id] : i + 1;
        return result;
    }

    /// <summary>Mean of the values that are present, to two decimal places; null when none is.</summary>
    public static decimal? Average(IEnumerable<decimal?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : decimal.Round(present.Sum() / present.Count, 2, MidpointRounding.AwayFromZero);
    }
}
