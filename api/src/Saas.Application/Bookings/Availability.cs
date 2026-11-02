using Saas.Domain.Bookings;

namespace Saas.Application.Bookings;

/// <summary>A time range [Start, End), as instants.</summary>
public readonly record struct Span(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Overlaps(Span other) => Start < other.End && other.Start < End;
}

public sealed record StaffInput(Guid StaffId, IReadOnlyList<ScheduleInterval> Schedule, IReadOnlyList<Span> Busy);

public sealed record AvailabilityInput(
    DateOnly Date,
    TimeZoneInfo Zone,
    /// <summary>Business opening hours for that weekday, or null when closed.</summary>
    (TimeOnly Open, TimeOnly Close)? Hours,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int IntervalMinutes,
    /// <summary>No slot may start before this (now + minimum notice).</summary>
    DateTimeOffset EarliestStart,
    IReadOnlyList<StaffInput> Staff,
    /// <summary>Ignore opening hours and schedules (manual bookings outside hours); busy times still apply.</summary>
    bool IgnoreHours = false);

public sealed record SlotResult(DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> StaffIds);

/// <summary>
/// Pure slot computation, kept free of I/O so every rule is unit-tested.
/// A slot is offered for a staff member when the service time fits inside their working time (their schedule
/// intersected with opening hours) and the service plus its buffers doesn't overlap anything they're busy with.
/// Buffers may run past closing time (clean-up after the last client is normal).
/// Candidate starts sit on a grid of <c>IntervalMinutes</c> counted from local midnight, so slots read 10:00, 10:15...
/// </summary>
public static class AvailabilityEngine
{
    public static IReadOnlyList<SlotResult> Compute(AvailabilityInput input)
    {
        if (input.DurationMinutes <= 0 || input.IntervalMinutes <= 0) return [];
        var weekday = (int)input.Date.DayOfWeek;
        var duration = TimeSpan.FromMinutes(input.DurationMinutes);
        var before = TimeSpan.FromMinutes(input.BufferBeforeMinutes);
        var after = TimeSpan.FromMinutes(input.BufferAfterMinutes);
        var byStart = new SortedDictionary<DateTimeOffset, List<Guid>>();

        foreach (var staff in input.Staff)
        {
            foreach (var window in WorkingWindows(input, staff, weekday))
            {
                // First grid point at or after the window start, counted in local minutes since midnight.
                var minutes = (int)Math.Ceiling(window.StartMinutes / (double)input.IntervalMinutes) * input.IntervalMinutes;
                for (; minutes + input.DurationMinutes <= window.EndMinutes; minutes += input.IntervalMinutes)
                {
                    if (ToInstant(input.Date, minutes, input.Zone) is not { } start) continue; // skipped by a clock change
                    var end = start + duration;
                    if (start < input.EarliestStart) continue;
                    // Real elapsed time can differ from wall-clock minutes across a clock change; re-check the end.
                    if (!input.IgnoreHours && ToInstant(input.Date, window.EndMinutes, input.Zone) is { } windowEnd && end > windowEnd) continue;
                    var occupied = new Span(start - before, end + after);
                    if (staff.Busy.Any(b => b.Overlaps(occupied))) continue;
                    if (!byStart.TryGetValue(start, out var list)) byStart[start] = list = [];
                    list.Add(staff.StaffId);
                }
            }
        }

        return byStart.Select(kv => new SlotResult(kv.Key, kv.Key + duration, kv.Value)).ToList();
    }

    private readonly record struct Window(int StartMinutes, int EndMinutes);

    /// <summary>The staff member's schedule for the weekday, clipped to opening hours, as minutes since local midnight.</summary>
    private static IEnumerable<Window> WorkingWindows(AvailabilityInput input, StaffInput staff, int weekday)
    {
        if (input.IgnoreHours)
        {
            yield return new Window(0, 24 * 60);
            yield break;
        }
        if (input.Hours is not { } hours) yield break;
        var open = Minutes(hours.Open);
        var close = Minutes(hours.Close);
        foreach (var interval in staff.Schedule.Where(s => s.Weekday == weekday).OrderBy(s => s.Start))
        {
            var start = Math.Max(Minutes(interval.Start), open);
            var end = Math.Min(Minutes(interval.End), close);
            if (end > start) yield return new Window(start, end);
        }
    }

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>
    /// Local date + minutes → instant. Returns null for local times that don't exist (spring-forward gap).
    /// For ambiguous times (fall-back) the earlier instant is used.
    /// </summary>
    public static DateTimeOffset? ToInstant(DateOnly date, int minutes, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
        if (zone.IsInvalidTime(local)) return null;
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public static TimeZoneInfo Zone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The instants that bound a local calendar day, in UTC (the database only accepts UTC instants).</summary>
    public static Span Day(DateOnly date, TimeZoneInfo zone)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)));
        var next = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new Span(start.ToUniversalTime(), new DateTimeOffset(next, zone.GetUtcOffset(next)).ToUniversalTime());
    }
}
