using Saas.Application.School;

namespace Saas.UnitTests;

public class ReportCardTextTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(10, "10th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(30, "30th")]
    [InlineData(101, "101st")]
    [InlineData(111, "111th")]
    [InlineData(112, "112th")]
    [InlineData(113, "113th")]
    public void Ordinals_follow_english_rules(int n, string expected) => Assert.Equal(expected, ReportCardText.Ordinal(n));

    [Fact]
    public void Position_reads_as_ordinal_of_class_size() => Assert.Equal("2nd of 30", ReportCardText.Position(2, 30));

    [Fact]
    public void Attendance_lists_present_late_absent_and_excused() =>
        Assert.Equal("Present 1, late 1, absent 0, excused 0 of 2 days", ReportCardText.Attendance(1, 1, 0, 0, 2));
}
