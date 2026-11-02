using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record ResultSubjectDto(Guid ClassSubjectId, string Subject, string SubjectCode, decimal Total, string Grade, string? Remark, decimal ClassAverage, int Position, bool Incomplete);
public sealed record ResultDevelopmentalDto(string Area, string? Scale, string? Comment);
/// <summary>
/// <see cref="Mode"/> is the mode the row was computed in. Late, absent and excused are null on rows published before they were counted.
/// </summary>
public sealed record ResultStudentDto(
    Guid EnrolmentId, Guid StudentId, string Name, string AdmissionNo, decimal Total, decimal Average, int Position, int ClassSize,
    int DaysPresent, int? DaysAbsent, int? DaysLate, int? DaysExcused, int DaysOpen, string? TeacherRemark, string? PrincipalRemark, bool Incomplete, IReadOnlyList<ResultSubjectDto> Subjects,
    IReadOnlyList<ResultDevelopmentalDto> Developmental, AssessmentMode Mode);
/// <summary><see cref="Mode"/> is the mode of the stored rows, or the level's current mode when there are none yet.</summary>
public sealed record ResultsDto(
    Guid ClassId, string ClassName, Guid TermId, string TermName, bool Locked, DateTimeOffset? PublishedAt, int PendingStudents, IReadOnlyList<ResultStudentDto> Students,
    AssessmentMode Mode);
/// <summary>
/// One child's results for a term. <see cref="Subjects"/> is every subject for leadership and the class teacher, and only the
/// class-subjects a subject teacher teaches. Totals stay on the class grid. <see cref="Suggestions"/> is set only when asked.
/// </summary>
public sealed record ChildResultDto(
    Guid StudentId, Guid EnrolmentId, string Name, string AdmissionNo,
    Guid ClassId, string ClassName, Guid TermId, string TermName,
    bool Locked, DateTimeOffset? PublishedAt,
    int DaysPresent, int? DaysAbsent, int? DaysLate, int? DaysExcused, int DaysOpen,
    IReadOnlyList<ResultSubjectDto> Subjects, IReadOnlyList<DevelopmentalRatingItemDto> Developmental,
    IReadOnlyList<TraitRatingItemDto> Psychomotor, IReadOnlyList<TraitRatingItemDto> Affective,
    string? TeacherRemark, string? PrincipalRemark, CommentSuggestionsDto? Suggestions, AssessmentMode Mode = AssessmentMode.Numeric);
/// <summary>Null remark fields are left as they are; an empty one clears the remark.</summary>
public sealed record SetRemarksRequest(Guid EnrolmentId, string? TeacherRemark, string? PrincipalRemark);
/// <summary>An empty comment clears the subject comment.</summary>
public sealed record SetSubjectCommentRequest(Guid EnrolmentId, Guid ClassSubjectId, string? Comment);
public sealed record SubjectCommentDto(Guid EnrolmentId, Guid ClassSubjectId, string? Comment);
public sealed record PublishResultDto(Guid ClassId, Guid TermId, bool Published, DateTimeOffset? PublishedAt, int Emailed);

