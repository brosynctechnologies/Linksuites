using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SaveBankQuestionRequest(Guid SubjectId, Guid? LevelId, string? Stem, IReadOnlyList<string>? Options, int CorrectIndex, decimal Marks);

public sealed record BankQuestionDto(
    Guid Id, Guid SubjectId, Guid? LevelId, string Stem, IReadOnlyList<string> Options, int CorrectIndex, decimal Marks, bool Archived, Guid CreatedBy);

public sealed record CreateCbtPaperRequest(
    Guid ClassSubjectId, Guid TermId, string? Title, int DurationMinutes, Guid? ExamSittingId,
    DateTimeOffset? OpenAt, DateTimeOffset? CloseAt, Guid? AssessmentComponentId, int DrawCount, IReadOnlyList<Guid>? PickQuestionIds);

public sealed record CbtPaperDto(
    Guid Id, Guid ClassSubjectId, Guid TermId, string Title, int DurationMinutes, Guid? ExamSittingId,
    DateTimeOffset OpenAt, DateTimeOffset CloseAt, Guid? AssessmentComponentId, int DrawCount,
    IReadOnlyList<Guid> PickQuestionIds, DateTimeOffset? PublishedAt, Guid CreatedBy, DateTimeOffset? ReleasedKeyAt);

public enum CbtAttemptStatus { NotStarted, InProgress, Submitted }

/// <summary>A paper on the student's list, with the student's own current attempt. No key and no questions.</summary>
public sealed record StudentCbtPaperDto(
    Guid Id, string Title, string Subject, string SubjectCode, int DurationMinutes, DateTimeOffset OpenAt, DateTimeOffset CloseAt,
    bool Counted, CbtAttemptStatus Status, Guid? AttemptId, DateTimeOffset? DeadlineAt, DateTimeOffset? SubmittedAt, decimal? TotalMarks);

public sealed record CbtStaffItemDto(int Order, string Stem, decimal Marks, decimal? MarksAwarded);
public sealed record CbtStaffAttemptDto(
    Guid AttemptId, DateTimeOffset StartedAt, DateTimeOffset DeadlineAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ReopenedAt,
    decimal TotalMarks, decimal PaperMarks, IReadOnlyList<CbtStaffItemDto> Items);
public sealed record CbtRosterRowDto(Guid EnrolmentId, string Name, string AdmissionNo, IReadOnlyList<CbtStaffAttemptDto> Attempts);
/// <summary>Every student on the class roster, with their attempts newest first. Marks awarded are null until an attempt is handed in.</summary>
public sealed record CbtAttemptsDto(Guid PaperId, string Title, DateTimeOffset? ReleasedKeyAt, IReadOnlyList<CbtRosterRowDto> Students);

public sealed record CbtStartItemDto(Guid Id, string Stem, IReadOnlyList<string> Options);
public sealed record CbtStartDto(Guid AttemptId, DateTimeOffset DeadlineAt, IReadOnlyList<CbtStartItemDto> Items);
public sealed record SaveCbtAnswerRequest(Guid ItemId, int ChosenIndex);
public sealed record CbtOpenItemDto(Guid Id, string Stem, IReadOnlyList<string> Options, int? ChosenIndex);
public sealed record CbtAttemptOpenDto(Guid AttemptId, DateTimeOffset DeadlineAt, IReadOnlyList<CbtOpenItemDto> Items);
public sealed record CbtAttemptTotalDto(Guid AttemptId, DateTimeOffset DeadlineAt, DateTimeOffset? SubmittedAt, decimal TotalMarks, bool AutoMarked);
public sealed record CbtMarkedItemDto(Guid Id, string Stem, IReadOnlyList<string> Options, int? ChosenIndex, bool Correct, decimal MarksAwarded);
public sealed record CbtAttemptMarkedDto(
    Guid AttemptId, DateTimeOffset DeadlineAt, DateTimeOffset SubmittedAt, decimal TotalMarks, bool AutoMarked, IReadOnlyList<CbtMarkedItemDto> Items);
public sealed record CbtReleaseDto(Guid Id, DateTimeOffset ReleasedKeyAt);
public sealed record CbtReopenDto(Guid Id, DateTimeOffset ReopenedAt);

