using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>
/// The school calendar rules. School dates are <see cref="DateOnly"/> in Lagos time (UTC+1, no daylight saving).
/// A session runs September to August; its three terms are Sep-Dec, Jan-Apr and Apr-Jul.
/// </summary>
public static class SchoolCalendar
{
    public static readonly TimeSpan LagosOffset = TimeSpan.FromHours(1);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(LagosOffset).DateTime);

    /// <summary>The year the session covering <paramref name="today"/> started: September onwards belongs to this year's session.</summary>
    public static int SessionStartYear(DateOnly today) => today.Month >= 9 ? today.Year : today.Year - 1;

    public static string SessionName(int startYear) => $"{startYear}/{startYear + 1}";

    public sealed record TermDates(int Number, string Name, DateOnly Start, DateOnly End, DateOnly? NextStarts);

    public static IReadOnlyList<TermDates> Terms(int startYear)
    {
        var first = new TermDates(1, "First term", new(startYear, 9, 1), new(startYear, 12, 18), new(startYear + 1, 1, 5));
        var second = new TermDates(2, "Second term", new(startYear + 1, 1, 5), new(startYear + 1, 4, 3), new(startYear + 1, 4, 20));
        var third = new TermDates(3, "Third term", new(startYear + 1, 4, 20), new(startYear + 1, 7, 24), null);
        return [first, second, third];
    }

    /// <summary>The term running today, else the next one to start, else the last of the session.</summary>
    public static int CurrentTermNumber(IReadOnlyList<TermDates> terms, DateOnly today) =>
        (terms.FirstOrDefault(t => today >= t.Start && today <= t.End)
            ?? terms.FirstOrDefault(t => t.Start > today)
            ?? terms[^1]).Number;
}

/// <summary>Creates the school profile, the current academic session and its three terms when a school signs up.</summary>
public sealed class SchoolProvisioner(IAppDbContext db, TimeProvider clock) : Auth.IProductProvisioner
{
    public string Product => Products.School;

    public Task ProvisionAsync(Tenant tenant, Auth.SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        db.SchoolProfiles.Add(new SchoolProfile { TenantId = tenant.Id, Name = tenant.Name, Slug = tenant.Slug });

        // The owner's membership was added just before this hook runs; give it its staff profile.
        var ownerMembership = db.ChangeTracker.Entries<Membership>().Select(e => e.Entity).First(m => m.UserId == ownerUserId);
        db.StaffProfiles.Add(new StaffProfile
        {
            TenantId = tenant.Id, MembershipId = ownerMembership.Id, UserId = ownerUserId, JobTitle = StaffService.DefaultJobTitle(MemberRole.Owner),
        });

        var today = SchoolCalendar.Today(clock.GetUtcNow());
        var startYear = SchoolCalendar.SessionStartYear(today);
        var session = new AcademicSession
        {
            TenantId = tenant.Id, Name = SchoolCalendar.SessionName(startYear), IsCurrent = true,
            StartDate = new DateOnly(startYear, 9, 1), EndDate = new DateOnly(startYear + 1, 8, 31),
        };
        db.AcademicSessions.Add(session);

        var terms = SchoolCalendar.Terms(startYear);
        var current = SchoolCalendar.CurrentTermNumber(terms, today);
        foreach (var t in terms)
            db.Terms.Add(new Term
            {
                TenantId = tenant.Id, SessionId = session.Id, Number = t.Number, Name = t.Name, StartDate = t.Start, EndDate = t.End,
                NextTermStarts = t.NextStarts, IsCurrent = t.Number == current,
            });
        return Task.CompletedTask;
    }
}

/// <summary>School billing usage: active students against the plan. Team seats come from the shared <see cref="Team.TeamUsage"/>.</summary>
public sealed class SchoolUsage(IAppDbContext db) : IUsageProvider
{
    public string Product => Products.School;

    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        return [new(LimitKeys.Students, "Students", active, plan.Limit(LimitKeys.Students))];
    }
}
