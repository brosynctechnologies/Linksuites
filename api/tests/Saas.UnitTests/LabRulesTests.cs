using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.UnitTests;

/// <summary>Wave 4 (CareLink lab): reference ranges by sex and age band, flags, ages and the result summary.</summary>
public class LabRulesTests
{
    private static readonly LabRange Women = new() { Sex = PatientSex.Female, MinAgeYears = 18, Low = 12m, High = 16m, CriticalLow = 7m, CriticalHigh = 20m };
    private static readonly LabRange Men = new() { Sex = PatientSex.Male, MinAgeYears = 18, Low = 13m, High = 17m, CriticalLow = 7m, CriticalHigh = 20m };
    private static readonly LabRange Children = new() { MaxAgeYears = 18, Low = 11m, High = 14m };
    private static readonly LabRange[] Haemoglobin = [Children, Women, Men];

    [Fact]
    public void Picks_the_range_for_sex_and_age_band()
    {
        Assert.Same(Women, LabRules.Pick(Haemoglobin, PatientSex.Female, 30));
        Assert.Same(Men, LabRules.Pick(Haemoglobin, PatientSex.Male, 18)); // the band's lower age is inclusive
        Assert.Same(Children, LabRules.Pick(Haemoglobin, PatientSex.Male, 17)); // the upper age is exclusive
        Assert.Null(LabRules.Pick([Women], PatientSex.Male, 30));
    }

    [Fact]
    public void A_range_for_the_patients_sex_beats_one_for_both()
    {
        var both = new LabRange { Low = 1m, High = 2m };
        var female = new LabRange { Sex = PatientSex.Female, Low = 3m, High = 4m };
        Assert.Same(female, LabRules.Pick([both, female], PatientSex.Female, 40));
        Assert.Same(both, LabRules.Pick([both, female], PatientSex.Male, 40));
    }

    [Theory]
    [InlineData("13", LabFlag.Normal)]
    [InlineData("12", LabFlag.Normal)] // the limits themselves are in range
    [InlineData("16", LabFlag.Normal)]
    [InlineData("11.9", LabFlag.Low)]
    [InlineData("16.1", LabFlag.High)]
    [InlineData("7", LabFlag.CriticalLow)] // at the critical limit is critical
    [InlineData("6.5", LabFlag.CriticalLow)]
    [InlineData("20", LabFlag.CriticalHigh)]
    public void Flags_against_the_range(string value, LabFlag expected) =>
        Assert.Equal(expected, LabRules.Flag(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Women));

    [Fact]
    public void No_value_or_no_limits_is_no_flag()
    {
        Assert.Equal(LabFlag.None, LabRules.Flag(null, Women));
        Assert.Equal(LabFlag.None, LabRules.Flag(5m, null));
        Assert.Equal(LabFlag.None, LabRules.Flag(5m, new LabRange { Sex = PatientSex.Female }));
        Assert.Equal(LabFlag.High, LabRules.Flag(5m, new LabRange { High = 4m })); // one-sided ranges work
    }

    [Fact]
    public void Age_is_whole_years_on_the_day()
    {
        Assert.Equal(17, LabRules.AgeYears(new DateOnly(2008, 11, 21), new DateOnly(2026, 11, 20)));
        Assert.Equal(18, LabRules.AgeYears(new DateOnly(2008, 11, 20), new DateOnly(2026, 11, 20)));
        Assert.Equal(0, LabRules.AgeYears(new DateOnly(2027, 1, 1), new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void Summary_lists_values_with_flags_ranges_and_the_comment()
    {
        var text = LabRules.Summary(
            [("Haemoglobin", "g/dL", "9.1", LabFlag.Low, 12m, 16m), ("Malaria parasites", null, "Not seen", LabFlag.None, null, null),
             ("Potassium", "mmol/L", "6.8", LabFlag.CriticalHigh, 3.5m, 5.0m)], " Haemolysed sample ");
        Assert.Equal("Haemoglobin: 9.1 g/dL (low; ref 12-16)\nMalaria parasites: Not seen\nPotassium: 6.8 mmol/L (critically high; ref 3.5-5)\nComment: Haemolysed sample", text);
    }
}
