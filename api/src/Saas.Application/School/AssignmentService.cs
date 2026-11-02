using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record CreateAssignmentRequest(
    Guid ClassSubjectId, Guid TermId, string? Title, string? Instructions, DateTimeOffset? DueAt, decimal? MaxMarks, Guid? AssessmentComponentId);
public sealed record MarkAssignmentRequest(decimal? Mark, string? Feedback, bool? Reopen, bool? Received);
public sealed record AssignmentSubmissionDto(
    Guid EnrolmentId, string Name, string AdmissionNo, AssignmentSubmissionStatus Status, bool Submitted, bool Late,
    string? Body, DateTimeOffset? SubmittedAt, decimal? Mark, string? Feedback, string? FileName, string? FileUrl);
public sealed record AssignmentDto(
    Guid Id, Guid ClassSubjectId, Guid TermId, string Title, string Instructions, DateTimeOffset DueAt,
    decimal? MaxMarks, Guid? AssessmentComponentId, IReadOnlyList<AssignmentSubmissionDto> Submissions);
public sealed record StudentAssignmentDto(
    Guid Id, string Title, string Instructions, DateTimeOffset DueAt, decimal? MaxMarks, bool Submitted, bool Late,
    string? Body, decimal? Mark, string? Feedback, string? FileName, string? FileUrl);
/// <summary>What a parent may see: the title, when it is due, whether it has come in, and the mark once the teacher has saved one.</summary>
public sealed record ParentAssignmentDto(Guid Id, string Title, DateTimeOffset DueAt, bool Submitted, decimal? Mark);
public sealed record AssignmentFile(Stream? Content, string? RedirectUrl, string ContentType, string FileName);

