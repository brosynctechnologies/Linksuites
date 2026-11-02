using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record CommentBankEntryDto(Guid Id, CommentScope Scope, string MatchKey, string Text, int Order);
public sealed record CommentBankDto(IReadOnlyList<CommentBankEntryDto> Entries);
public sealed record SaveCommentBankEntry(CommentScope Scope, string MatchKey, string Text, int Order);
public sealed record SaveCommentBankRequest(IReadOnlyList<SaveCommentBankEntry>? Entries);
public sealed record CommentSuggestionsDto(IReadOnlyList<string> Subject, IReadOnlyList<string> TeacherOverall, IReadOnlyList<string> PrincipalOverall);

/// <summary>
/// The school's comment bank and the lines that match a student's grade. Subject lines match <see cref="GradingBand.Grade"/>.
/// A developmental class matches the child's scale labels this term and returns those lines as the teacher's overall comment.
/// The first read or save for a school with no rows stores the template.
/// </summary>
public sealed class CommentBankService(IAppDbContext db, ITenantContext tenant, SchoolAccess access, AcademicService academic, ResultService results)
{
    public const int MaxTextLength = 500;
    public const int MaxMatchKeyLength = 40;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public async Task<CommentBankDto> GetAsync(CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        return await ViewAsync(ct);
    }

    /// <summary>Leadership replaces the whole bank. Staff who are not leadership are refused by the route.</summary>
    public async Task<CommentBankDto> SaveAsync(SaveCommentBankRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can change the comment bank.");
        var entries = Clean(r.Entries);
        var tenantId = TenantId;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(SeedLockKey(tenantId), ct);
        var existing = await db.CommentBankEntries.ToListAsync(ct);
        db.CommentBankEntries.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        db.CommentBankEntries.AddRange(entries.Select(e => new CommentBankEntry
        {
            TenantId = tenantId, Scope = e.Scope, MatchKey = e.MatchKey, Text = e.Text, Order = e.Order,
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ViewAsync(ct);
    }

    /// <summary>
    /// Lines whose match key is this student's grade. <paramref name="classSubjectId"/> picks the subject grade.
    /// Overall lines use the grade of the student's average. Leadership, the class teacher, or the subject teacher may ask.
    /// </summary>
    public async Task<CommentSuggestionsDto> SuggestAsync(Guid classId, Guid termId, Guid enrolmentId, Guid? classSubjectId, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");

        ClassSubject? subject = null;
        if (classSubjectId is { } asked)
            subject = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == asked && c.ClassId == classId, ct)
                ?? throw new NotFoundException("Class subject");

        if (access.IsBursar) throw new ForbiddenException("The bursar does not write subject comments.");
        var staffId = await access.StaffIdAsync(ct);
        var isClassTeacher = staffId is { } id && cls.ClassTeacherStaffId == id;
        var teachesSubject = subject is not null && await access.TeachesClassSubjectAsync(subject.Id, ct);
        // No subject: a subject teacher of the class may read the overall lines. A named subject still requires that subject.
        var teachesClass = subject is null && await access.TeachesClassAsync(classId, ct);
        if (!access.IsLeadership && !isClassTeacher && !teachesSubject && !teachesClass)
            throw new NotFoundException(subject is null ? "Class" : "Class subject");

        await results.EnsureCurrentAsync(classId, termId, ct);
        await EnsureSeededAsync(ct);

        var mode = await db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct);
        if (mode == AssessmentMode.Developmental)
        {
            if (!await db.Enrolments.AsNoTracking().AnyAsync(e => e.Id == enrolmentId && e.ClassId == classId, ct))
                throw new NotFoundException("Student");
            var labels = (await db.DevelopmentalRatings.AsNoTracking()
                .Where(r => r.EnrolmentId == enrolmentId && r.TermId == termId
                    && db.Enrolments.Any(e => e.Id == r.EnrolmentId && e.ClassId == classId))
                .Join(db.DevelopmentalScales.AsNoTracking(), r => r.ScaleId, s => s.Id, (r, s) => s.Label)
                .ToListAsync(ct)).Distinct().ToList();
            var texts = labels.Count == 0 ? [] : await db.CommentBankEntries.AsNoTracking()
                .Where(e => e.Scope == CommentScope.Developmental && labels.Contains(e.MatchKey))
                .OrderBy(e => e.Order).ThenBy(e => e.MatchKey)
                .Select(e => e.Text)
                .ToListAsync(ct);
            return new CommentSuggestionsDto([], texts, []);
        }

        string? subjectGrade = null;
        if (subject is not null)
            subjectGrade = await db.SubjectResults.AsNoTracking()
                .Where(x => x.ClassId == classId && x.TermId == termId && x.EnrolmentId == enrolmentId && x.ClassSubjectId == subject.Id)
                .Select(x => x.Grade).FirstOrDefaultAsync(ct);

        var average = await db.TermResults.AsNoTracking()
            .Where(x => x.ClassId == classId && x.TermId == termId && x.EnrolmentId == enrolmentId)
            .Select(x => (decimal?)x.Average).FirstOrDefaultAsync(ct);
        string? overallGrade = null;
        if (average is { } value)
            overallGrade = ResultRules.Grade(value, await academic.BandsFor(cls.LevelId)).Grade;

        var bank = await db.CommentBankEntries.AsNoTracking().ToListAsync(ct);
        IReadOnlyList<string> Match(CommentScope scope, string? key) =>
            key is null ? [] : bank.Where(e => e.Scope == scope && e.MatchKey == key).OrderBy(e => e.Order).Select(e => e.Text).ToList();

        return new CommentSuggestionsDto(Match(CommentScope.Subject, subjectGrade), Match(CommentScope.TeacherOverall, overallGrade), Match(CommentScope.PrincipalOverall, overallGrade));
    }

    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (await db.CommentBankEntries.AnyAsync(ct)) return;
        var outer = db.Database.CurrentTransaction;
        await using var own = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(SeedLockKey(TenantId), ct);
        if (await db.CommentBankEntries.AnyAsync(ct))
        {
            if (own is not null) await own.CommitAsync(ct);
            return;
        }
        db.CommentBankEntries.AddRange(Defaults(TenantId));
        await db.SaveChangesAsync(ct);
        if (own is not null) await own.CommitAsync(ct);
    }

