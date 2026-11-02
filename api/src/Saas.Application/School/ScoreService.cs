using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record ScoreComponentDto(Guid Id, string Name, decimal MaxScore);
/// <summary><c>Values</c> is keyed by component id; a null value is a score not entered yet.</summary>
public sealed record ScoreStudentDto(Guid EnrolmentId, string Name, string AdmissionNo, IReadOnlyDictionary<string, decimal?> Values);
public sealed record ScoreGridDto(
    Guid ClassId, Guid ClassSubjectId, Guid TermId, bool Locked, IReadOnlyList<ScoreComponentDto> Components, IReadOnlyList<ScoreStudentDto> Students);
public sealed record ScoreEntryInput(Guid EnrolmentId, Guid ComponentId, decimal? Value);
public sealed record SaveScoresRequest(Guid ClassSubjectId, Guid TermId, IReadOnlyList<ScoreEntryInput>? Entries);
public sealed record ResultLockDto(Guid ClassId, Guid TermId, bool Locked);

/// <summary>
/// Score entry for one class subject in one term, and locking a class's term. A save is partial (only the cells sent change) and
/// saves for the same class and term wait for each other, so the last write wins. Locked terms refuse every change.
/// </summary>
public sealed class ScoreService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, AcademicService academic, IDbErrors dbErrors, TimeProvider clock)
{
    public const int MaxEntriesPerSave = 2000;
    internal const string TermLockedMessage = "This term is locked for the class, so scores can't change. Ask the principal to unlock it.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Grid ----------

