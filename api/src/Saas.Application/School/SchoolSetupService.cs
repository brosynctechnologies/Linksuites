using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SchoolDto(string Name, string Slug, string? Address, string? Motto, bool HasLogo);
public sealed record TermDto(Guid Id, int Number, string Name, DateOnly StartDate, DateOnly EndDate, DateOnly? NextTermStarts, bool IsCurrent);
public sealed record SessionDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent, IReadOnlyList<TermDto> Terms);
public sealed record ChecklistItemDto(string Key, string Label, bool Done);
public sealed record SchoolSetupDto(SchoolDto School, IReadOnlyList<SessionDto> Sessions, IReadOnlyList<ChecklistItemDto> Checklist);

/// <summary>What the school app shows on its first screen: the school, its sessions with terms, and what is left to set up.</summary>
public sealed class SchoolSetupService(IAppDbContext db, ITenantContext tenant)
{
    public async Task<SchoolSetupDto> GetAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var sessions = await db.AcademicSessions.AsNoTracking().OrderByDescending(s => s.StartDate).ToListAsync(ct);
        var terms = await db.Terms.AsNoTracking().OrderBy(t => t.Number).ToListAsync(ct);

        var sessionDtos = sessions.Select(s => new SessionDto(s.Id, s.Name, s.StartDate, s.EndDate, s.IsCurrent,
            terms.Where(t => t.SessionId == s.Id).Select(t => new TermDto(t.Id, t.Number, t.Name, t.StartDate, t.EndDate, t.NextTermStarts, t.IsCurrent)).ToList())).ToList();

        // Students are done once one is active. Staff is done once someone besides the owner has joined.
        var checklist = new List<ChecklistItemDto>
        {
            new("levels-and-classes", "Set up levels and classes", await db.SchoolClasses.AnyAsync(ct)),
            new("subjects", "Add subjects", await db.Subjects.AnyAsync(ct)),
            new("students", "Add students", await db.Students.AnyAsync(s => s.Status == StudentStatus.Active, ct)),
            new("staff", "Add staff", await db.Memberships.AnyAsync(m => m.Role != MemberRole.Owner && m.Status == MembershipStatus.Active, ct)),
        };
        return new SchoolSetupDto(new SchoolDto(profile.Name, profile.Slug, profile.Address, profile.Motto, profile.LogoKey is not null), sessionDtos, checklist);
    }
}
