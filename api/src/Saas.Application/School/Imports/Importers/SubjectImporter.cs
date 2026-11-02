using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.School.Imports.Importers;

/// <summary>Shared by the setup imports (subjects, classes, class subjects, staff): leadership only, and staff named by email.</summary>
internal static class SetupImport
{
    public const string StaffMissing = "No active staff member of this school has this email.";

    /// <summary>Leadership only, as on the setup and staff screens. Teachers and the bursar get 403 before anything is looked up.</summary>
    public static Task EnsureLeadership(SchoolAccess access, string what) =>
        access.IsLeadership ? Task.CompletedTask : throw new ForbiddenException($"Only the proprietor or principal can import {what}.");

    /// <summary>Active staff of the school by email (lower case) → staff profile id. Pending invitations are not staff yet.</summary>
    public static async Task<Dictionary<string, Guid>> StaffByEmailAsync(IAppDbContext db, CancellationToken ct)
    {
        var rows = await db.Memberships.AsNoTracking().Where(m => m.Status == MembershipStatus.Active)
            .Join(db.StaffProfiles.AsNoTracking(), m => m.Id, p => p.MembershipId, (m, p) => new { m.User!.Email, p.Id })
            .ToListAsync(ct);
        var map = new Dictionary<string, Guid>();
        foreach (var r in rows) if (r.Email is { } e) map.TryAdd(e.Trim().ToLowerInvariant(), r.Id);
        return map;
    }

    /// <summary>Null when the cell is blank (keep the stored teacher), the staff id, or an error.</summary>
    public static Guid? Staff(Dictionary<string, Guid> staff, string email, out string? error)
    {
        error = null;
        if (email.Length == 0) return null;
        if (staff.TryGetValue(email.ToLowerInvariant(), out var id)) return id;
        error = StaffMissing;
        return null;
    }
}

/// <summary>
/// Subjects, one per row (kind <c>subjects</c>). Leadership only. Rows match on code: an existing code updates the subject's name, a new code
/// adds the subject. The name and code rules are <see cref="AcademicService"/>'s, and rows are saved through its create and update methods.
/// </summary>
public sealed class SubjectImporter(IAppDbContext db, SchoolAccess access, AcademicService academic) : IImporter, IImportNotice
{
    public const string KindName = "subjects";
    public const string CodeTwice = "This code appears more than once in the file.";
    public const string NameTwice = "This subject name appears more than once in the file.";

    public string Kind => KindName;
    public string SheetName => "Subjects";
    public string NoticeTitle => "Subject import finished";
    public string NoticeLink => "/app/setup/subjects";

    private static readonly ImportColumns AllColumns = new(
    [
        new("name", "name", true, "The subject's name, for example Mathematics. Up to 80 characters."),
        new("code", "code", true, "A short code of up to 10 letters or digits, for example MTH. A code the school already has renames that subject."),
    ]);

    private static readonly string[] Notes =
    [
        "One subject per row on the Subjects sheet. Delete nothing from the heading row.",
        "name and code are both required.",
        "Rows match on code: a code the school already has changes that subject's name; a new code adds a subject.",
        "Each name and each code can appear once in the file, and a name can't belong to another subject.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private sealed record Plan(Guid? Id, SaveSubjectRequest Request);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => SetupImport.EnsureLeadership(access, "subjects");

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var existing = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name, s.Code }).ToListAsync(ct);

        var cleaned = rows.Select(row =>
        {
            try
            {
                var (name, code) = AcademicService.CleanSubject(new SaveSubjectRequest(row.Cells.GetValueOrDefault("name") ?? "", row.Cells.GetValueOrDefault("code") ?? ""));
                return (Row: row, Name: name, Code: code, Error: (string?)null);
            }
            catch (BadRequestException ex)
            {
                return (Row: row, Name: (row.Cells.GetValueOrDefault("name") ?? "").Trim(), Code: (row.Cells.GetValueOrDefault("code") ?? "").Trim().ToUpperInvariant(), Error: (string?)ex.Message);
            }
        }).ToList();

        var valid = cleaned.Where(c => c.Error is null).ToList();
        var codesTwice = valid.GroupBy(c => c.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var namesTwice = valid.GroupBy(c => c.Name.ToLowerInvariant()).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        return cleaned.Select(c =>
        {
            var label = c.Code.Length > 0 ? $"{c.Name} ({c.Code})" : c.Name;
            var error = c.Error
                ?? (codesTwice.Contains(c.Code) ? CodeTwice : null)
                ?? (namesTwice.Contains(c.Name.ToLowerInvariant()) ? NameTwice : null);
            var match = existing.FirstOrDefault(s => s.Code == c.Code);
            if (error is null && existing.Any(s => s.Id != match?.Id && s.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)))
                error = "A subject with that name already exists.";
            if (error is not null) return new ImportRowResult(c.Row.Row, ImportRowStatus.Error, label, error, null);
            if (match is null) return new ImportRowResult(c.Row.Row, ImportRowStatus.New, label, null, new Plan(null, new SaveSubjectRequest(c.Name, c.Code)));
            return string.Equals(match.Name, c.Name, StringComparison.Ordinal)
                ? new ImportRowResult(c.Row.Row, ImportRowStatus.Unchanged, label, null, null)
                : new ImportRowResult(c.Row.Row, ImportRowStatus.Update, label, null, new Plan(match.Id, new SaveSubjectRequest(c.Name, c.Code)));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Plan is not Plan plan) continue;
            if (plan.Id is { } id)
            {
                await academic.UpdateSubjectAsync(id, plan.Request, ct);
                updated++;
            }
            else
            {
                await academic.CreateSubjectAsync(plan.Request, ct);
                created++;
            }
        }
        return new ImportApplied(created, updated);
    }
}