    public async Task<ScoreGridDto> GetAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        var (cs, cls, _) = await LoadAsync(classSubjectId, termId, ct);
        await EnsureCanReadAsync(cs, cls, ct);
        return await GridAsync(cs, cls, termId, ct);
    }

    public async Task<ScoreGridDto> SaveAsync(SaveScoresRequest request, CancellationToken ct)
    {
        var (cs, cls, _) = await LoadAsync(request.ClassSubjectId, request.TermId, ct);
        if (await db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct) == AssessmentMode.Developmental)
            throw new BadRequestException("This class uses developmental assessment.");
        await EnsureCanWriteAsync(cs, ct);
        var entries = request.Entries ?? throw BadRequestException.Field("entries", "Send at least one score to save.");
        if (entries.Count > MaxEntriesPerSave) throw BadRequestException.Field("entries", $"Save at most {MaxEntriesPerSave} scores at a time.");
        // An import's confirm already has a transaction open: join it (it commits), otherwise open our own.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Shared: many saves at once, but an assessment edit waits for them (and they for it), so the maximums checked here still hold at commit.
        await db.LockSharedAsync(AcademicService.AssessmentLockKey(TenantId), ct);

        var components = (await academic.EnsureSchemeAsync(cls.LevelId, ct)).ToDictionary(c => c.Id);
        var roster = await RosterAsync(cls, ct);
        var enrolmentIds = roster.Select(r => r.EnrolmentId).ToHashSet();
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var e in entries)
        {
            if (!components.TryGetValue(e.ComponentId, out var component))
                throw BadRequestException.Field("componentId", "One of the columns is not part of this class's assessment. Reload the sheet and try again.");
            if (!enrolmentIds.Contains(e.EnrolmentId))
                throw BadRequestException.Field("entries", "One of the students is not in this class. Reload the sheet and try again.");
            if (!seen.Add((e.EnrolmentId, e.ComponentId)))
                throw BadRequestException.Field("entries", "Each score can be sent only once in a save.");
            if (e.Value is { } value)
            {
                if (value < 0 || value > component.MaxScore)
                    throw BadRequestException.Field("entries", $"{component.Name} scores must be between 0 and {component.MaxScore:0.#}.");
                if (decimal.Round(value, 1) != value)
                    throw BadRequestException.Field("entries", "Scores can have at most one decimal place.");
            }
        }

        var staffId = await access.StaffIdAsync(ct);
        var tenantId = TenantId;
        // Saves, locks and unlocks for one class and term wait for each other.
        await db.LockAsync(LockKey(cls.Id, request.TermId), ct);
        await EnsureOpenAsync(cls.Id, request.TermId, ct);

        var ids = entries.Select(e => e.EnrolmentId).Distinct().ToList();
        var existing = await db.Scores.Where(s => s.ClassSubjectId == cs.Id && s.TermId == request.TermId && ids.Contains(s.EnrolmentId)).ToListAsync(ct);
        foreach (var e in entries)
        {
            var row = existing.FirstOrDefault(s => s.EnrolmentId == e.EnrolmentId && s.ComponentId == e.ComponentId);
            if (e.Value is not { } value)
            {
                if (row is not null) db.Scores.Remove(row);
            }
            else
                ApplyLoaded(row, e.EnrolmentId, cs.Id, request.TermId, e.ComponentId, value, staffId, tenantId);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("These scores were saved from another device at the same moment. Reload the sheet to see the latest.", "scores_conflict");
        }
        if (tx is not null) await tx.CommitAsync(ct);
        await MarkStaleAsync(cls.Id, request.TermId, ct);
        return await GridAsync(cs, cls, request.TermId, ct);
    }

    // ---------- Lock ----------

    public async Task<ResultLockDto> LockAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var cls = await ClassAndTermAsync(classId, termId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockSharedAsync(AcademicService.AssessmentLockKey(TenantId), ct);
        await db.LockAsync(LockKey(cls.Id, termId), ct);
        if (await db.TermResultLocks.AnyAsync(l => l.ClassId == classId && l.TermId == termId, ct))
            throw new ConflictException("This term is already locked for the class.", "term_locked");

        var now = clock.GetUtcNow();
        var row = new TermResultLock { TenantId = TenantId, ClassId = classId, TermId = termId, LockedAt = now, LockedBy = user.UserId };
        db.TermResultLocks.Add(row);
        db.AuditLogs.Add(Audit("results.lock", row, now));
        await SaveLockChangeAsync(ct);
        await tx.CommitAsync(ct);
        await MarkStaleAsync(classId, termId, ct);
        return new ResultLockDto(classId, termId, true);
    }

    public async Task<ResultLockDto> UnlockAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var cls = await ClassAndTermAsync(classId, termId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockSharedAsync(AcademicService.AssessmentLockKey(TenantId), ct);
        await db.LockAsync(LockKey(cls.Id, termId), ct);
        var row = await db.TermResultLocks.FirstOrDefaultAsync(l => l.ClassId == classId && l.TermId == termId, ct)
            ?? throw new ConflictException("This term is not locked for the class.", "term_not_locked");
        if (await db.TermResults.AnyAsync(r => r.ClassId == classId && r.TermId == termId && r.PublishedAt != null, ct))
            throw new ConflictException("Unpublish the results before unlocking.", "results_published");

        db.TermResultLocks.Remove(row);
        db.AuditLogs.Add(Audit("results.unlock", row, clock.GetUtcNow()));
        await SaveLockChangeAsync(ct);
        await tx.CommitAsync(ct);
        await MarkStaleAsync(classId, termId, ct);
        return new ResultLockDto(classId, termId, false);
    }

    private async Task SaveLockChangeAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("The lock was changed from another device at the same moment. Reload and try again.", "lock_conflict");
        }
    }

    private AuditLog Audit(string action, TermResultLock row, DateTimeOffset now) => new()
    {
        TenantId = TenantId, ActorId = user.UserId, Action = action, EntityType = nameof(TermResultLock), EntityId = row.Id.ToString(),
        AfterJson = JsonSerializer.Serialize(new { classId = row.ClassId, termId = row.TermId }), Ip = user.IpAddress, CreatedAt = now,
    };

    /// <summary>
    /// Writes one score cell: the same row a score-sheet save writes for that enrolment, class subject, term and component.
    /// When the caller already has a transaction open, only the row is written (the caller saves and commits). Otherwise this
    /// takes the class-term lock, refuses a locked term, and commits.
    /// </summary>
    internal async Task UpsertAsync(Guid enrolmentId, Guid classSubjectId, Guid termId, Guid componentId, decimal value, CancellationToken ct)
    {
        var (cs, cls, _) = await LoadAsync(classSubjectId, termId, ct);
        if (await db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct) == AssessmentMode.Developmental)
            throw new BadRequestException("This class uses developmental assessment.");
        var components = (await academic.EnsureSchemeAsync(cls.LevelId, ct)).ToDictionary(c => c.Id);
        if (!components.TryGetValue(componentId, out var component))
            throw BadRequestException.Field("componentId", "One of the columns is not part of this class's assessment. Reload the sheet and try again.");
        if (value < 0 || value > component.MaxScore)
            throw BadRequestException.Field("value", $"{component.Name} scores must be between 0 and {component.MaxScore:0.#}.");
        if (decimal.Round(value, 1) != value)
            throw BadRequestException.Field("value", "Scores can have at most one decimal place.");
        var onRoster = await db.Enrolments.AnyAsync(e => e.Id == enrolmentId && e.ClassId == cls.Id && e.SessionId == cls.SessionId
            && db.Students.Any(s => s.Id == e.StudentId && s.Status == StudentStatus.Active), ct);
        if (!onRoster) throw BadRequestException.Field("enrolmentId", "This student is not in this class.");

        if (db.Database.CurrentTransaction is null)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await WriteLockedAsync(cls.Id, cs.Id, termId, enrolmentId, componentId, value, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
            {
                throw new ConflictException("These scores were saved from another device at the same moment. Reload the sheet to see the latest.", "scores_conflict");
            }
            await tx.CommitAsync(ct);
            await MarkStaleAsync(cls.Id, termId, ct);
            return;
        }

        await WriteLockedAsync(cls.Id, cs.Id, termId, enrolmentId, componentId, value, ct);
        await MarkStaleAsync(cls.Id, termId, ct);
    }

    private async Task WriteLockedAsync(Guid classId, Guid classSubjectId, Guid termId, Guid enrolmentId, Guid componentId, decimal value, CancellationToken ct)
    {
        await db.LockSharedAsync(AcademicService.AssessmentLockKey(TenantId), ct);
        await db.LockAsync(LockKey(classId, termId), ct);
        await EnsureOpenAsync(classId, termId, ct);
        var row = await db.Scores.FirstOrDefaultAsync(s =>
            s.EnrolmentId == enrolmentId && s.ClassSubjectId == classSubjectId && s.TermId == termId && s.ComponentId == componentId, ct);
        ApplyLoaded(row, enrolmentId, classSubjectId, termId, componentId, value, await access.StaffIdAsync(ct), TenantId);
    }

    private async Task EnsureOpenAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        if (await db.TermResultLocks.AnyAsync(l => l.ClassId == classId && l.TermId == termId, ct))
            throw new ConflictException(TermLockedMessage, "term_locked");
    }

    /// <summary>Inserts or updates the one score row for this cell. Shared by the sheet save and <see cref="UpsertAsync"/>.</summary>
    private void ApplyLoaded(Score? row, Guid enrolmentId, Guid classSubjectId, Guid termId, Guid componentId, decimal value, Guid? staffId, Guid tenantId)
    {
        if (row is null)
        {
            db.Scores.Add(new Score
            {
                TenantId = tenantId, EnrolmentId = enrolmentId, ClassSubjectId = classSubjectId, TermId = termId,
                ComponentId = componentId, Value = value, EnteredByStaffId = staffId,
            });
            return;
        }
        if (row.Value != value || row.EnteredByStaffId != staffId)
        {
            row.Value = value;
            row.EnteredByStaffId = staffId;
        }
    }

    /// <summary>True when the class's term is locked. Used by later result and publish steps.</summary>
    public Task<bool> IsLockedAsync(Guid classId, Guid termId, CancellationToken ct) =>
        db.TermResultLocks.AnyAsync(l => l.ClassId == classId && l.TermId == termId, ct);

    /// <summary>Called whenever the scores of a class's term change or its lock changes: the term's results are rebuilt before they are next viewed or published.</summary>
    internal Task MarkStaleAsync(Guid classId, Guid termId, CancellationToken ct) => ResultStaleness.MarkAsync(db, classId, termId, ct);

    // ---------- Rules ----------

    internal static long LockKey(Guid classId, Guid termId) =>
        BitConverter.ToInt64(classId.ToByteArray(), 8) ^ BitConverter.ToInt64(termId.ToByteArray(), 8) ^ 0x5C0E0011;

    private async Task<(ClassSubject Cs, SchoolClass Cls, Term Term)> LoadAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        var cs = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classSubjectId, ct) ?? throw new NotFoundException("Class subject");
        var cls = await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == cs.ClassId, ct);
        var term = await TermOfAsync(cls, termId, ct);
        return (cs, cls, term);
    }

    private async Task<SchoolClass> ClassAndTermAsync(Guid classId, Guid termId, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        await TermOfAsync(cls, termId, ct);
        return cls;
    }

    private async Task<Term> TermOfAsync(SchoolClass cls, Guid termId, CancellationToken ct)
    {
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");
        return term;
    }

    /// <summary>Leadership reads everything, the subject teacher their subject, and the class teacher every subject of the class.</summary>
    private async Task EnsureCanReadAsync(ClassSubject cs, SchoolClass cls, CancellationToken ct)
    {
        if (access.IsLeadership || await access.TeachesClassSubjectAsync(cs.Id, ct)) return;
        if (await access.StaffIdAsync(ct) is { } staffId && cls.ClassTeacherStaffId == staffId) return;
        throw new ForbiddenException("You can only see scores for subjects you teach, or for your own class.");
    }

    private async Task EnsureCanWriteAsync(ClassSubject cs, CancellationToken ct)
    {
        if (access.IsLeadership || await access.TeachesClassSubjectAsync(cs.Id, ct)) return;
        throw new ForbiddenException("You can only enter scores for subjects you teach.");
    }

    private sealed record RosterRow(Guid EnrolmentId, string Name, string AdmissionNo);

    private async Task<List<RosterRow>> RosterAsync(SchoolClass cls, CancellationToken ct)
    {
        var rows = await db.Enrolments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.SessionId == cls.SessionId)
            .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id,
                (e, s) => new { EnrolmentId = e.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        return rows.Select(x => new RosterRow(x.EnrolmentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();
    }

    private async Task<ScoreGridDto> GridAsync(ClassSubject cs, SchoolClass cls, Guid termId, CancellationToken ct)
    {
        var components = await academic.EnsureSchemeAsync(cls.LevelId, ct);
        var roster = await RosterAsync(cls, ct);
        var scores = await db.Scores.AsNoTracking().Where(s => s.ClassSubjectId == cs.Id && s.TermId == termId)
            .Select(s => new { s.EnrolmentId, s.ComponentId, s.Value }).ToListAsync(ct);
        var byCell = scores.ToDictionary(s => (s.EnrolmentId, s.ComponentId), s => s.Value);
        var students = roster.Select(r => new ScoreStudentDto(r.EnrolmentId, r.Name, r.AdmissionNo,
            components.ToDictionary(c => c.Id.ToString(), c => byCell.TryGetValue((r.EnrolmentId, c.Id), out var v) ? (decimal?)v : null))).ToList();
        var locked = await IsLockedAsync(cls.Id, termId, ct);
        return new ScoreGridDto(cls.Id, cs.Id, termId, locked, components.Select(c => new ScoreComponentDto(c.Id, c.Name, c.MaxScore)).ToList(), students);
    }
}
