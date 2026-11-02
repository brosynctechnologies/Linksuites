using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>One card. <c>Photo</c> and <c>Logo</c> are image bytes, or null for the placeholder and no logo.</summary>
public sealed record IdCardData(string StudentName, string AdmissionNo, string ClassName, string SessionName, string SchoolName, byte[]? Photo, byte[]? Logo);

/// <summary>The school's PDFs. Implemented with QuestPDF in Infrastructure; a later task adds receipts here.</summary>
public interface ISchoolPdf
{
    /// <summary>Student ID cards, eight to an A4 page.</summary>
    Task<byte[]> IdCardsAsync(IReadOnlyList<IdCardData> cards, CancellationToken ct);

    /// <summary>One report card, on as many A4 pages as its subjects need.</summary>
    Task<byte[]> ReportCardAsync(ReportCardData card, CancellationToken ct);

    /// <summary>Several report cards in one document, each starting on a new page.</summary>
    Task<byte[]> ReportCardsAsync(IReadOnlyList<ReportCardData> cards, CancellationToken ct);

    /// <summary>One class broadsheet for the term, on landscape A4.</summary>
    Task<byte[]> BroadsheetAsync(BroadsheetData data, CancellationToken ct);

    /// <summary>One fee receipt on an A5 page; a reversed payment is printed with the reversal note.</summary>
    Task<byte[]> ReceiptAsync(ReceiptData receipt, CancellationToken ct);
}

/// <summary>Gathers the cards of a class or of chosen students: leadership and the bursar. Photos are read from private storage here, never linked.</summary>
public sealed class IdCardService(IAppDbContext db, IFileStorage storage, ISchoolPdf pdf)
{
    public const int MaxCards = 1000;

    public async Task<byte[]> RenderAsync(Guid? classId, string? studentIds, CancellationToken ct)
    {
        var picked = new List<Guid>();
        foreach (var part in (studentIds ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(part, out var id)) throw BadRequestException.Field("studentIds", "Student ids must be separated by commas.");
            picked.Add(id);
        }
        if (classId is null && picked.Count == 0) throw BadRequestException.Field("classId", "Choose a class or some students.");
        if (picked.Count > MaxCards) throw BadRequestException.Field("studentIds", $"Choose at most {MaxCards:N0} students at a time.");

        var session = await db.AcademicSessions.AsNoTracking().FirstOrDefaultAsync(s => s.IsCurrent, ct)
            ?? throw BadRequestException.Field("sessionId", "Set a current session first.");
        var query = db.Enrolments.AsNoTracking().Where(e => e.SessionId == session.Id);
        if (classId is { } cid) query = query.Where(e => e.ClassId == cid);
        if (picked.Count > 0) query = query.Where(e => picked.Contains(e.StudentId));

        var rows = await query
            .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id, (e, s) => new { e.ClassId, Student = s })
            .Join(db.SchoolClasses.AsNoTracking(), x => x.ClassId, c => c.Id, (x, c) => new { ClassName = c.Name, x.Student })
            .OrderBy(x => x.ClassName).ThenBy(x => x.Student.LastName).ThenBy(x => x.Student.FirstName).ThenBy(x => x.Student.AdmissionNo)
            .Take(MaxCards + 1).ToListAsync(ct);
        if (rows.Count == 0) throw new NotFoundException("Students");
        if (rows.Count > MaxCards) throw BadRequestException.Field("classId", $"Choose at most {MaxCards:N0} students at a time.");

        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var schoolName = profile?.Name ?? "School";
        var logo = profile?.LogoKey is { } logoKey ? await ReadAsync(logoKey, ct) : null; // left out when missing

        var cards = new List<IdCardData>(rows.Count);
        foreach (var row in rows)
        {
            var s = row.Student;
            var name = string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
            var photo = s.PhotoKey is { } key ? await ReadAsync(key, ct) : null;
            cards.Add(new IdCardData(name, s.AdmissionNo, row.ClassName, session.Name, schoolName, photo, logo));
        }
        return await pdf.IdCardsAsync(cards, ct);
    }

    private async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(key, ct);
            if (stream is null) return null;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            return null; // a missing or unreadable photo prints as the placeholder
        }
    }
}
