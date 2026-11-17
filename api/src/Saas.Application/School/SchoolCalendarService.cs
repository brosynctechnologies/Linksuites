using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SaveSessionRequest(string? Name, DateOnly StartDate, DateOnly EndDate);
public sealed record SaveTermRequest(string? Name, DateOnly StartDate, DateOnly EndDate, DateOnly? NextTermStarts);

/// <summary>
/// The school's own calendar: correct the session's and terms' names and dates, and move to the next term. Starting the next
/// session (with promotion of every student) stays with <see cref="PromotionService"/>. Sessions and terms are audited entities.
/// </summary>
public sealed class SchoolCalendarService(IAppDbContext db)
{
    /// <summary>Column sizes: session names such as "2026/2027", term names such as "First Term".</summary>
    public const int MaxSessionName = 20, MaxTermName = 40;

    public async Task<SessionDto> UpdateSessionAsync(Guid id, SaveSessionRequest r, CancellationToken ct)
    {
        var session = await db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Session");
        var name = CleanName(r.Name, MaxSessionName);
        if (r.EndDate <= r.StartDate) throw BadRequestException.Field("endDate", "The session must end after it starts.");
        if (await db.AcademicSessions.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), ct))
            throw BadRequestException.Field("name", "Another session already has this name.");
        if (await db.AcademicSessions.AnyAsync(s => s.Id != id && s.StartDate <= r.EndDate && s.EndDate >= r.StartDate, ct))
            throw BadRequestException.Field("startDate", "These dates overlap another session.");
        // Wave 2: the session keeps every one of its terms inside its dates.
        var terms = await db.Terms.AsNoTracking().Where(t => t.SessionId == id).ToListAsync(ct);
        if (terms.Where(t => t.StartDate < r.StartDate).OrderBy(t => t.StartDate).FirstOrDefault() is { } early)
            throw BadRequestException.Field("startDate", $"{early.Name} starts on {Day(early.StartDate)}. The session must start on or before that.");
        if (terms.Where(t => t.EndDate > r.EndDate).OrderByDescending(t => t.EndDate).FirstOrDefault() is { } late)
            throw BadRequestException.Field("endDate", $"{late.Name} ends on {Day(late.EndDate)}. The session must end on or after that.");

        session.Name = name;
        session.StartDate = r.StartDate;
        session.EndDate = r.EndDate;
        await db.SaveChangesAsync(ct);
        return await SessionDtoAsync(session, ct);
    }

    public async Task<SessionDto> UpdateTermAsync(Guid id, SaveTermRequest r, CancellationToken ct)
    {
        var term = await db.Terms.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Term");
        var name = CleanName(r.Name, MaxTermName);
        if (r.EndDate <= r.StartDate) throw BadRequestException.Field("endDate", "The term must end after it starts.");
        if (r.NextTermStarts is { } next && next <= r.EndDate)
            throw BadRequestException.Field("nextTermStarts", "The next term must start after this term ends.");

        // Terms stay in order: this one starts after the term before it ends and ends before the term after it starts.
        var siblings = await db.Terms.Where(t => t.SessionId == term.SessionId && t.Id != id).ToListAsync(ct);
        if (siblings.Where(t => t.Number < term.Number).OrderByDescending(t => t.Number).FirstOrDefault() is { } before && r.StartDate <= before.EndDate)
            throw BadRequestException.Field("startDate", $"{before.Name} ends on {before.EndDate:d MMM yyyy}. This term must start after that.");
        if (siblings.Where(t => t.Number > term.Number).OrderBy(t => t.Number).FirstOrDefault() is { } after && r.EndDate >= after.StartDate)
            throw BadRequestException.Field("endDate", $"{after.Name} starts on {after.StartDate:d MMM yyyy}. This term must end before that.");
        // Wave 2: a term stays inside its session's dates.
        var owner = await db.AcademicSessions.AsNoTracking().FirstAsync(s => s.Id == term.SessionId, ct);
        if (r.StartDate < owner.StartDate)
            throw BadRequestException.Field("startDate", $"The session starts on {Day(owner.StartDate)}. The term can't start before that.");
        if (r.EndDate > owner.EndDate)
            throw BadRequestException.Field("endDate", $"The session ends on {Day(owner.EndDate)}. The term can't end after that.");

        term.Name = name;
        term.StartDate = r.StartDate;
        term.EndDate = r.EndDate;
        term.NextTermStarts = r.NextTermStarts;
        await db.SaveChangesAsync(ct);
        var session = await db.AcademicSessions.FirstAsync(s => s.Id == term.SessionId, ct);
        return await SessionDtoAsync(session, ct);
    }

    /// <summary>Makes a term of the current session the current term (forwards, or back to correct a mistake).</summary>
    public async Task<SessionDto> MakeCurrentAsync(Guid id, CancellationToken ct)
    {
        var term = await db.Terms.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Term");
        var session = await db.AcademicSessions.FirstAsync(s => s.Id == term.SessionId, ct);
        if (!session.IsCurrent)
            throw new ConflictException("This term belongs to a past session. Only terms of the current session can be made current.", "past_session");
        if (!term.IsCurrent)
        {
            // Two saves in one transaction: "one current term per school" is a unique index checked statement by statement.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var other in await db.Terms.Where(t => t.IsCurrent).ToListAsync(ct)) other.IsCurrent = false;
                await db.SaveChangesAsync(ct);
                term.IsCurrent = true;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Someone else changed the current term at the same moment.
                throw new ConflictException("The current term was just changed by someone else. Refresh to see it.", "term_changed");
            }
        }
        return await SessionDtoAsync(session, ct);
    }

    private static string Day(DateOnly d) => d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string CleanName(string? name, int max)
    {
        var clean = name?.Trim() ?? "";
        if (clean.Length == 0) throw BadRequestException.Field("name", "Enter a name.");
        if (clean.Length > max) throw BadRequestException.Field("name", $"Keep the name to {max} characters.");
        return clean;
    }

    private async Task<SessionDto> SessionDtoAsync(AcademicSession s, CancellationToken ct)
    {
        var terms = await db.Terms.AsNoTracking().Where(t => t.SessionId == s.Id).OrderBy(t => t.Number)
            .Select(t => new TermDto(t.Id, t.Number, t.Name, t.StartDate, t.EndDate, t.NextTermStarts, t.IsCurrent)).ToListAsync(ct);
        return new SessionDto(s.Id, s.Name, s.StartDate, s.EndDate, s.IsCurrent, terms);
    }
}