/// <summary>
/// The question bank and computer-based papers. A subject teacher, or leadership, writes questions for a subject and papers
/// for a class subject they teach. A paper picks questions and draws more from the bank when a student starts. The student
/// sits one attempt, which is shuffled and marked from the snapshot taken at the start. The bursar is refused before a
/// subject, a paper, or an attempt is looked up.
/// </summary>
public sealed class CbtService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, AcademicService academic,
    StudentAccess students, ScoreService scores, IDbErrors dbErrors, TimeProvider clock)
{
    public const int MaxTitleLength = 150;
    public const int MaxStemLength = 2000;
    public const int MaxOptionLength = 500;
    public const int MaxOptions = 8;
    public const int MaxDurationMinutes = 480;
    public const decimal MaxMarks = 1000;
    public const string NotEnough = "There are not enough questions in the bank for this test.";
    public const string WindowRequired = "Choose when the test opens and when it closes.";
    public const string Developmental = "This class uses developmental assessment.";
    public const string TimeUp = "Time is up. Your answers were saved.";
    public const string AlreadyStarted = "You have already started this test.";
    public const string HandedIn = "This test has already been handed in.";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();
    private Guid UserId => user.UserId ?? throw new ForbiddenException();

    public async Task<IReadOnlyList<BankQuestionDto>> ListQuestionsAsync(Guid? subjectId, Guid? levelId, CancellationToken ct)
    {
        RefuseBursar();
        if (subjectId is not { } id || id == Guid.Empty) throw BadRequestException.Field("subjectId", "Choose a subject.");
        await OpenSubjectAsync(id, ct);
        var query = db.BankQuestions.AsNoTracking().Where(q => q.SubjectId == id);
        if (levelId is { } level) query = query.Where(q => q.LevelId == level);
        var rows = await query.OrderBy(q => q.CreatedAt).ThenBy(q => q.Stem).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<BankQuestionDto> CreateQuestionAsync(SaveBankQuestionRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var clean = CheckQuestion(r.Stem, r.Options, r.CorrectIndex, r.Marks);
        await OpenSubjectAsync(r.SubjectId, ct);
        if (r.LevelId is { } levelId && !await db.Levels.AnyAsync(l => l.Id == levelId, ct))
            throw BadRequestException.Field("levelId", "Choose a level of this school.");

        var row = new BankQuestion
        {
            TenantId = TenantId, SubjectId = r.SubjectId, LevelId = r.LevelId, Stem = clean.Stem, Options = clean.Options,
            CorrectIndex = clean.CorrectIndex, Marks = clean.Marks, Archived = false, CreatedBy = UserId,
        };
        db.BankQuestions.Add(row);
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <summary>
    /// Archives or restores a bank question. An archived question is never drawn and is skipped in new attempts of papers that
    /// picked it; attempts already started keep their snapshot. Same access as writing questions for the subject; a question of
    /// another school, or of a subject the teacher does not teach, is not found.
    /// </summary>
    public async Task<BankQuestionDto> SetArchivedAsync(Guid id, bool archived, CancellationToken ct)
    {
        RefuseBursar();
        var row = await db.BankQuestions.FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("This question");
        if (!access.IsLeadership && !await TeachesSubjectAsync(row.SubjectId, ct)) throw new NotFoundException("This question");
        if (row.Archived != archived)
        {
            row.Archived = archived;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(row);
    }

    public async Task<CbtPaperDto> CreatePaperAsync(CreateCbtPaperRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var title = Required(r.Title, "title", "Enter a title.", MaxTitleLength);
        if (r.DurationMinutes is < 1 or > MaxDurationMinutes)
            throw BadRequestException.Field("durationMinutes", $"Duration must be from 1 to {MaxDurationMinutes} minutes.");
        if (r.DrawCount < 0) throw BadRequestException.Field("drawCount", "The number to draw can't be negative.");

        var (cs, cls) = await OpenClassSubjectAsync(r.ClassSubjectId, ct);
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TermId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");
        if (await db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct) == AssessmentMode.Developmental)
            throw new BadRequestException(Developmental);
        if (r.AssessmentComponentId is { } componentId)
        {
            var components = await academic.EnsureSchemeAsync(cls.LevelId, ct);
            if (components.All(c => c.Id != componentId))
                throw BadRequestException.Field("assessmentComponentId", "Choose a component of this class's assessment.");
        }

        var window = await WindowAsync(r, cs.Id, term.Id, ct);
        var picks = await PicksAsync(r, cs.SubjectId, cls.LevelId, ct);
        var paper = new CbtPaper
        {
            TenantId = TenantId, ClassSubjectId = cs.Id, TermId = term.Id, Title = title, DurationMinutes = r.DurationMinutes,
            ExamSittingId = window.SittingId, OpenAt = window.Open, CloseAt = window.Close,
            AssessmentComponentId = r.AssessmentComponentId, DrawCount = r.DrawCount, CreatedBy = UserId,
        };
        db.CbtPapers.Add(paper);
        for (var i = 0; i < picks.Count; i++)
            db.CbtPaperPicks.Add(new CbtPaperPick { TenantId = TenantId, PaperId = paper.Id, BankQuestionId = picks[i], Order = i });
        await db.SaveChangesAsync(ct);
        return ToDto(paper, picks);
    }

    /// <summary>Papers for a class subject and term, newest window first. Same access as creating one.</summary>
    public async Task<IReadOnlyList<CbtPaperDto>> ListPapersAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        RefuseBursar();
        await OpenClassSubjectAsync(classSubjectId, ct);
        var papers = await db.CbtPapers.AsNoTracking().Where(p => p.ClassSubjectId == classSubjectId && p.TermId == termId)
            .OrderByDescending(p => p.OpenAt).ThenByDescending(p => p.CreatedAt).ToListAsync(ct);
        var ids = papers.Select(p => p.Id).ToList();
        var picks = (await db.CbtPaperPicks.AsNoTracking().Where(p => ids.Contains(p.PaperId)).OrderBy(p => p.Order).ThenBy(p => p.Id)
                .Select(p => new { p.PaperId, p.BankQuestionId }).ToListAsync(ct))
            .ToLookup(p => p.PaperId, p => p.BankQuestionId);
        return papers.Select(p => ToDto(p, picks[p.Id].ToList())).ToList();
    }

    /// <summary>
    /// The class roster with each student's attempts at a paper and the marks awarded per question. Same access as release and
    /// reopen: the subject teacher of the class subject or leadership; another teacher gets 404.
    /// </summary>
    public async Task<CbtAttemptsDto> AttemptsAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var paper = await db.CbtPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("This test");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(paper.ClassSubjectId, ct))
            throw new NotFoundException("This test");
        var classId = await db.ClassSubjects.AsNoTracking().Where(c => c.Id == paper.ClassSubjectId).Select(c => c.ClassId).FirstAsync(ct);

        var now = clock.GetUtcNow();
        var timedOut = await db.CbtAttempts.AsNoTracking()
            .Where(a => a.PaperId == paper.Id && a.SubmittedAt == null && a.ReopenedAt == null && a.DeadlineAt < now).Select(a => a.Id).ToListAsync(ct);
        if (timedOut.Count > 0) await FinalizeTimedOutAsync(timedOut, ct);

        var attempts = await db.CbtAttempts.AsNoTracking().Where(a => a.PaperId == paper.Id)
            .OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.Id).ToListAsync(ct);
        var attemptIds = attempts.Select(a => a.Id).ToList();
        var items = (await db.CbtAttemptItems.AsNoTracking().Where(i => attemptIds.Contains(i.AttemptId))
                .OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync(ct))
            .ToLookup(i => i.AttemptId);
        var withAttempts = attempts.Select(a => a.EnrolmentId).Distinct().ToList();
        var roster = await db.Enrolments.AsNoTracking()
            .Where(e => withAttempts.Contains(e.Id)
                || (e.ClassId == classId && db.Students.Any(s => s.Id == e.StudentId && s.Status == StudentStatus.Active)))
            .Join(db.Students.AsNoTracking(), e => e.StudentId, s => s.Id, (e, s) => new { EnrolmentId = e.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        var byEnrolment = attempts.ToLookup(a => a.EnrolmentId);

        var rows = roster.Select(r => new CbtRosterRowDto(r.EnrolmentId, $"{r.FirstName} {r.LastName}", r.AdmissionNo,
            byEnrolment[r.EnrolmentId].Select(a =>
            {
                var shown = items[a.Id].ToList();
                return new CbtStaffAttemptDto(a.Id, a.StartedAt, a.DeadlineAt, a.SubmittedAt, a.ReopenedAt, a.TotalMarks, shown.Sum(i => i.Marks),
                    shown.Select(i => new CbtStaffItemDto(i.Order + 1, i.Stem, i.Marks, i.MarksAwarded)).ToList());
            }).ToList())).ToList();
        return new CbtAttemptsDto(paper.Id, paper.Title, paper.ReleasedKeyAt, rows);
    }

    /// <summary>
    /// Published papers of the student's current class this term (open, upcoming and closed), with the student's current attempt.
    /// A reopened attempt no longer counts, so the paper shows as not started. No key and no questions are sent.
    /// </summary>
    public async Task<IReadOnlyList<StudentCbtPaperDto>> StudentPapersAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var enrolment = await CurrentEnrolmentAsync(me.StudentId, ct);
        if (enrolment is null) return [];
        var papers = await (from p in db.CbtPapers.AsNoTracking()
                            join cs in db.ClassSubjects.AsNoTracking() on p.ClassSubjectId equals cs.Id
                            join s in db.Subjects.AsNoTracking() on cs.SubjectId equals s.Id
                            where cs.ClassId == enrolment.ClassId && p.PublishedAt != null && db.Terms.Any(t => t.Id == p.TermId && t.IsCurrent)
                            orderby p.OpenAt, p.Title
                            select new { Paper = p, s.Name, s.Code }).ToListAsync(ct);
        var ids = papers.Select(p => p.Paper.Id).ToList();
        var now = clock.GetUtcNow();
        var timedOut = await db.CbtAttempts.AsNoTracking()
            .Where(a => ids.Contains(a.PaperId) && a.EnrolmentId == enrolment.Id && a.SubmittedAt == null && a.ReopenedAt == null && a.DeadlineAt < now)
            .Select(a => a.Id).ToListAsync(ct);
        if (timedOut.Count > 0) await FinalizeTimedOutAsync(timedOut, ct);
        var attempts = (await db.CbtAttempts.AsNoTracking()
                .Where(a => ids.Contains(a.PaperId) && a.EnrolmentId == enrolment.Id && a.ReopenedAt == null).ToListAsync(ct))
            .GroupBy(a => a.PaperId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.StartedAt).First());
        return papers.Select(x =>
        {
            var p = x.Paper;
            var a = attempts.GetValueOrDefault(p.Id);
            var status = a is null ? CbtAttemptStatus.NotStarted : a.SubmittedAt is null ? CbtAttemptStatus.InProgress : CbtAttemptStatus.Submitted;
            return new StudentCbtPaperDto(p.Id, p.Title, x.Name, x.Code, p.DurationMinutes, p.OpenAt, p.CloseAt, p.AssessmentComponentId is not null,
                status, a?.Id, a?.DeadlineAt, a?.SubmittedAt, a?.SubmittedAt is null ? null : a.TotalMarks);
        }).ToList();
    }

    public async Task<CbtPaperDto> PublishAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var paper = await db.CbtPapers.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("This test");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(paper.ClassSubjectId, ct))
            throw new NotFoundException("This test");
        paper.PublishedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        paper.PublishedAt = await db.CbtPapers.AsNoTracking().Where(p => p.Id == paper.Id).Select(p => p.PublishedAt).FirstAsync(ct);
        var picks = await db.CbtPaperPicks.AsNoTracking().Where(p => p.PaperId == paper.Id).OrderBy(p => p.Order).ThenBy(p => p.Id)
            .Select(p => p.BankQuestionId).ToListAsync(ct);
        return ToDto(paper, picks);
    }

    public async Task<CbtReleaseDto> ReleaseAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var paper = await db.CbtPapers.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("This test");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(paper.ClassSubjectId, ct))
            throw new NotFoundException("This test");
        var now = clock.GetUtcNow();
        paper.ReleasedKeyAt ??= now;
        var open = await db.CbtAttempts.Where(a => a.PaperId == paper.Id && a.ReleasedKeyAt == null).ToListAsync(ct);
        foreach (var attempt in open) attempt.ReleasedKeyAt = paper.ReleasedKeyAt;
        await db.SaveChangesAsync(ct);
        return new CbtReleaseDto(paper.Id, paper.ReleasedKeyAt.Value);
    }

    public async Task<CbtReopenDto> ReopenAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var attempt = await db.CbtAttempts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("This test");
        var paper = await db.CbtPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == attempt.PaperId, ct) ?? throw new NotFoundException("This test");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(paper.ClassSubjectId, ct))
            throw new NotFoundException("This test");
        attempt.ReopenedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new CbtReopenDto(attempt.Id, attempt.ReopenedAt.Value);
    }

    public async Task<CbtStartDto> StartAsync(Guid id, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var enrolment = await CurrentEnrolmentAsync(me.StudentId, ct);
        var paper = await db.CbtPapers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (paper is null || paper.PublishedAt is null || enrolment is null) throw new NotFoundException("This test");
        var link = await db.ClassSubjects.AsNoTracking().Where(c => c.Id == paper.ClassSubjectId)
            .Select(c => new { c.ClassId, c.SubjectId }).FirstAsync(ct);
        if (link.ClassId != enrolment.ClassId) throw new NotFoundException("This test");
        var now = clock.GetUtcNow();
        if (now < paper.OpenAt || now >= paper.CloseAt) throw new NotFoundException("This test");
        if (paper.AssessmentComponentId is not null && await scores.IsLockedAsync(link.ClassId, paper.TermId, ct))
            throw new ConflictException(ScoreService.TermLockedMessage, "term_locked");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.CbtAttempts.AnyAsync(a => a.PaperId == paper.Id && a.EnrolmentId == enrolment.Id && a.ReopenedAt == null, ct))
            throw new ConflictException(AlreadyStarted);

        var levelId = await db.SchoolClasses.AsNoTracking().Where(c => c.Id == link.ClassId).Select(c => c.LevelId).FirstAsync(ct);
        var drawn = await DrawAsync(paper, link.SubjectId, levelId, ct);
        var attempt = new CbtAttempt
        {
            TenantId = TenantId, PaperId = paper.Id, EnrolmentId = enrolment.Id, StartedAt = now,
            DeadlineAt = Min(now.AddMinutes(paper.DurationMinutes), paper.CloseAt), ReleasedKeyAt = paper.ReleasedKeyAt,
        };
        db.CbtAttempts.Add(attempt);
        var items = new List<CbtAttemptItem>(drawn.Count);
        for (var i = 0; i < drawn.Count; i++)
        {
            var (shown, order, correctShown) = ShuffleOptions(drawn[i].Options, drawn[i].CorrectIndex);
            var item = new CbtAttemptItem
            {
                TenantId = TenantId, AttemptId = attempt.Id, BankQuestionId = drawn[i].Id, Order = i, Stem = drawn[i].Stem,
                Options = shown, OptionOrder = order, CorrectShownIndex = correctShown, Marks = drawn[i].Marks,
            };
            items.Add(item);
            db.CbtAttemptItems.Add(item);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException(AlreadyStarted);
        }
        await tx.CommitAsync(ct);
        return new CbtStartDto(attempt.Id, attempt.DeadlineAt, items.Select(i => new CbtStartItemDto(i.Id, i.Stem, i.Options)).ToList());
    }

    public async Task<object> GetAttemptAsync(Guid id, CancellationToken ct)
    {
        var attempt = await OwnAttemptAsync(id, ct);
        if (IsTimedOut(attempt))
        {
            await FinalizeTimedOutAsync([attempt.Id], ct);
            attempt = await db.CbtAttempts.AsNoTracking().FirstAsync(a => a.Id == attempt.Id, ct);
        }
        return await ViewAsync(attempt, ct);
    }

    private bool IsTimedOut(CbtAttempt a) => a.SubmittedAt is null && a.ReopenedAt is null && clock.GetUtcNow() > a.DeadlineAt;

    /// <summary>
    /// Marks attempts whose time ran out without being handed in, exactly as a hand-in would, once: each is re-read under its
    /// own lock and skipped when it was handed in or reopened meanwhile. A counted paper writes the score, unless the class's
    /// term is locked: then the attempt is marked and the score is left alone.
    /// </summary>
    private async Task FinalizeTimedOutAsync(IReadOnlyList<Guid> attemptIds, CancellationToken ct)
    {
        foreach (var attemptId in attemptIds)
        {
            // Read paths only: nothing is pending, and the attempt must be read fresh under the lock, not from the tracker.
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.LockAsync(AttemptLockKey(attemptId), ct);
            var attempt = await db.CbtAttempts.FirstAsync(a => a.Id == attemptId, ct);
            if (!IsTimedOut(attempt)) continue;
            var paper = await db.CbtPapers.AsNoTracking().FirstAsync(p => p.Id == attempt.PaperId, ct);
            var items = await MarkAsync(attempt, attempt.DeadlineAt, ct);
            if (paper.AssessmentComponentId is not null)
            {
                try
                {
                    await WriteScoreAsync(paper, attempt.EnrolmentId, attempt.TotalMarks, items.Sum(i => i.Marks), ct);
                }
                catch (ConflictException ex) when (ex.Code == "term_locked")
                {
                    // The term is locked: keep the mark and leave the score sheet as the principal locked it.
                }
                catch (BadRequestException)
                {
                    // The student has left the class (or the class changed mode) since starting: keep the mark, there is no score cell to write.
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
    }

    /// <summary>Marks every item from the snapshot and stamps the attempt as handed in. Saved by the caller.</summary>
    private async Task<List<CbtAttemptItem>> MarkAsync(CbtAttempt attempt, DateTimeOffset submittedAt, CancellationToken ct)
    {
        var items = await db.CbtAttemptItems.Where(i => i.AttemptId == attempt.Id).ToListAsync(ct);
        foreach (var item in items)
            item.MarksAwarded = item.ChosenIndex == item.CorrectShownIndex ? item.Marks : 0m;
        attempt.TotalMarks = items.Sum(i => i.MarksAwarded ?? 0m);
        attempt.AutoMarked = true;
        attempt.SubmittedAt = submittedAt;
        return items;
    }

    private static long AttemptLockKey(Guid attemptId) => BitConverter.ToInt64(attemptId.ToByteArray(), 8) ^ 0x0CB7A77E;

    public async Task<CbtAttemptOpenDto> AnswerAsync(Guid id, SaveCbtAnswerRequest r, CancellationToken ct)
    {
        var attempt = await OwnAttemptAsync(id, ct);
        if (attempt.SubmittedAt is not null) throw new ConflictException(HandedIn);
        if (attempt.ReopenedAt is not null) throw new ConflictException("This sitting was reopened. Start the test again.");
        var item = await db.CbtAttemptItems.FirstOrDefaultAsync(i => i.Id == r.ItemId && i.AttemptId == attempt.Id, ct)
            ?? throw new NotFoundException("This test");
        if (r.ChosenIndex < 0 || r.ChosenIndex >= item.Options.Count)
            throw BadRequestException.Field("chosenIndex", "Choose one of the options.");
        if (clock.GetUtcNow() > attempt.DeadlineAt) throw new BadRequestException(TimeUp);
        item.ChosenIndex = r.ChosenIndex;
        await db.SaveChangesAsync(ct);
        return OpenDto(attempt, await ItemsAsync(attempt.Id, ct));
    }

    public async Task<object> SubmitAsync(Guid id, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var enrolment = await CurrentEnrolmentAsync(me.StudentId, ct) ?? throw new NotFoundException("This test");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The same lock as marking a timed-out attempt, so an attempt is marked once.
        await db.LockAsync(AttemptLockKey(id), ct);
        var attempt = await db.CbtAttempts.FirstOrDefaultAsync(a => a.Id == id && a.EnrolmentId == enrolment.Id, ct)
            ?? throw new NotFoundException("This test");
        if (attempt.SubmittedAt is not null) throw new ConflictException(HandedIn);
        if (attempt.ReopenedAt is not null) throw new ConflictException("This sitting was reopened. Start the test again.");
        var paper = await db.CbtPapers.FirstAsync(p => p.Id == attempt.PaperId, ct);
        var items = await MarkAsync(attempt, clock.GetUtcNow(), ct);
        if (paper.AssessmentComponentId is not null)
            await WriteScoreAsync(paper, enrolment.Id, attempt.TotalMarks, items.Sum(i => i.Marks), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ViewAsync(attempt, ct);
    }

    private async Task WriteScoreAsync(CbtPaper paper, Guid enrolmentId, decimal awarded, decimal paperTotal, CancellationToken ct)
    {
        if (paper.AssessmentComponentId is not Guid componentId || paperTotal <= 0) return;
        var classId = await db.ClassSubjects.AsNoTracking().Where(c => c.Id == paper.ClassSubjectId).Select(c => c.ClassId).FirstAsync(ct);
        var levelId = await db.SchoolClasses.AsNoTracking().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(ct);
        var component = (await academic.EnsureSchemeAsync(levelId, ct)).FirstOrDefault(c => c.Id == componentId)
            ?? throw BadRequestException.Field("assessmentComponentId", "Choose a component of this class's assessment.");
        var value = paperTotal == component.MaxScore
            ? decimal.Round(awarded, 1, MidpointRounding.AwayFromZero)
            : decimal.Round(component.MaxScore * awarded / paperTotal, 1, MidpointRounding.AwayFromZero);
        await scores.UpsertAsync(enrolmentId, paper.ClassSubjectId, paper.TermId, componentId, value, ct);
    }

    private async Task<List<BankQuestion>> DrawAsync(CbtPaper paper, Guid subjectId, Guid levelId, CancellationToken ct)
    {
        var pickIds = await db.CbtPaperPicks.AsNoTracking().Where(p => p.PaperId == paper.Id).OrderBy(p => p.Order).ThenBy(p => p.Id)
            .Select(p => p.BankQuestionId).ToListAsync(ct);
        var pickSet = pickIds.ToHashSet();
        var pool = await db.BankQuestions.AsNoTracking()
            .Where(q => q.SubjectId == subjectId && !q.Archived && (q.LevelId == null || q.LevelId == levelId) && !pickSet.Contains(q.Id))
            .ToListAsync(ct);
        if (paper.DrawCount > pool.Count) throw new BadRequestException(NotEnough);
        Shuffle(pool);
        // An archived pick is left out of new attempts.
        var picks = pickIds.Count == 0
            ? []
            : await db.BankQuestions.AsNoTracking().Where(q => pickIds.Contains(q.Id) && !q.Archived).ToListAsync(ct);
        var byId = picks.ToDictionary(q => q.Id);
        var chosen = new List<BankQuestion>(pickIds.Count + paper.DrawCount);
        foreach (var id in pickIds)
            if (byId.TryGetValue(id, out var pick)) chosen.Add(pick);
        chosen.AddRange(pool.Take(paper.DrawCount));
        if (chosen.Count == 0) throw new BadRequestException(NotEnough);
        Shuffle(chosen);
        return chosen;
    }

    private async Task<CbtAttempt> OwnAttemptAsync(Guid id, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var enrolment = await CurrentEnrolmentAsync(me.StudentId, ct);
        var attempt = await db.CbtAttempts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attempt is null || enrolment is null || attempt.EnrolmentId != enrolment.Id) throw new NotFoundException("This test");
        return attempt;
    }

    private sealed record SittingEnrolment(Guid Id, Guid ClassId);

    private async Task<SittingEnrolment?> CurrentEnrolmentAsync(Guid studentId, CancellationToken ct)
    {
        var row = await db.Enrolments.AsNoTracking()
            .Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Select(e => new { e.Id, e.ClassId }).FirstOrDefaultAsync(ct);
        return row is null ? null : new SittingEnrolment(row.Id, row.ClassId);
    }

    private async Task<object> ViewAsync(CbtAttempt attempt, CancellationToken ct)
    {
        if (attempt.SubmittedAt is null)
            return OpenDto(attempt, await ItemsAsync(attempt.Id, ct));
        var paper = await db.CbtPapers.AsNoTracking().FirstAsync(p => p.Id == attempt.PaperId, ct);
        var released = attempt.ReleasedKeyAt is not null || paper.ReleasedKeyAt is not null || clock.GetUtcNow() >= paper.CloseAt;
        if (!released)
            return new CbtAttemptTotalDto(attempt.Id, attempt.DeadlineAt, attempt.SubmittedAt, attempt.TotalMarks, attempt.AutoMarked);
        var items = await ItemsAsync(attempt.Id, ct);
        return new CbtAttemptMarkedDto(attempt.Id, attempt.DeadlineAt, attempt.SubmittedAt.Value, attempt.TotalMarks, attempt.AutoMarked,
            items.Select(i => new CbtMarkedItemDto(i.Id, i.Stem, i.Options, i.ChosenIndex, i.ChosenIndex == i.CorrectShownIndex, i.MarksAwarded ?? 0)).ToList());
    }

    /// <summary>The key is revealed at the close, so no sitting may run past it.</summary>
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    private static CbtAttemptOpenDto OpenDto(CbtAttempt attempt, IReadOnlyList<CbtAttemptItem> items) => new(
        attempt.Id, attempt.DeadlineAt, items.Select(i => new CbtOpenItemDto(i.Id, i.Stem, i.Options, i.ChosenIndex)).ToList());

    private Task<List<CbtAttemptItem>> ItemsAsync(Guid attemptId, CancellationToken ct) =>
        db.CbtAttemptItems.AsNoTracking().Where(i => i.AttemptId == attemptId).OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync(ct);

    /// <summary>Fisher–Yates. The same paper is not shown in the same order to every student.</summary>
    private static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static (List<string> Shown, List<int> Order, int CorrectShown) ShuffleOptions(IReadOnlyList<string> options, int correctIndex)
    {
        var order = Enumerable.Range(0, options.Count).ToList();
        Shuffle(order);
        var shown = new List<string>(order.Count);
        foreach (var index in order) shown.Add(options[index]);
        return (shown, order, order.IndexOf(correctIndex));
    }

    private async Task<(Guid? SittingId, DateTimeOffset Open, DateTimeOffset Close)> WindowAsync(
        CreateCbtPaperRequest r, Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        if (r.ExamSittingId is { } sittingId)
        {
            var sitting = await db.ExamSittings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sittingId, ct)
                ?? throw new NotFoundException("This timetable");
            if (sitting.ClassSubjectId != classSubjectId)
                throw BadRequestException.Field("examSittingId", "Choose a sitting for this class and subject.");
            var periodTerm = await db.ExamPeriods.AsNoTracking().Where(p => p.Id == sitting.PeriodId).Select(p => p.TermId).FirstAsync(ct);
            if (periodTerm != termId) throw BadRequestException.Field("examSittingId", "Choose a sitting in this term.");
            return (sitting.Id, At(sitting.Date, sitting.Start), At(sitting.Date, sitting.End));
        }

        if (r.OpenAt is not { } open || r.CloseAt is not { } close || open == default || close == default)
            throw BadRequestException.Field("openAt", WindowRequired);
        if (close <= open) throw BadRequestException.Field("closeAt", "The test must close after it opens.");
        return (null, open.ToUniversalTime(), close.ToUniversalTime());
    }

    /// <summary>A sitting's clock time is Lagos local. Postgres stores the instant in UTC.</summary>
    private static DateTimeOffset At(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), SchoolCalendar.LagosOffset).ToUniversalTime();

    /// <summary>
    /// Picks must already be in the pool. The pool is unarchived questions for the subject whose level is unset or this class's level.
    /// <see cref="CreateCbtPaperRequest.DrawCount"/> is how many more, so it cannot exceed the pool minus the picks.
    /// </summary>
    private async Task<List<Guid>> PicksAsync(CreateCbtPaperRequest r, Guid subjectId, Guid levelId, CancellationToken ct)
    {
        var picks = (r.PickQuestionIds ?? []).ToList();
        if (picks.Count != picks.Distinct().Count())
            throw BadRequestException.Field("pickQuestionIds", "Each question can be chosen only once.");
        if (r.DrawCount == 0 && picks.Count == 0)
            throw BadRequestException.Field("drawCount", "Choose questions or a number to draw from the bank.");

        var eligible = await db.BankQuestions.AsNoTracking()
            .Where(q => q.SubjectId == subjectId && !q.Archived && (q.LevelId == null || q.LevelId == levelId))
            .Select(q => q.Id).ToListAsync(ct);
        var pool = eligible.ToHashSet();
        if (picks.Any(id => !pool.Contains(id)))
            throw BadRequestException.Field("pickQuestionIds", "Choose questions from this subject's bank for this class.");
        if (r.DrawCount > pool.Count - picks.Count) throw new BadRequestException(NotEnough);
        return picks;
    }

    private async Task OpenSubjectAsync(Guid subjectId, CancellationToken ct)
    {
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct) || (!access.IsLeadership && !await TeachesSubjectAsync(subjectId, ct)))
            throw new NotFoundException("This subject");
    }

    /// <summary>The subjects the caller may write questions for: every subject for leadership, otherwise those the teacher teaches in some class.</summary>
    internal async Task<IReadOnlySet<Guid>> WritableSubjectIdsAsync(CancellationToken ct)
    {
        if (access.IsLeadership) return (await db.Subjects.AsNoTracking().Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        if (await access.StaffIdAsync(ct) is not { } staffId) return new HashSet<Guid>();
        return (await db.ClassSubjects.AsNoTracking().Where(cs => cs.TeacherStaffId == staffId).Select(cs => cs.SubjectId).Distinct().ToListAsync(ct)).ToHashSet();
    }

    /// <summary>A bank question's text, options, correct option and marks, checked and trimmed.</summary>
    internal sealed record CleanQuestion(string Stem, List<string> Options, int CorrectIndex, decimal Marks);

    public const string MarksRule = "The marks must be more than 0 and no more than 1000, with at most one decimal place.";

    /// <summary>
    /// The rules for a bank question's own fields, shared by the question screen and the question import. Throws a field
    /// <see cref="BadRequestException"/> with the screen's sentence. Subject and level are checked by the caller.
    /// </summary>
    internal static CleanQuestion CheckQuestion(string? stem, IReadOnlyList<string>? options, int correctIndex, decimal marks)
    {
        var text = Required(stem, "stem", "Write the question.", MaxStemLength);
        var clean = ReadOptions(options);
        if (correctIndex < 0 || correctIndex >= clean.Count) throw BadRequestException.Field("correctIndex", "Choose the correct option.");
        if (marks <= 0 || marks > MaxMarks || decimal.Round(marks, 1) != marks) throw BadRequestException.Field("marks", MarksRule);
        return new CleanQuestion(text, clean, correctIndex, marks);
    }

    private async Task<bool> TeachesSubjectAsync(Guid subjectId, CancellationToken ct)
    {
        if (await access.StaffIdAsync(ct) is not { } staffId) return false;
        return await db.ClassSubjects.AnyAsync(cs => cs.SubjectId == subjectId && cs.TeacherStaffId == staffId, ct);
    }

    private async Task<(ClassSubject Cs, SchoolClass Cls)> OpenClassSubjectAsync(Guid classSubjectId, CancellationToken ct)
    {
        var cs = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classSubjectId, ct);
        if (cs is null || (!access.IsLeadership && !await access.TeachesClassSubjectAsync(classSubjectId, ct)))
            throw new NotFoundException("Class subject");
        var cls = await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == cs.ClassId, ct);
        return (cs, cls);
    }

    internal void RefuseBursar()
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not set tests.");
    }

    private static List<string> ReadOptions(IReadOnlyList<string>? options)
    {
        var clean = (options ?? []).Select(o => (o ?? "").Trim()).ToList();
        if (clean.Count < 2) throw BadRequestException.Field("options", "Add at least two options.");
        if (clean.Count > MaxOptions) throw BadRequestException.Field("options", $"Use {MaxOptions} options or fewer.");
        if (clean.Any(o => o.Length == 0)) throw BadRequestException.Field("options", "Each option needs text.");
        if (clean.Any(o => o.Length > MaxOptionLength)) throw BadRequestException.Field("options", $"Use {MaxOptionLength} characters or fewer for an option.");
        if (clean.Distinct().Count() != clean.Count) throw BadRequestException.Field("options", "Each option must be different.");
        return clean;
    }

    private static string Required(string? value, string field, string empty, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw BadRequestException.Field(field, empty);
        if (text.Length > max) throw BadRequestException.Field(field, $"Use {max} characters or fewer.");
        return text;
    }

    private static BankQuestionDto ToDto(BankQuestion row) => new(
        row.Id, row.SubjectId, row.LevelId, row.Stem, row.Options, row.CorrectIndex, row.Marks, row.Archived, row.CreatedBy);

    private static CbtPaperDto ToDto(CbtPaper paper, IReadOnlyList<Guid> picks) => new(
        paper.Id, paper.ClassSubjectId, paper.TermId, paper.Title, paper.DurationMinutes, paper.ExamSittingId,
        paper.OpenAt, paper.CloseAt, paper.AssessmentComponentId, paper.DrawCount, picks, paper.PublishedAt, paper.CreatedBy, paper.ReleasedKeyAt);
}