    private static List<CommentBankEntry> Defaults(Guid tenantId)
    {
        var rows = new List<CommentBankEntry>();
        var order = 0;
        foreach (var band in SchoolTemplates.Bands)
            rows.Add(new CommentBankEntry { TenantId = tenantId, Scope = CommentScope.Subject, MatchKey = band.Grade, Text = band.Remark, Order = order++ });
        var overallOrder = new Dictionary<CommentScope, int>();
        foreach (var line in SchoolTemplates.OverallComments.Concat(SchoolTemplates.DevelopmentalComments))
        {
            var n = overallOrder.GetValueOrDefault(line.Scope);
            overallOrder[line.Scope] = n + 1;
            rows.Add(new CommentBankEntry { TenantId = tenantId, Scope = line.Scope, MatchKey = line.MatchKey, Text = line.Text, Order = n });
        }
        return rows;
    }

    private static List<SaveCommentBankEntry> Clean(IReadOnlyList<SaveCommentBankEntry>? entries)
    {
        if (entries is null || entries.Count == 0) throw BadRequestException.Field("entries", "Add at least one comment.");
        var cleaned = new List<SaveCommentBankEntry>(entries.Count);
        var seen = new HashSet<(CommentScope Scope, string Key, int Order)>();
        foreach (var entry in entries)
        {
            var key = (entry.MatchKey ?? "").Trim();
            var text = (entry.Text ?? "").Trim();
            if (key.Length is 0 or > MaxMatchKeyLength) throw BadRequestException.Field("matchKey", $"Give each comment a grade of up to {MaxMatchKeyLength} characters.");
            if (text.Length is 0 or > MaxTextLength) throw BadRequestException.Field("text", $"A comment can have at most {MaxTextLength} characters.");
            if (entry.Order < 0) throw BadRequestException.Field("order", "Put comments in order from 0.");
            if (!seen.Add((entry.Scope, key, entry.Order))) throw BadRequestException.Field("entries", "Two comments share the same place for one grade.");
            cleaned.Add(new SaveCommentBankEntry(entry.Scope, key, text, entry.Order));
        }
        return cleaned;
    }

    private async Task<CommentBankDto> ViewAsync(CancellationToken ct)
    {
        var rows = await db.CommentBankEntries.AsNoTracking().OrderBy(e => e.Scope).ThenBy(e => e.Order).ThenBy(e => e.MatchKey).ToListAsync(ct);
        return new CommentBankDto(rows.Select(e => new CommentBankEntryDto(e.Id, e.Scope, e.MatchKey, e.Text, e.Order)).ToList());
    }

    private static long SeedLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0C0E17C0;
}