/// <summary>
/// Homework for one class subject and term. The subject teacher and leadership set it and mark it. A counted mark upserts the same
/// <see cref="Score"/> cell the score sheet uses. Practice does not. A student with a login sends text and one private file; a nursery
/// teacher records received on the roster, because those children do not sign in.
/// </summary>
public sealed class AssignmentService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, ParentAccess parents, StudentAccess students,
    AcademicService academic, ScoreService scores, IDbErrors dbErrors, IFileStorage storage, IStudentPhotoProcessor images,
    SchoolPhotoSigner signer, TimeProvider clock)
{
    public const int MaxTitleLength = 150;
    public const int MaxInstructionsLength = 5000;
    public const int MaxBodyLength = 5000;
    public const int MaxFeedbackLength = 500;
    public const int AttachmentMaxSide = 1600;
    private const decimal MarkCeiling = 1000;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Staff ----------

    public async Task<IReadOnlyList<AssignmentDto>> ListAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        RefuseBursar();
        var (cs, cls, _) = await LoadSubjectAsync(classSubjectId, termId, ct);
        await EnsureCanSetAsync(cs.Id, ct);
        var rows = await db.Assignments.AsNoTracking().Where(a => a.ClassSubjectId == cs.Id && a.TermId == termId)
            .OrderBy(a => a.DueAt).ThenBy(a => a.Title).ToListAsync(ct);
        return await WithRosterAsync(cls, rows, ct);
    }

    public async Task<AssignmentDto> CreateAsync(CreateAssignmentRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var title = (r.Title ?? "").Trim();
        if (title.Length == 0) throw BadRequestException.Field("title", "Enter a title.");
        if (title.Length > MaxTitleLength) throw BadRequestException.Field("title", $"Use {MaxTitleLength} characters or fewer for the title.");
        var instructions = (r.Instructions ?? "").Trim();
        if (instructions.Length == 0) throw BadRequestException.Field("instructions", "Write the instructions.");
        if (instructions.Length > MaxInstructionsLength) throw BadRequestException.Field("instructions", $"Use {MaxInstructionsLength} characters or fewer.");
        if (r.DueAt is not { } dueAt || dueAt == default) throw BadRequestException.Field("dueAt", "Choose a due date.");
        if (r.MaxMarks is { } max && (max <= 0 || max > MarkCeiling || decimal.Round(max, 1) != max))
            throw BadRequestException.Field("maxMarks", $"The maximum mark must be more than 0 and no more than {MarkCeiling:0}, with at most one decimal place.");

        var userId = user.UserId ?? throw new ForbiddenException();
        var (cs, cls, _) = await LoadSubjectAsync(r.ClassSubjectId, r.TermId, ct);
        await EnsureCanSetAsync(cs.Id, ct);
        if (r.AssessmentComponentId is { } componentId) await ComponentOfAsync(cls, componentId, ct);

        var row = new Assignment
        {
            TenantId = TenantId, ClassSubjectId = cs.Id, TermId = r.TermId, Title = title, Instructions = instructions, DueAt = dueAt,
            MaxMarks = r.MaxMarks, AssessmentComponentId = r.AssessmentComponentId, CreatedBy = userId,
        };
        db.Assignments.Add(row);
        await SaveAsync(ct);
        return (await WithRosterAsync(cls, [row], ct))[0];
    }

    public async Task<AssignmentDto> MarkAsync(Guid id, Guid enrolmentId, MarkAssignmentRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("This assignment");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(assignment.ClassSubjectId, ct))
            throw new NotFoundException("This assignment");
        if (r.Mark is null && r.Feedback is null && r.Received is null && r.Reopen is null)
            throw BadRequestException.Field("mark", "Send a mark, feedback, whether it was received, or reopen.");

        var cls = await ClassOfAsync(assignment.ClassSubjectId, ct);
        await EnsureOnRosterAsync(cls, enrolmentId, ct);
        var feedback = r.Feedback is null ? null : CleanFeedback(r.Feedback);

        // Joins an import's transaction when one is open, so a file of marks lands whole or not at all.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(assignment.Id), ct);
        var row = await SubmissionAsync(assignment.Id, enrolmentId, ct);
        if (r.Received is false && row.SubmittedAt is null && row.FileKey is null && string.IsNullOrEmpty(row.Body))
        {
            row.Status = AssignmentSubmissionStatus.None;
            row.Late = false;
        }
        if (r.Received is true && row.Status == AssignmentSubmissionStatus.None)
            row.Status = AssignmentSubmissionStatus.Received;
        if (feedback is not null) row.Feedback = feedback;
        if (r.Mark is { } mark)
        {
            await ApplyMarkAsync(assignment, cls, enrolmentId, mark, ct);
            row.Mark = mark;
            row.Reopened = false;
            row.Status = AssignmentSubmissionStatus.Returned;
        }
        if (r.Reopen is true) row.Reopened = true;
        row.MarkedByStaffId = await access.StaffIdAsync(ct);
        await SaveAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (await WithRosterAsync(cls, [assignment], ct))[0];
    }

    /// <summary>The file behind a valid signed link, or null. Reads across schools: the signature is the only gate.</summary>
    public async Task<AssignmentFile?> FileByLinkAsync(Guid id, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsAssignmentFileValid(id, expires, signature)) throw new ForbiddenException("This link has expired. Open the assignment again.");
        var row = await db.AssignmentSubmissions.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.FileKey, s.FileType, s.FileName }).FirstOrDefaultAsync(ct);
        if (row?.FileKey is null) return null;
        var type = row.FileType ?? "application/octet-stream";
        var name = row.FileName ?? "attachment";
        if (storage.TemporaryUrl(row.FileKey, SchoolPhotoSigner.Lifetime) is { } url) return new AssignmentFile(null, url, type, name);
        var stream = await storage.OpenReadAsync(row.FileKey, ct);
        return stream is null ? null : new AssignmentFile(stream, null, type, name);
    }

    // ---------- Student ----------

    public async Task<IReadOnlyList<StudentAssignmentDto>> ForSelfAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var enrolment = await CurrentEnrolmentAsync(me.StudentId, ct);
        if (enrolment is null) return [];
        var classSubjectIds = await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == enrolment.ClassId).Select(c => c.Id).ToListAsync(ct);
        var rows = await db.Assignments.AsNoTracking().Where(a => classSubjectIds.Contains(a.ClassSubjectId))
            .OrderByDescending(a => a.DueAt).ThenBy(a => a.Title).ToListAsync(ct);
        var ids = rows.Select(a => a.Id).ToList();
        var submissions = await db.AssignmentSubmissions.AsNoTracking()
            .Where(s => ids.Contains(s.AssignmentId) && s.EnrolmentId == enrolment.Id).ToListAsync(ct);
        var byAssignment = submissions.ToDictionary(s => s.AssignmentId);
        return rows.Select(a => StudentDto(a, byAssignment.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<StudentAssignmentDto> SubmitAsync(Guid id, string? text, Upload? file, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var enrolment = await CurrentEnrolmentAsync(me.StudentId, ct) ?? throw new NotFoundException("This assignment");
        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("This assignment");
        var classId = await db.ClassSubjects.Where(c => c.Id == assignment.ClassSubjectId).Select(c => c.ClassId).FirstAsync(ct);
        if (classId != enrolment.ClassId) throw new NotFoundException("This assignment");

        var body = (text ?? "").Trim();
        if (body.Length > MaxBodyLength) throw BadRequestException.Field("body", $"Use {MaxBodyLength} characters or fewer.");
        var stored = file is null || file.Length <= 0 ? null : await ReadFileAsync(file, ct);
        if (body.Length == 0 && stored is null) throw BadRequestException.Field("body", "Write your answer or attach a file.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(LockKey(assignment.Id), ct);
        var row = await SubmissionAsync(assignment.Id, enrolment.Id, ct);
        if (row.Mark is not null && !row.Reopened)
            throw new ConflictException("This work has been marked. Ask your teacher to reopen it before you send it again.", "submission_marked");

        string? oldKey = null;
        if (stored is not null)
        {
            var key = $"private/school/{TenantId}/assignments/{row.Id:N}-{Guid.NewGuid():N}{stored.Extension}";
            await using (var stream = new MemoryStream(stored.Bytes)) await storage.SaveAsync(key, stream, stored.ContentType, ct);
            oldKey = row.FileKey;
            row.FileKey = key;
            row.FileType = stored.ContentType;
            row.FileName = stored.Name;
        }
        if (body.Length > 0) row.Body = body;
        var now = clock.GetUtcNow();
        row.SubmittedAt = now;
        row.Late = now > assignment.DueAt;
        row.Status = AssignmentSubmissionStatus.Received;
        row.Reopened = false;
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        await DeleteQuietlyAsync(oldKey, ct);
        return StudentDto(assignment, row);
    }

    // ---------- Parent ----------

    public async Task<IReadOnlyList<ParentAssignmentDto>> ForChildAsync(Guid studentId, CancellationToken ct)
    {
        await parents.RequireChildAsync(studentId, ct);
        var enrolment = await CurrentEnrolmentAsync(studentId, ct);
        if (enrolment is null) return [];
        var classSubjectIds = await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == enrolment.ClassId).Select(c => c.Id).ToListAsync(ct);
        var rows = await db.Assignments.AsNoTracking().Where(a => classSubjectIds.Contains(a.ClassSubjectId))
            .OrderByDescending(a => a.DueAt).ThenBy(a => a.Title).ToListAsync(ct);
        var ids = rows.Select(a => a.Id).ToList();
        var submissions = await db.AssignmentSubmissions.AsNoTracking()
            .Where(s => ids.Contains(s.AssignmentId) && s.EnrolmentId == enrolment.Id)
            .ToDictionaryAsync(s => s.AssignmentId, ct);
        return rows.Select(a =>
        {
            submissions.TryGetValue(a.Id, out var submission);
            return new ParentAssignmentDto(a.Id, a.Title, a.DueAt, IsSubmitted(submission), submission?.Mark);
        }).ToList();
    }

    // ---------- Rules ----------

    /// <summary>
    /// A counted mark becomes the component score: the assignment maximum scales onto the component maximum.
    /// Without an assignment maximum, the mark is already on the component's scale.
    /// </summary>
    private async Task ApplyMarkAsync(Assignment assignment, SchoolClass cls, Guid enrolmentId, decimal mark, CancellationToken ct)
    {
        if (decimal.Round(mark, 1) != mark) throw BadRequestException.Field("mark", MarkDecimals);
        if (mark < 0) throw BadRequestException.Field("mark", MarkNegative);

        if (assignment.AssessmentComponentId is not Guid componentId)
        {
            var practiceLimit = assignment.MaxMarks ?? MarkCeiling;
            if (mark > practiceLimit) throw BadRequestException.Field("mark", MarkRange(practiceLimit));
            return;
        }

        var component = await ComponentOfAsync(cls, componentId, ct);
        var limit = assignment.MaxMarks ?? component.MaxScore;
        if (mark > limit) throw BadRequestException.Field("mark", MarkRange(limit));
        var value = assignment.MaxMarks is { } assignmentMax
            ? decimal.Round(component.MaxScore * mark / assignmentMax, 1, MidpointRounding.AwayFromZero)
            : mark;
        await scores.UpsertAsync(enrolmentId, assignment.ClassSubjectId, assignment.TermId, componentId, value, ct);
    }

    internal const string MarkDecimals = "Marks can have at most one decimal place.";
    internal const string MarkNegative = "The mark can't be negative.";
    internal static string MarkRange(decimal limit) => $"The mark must be between 0 and {limit:0.#}.";

    /// <summary>
    /// The highest mark this assignment takes, as <see cref="MarkAsync"/> checks it: its own maximum, else the counted component's, else the
    /// practice ceiling. The marks import uses it to report a mark out of range on its row.
    /// </summary>
    internal async Task<decimal> MarkLimitAsync(Assignment assignment, SchoolClass cls, CancellationToken ct) =>
        assignment.MaxMarks
        ?? (assignment.AssessmentComponentId is Guid componentId ? (await ComponentOfAsync(cls, componentId, ct)).MaxScore : MarkCeiling);

    /// <summary>The sentence <see cref="MarkAsync"/> refuses this mark with, or null.</summary>
    internal static string? MarkError(decimal mark, decimal limit) =>
        decimal.Round(mark, 1) != mark ? MarkDecimals : mark < 0 ? MarkNegative : mark > limit ? MarkRange(limit) : null;

    private async Task<AssessmentComponent> ComponentOfAsync(SchoolClass cls, Guid componentId, CancellationToken ct)
    {
        if (await db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct) == AssessmentMode.Developmental)
            throw new BadRequestException("This class uses developmental assessment.");
        var components = await academic.EnsureSchemeAsync(cls.LevelId, ct);
        return components.FirstOrDefault(c => c.Id == componentId)
            ?? throw BadRequestException.Field("assessmentComponentId", "Choose a component of this class's assessment.");
    }

    private void RefuseBursar()
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not set assignments.");
    }

    private async Task EnsureCanSetAsync(Guid classSubjectId, CancellationToken ct)
    {
        if (access.IsLeadership || await access.TeachesClassSubjectAsync(classSubjectId, ct)) return;
        throw new ForbiddenException("You can only set assignments for subjects you teach.");
    }

    private async Task<(ClassSubject Cs, SchoolClass Cls, Term Term)> LoadSubjectAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        var cs = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classSubjectId, ct) ?? throw new NotFoundException("Class subject");
        var cls = await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == cs.ClassId, ct);
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");
        return (cs, cls, term);
    }

    private async Task<SchoolClass> ClassOfAsync(Guid classSubjectId, CancellationToken ct)
    {
        var classId = await db.ClassSubjects.Where(c => c.Id == classSubjectId).Select(c => c.ClassId).FirstAsync(ct);
        return await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == classId, ct);
    }

    private async Task EnsureOnRosterAsync(SchoolClass cls, Guid enrolmentId, CancellationToken ct)
    {
        var onRoster = await db.Enrolments.AnyAsync(e => e.Id == enrolmentId && e.ClassId == cls.Id && e.SessionId == cls.SessionId
            && db.Students.Any(s => s.Id == e.StudentId && s.Status == StudentStatus.Active), ct);
        if (!onRoster) throw BadRequestException.Field("enrolmentId", "This student is not in this class.");
    }

    private async Task<Enrolment?> CurrentEnrolmentAsync(Guid studentId, CancellationToken ct) =>
        await db.Enrolments.AsNoTracking()
            .Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .FirstOrDefaultAsync(ct);

    private async Task<AssignmentSubmission> SubmissionAsync(Guid assignmentId, Guid enrolmentId, CancellationToken ct)
    {
        var row = await db.AssignmentSubmissions.FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.EnrolmentId == enrolmentId, ct);
        if (row is not null) return row;
        row = new AssignmentSubmission { TenantId = TenantId, AssignmentId = assignmentId, EnrolmentId = enrolmentId };
        db.AssignmentSubmissions.Add(row);
        return row;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("This was saved from another device at the same moment. Reload and try again.", "assignment_conflict");
        }
    }

    private sealed record RosterRow(Guid EnrolmentId, string Name, string AdmissionNo);

    private async Task<List<AssignmentDto>> WithRosterAsync(SchoolClass cls, IReadOnlyList<Assignment> rows, CancellationToken ct)
    {
        var loaded = await db.Enrolments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.SessionId == cls.SessionId)
            .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id,
                (e, s) => new { EnrolmentId = e.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        var roster = loaded.Select(x => new RosterRow(x.EnrolmentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();
        var ids = rows.Select(a => a.Id).ToList();
        var submissions = await db.AssignmentSubmissions.AsNoTracking().Where(s => ids.Contains(s.AssignmentId)).ToListAsync(ct);
        return rows.Select(a =>
        {
            var mine = submissions.Where(s => s.AssignmentId == a.Id).ToDictionary(s => s.EnrolmentId);
            var list = roster.Select(r =>
            {
                mine.TryGetValue(r.EnrolmentId, out var submission);
                return ToSubmission(r, submission);
            }).ToList();
            return new AssignmentDto(a.Id, a.ClassSubjectId, a.TermId, a.Title, a.Instructions, a.DueAt, a.MaxMarks, a.AssessmentComponentId, list);
        }).ToList();
    }

    private AssignmentSubmissionDto ToSubmission(RosterRow roster, AssignmentSubmission? row) => new(
        roster.EnrolmentId, roster.Name, roster.AdmissionNo, row?.Status ?? AssignmentSubmissionStatus.None, IsSubmitted(row), row?.Late ?? false,
        row?.Body, row?.SubmittedAt, row?.Mark, row?.Feedback, row?.FileName, row?.FileKey is null ? null : signer.AssignmentFileUrl(row.Id));

    private StudentAssignmentDto StudentDto(Assignment assignment, AssignmentSubmission? row) => new(
        assignment.Id, assignment.Title, assignment.Instructions, assignment.DueAt, assignment.MaxMarks, IsSubmitted(row), row?.Late ?? false,
        row?.Body, row?.Mark, row?.Feedback, row?.FileName, row?.FileKey is null ? null : signer.AssignmentFileUrl(row.Id));

    private static bool IsSubmitted(AssignmentSubmission? row) =>
        row is not null && (row.Status is AssignmentSubmissionStatus.Received or AssignmentSubmissionStatus.Returned || row.SubmittedAt is not null);

    private static string? CleanFeedback(string feedback)
    {
        var text = feedback.Trim();
        if (text.Length == 0) return null;
        if (text.Length > MaxFeedbackLength) throw BadRequestException.Field("feedback", $"Use {MaxFeedbackLength} characters or fewer for the feedback.");
        return text;
    }

    private static long LockKey(Guid assignmentId) => BitConverter.ToInt64(assignmentId.ToByteArray(), 8) ^ 0xA5516001;

    private sealed record StoredFile(byte[] Bytes, string ContentType, string Extension, string Name);

    private async Task<StoredFile> ReadFileAsync(Upload upload, CancellationToken ct)
    {
        if (upload.Length > ImageUploads.MaxBytes) throw BadRequestException.Field("file", "Attachments must be 5 MB or smaller.");
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length <= 0) throw BadRequestException.Field("file", "Choose a file to attach.");
        if (buffer.Length > ImageUploads.MaxBytes) throw BadRequestException.Field("file", "Attachments must be 5 MB or smaller.");

        byte[] bytes;
        string contentType, extension;
        if (buffer.Length >= 5 && buffer.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            bytes = buffer.ToArray();
            (contentType, extension) = ("application/pdf", ".pdf");
        }
        else
        {
            buffer.Position = 0;
            try
            {
                bytes = images.ToWebp(buffer, AttachmentMaxSide);
            }
            catch (InvalidDataException)
            {
                throw BadRequestException.Field("file", "Attach a PDF or a JPEG, PNG or WebP image.");
            }
            (contentType, extension) = ("image/webp", ".webp");
        }
        return new StoredFile(bytes, contentType, extension, AttachmentName(upload.FileName, extension));
    }

    private static string AttachmentName(string? fileName, string extension)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();
        var safe = new string(stem.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "attachment";
        if (safe.Length > 100) safe = safe[..100];
        return safe + extension;
    }

    private async Task DeleteQuietlyAsync(string? key, CancellationToken ct)
    {
        if (key is null) return;
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            // The old file is orphaned, not harmful; the submission already points elsewhere.
        }
    }
}
