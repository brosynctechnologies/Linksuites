using Saas.Application.School;
using Saas.Domain.School;

namespace Saas.UnitTests;

public class ResultRulesTests
{
    private static GradingBand Band(decimal min, decimal max, string grade) => new() { TenantId = Guid.NewGuid(), Min = min, Max = max, Grade = grade, Remark = grade + " remark" };

    private static readonly IReadOnlyList<GradingBand> Bands =
    [
        Band(75, 100, "A1"), Band(70, 74, "B2"), Band(65, 69, "B3"), Band(60, 64, "C4"), Band(55, 59, "C5"),
        Band(50, 54, "C6"), Band(45, 49, "D7"), Band(40, 44, "E8"), Band(0, 39, "F9"),
    ];

    [Fact]
    public void Ties_share_a_position_and_the_next_is_skipped()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var positions = ResultRules.Positions([(a, 90m), (b, 85m), (c, 85m), (d, 70m)]);
        Assert.Equal(1, positions[a]);
        Assert.Equal(2, positions[b]);
        Assert.Equal(2, positions[c]);
        Assert.Equal(4, positions[d]);
    }

    [Fact]
    public void Positions_do_not_depend_on_input_order()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var positions = ResultRules.Positions([(c, 10m), (a, 50m), (b, 30m)]);
        Assert.Equal(1, positions[a]);
        Assert.Equal(2, positions[b]);
        Assert.Equal(3, positions[c]);
    }

    [Theory]
    [InlineData(100, "A1")]
    [InlineData(75, "A1")]
    [InlineData(74.5, "B2")]
    [InlineData(70, "B2")]
    [InlineData(69.9, "B3")]
    [InlineData(40, "E8")]
    [InlineData(39.9, "F9")]
    [InlineData(0, "F9")]
    public void Grades_come_from_the_band_containing_the_total(double total, string grade) =>
        Assert.Equal(grade, ResultRules.Grade((decimal)total, Bands).Grade);

    [Fact]
    public void Average_ignores_subjects_with_no_scores()
    {
        Assert.Equal(60m, ResultRules.Average([80m, null, 40m]));
        Assert.Null(ResultRules.Average([null, null]));
    }

    [Fact]
    public void Average_rounds_to_two_places_away_from_zero()
    {
        Assert.Equal(66.67m, ResultRules.Average([60m, 70m, 70m]));
        Assert.Equal(0.13m, ResultRules.Average([0.125m]));
    }
}
