using Saas.Application.Bookings;
using Saas.Domain.Bookings;

namespace Saas.UnitTests;

public class AvailabilityEngineTests
{
    private static readonly TimeZoneInfo Lagos = AvailabilityEngine.Zone("Africa/Lagos");
    private static readonly TimeZoneInfo London = AvailabilityEngine.Zone("Europe/London");
    private static readonly DateOnly Saturday = new(2026, 10, 17);
    private static readonly Guid Tunde = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Ada = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static ScheduleInterval Work(DateOnly date, int fromHour, int toHour, int fromMinute = 0, int toMinute = 0) =>
        new() { Weekday = (int)date.DayOfWeek, Start = new TimeOnly(fromHour, fromMinute), End = new TimeOnly(toHour, toMinute) };

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0, TimeZoneInfo? zone = null) =>
        AvailabilityEngine.ToInstant(date, hour * 60 + minute, zone ?? Lagos)!.Value;

    private static AvailabilityInput Input(
        IReadOnlyList<StaffInput> staff, int duration = 60, int interval = 30, int before = 0, int after = 0,
        (TimeOnly, TimeOnly)? hours = null, DateTimeOffset? earliest = null, DateOnly? date = null, TimeZoneInfo? zone = null) =>
        new(date ?? Saturday, zone ?? Lagos, hours ?? (new TimeOnly(9, 0), new TimeOnly(18, 0)), duration, before, after, interval,
            earliest ?? DateTimeOffset.MinValue, staff);

    private static List<string> Times(IEnumerable<SlotResult> slots, TimeZoneInfo? zone = null) =>
        slots.Select(s => TimeZoneInfo.ConvertTime(s.StartsAt, zone ?? Lagos).ToString("HH:mm")).ToList();

    [Fact]
    public void Offers_slots_on_the_grid_that_fit_before_closing()
    {
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 12)], [])]));
        Assert.Equal(["09:00", "09:30", "10:00", "10:30", "11:00"], Times(slots));
        Assert.Equal(At(Saturday, 10), slots[2].StartsAt);
        Assert.Equal(At(Saturday, 11), slots[2].EndsAt);
    }

    [Fact]
    public void Schedule_is_clipped_to_opening_hours_and_a_closed_day_has_nothing()
    {
        var staff = new[] { new StaffInput(Tunde, [Work(Saturday, 7, 20)], []) };
        var slots = AvailabilityEngine.Compute(Input(staff, hours: (new TimeOnly(9, 0), new TimeOnly(11, 0))));
        Assert.Equal(["09:00", "09:30", "10:00"], Times(slots));

        var closed = AvailabilityEngine.Compute(Input(staff) with { Hours = null });
        Assert.Empty(closed);
    }

    [Fact]
    public void Gaps_in_the_schedule_are_breaks()
    {
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 12), Work(Saturday, 13, 15)], [])]));
        Assert.DoesNotContain("11:30", Times(slots));
        Assert.DoesNotContain("12:00", Times(slots));
        Assert.Contains("13:00", Times(slots));
        Assert.Equal("14:00", Times(slots)[^1]);
    }

    [Fact]
    public void Existing_bookings_and_time_off_block_overlapping_slots_only()
    {
        var busy = new[] { new Span(At(Saturday, 10), At(Saturday, 11)) };
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 13)], busy)]));
        // 09:00-10:00 ends exactly when the booking starts (allowed); 09:30 and 10:00/10:30 overlap.
        Assert.Equal(["09:00", "11:00", "11:30", "12:00"], Times(slots));
    }

    [Fact]
    public void Buffers_keep_space_around_other_bookings_but_may_run_past_closing()
    {
        var busy = new[] { new Span(At(Saturday, 11), At(Saturday, 12)) };
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 13)], busy)], before: 15, after: 15));
        // 10:00 + 60 + 15 clean-up = 11:15 overlaps the 11:00 booking; 12:00 needs 11:45 prep, also overlapping.
        Assert.Equal(["09:00", "09:30"], Times(slots).Where(t => string.CompareOrdinal(t, "12:00") < 0).ToList());
        Assert.DoesNotContain("12:00", Times(slots));
        Assert.Contains("12:00", Times(AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 13)], busy)], before: 0, after: 15))));
        // The last slot ends at closing; its 15-minute clean-up runs past 13:00, which is fine.
        Assert.Equal("12:00", Times(AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 13)], [])], after: 15)))[^1]);
    }

    [Fact]
    public void Minimum_notice_hides_slots_that_start_too_soon()
    {
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 12)], [])], earliest: At(Saturday, 10, 10)));
        Assert.Equal(["10:30", "11:00"], Times(slots));
    }

    [Fact]
    public void Anyone_available_merges_staff_per_slot()
    {
        var slots = AvailabilityEngine.Compute(Input(
        [
            new StaffInput(Tunde, [Work(Saturday, 9, 11)], [new Span(At(Saturday, 9), At(Saturday, 10))]),
            new StaffInput(Ada, [Work(Saturday, 9, 11)], []),
        ]));
        Assert.Equal([Ada], slots.Single(s => Times([s])[0] == "09:00").StaffIds);
        Assert.Equal([Tunde, Ada], slots.Single(s => Times([s])[0] == "10:00").StaffIds.OrderBy(g => g).ToList());
    }

    [Fact]
    public void Grid_aligns_to_the_clock_not_to_an_odd_shift_start()
    {
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(Saturday, 9, 11, fromMinute: 10)], [])], duration: 30, interval: 15));
        Assert.Equal("09:15", Times(slots)[0]);
    }

    [Fact]
    public void Spring_forward_gap_has_no_slots_and_durations_stay_real()
    {
        // UK clocks go 01:00 -> 02:00 on 29 March 2026.
        var day = new DateOnly(2026, 3, 29);
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(day, 0, 4)], [])],
            duration: 30, interval: 30, hours: (new TimeOnly(0, 0), new TimeOnly(4, 0)), date: day, zone: London));
        var times = Times(slots, London);
        Assert.DoesNotContain("01:00", times);
        Assert.DoesNotContain("01:30", times);
        Assert.Contains("02:00", times);
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndsAt - s.StartsAt));
    }

    [Fact]
    public void Fall_back_hour_is_offered_once()
    {
        // UK clocks go 02:00 -> 01:00 on 25 October 2026; 01:00-01:59 happens twice.
        var day = new DateOnly(2026, 10, 25);
        var slots = AvailabilityEngine.Compute(Input([new StaffInput(Tunde, [Work(day, 0, 3)], [])],
            duration: 30, interval: 30, hours: (new TimeOnly(0, 0), new TimeOnly(3, 0)), date: day, zone: London));
        Assert.Single(slots, s => TimeZoneInfo.ConvertTime(s.StartsAt, London).ToString("HH:mm") == "01:00");
    }

    [Fact]
    public void Ignore_hours_allows_manual_bookings_outside_schedule_but_not_over_bookings()
    {
        var busy = new[] { new Span(At(Saturday, 20), At(Saturday, 21)) };
        var input = Input([new StaffInput(Tunde, [], busy)], interval: 60) with { Hours = null, IgnoreHours = true };
        var times = Times(AvailabilityEngine.Compute(input));
        Assert.Contains("07:00", times);
        Assert.DoesNotContain("20:00", times);
        Assert.Contains("21:00", times);
    }

    [Theory]
    [InlineData(DepositType.None, 0, 0)]
    [InlineData(DepositType.Fixed, 500_000, 500_000)]
    [InlineData(DepositType.Fixed, 9_000_000, 1_500_000)] // never more than the price
    [InlineData(DepositType.Percent, 30, 450_000)]
    public void Deposit_is_fixed_or_a_share_of_the_price(DepositType type, long value, long expected) =>
        Assert.Equal(expected, new BookableService { Name = "Braids", PriceMinor = 1_500_000, DepositType = type, DepositValue = value }.DepositMinor);

    /// <summary>booking-web's depositFor() previews with Math.floor so it shows exactly this (B7).</summary>
    [Fact]
    public void Percent_deposit_rounds_down_to_the_kobo() =>
        Assert.Equal(40_738, new BookableService { Name = "Braids", PriceMinor = 123_450, DepositType = DepositType.Percent, DepositValue = 33 }.DepositMinor);
}