/// <summary>
/// Results for a class's term: totals, grades and positions rebuilt from the scores, attendance, remarks, and publishing.
/// Editing scores or the lock marks the results stale; stale results are recomputed before they are viewed or published.
/// Computing, publishing and unpublishing take the same locks as score writers (shared assessment lock, then the class and term lock).
/// </summary>
public sealed class ResultService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, AcademicService academic,
    NotificationQueue notifications, SchoolFeed feed, AppUrls urls, IDbErrors dbErrors, TimeProvider clock,
    DevelopmentalService developmental, TraitService traits)
{
    public const int MaxRemarkLength = 500;
    public const string PublishedMessage = "Unpublish the results before changing remarks.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- View ----------

    /// <summary>Leadership, the class teacher, and a subject teacher of the class read a class's results. A subject teacher sees only the subjects they teach.</summary>
    public async Task<ResultsDto> GetAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        await EnsureCanReadAsync(cls, ct);
        await EnsureFreshAsync(cls, term, ct);
        return await ViewAsync(cls, term, await SubjectLimitAsync(cls, ct), ct);
    }

    /// <summary>
    /// One child's results. The bursar is refused before the child is looked up. A teacher who does not teach the child's class
    /// gets the same not-found response as an unknown child. Without <paramref name="classId"/>: the one class the student has a
    /// row in, 400 when they have rows in several and the caller may read at least one of those classes, and their current class
    /// when they have none. A caller who teaches none of the classes is not told that several exist.
    /// </summary>
    public async Task<ChildResultDto> GetChildAsync(Guid studentId, Guid? termId, Guid? classId, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not read results.");
        if (termId is not { } term) throw BadRequestException.Field("termId", "Choose a term.");

        var termRow = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == term, ct) ?? throw new NotFoundException("Term");
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Child");
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.StudentId == studentId && e.SessionId == termRow.SessionId, ct)
            ?? throw new NotFoundException("Child");

        var rowClasses = await db.TermResults.AsNoTracking().Where(r => r.EnrolmentId == enrolment.Id && r.TermId == term)
            .Select(r => r.ClassId).Distinct().ToListAsync(ct);
        Guid resolved;
        if (classId is { } asked)
        {
            if (!rowClasses.Contains(asked) && asked != enrolment.ClassId) throw new NotFoundException("Child");
            resolved = asked;
        }
        else if (rowClasses.Count > 1)
        {
            // A teacher of none of these classes gets the same 404 as an unknown child, before the 400 that names the situation.
            if (!access.IsLeadership && !await TeachesAnyAsync(rowClasses, ct)) throw new NotFoundException("Child");
            throw BadRequestException.Field("classId", "This student was in more than one class this term. Choose the class.");
        }
        else
            resolved = rowClasses.Count == 1 ? rowClasses[0] : enrolment.ClassId;

        var (cls, loaded) = await LoadAsync(resolved, term, ct);
        if (!access.IsLeadership && !await IsClassTeacherAsync(cls, ct) && !await access.TeachesClassAsync(cls.Id, ct))
            throw new NotFoundException("Child");

        await EnsureFreshAsync(cls, loaded, ct);
        var view = await ViewAsync(cls, loaded, await SubjectLimitAsync(cls, ct), ct);
        var row = view.Students.FirstOrDefault(s => s.StudentId == studentId) ?? throw new NotFoundException("Child");
        var mode = row.Mode;
        var areas = mode == AssessmentMode.Developmental
            ? await developmental.RatingsForAsync(cls.LevelId, row.EnrolmentId, studentId, term, ct)
            : [];
        return new ChildResultDto(
            student.Id, row.EnrolmentId, row.Name, student.AdmissionNo, cls.Id, cls.Name, loaded.Id, loaded.Name,
            view.Locked, view.PublishedAt, row.DaysPresent, row.DaysAbsent, row.DaysLate, row.DaysExcused, row.DaysOpen,
            row.Subjects, areas,
            await traits.RatingsForAsync(cls.LevelId, row.EnrolmentId, studentId, term, TraitKind.Psychomotor, ct),
            await traits.RatingsForAsync(cls.LevelId, row.EnrolmentId, studentId, term, TraitKind.Affective, ct),
            row.TeacherRemark, row.PrincipalRemark, null, mode);
    }

    /// <summary>Throws unless the member may open this class's report card (leadership or the class teacher); checks the class and term exist too.</summary>
    public async Task EnsureCanReadClassAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, _) = await LoadAsync(classId, termId, ct);
        await EnsureClassTeacherOrLeadershipAsync(cls, ct);
    }

    /// <summary>
    /// The class's results for report cards: the same view as <see cref="GetAsync"/> (stale unpublished results are recomputed, published
    /// rows are never touched) without the sign-in check, so the report card job can use it. Callers check access first.
    /// </summary>
    public async Task<ResultsDto> ReadForReportCardsAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        await EnsureFreshAsync(cls, term, ct);
        return await ViewAsync(cls, term, null, ct);
    }

    /// <summary>
    /// The class's stored results exactly as they are, never recomputed: what the parent and student portals read. Callers check access and
    /// use only rows that are published.
    /// </summary>
    public async Task<ResultsDto> ReadStoredAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        return await ViewAsync(cls, term, null, ct);
    }

    /// <summary>
    /// One student's attendance in the class for the term, counted the same way as the results (from their first register
    /// in the class; present never exceeds open). Present is present marks only. For a student who is in the class now.
    /// </summary>
    public async Task<(int Present, int Absent, int Late, int Excused, int Open)> TermAttendanceAsync(Guid classId, Guid termId, Guid enrolmentId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        var counts = await AttendanceAsync(cls, term, [new RosterRow(enrolmentId, Guid.Empty, "", "")], ct);
        var row = counts[enrolmentId];
        return (row.Present, row.Absent, row.Late, row.Excused, row.Open);
    }

    // ---------- Remarks ----------

    public async Task<ResultsDto> SetRemarksAsync(Guid classId, Guid termId, SetRemarksRequest r, CancellationToken ct)
    {
        var (cls, term) = await SaveRemarksAsync(classId, termId, r, ct);
        return await ViewAsync(cls, term, null, ct);
    }

    /// <summary>
    /// The remark save without the class view the screen gets back, for the remarks import (one call per student). Joins an import's open
    /// transaction, which then commits; otherwise opens its own.
    /// </summary>
    internal async Task<(SchoolClass Cls, Term Term)> SaveRemarksAsync(Guid classId, Guid termId, SetRemarksRequest r, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        await EnsureClassTeacherOrLeadershipAsync(cls, ct);
        if (!access.IsLeadership && r.PrincipalRemark is not null)
            throw new ForbiddenException("Only the principal can write the principal's remark.");
        var teacherRemark = CleanRemark(r.TeacherRemark, "teacherRemark");
        var principalRemark = CleanRemark(r.PrincipalRemark, "principalRemark");

        if (await IsPublishedAsync(classId, termId, ct))
            throw new ConflictException(PublishedMessage, "results_published");
        await EnsureFreshAsync(cls, term, ct);

        // An import's confirm already has a transaction open: join it (it commits), otherwise open our own.
        await using (var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await TakeLocksAsync(cls.Id, termId, ct);
            // Checked again under the lock: a publish may have finished while this request was waiting.
            if (await IsPublishedAsync(classId, termId, ct))
                throw new ConflictException(PublishedMessage, "results_published");
            var row = await db.TermResults.FirstOrDefaultAsync(x => x.EnrolmentId == r.EnrolmentId && x.ClassId == classId && x.TermId == termId, ct)
                ?? throw BadRequestException.Field("enrolmentId", "This student is not in the class's results. Reload the page and try again.");
            if (r.TeacherRemark is not null) row.TeacherRemark = teacherRemark;
            if (r.PrincipalRemark is not null) row.PrincipalRemark = principalRemark;
            await SaveAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        return (cls, term);
    }

    /// <summary>
    /// The subject teacher of this class subject, or leadership, writes one subject's comment. A teacher who does not teach it
    /// gets the same not-found response as a missing subject. Published results refuse the change until they are unpublished.
    /// </summary>
    public async Task<SubjectCommentDto> SetSubjectCommentAsync(Guid classId, Guid termId, SetSubjectCommentRequest r, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        var cs = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClassSubjectId && c.ClassId == classId, ct)
            ?? throw new NotFoundException("Class subject");
        if (access.IsBursar) throw new ForbiddenException("The bursar does not write subject comments.");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(cs.Id, ct))
            throw new NotFoundException("Class subject");
        var comment = CleanRemark(r.Comment, "comment");

        if (await IsPublishedAsync(classId, termId, ct))
            throw new ConflictException(PublishedMessage, "results_published");
        await EnsureFreshAsync(cls, term, ct);

        // An import's confirm already has a transaction open: join it (it commits), otherwise open our own.
        await using (var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await TakeLocksAsync(cls.Id, termId, ct);
            if (await IsPublishedAsync(classId, termId, ct))
                throw new ConflictException(PublishedMessage, "results_published");
            var row = await db.SubjectResults.FirstOrDefaultAsync(x => x.EnrolmentId == r.EnrolmentId && x.ClassSubjectId == r.ClassSubjectId && x.ClassId == classId && x.TermId == termId, ct)
                ?? throw BadRequestException.Field("enrolmentId", "This student has no result for that subject. Reload the page and try again.");
            row.Remark = comment;
            await SaveAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        return new SubjectCommentDto(r.EnrolmentId, r.ClassSubjectId, comment);
    }

    /// <summary>Recomputes unpublished results when they are stale, so a suggestion sees the grades just computed.</summary>
    public async Task EnsureCurrentAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        await EnsureFreshAsync(cls, term, ct);
    }

    private static string? CleanRemark(string? value, string field)
    {
        if (value is null) return null;
        var text = value.Trim();
        if (text.Length > MaxRemarkLength) throw BadRequestException.Field(field, $"A remark can have at most {MaxRemarkLength} characters.");
        return text.Length == 0 ? null : text;
    }

    // ---------- Publish ----------

    public async Task<PublishResultDto> PublishAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TakeLocksAsync(cls.Id, termId, ct);
        if (!await db.TermResultLocks.AnyAsync(l => l.ClassId == classId && l.TermId == termId, ct))
            throw new ConflictException("Lock the term for this class before publishing its results.", "term_not_locked");

        // Published results are frozen: only a class that is not published yet is rebuilt, then frozen below.
        if (!await IsPublishedAsync(classId, termId, ct)) await ComputeCoreAsync(cls, term, ct);
        var rows = await db.TermResults.Where(x => x.ClassId == classId && x.TermId == termId).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var fresh = rows.Where(x => x.PublishedAt is null).ToList();
        foreach (var row in fresh) row.PublishedAt = now;

        var enrolmentIds = rows.Select(x => x.EnrolmentId).ToList();
        var roster = (await db.Enrolments.AsNoTracking().Where(e => enrolmentIds.Contains(e.Id))
                .Join(db.Students.AsNoTracking(), e => e.StudentId, s => s.Id, (e, s) => new { EnrolmentId = e.Id, StudentId = s.Id, s.FirstName, s.LastName, s.AdmissionNo }).ToListAsync(ct))
            .Select(x => new RosterRow(x.EnrolmentId, x.StudentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();
        var emailed = await QueueNoticesAsync(roster, term, ct);
        if (fresh.Count > 0) db.AuditLogs.Add(Audit("results.publish", cls.Id, termId, now));
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return new PublishResultDto(classId, termId, true, rows.Max(x => x.PublishedAt) ?? now, emailed);
    }

    private Task<bool> IsPublishedAsync(Guid classId, Guid termId, CancellationToken ct) =>
        db.TermResults.AnyAsync(r => r.ClassId == classId && r.TermId == termId && r.PublishedAt != null, ct);

    public async Task<PublishResultDto> UnpublishAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, _) = await LoadAsync(classId, termId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TakeLocksAsync(cls.Id, termId, ct);
        var all = await db.TermResults.Where(x => x.ClassId == classId && x.TermId == termId).ToListAsync(ct);
        var rows = all.Where(x => x.PublishedAt != null).ToList();
        // The rows can be rebuilt again, so the next view recomputes them.
        foreach (var row in rows.Count > 0 ? all : rows) { row.PublishedAt = null; row.Stale = true; }
        if (rows.Count > 0) db.AuditLogs.Add(Audit("results.unpublish", cls.Id, termId, clock.GetUtcNow()));
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return new PublishResultDto(classId, termId, false, null, 0);
    }

    /// <summary>
    /// Emails each linked guardian who has an address and tells the linked parent and student accounts in the app,
    /// once per child per term. Saved by the caller.
    /// </summary>
    private async Task<int> QueueNoticesAsync(List<RosterRow> roster, Term term, CancellationToken ct)
    {
        var studentIds = roster.Select(x => x.StudentId).ToList();
        var told = (await db.ResultsNotices.Where(n => n.TermId == term.Id && studentIds.Contains(n.StudentId)).Select(n => n.StudentId).ToListAsync(ct)).ToHashSet();
        var pending = roster.Where(x => !told.Contains(x.StudentId)).ToList();
        if (pending.Count == 0) return 0;
        var pendingIds = pending.Select(x => x.StudentId).ToList();
        var guardians = await db.StudentGuardians.AsNoTracking().Where(l => pendingIds.Contains(l.StudentId))
            .Join(db.Guardians.AsNoTracking().Where(g => g.Email != null || g.UserId != null), l => l.GuardianId, g => g.Id,
                (l, g) => new { l.StudentId, g.FullName, g.Email, g.UserId })
            .ToListAsync(ct);
        var studentUsers = await db.Students.AsNoTracking().Where(s => pendingIds.Contains(s.Id) && s.UserId != null)
            .ToDictionaryAsync(s => s.Id, s => s.UserId!.Value, ct);
        var school = await db.SchoolProfiles.Select(p => p.Name).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var link = $"{urls.Web(Products.School)}/parent";

        var emailed = 0;
        foreach (var student in pending)
        {
            var mine = guardians.Where(g => g.StudentId == student.StudentId).ToList();
            var studentUser = studentUsers.GetValueOrDefault(student.StudentId);
            if (mine.Count == 0 && studentUser == Guid.Empty) continue;
            foreach (var guardian in mine.Where(g => g.Email != null))
            {
                notifications.Email(guardian.Email!, "school.results_published",
                    new() { ["guardian"] = guardian.FullName, ["child"] = student.Name, ["term"] = term.Name, ["school"] = school, ["link"] = link }, TenantId);
                emailed++;
            }
            var title = $"{student.Name}'s {term.Name} results are out";
            var body = $"{school} has published the {term.Name} results.";
            feed.Notify(mine.Where(g => g.UserId != null).Select(g => g.UserId!.Value), "school.results_published", title, body, SchoolFeed.ParentLink("results", student.StudentId));
            if (studentUser != Guid.Empty)
                feed.Notify(studentUser, "school.results_published", $"Your {term.Name} results are out", body, "/student/results");
            db.ResultsNotices.Add(new ResultsNotice { TenantId = TenantId, StudentId = student.StudentId, TermId = term.Id });
        }
        return emailed;
    }

    private AuditLog Audit(string action, Guid classId, Guid termId, DateTimeOffset now) => new()
    {
        TenantId = TenantId, ActorId = user.UserId, Action = action, EntityType = nameof(TermResult), EntityId = classId.ToString(),
        AfterJson = JsonSerializer.Serialize(new { classId, termId }), Ip = user.IpAddress, CreatedAt = now,
    };

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("The results were changed from another device at the same moment. Reload and try again.", "results_conflict");
        }
    }

    // ---------- Compute ----------

    /// <summary>Rebuilds the class's results for the term from the scores, the lock and the registers. Remarks and the published time are kept.</summary>
    public async Task ComputeAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var (cls, term) = await LoadAsync(classId, termId, ct);
        // An import's confirm already has a transaction open (a remark save first recomputes stale results): join it, otherwise open our own.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await TakeLocksAsync(cls.Id, termId, ct);
        // Published results are frozen; unpublish first to change them.
        if (!await IsPublishedAsync(classId, termId, ct)) await ComputeCoreAsync(cls, term, ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private async Task EnsureFreshAsync(SchoolClass cls, Term term, CancellationToken ct)
    {
        if (await IsPublishedAsync(cls.Id, term.Id, ct)) return;
        var roster = await RosterAsync(cls, term, ct);
        var rows = await db.TermResults.AsNoTracking().Where(x => x.ClassId == cls.Id && x.TermId == term.Id)
            .Select(x => new { x.EnrolmentId, x.Stale, x.ClassSize }).ToListAsync(ct);
        if (roster.Count == 0 && rows.Count == 0) return;
        // A student who joined or left changes everyone's position, so any difference in who is in the class needs a recompute.
        var ids = roster.Select(x => x.EnrolmentId).ToHashSet();
        if (rows.Count == ids.Count && rows.All(x => !x.Stale && x.ClassSize == ids.Count && ids.Contains(x.EnrolmentId))) return;
        await ComputeAsync(cls.Id, term.Id, ct);
    }

    private async Task TakeLocksAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        await db.LockSharedAsync(AcademicService.AssessmentLockKey(TenantId), ct);
        await db.LockAsync(ScoreService.LockKey(classId, termId), ct);
    }

    private sealed record RosterRow(Guid EnrolmentId, Guid StudentId, string Name, string AdmissionNo, bool Current = true);

    /// <summary>
    /// Who is in the class's results for the term: the active students now enrolled in the class, plus anyone who has scores in the
    /// class's subjects for the term (they were in the class that term, whatever their status or class is now).
    /// </summary>
    private async Task<List<RosterRow>> RosterAsync(SchoolClass cls, Term term, CancellationToken ct)
    {
        var scored = await db.Scores.AsNoTracking()
            .Where(s => s.TermId == term.Id && db.ClassSubjects.Any(c => c.Id == s.ClassSubjectId && c.ClassId == cls.Id))
            .Select(s => s.EnrolmentId).Distinct().ToListAsync(ct);
        var rows = await db.Enrolments.AsNoTracking()
            .Join(db.Students.AsNoTracking(), e => e.StudentId, s => s.Id,
                (e, s) => new { e.ClassId, e.SessionId, s.Status, EnrolmentId = e.Id, StudentId = s.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .Where(x => (x.ClassId == cls.Id && x.SessionId == cls.SessionId && x.Status == StudentStatus.Active) || scored.Contains(x.EnrolmentId))
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        return rows.Select(x => new RosterRow(x.EnrolmentId, x.StudentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo,
            x.ClassId == cls.Id && x.SessionId == cls.SessionId && x.Status == StudentStatus.Active)).ToList();
    }

    private async Task ComputeCoreAsync(SchoolClass cls, Term term, CancellationToken ct)
    {
        if (await ModeOfAsync(cls, ct) == AssessmentMode.Developmental)
        {
            await ComputeDevelopmentalAsync(cls, term, ct);
            return;
        }

        var roster = await RosterAsync(cls, term, ct);
        var enrolmentIds = roster.Select(x => x.EnrolmentId).ToList();
        var components = (await academic.EnsureSchemeAsync(cls.LevelId, ct)).Select(c => c.Id).ToHashSet();
        var bands = await academic.BandsFor(cls.LevelId);
        var classSubjectIds = await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == cls.Id).Select(c => c.Id).ToListAsync(ct);
        var locked = await db.TermResultLocks.AnyAsync(l => l.ClassId == cls.Id && l.TermId == term.Id, ct);

        var scoreRows = await db.Scores.AsNoTracking()
            .Where(s => s.TermId == term.Id && classSubjectIds.Contains(s.ClassSubjectId) && enrolmentIds.Contains(s.EnrolmentId))
            .Select(s => new { s.EnrolmentId, s.ClassSubjectId, s.ComponentId, s.Value }).ToListAsync(ct);
        var byCell = scoreRows.Where(s => components.Contains(s.ComponentId)).GroupBy(s => (s.EnrolmentId, s.ClassSubjectId))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(x => x.Value), Count: g.Count()));

        // Before the lock a subject with any score is counted (incomplete when a component is missing) and one with none is left out.
        // After the lock every subject of the class counts, and whatever is missing counts as 0.
        var subjectTotals = new Dictionary<(Guid Enrolment, Guid ClassSubject), (decimal Total, bool Incomplete)>();
        var studentIncomplete = roster.ToDictionary(x => x.EnrolmentId, _ => false);
        foreach (var student in roster)
            foreach (var subjectId in classSubjectIds)
            {
                var has = byCell.TryGetValue((student.EnrolmentId, subjectId), out var cell);
                var missing = !has || cell.Count < components.Count;
                if (missing && !locked) studentIncomplete[student.EnrolmentId] = true;
                if (has) subjectTotals[(student.EnrolmentId, subjectId)] = (cell.Total, missing && !locked);
                else if (locked) subjectTotals[(student.EnrolmentId, subjectId)] = (0m, false);
            }

        var existingSubjects = await db.SubjectResults.Where(r => r.ClassId == cls.Id && r.TermId == term.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var tenantId = TenantId;
        var kept = new HashSet<Guid>();
        foreach (var subjectId in classSubjectIds)
        {
            var scored = subjectTotals.Where(t => t.Key.ClassSubject == subjectId).ToList();
            if (scored.Count == 0) continue;
            var average = ResultRules.Average(scored.Select(t => (decimal?)t.Value.Total)) ?? 0m;
            var positions = ResultRules.Positions(scored.Select(t => (Id: t.Key.Enrolment, Score: t.Value.Total)).ToList());
            foreach (var t in scored)
            {
                var band = ResultRules.Grade(t.Value.Total, bands);
                var row = existingSubjects.FirstOrDefault(r => r.EnrolmentId == t.Key.Enrolment && r.ClassSubjectId == subjectId);
                if (row is null)
                {
                    row = new SubjectResult { TenantId = tenantId, ClassId = cls.Id, EnrolmentId = t.Key.Enrolment, ClassSubjectId = subjectId, TermId = term.Id, Grade = band.Grade };
                    db.SubjectResults.Add(row);
                }
                row.Total = t.Value.Total;
                row.Grade = band.Grade;
                // The subject comment is the teacher's. Compute leaves a new one null and keeps one already written.
                row.ClassAverage = average;
                row.PositionInSubject = positions[t.Key.Enrolment];
                row.Incomplete = t.Value.Incomplete;
                kept.Add(row.Id);
            }
        }
        db.SubjectResults.RemoveRange(existingSubjects.Where(r => !kept.Contains(r.Id)));

        var termTotals = roster.ToDictionary(x => x.EnrolmentId, x => subjectTotals.Where(t => t.Key.Enrolment == x.EnrolmentId).Select(t => t.Value.Total).ToList());
        var averages = roster.ToDictionary(x => x.EnrolmentId, x => ResultRules.Average(termTotals[x.EnrolmentId].Select(t => (decimal?)t)) ?? 0m);
        var classPositions = ResultRules.Positions(roster.Select(x => (Id: x.EnrolmentId, Score: averages[x.EnrolmentId])).ToList());
        var attendance = await AttendanceAsync(cls, term, roster, ct);

        var existing = await db.TermResults.Where(r => r.ClassId == cls.Id && r.TermId == term.Id).ToListAsync(ct);
        // Rows of students who are no longer part of the class's results (unpublished only) go.
        db.TermResults.RemoveRange(existing.Where(r => !enrolmentIds.Contains(r.EnrolmentId)));
        foreach (var student in roster)
        {
            var row = existing.FirstOrDefault(r => r.EnrolmentId == student.EnrolmentId);
            if (row is null)
            {
                row = new TermResult { TenantId = tenantId, ClassId = cls.Id, EnrolmentId = student.EnrolmentId, TermId = term.Id };
                db.TermResults.Add(row);
            }
            var counts = attendance[student.EnrolmentId];
            row.Total = termTotals[student.EnrolmentId].Sum();
            row.Average = averages[student.EnrolmentId];
            row.Position = classPositions[student.EnrolmentId];
            row.ClassSize = roster.Count;
            row.DaysPresent = counts.Present;
            row.DaysAbsent = counts.Absent;
            row.DaysLate = counts.Late;
            row.DaysExcused = counts.Excused;
            row.DaysOpen = counts.Open;
            row.AssessmentMode = AssessmentMode.Numeric;
            row.Incomplete = studentIncomplete[student.EnrolmentId];
            row.ComputedAt = now;
            row.Stale = false;
        }
        await SaveAsync(ct);
    }

    /// <summary>
    /// A developmental class has no subject totals. Every area without a rating leaves the student incomplete.
    /// Attendance and remarks are kept. Position stays 0 so the card does not rank the class.
    /// </summary>
    private async Task ComputeDevelopmentalAsync(SchoolClass cls, Term term, CancellationToken ct)
    {
        var roster = await RosterAsync(cls, term, ct);
        var enrolmentIds = roster.Select(x => x.EnrolmentId).ToList();
        var areas = await developmental.AreasForAsync(cls.LevelId, ct);
        var areaIds = areas.Select(a => a.Id).ToList();
        var rated = areaIds.Count == 0 || enrolmentIds.Count == 0
            ? []
            : await db.DevelopmentalRatings.AsNoTracking()
                .Where(r => r.TermId == term.Id && enrolmentIds.Contains(r.EnrolmentId) && areaIds.Contains(r.AreaId))
                .Select(r => new { r.EnrolmentId, r.AreaId }).ToListAsync(ct);
        var ratedSet = rated.Select(r => (r.EnrolmentId, r.AreaId)).ToHashSet();

        var existingSubjects = await db.SubjectResults.Where(r => r.ClassId == cls.Id && r.TermId == term.Id).ToListAsync(ct);
        db.SubjectResults.RemoveRange(existingSubjects);

        var attendance = await AttendanceAsync(cls, term, roster, ct);
        var existing = await db.TermResults.Where(r => r.ClassId == cls.Id && r.TermId == term.Id).ToListAsync(ct);
        db.TermResults.RemoveRange(existing.Where(r => !enrolmentIds.Contains(r.EnrolmentId)));
        var now = clock.GetUtcNow();
        var tenantId = TenantId;
        foreach (var student in roster)
        {
            var row = existing.FirstOrDefault(r => r.EnrolmentId == student.EnrolmentId);
            if (row is null)
            {
                row = new TermResult { TenantId = tenantId, ClassId = cls.Id, EnrolmentId = student.EnrolmentId, TermId = term.Id };
                db.TermResults.Add(row);
            }
            var counts = attendance[student.EnrolmentId];
            row.Total = 0;
            row.Average = 0;
            row.Position = 0;
            row.ClassSize = roster.Count;
            row.DaysPresent = counts.Present;
            row.DaysAbsent = counts.Absent;
            row.DaysLate = counts.Late;
            row.DaysExcused = counts.Excused;
            row.DaysOpen = counts.Open;
            row.AssessmentMode = AssessmentMode.Developmental;
            row.Incomplete = areas.Any(a => !ratedSet.Contains((student.EnrolmentId, a.Id)));
            row.ComputedAt = now;
            row.Stale = false;
        }
        await SaveAsync(ct);
    }

    private Task<AssessmentMode> ModeOfAsync(SchoolClass cls, CancellationToken ct) =>
        db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct);

    private sealed record AttendanceCounts(int Present, int Absent, int Late, int Excused, int Open);

    /// <summary>
    /// Attendance for each student, counted only from the first register that lists them in this class, so a student who
    /// joined mid-term is not measured against days before they arrived. Present is present marks only and never exceeds open.
    /// </summary>
    private async Task<Dictionary<Guid, AttendanceCounts>> AttendanceAsync(SchoolClass cls, Term term, List<RosterRow> roster, CancellationToken ct)
    {
        var registers = await db.AttendanceRegisters.AsNoTracking()
            .Where(r => r.ClassId == cls.Id && r.Date >= term.StartDate && r.Date <= term.EndDate)
            .Select(r => new { r.Id, r.Date }).ToListAsync(ct);
        var enrolmentIds = roster.Select(x => x.EnrolmentId).ToList();
        var current = roster.ToDictionary(x => x.EnrolmentId, x => x.Current);
        var registerIds = registers.Select(r => r.Id).ToList();
        var dateOf = registers.ToDictionary(r => r.Id, r => r.Date);
        var marks = registerIds.Count == 0 ? [] : await db.AttendanceMarks.AsNoTracking()
            .Where(m => enrolmentIds.Contains(m.EnrolmentId) && registerIds.Contains(m.RegisterId))
            .Select(m => new { m.EnrolmentId, m.RegisterId, m.Status }).ToListAsync(ct);
        var openDates = registers.Select(r => r.Date).Distinct().ToList();

        var result = new Dictionary<Guid, AttendanceCounts>();
        foreach (var id in enrolmentIds)
        {
            var mine = marks.Where(m => m.EnrolmentId == id).ToList();
            if (mine.Count == 0) { result[id] = new(0, 0, 0, 0, 0); continue; }
            var from = mine.Min(m => dateOf[m.RegisterId]);
            // A student who has left the class is measured only up to their last register there.
            var to = current[id] ? DateOnly.MaxValue : mine.Max(m => dateOf[m.RegisterId]);
            var open = openDates.Count(d => d >= from && d <= to);
            var window = mine.Where(m => dateOf[m.RegisterId] >= from && dateOf[m.RegisterId] <= to).ToList();
            int Count(AttendanceStatus status) => window.Count(m => m.Status == status);
            result[id] = new(Math.Min(Count(AttendanceStatus.Present), open), Count(AttendanceStatus.Absent), Count(AttendanceStatus.Late), Count(AttendanceStatus.Excused), open);
        }
        return result;
    }

    // ---------- View ----------

    /// <param name="subjectLimit">Null shows every subject. A set shows only those class-subjects. Student totals stay the stored figures.</param>
    private async Task<ResultsDto> ViewAsync(SchoolClass cls, Term term, HashSet<Guid>? subjectLimit, CancellationToken ct)
    {
        // The stored rows of this class and term, whoever is in the class now (published rows are frozen).
        var results = await db.TermResults.AsNoTracking().Where(r => r.ClassId == cls.Id && r.TermId == term.Id).ToListAsync(ct);
        var resultIds = results.Select(r => r.EnrolmentId).ToHashSet();
        var roster = (await db.Enrolments.AsNoTracking().Where(e => resultIds.Contains(e.Id))
                .Join(db.Students.AsNoTracking(), e => e.StudentId, s => s.Id, (e, s) => new { EnrolmentId = e.Id, StudentId = s.Id, s.FirstName, s.LastName, s.AdmissionNo }).ToListAsync(ct))
            .Select(x => new RosterRow(x.EnrolmentId, x.StudentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();
        var pending = (await RosterAsync(cls, term, ct)).Count(x => !resultIds.Contains(x.EnrolmentId));
        var subjects = await db.SubjectResults.AsNoTracking().Where(r => r.ClassId == cls.Id && r.TermId == term.Id)
            .Join(db.ClassSubjects.AsNoTracking(), r => r.ClassSubjectId, c => c.Id, (r, c) => new { Result = r, c.SubjectId, ClassSubjectId = c.Id })
            .Join(db.Subjects.AsNoTracking(), x => x.SubjectId, s => s.Id, (x, s) => new { x.Result, x.ClassSubjectId, s.Name, s.Code }).ToListAsync(ct);
        if (subjectLimit is not null)
            subjects = subjects.Where(s => subjectLimit.Contains(s.ClassSubjectId)).ToList();
        var locked = await db.TermResultLocks.AnyAsync(l => l.ClassId == cls.Id && l.TermId == term.Id, ct);
        // Each row shows the mode it was computed in, so changing the level's mode does not rewrite a published card.
        var modes = results.ToDictionary(r => r.EnrolmentId, RowMode);
        var developmentalIds = modes.Where(m => m.Value == AssessmentMode.Developmental).Select(m => m.Key).ToHashSet();
        var developmental = developmentalIds.Count > 0
            ? await DevelopmentalByEnrolmentAsync(cls, term, developmentalIds, ct)
            : new Dictionary<Guid, List<ResultDevelopmentalDto>>();

        var students = roster.Select(x => new { Student = x, Result = results.FirstOrDefault(r => r.EnrolmentId == x.EnrolmentId) })
            .Where(x => x.Result is not null)
            .OrderBy(x => x.Result!.Position).ThenBy(x => x.Student.Name)
            .Select(x => new ResultStudentDto(
                x.Student.EnrolmentId, x.Student.StudentId, x.Student.Name, x.Student.AdmissionNo, x.Result!.Total, x.Result.Average, x.Result.Position,
                x.Result.ClassSize, x.Result.DaysPresent, x.Result.DaysAbsent, x.Result.DaysLate, x.Result.DaysExcused, x.Result.DaysOpen, x.Result.TeacherRemark, x.Result.PrincipalRemark, x.Result.Incomplete,
                modes[x.Student.EnrolmentId] == AssessmentMode.Developmental
                    ? []
                    : subjects.Where(s => s.Result.EnrolmentId == x.Student.EnrolmentId).OrderBy(s => s.Name)
                        .Select(s => new ResultSubjectDto(s.ClassSubjectId, s.Name, s.Code, s.Result.Total, s.Result.Grade, s.Result.Remark, s.Result.ClassAverage, s.Result.PositionInSubject, s.Result.Incomplete))
                        .ToList(),
                modes[x.Student.EnrolmentId] == AssessmentMode.Developmental
                    ? developmental.GetValueOrDefault(x.Student.EnrolmentId) ?? []
                    : [],
                modes[x.Student.EnrolmentId]))
            .ToList();
        var sheetMode = results.Count > 0
            ? (developmentalIds.Count > 0 ? AssessmentMode.Developmental : AssessmentMode.Numeric)
            : await ModeOfAsync(cls, ct);
        return new ResultsDto(cls.Id, cls.Name, term.Id, term.Name, locked, results.Max(r => r.PublishedAt), pending, students, sheetMode);
    }

    /// <summary>The mode a stored row was computed in. Rows from before modes existed were numeric.</summary>
    private static AssessmentMode RowMode(TermResult row) => row.AssessmentMode ?? AssessmentMode.Numeric;

    private async Task<Dictionary<Guid, List<ResultDevelopmentalDto>>> DevelopmentalByEnrolmentAsync(SchoolClass cls, Term term, HashSet<Guid> enrolmentIds, CancellationToken ct)
    {
        var areas = await developmental.AreasForAsync(cls.LevelId, ct);
        var areaIds = areas.Select(a => a.Id).ToList();
        var ratings = areaIds.Count == 0 || enrolmentIds.Count == 0
            ? []
            : await db.DevelopmentalRatings.AsNoTracking()
                .Where(r => r.TermId == term.Id && enrolmentIds.Contains(r.EnrolmentId) && areaIds.Contains(r.AreaId))
                .Select(r => new { r.EnrolmentId, r.AreaId, r.ScaleId, r.Comment }).ToListAsync(ct);
        var scaleIds = ratings.Select(r => r.ScaleId).Distinct().ToList();
        var scales = scaleIds.Count == 0
            ? new Dictionary<Guid, (string Label, string Code)>()
            : await db.DevelopmentalScales.AsNoTracking().Where(s => scaleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => (s.Label, s.Code), ct);
        var byEnrolment = ratings.GroupBy(r => r.EnrolmentId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.AreaId));
        var result = new Dictionary<Guid, List<ResultDevelopmentalDto>>();
        foreach (var id in enrolmentIds)
        {
            byEnrolment.TryGetValue(id, out var mine);
            result[id] = areas.Select(a =>
            {
                var rating = mine is not null && mine.TryGetValue(a.Id, out var row) ? row : null;
                var scale = rating is not null && scales.TryGetValue(rating.ScaleId, out var point) ? point.Label : null;
                return new ResultDevelopmentalDto(a.Name, scale, rating?.Comment);
            }).ToList();
        }
        return result;
    }

    // ---------- Rules ----------

    private async Task<(SchoolClass Cls, Term Term)> LoadAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");
        return (cls, term);
    }

    /// <summary>Null for leadership and the class teacher (every subject). Otherwise the class-subjects this member teaches.</summary>
    private async Task<HashSet<Guid>?> SubjectLimitAsync(SchoolClass cls, CancellationToken ct)
    {
        if (access.IsLeadership || await IsClassTeacherAsync(cls, ct)) return null;
        if (await access.StaffIdAsync(ct) is not { } staffId) return [];
        return (await db.ClassSubjects.AsNoTracking().Where(cs => cs.ClassId == cls.Id && cs.TeacherStaffId == staffId).Select(cs => cs.Id).ToListAsync(ct)).ToHashSet();
    }

    private async Task<bool> IsClassTeacherAsync(SchoolClass cls, CancellationToken ct) =>
        await access.StaffIdAsync(ct) is { } staffId && cls.ClassTeacherStaffId == staffId;

    /// <summary>True when the member is class teacher of, or teaches a subject in, at least one of these classes.</summary>
    private async Task<bool> TeachesAnyAsync(IReadOnlyList<Guid> classIds, CancellationToken ct)
    {
        var taught = await access.TaughtClassIdsAsync(ct);
        return classIds.Any(taught.Contains);
    }

    private async Task EnsureCanReadAsync(SchoolClass cls, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not read results.");
        if (access.IsLeadership) return;
        if (await IsClassTeacherAsync(cls, ct)) return;
        if (await access.TeachesClassAsync(cls.Id, ct)) return;
        throw new ForbiddenException("Only the principal and the class teacher can see a class's results. Subject teachers use the score sheet.");
    }

    /// <summary>Report cards and overall remarks stay with leadership and the class teacher. A subject teacher who can read the grid cannot write them.</summary>
    private async Task EnsureClassTeacherOrLeadershipAsync(SchoolClass cls, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not read results.");
        if (access.IsLeadership) return;
        if (await IsClassTeacherAsync(cls, ct)) return;
        throw new ForbiddenException("Only the principal and the class teacher can see a class's results. Subject teachers use the score sheet.");
    }
}
