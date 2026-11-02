using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record LevelDto(Guid Id, string Name, Stage Stage, int Order, bool HasStudentLogins, AssessmentMode AssessmentMode);
public sealed record SaveLevelRequest(string Name, Stage Stage, int Order, bool HasStudentLogins, AssessmentMode? AssessmentMode = null);
public sealed record ClassDto(Guid Id, Guid SessionId, Guid LevelId, string LevelName, string Arm, string Name, Guid? ClassTeacherStaffId);
public sealed record SaveClassRequest(Guid LevelId, string Arm, Guid? SessionId, Guid? ClassTeacherStaffId);
public sealed record SubjectDto(Guid Id, string Name, string Code);
public sealed record SaveSubjectRequest(string Name, string Code);
public sealed record ClassSubjectDto(Guid Id, Guid ClassId, Guid SubjectId, string SubjectName, string SubjectCode, Guid? TeacherStaffId);
public sealed record AddClassSubjectRequest(Guid SubjectId, Guid? TeacherStaffId);
public sealed record SetTeacherRequest(Guid? TeacherStaffId);
public sealed record ApplyTemplateRequest(bool Levels, bool Subjects);
public sealed record ApplyTemplateResult(int LevelsAdded, int SubjectsAdded);

public sealed record ComponentDto(Guid Id, string Name, decimal MaxScore, int Order);
/// <summary>Send the id of an existing component to keep it (and its scores); leave it out for a new one.</summary>
public sealed record ComponentInput(string Name, decimal MaxScore, Guid? Id = null);
public sealed record SaveAssessmentRequest(Guid? LevelId, IReadOnlyList<ComponentInput> Components);
/// <summary><c>LevelId</c> is the level whose own scheme this is, or null when the school default applies.</summary>
public sealed record AssessmentDto(Guid? LevelId, IReadOnlyList<ComponentDto> Components);

public sealed record BandDto(decimal Min, decimal Max, string Grade, string Remark);
public sealed record BandInput(decimal Min, decimal Max, string Grade, string Remark);
public sealed record SaveGradingRequest(Guid? LevelId, IReadOnlyList<BandInput> Bands);
public sealed record GradingDto(Guid? LevelId, IReadOnlyList<BandDto> Bands);

/// <summary>Levels, classes, subjects, the assessment scheme and the grading bands of a school.</summary>
public sealed class AcademicService(IAppDbContext db, ITenantContext tenant)
{
    public const string ClassSubjectHasWork = "This subject has homework, tests, lesson plans or exam sittings. Remove those first.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Templates ----------

    public async Task<ApplyTemplateResult> ApplyTemplateAsync(ApplyTemplateRequest request, CancellationToken ct)
    {
        var tenantId = TenantId;
        int levelsAdded = 0, subjectsAdded = 0;
        if (request.Levels)
        {
            var existing = (await db.Levels.Select(l => l.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var order = 0;
            foreach (var t in SchoolTemplates.Levels)
            {
                order++;
                if (!existing.Add(t.Name)) continue;
                db.Levels.Add(new Level { TenantId = tenantId, Name = t.Name, Stage = t.Stage, Order = order, HasStudentLogins = t.HasStudentLogins, AssessmentMode = t.AssessmentMode });
                levelsAdded++;
            }
        }
        if (request.Subjects)
        {
            var current = await db.Subjects.Select(s => new { s.Name, s.Code }).ToListAsync(ct);
            var names = current.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var codes = current.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in SchoolTemplates.Subjects)
            {
                if (names.Contains(t.Name) || codes.Contains(t.Code)) continue;
                db.Subjects.Add(new Subject { TenantId = tenantId, Name = t.Name, Code = t.Code });
                names.Add(t.Name);
                codes.Add(t.Code);
                subjectsAdded++;
            }
        }
        await db.SaveChangesAsync(ct);
        return new ApplyTemplateResult(levelsAdded, subjectsAdded);
    }

    // ---------- Levels ----------

    /// <summary>On create, an omitted or null mode follows the stage: Creche and Nursery are developmental, every other stage is numeric.</summary>
    private static AssessmentMode ModeFor(Stage stage, AssessmentMode? mode) =>
        mode ?? (stage is Stage.Creche or Stage.Nursery ? AssessmentMode.Developmental : AssessmentMode.Numeric);

    private static LevelDto ToDto(Level l) => new(l.Id, l.Name, l.Stage, l.Order, l.HasStudentLogins, l.AssessmentMode);

    public async Task<IReadOnlyList<LevelDto>> ListLevelsAsync(CancellationToken ct) =>
        (await db.Levels.AsNoTracking().OrderBy(l => l.Order).ThenBy(l => l.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<LevelDto> CreateLevelAsync(SaveLevelRequest r, CancellationToken ct)
    {
        var name = CleanName(r.Name, "name", "level name", 40);
        if (await db.Levels.AnyAsync(l => l.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("A level with that name already exists.", "duplicate_level");
        var level = new Level { TenantId = TenantId, Name = name, Stage = r.Stage, Order = r.Order, HasStudentLogins = r.HasStudentLogins, AssessmentMode = ModeFor(r.Stage, r.AssessmentMode) };
        db.Levels.Add(level);
        await db.SaveChangesAsync(ct);
        return ToDto(level);
    }

    public async Task<LevelDto> UpdateLevelAsync(Guid id, SaveLevelRequest r, CancellationToken ct)
    {
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Level");
        var name = CleanName(r.Name, "name", "level name", 40);
        if (await db.Levels.AnyAsync(l => l.Id != id && l.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("A level with that name already exists.", "duplicate_level");
        var renamed = level.Name != name;
        level.Name = name;
        level.Stage = r.Stage;
        level.Order = r.Order;
        level.HasStudentLogins = r.HasStudentLogins;
        // An omitted mode keeps the stored one; the stage default is for a new level only.
        var modeChanged = r.AssessmentMode is { } mode && mode != level.AssessmentMode;
        if (r.AssessmentMode is { } chosen) level.AssessmentMode = chosen;
        // A class name is the level name followed by the arm, so a rename carries through.
        if (renamed)
            foreach (var c in await db.SchoolClasses.Where(c => c.LevelId == id).ToListAsync(ct))
                c.Name = $"{name}{c.Arm}";
        await db.SaveChangesAsync(ct);
        // Unpublished cards of the level are rebuilt in the new mode; published ones keep the mode they were computed in.
        if (modeChanged) await ResultStaleness.MarkLevelModeAsync(db, id, ct);
        return ToDto(level);
    }

    public async Task DeleteLevelAsync(Guid id, CancellationToken ct)
    {
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Level");
        if (await db.SchoolClasses.AnyAsync(c => c.LevelId == id, ct))
            throw new ConflictException("This level has classes. Delete its classes first.", "level_in_use");
        db.Levels.Remove(level);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Classes ----------

    private static ClassDto ToDto(SchoolClass c, string levelName) => new(c.Id, c.SessionId, c.LevelId, levelName, c.Arm, c.Name, c.ClassTeacherStaffId);

    public async Task<IReadOnlyList<ClassDto>> ListClassesAsync(Guid? sessionId, CancellationToken ct)
    {
        var session = sessionId ?? await CurrentSessionIdAsync(ct);
        var rows = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == session)
            .Join(db.Levels, c => c.LevelId, l => l.Id, (c, l) => new { Class = c, Level = l })
            .OrderBy(x => x.Level.Order).ThenBy(x => x.Class.Arm).ToListAsync(ct);
        return rows.Select(x => ToDto(x.Class, x.Level.Name)).ToList();
    }

    public async Task<ClassDto> CreateClassAsync(SaveClassRequest r, CancellationToken ct)
    {
        var arm = CleanName(r.Arm, "arm", "arm", 20);
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == r.LevelId, ct) ?? throw BadRequestException.Field("levelId", "Choose a level.");
        var sessionId = r.SessionId ?? await CurrentSessionIdAsync(ct);
        if (!await db.AcademicSessions.AnyAsync(s => s.Id == sessionId, ct)) throw BadRequestException.Field("sessionId", "Choose a session.");
        await EnsureClassFreeAsync(null, sessionId, level, arm, ct);
        await StaffService.EnsureStaffAsync(db, r.ClassTeacherStaffId, ct);

        var cls = new SchoolClass
        {
            TenantId = TenantId, SessionId = sessionId, LevelId = level.Id, Arm = arm, Name = $"{level.Name}{arm}",
            ClassTeacherStaffId = r.ClassTeacherStaffId,
        };
        db.SchoolClasses.Add(cls);
        await db.SaveChangesAsync(ct);
        return ToDto(cls, level.Name);
    }

    public async Task<ClassDto> UpdateClassAsync(Guid id, SaveClassRequest r, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Class");
        var arm = CleanName(r.Arm, "arm", "arm", 20);
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == r.LevelId, ct) ?? throw BadRequestException.Field("levelId", "Choose a level.");
        await EnsureClassFreeAsync(id, cls.SessionId, level, arm, ct);
        await StaffService.EnsureStaffAsync(db, r.ClassTeacherStaffId, ct);
        cls.LevelId = level.Id;
        cls.Arm = arm;
        cls.Name = $"{level.Name}{arm}";
        cls.ClassTeacherStaffId = r.ClassTeacherStaffId;
        await db.SaveChangesAsync(ct);
        return ToDto(cls, level.Name);
    }

    public async Task DeleteClassAsync(Guid id, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Class");
        if (await db.Enrolments.AnyAsync(e => e.ClassId == id, ct))
            throw new ConflictException("This class has students. Move them to another class first.", "class_in_use");
        if (await db.Scores.AnyAsync(s => db.ClassSubjects.Any(c => c.Id == s.ClassSubjectId && c.ClassId == id), ct)
            || await db.TermResults.AnyAsync(r => r.ClassId == id, ct)
            || await db.TermResultLocks.AnyAsync(l => l.ClassId == id, ct)
            || await db.AttendanceRegisters.AnyAsync(r => r.ClassId == id, ct))
            throw new ConflictException("This class has records. It can't be deleted.", "class_has_records");
        db.SchoolClasses.Remove(cls);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureClassFreeAsync(Guid? ignoreId, Guid sessionId, Level level, string arm, CancellationToken ct)
    {
        var name = $"{level.Name}{arm}".ToLower();
        if (await db.SchoolClasses.AnyAsync(c => c.Id != ignoreId && c.SessionId == sessionId
                && (c.Name.ToLower() == name || (c.LevelId == level.Id && c.Arm.ToLower() == arm.ToLower())), ct))
            throw new ConflictException($"{level.Name}{arm} already exists in this session.", "duplicate_class");
    }

    private async Task<Guid> CurrentSessionIdAsync(CancellationToken ct) =>
        await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw BadRequestException.Field("sessionId", "Set a current session first.");

    // ---------- Subjects ----------

    private static SubjectDto ToDto(Subject s) => new(s.Id, s.Name, s.Code);

    public async Task<IReadOnlyList<SubjectDto>> ListSubjectsAsync(CancellationToken ct) =>
        (await db.Subjects.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<SubjectDto> CreateSubjectAsync(SaveSubjectRequest r, CancellationToken ct)
    {
        var (name, code) = CleanSubject(r);
        await EnsureSubjectFreeAsync(null, name, code, ct);
        var subject = new Subject { TenantId = TenantId, Name = name, Code = code };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync(ct);
        return ToDto(subject);
    }

    public async Task<SubjectDto> UpdateSubjectAsync(Guid id, SaveSubjectRequest r, CancellationToken ct)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Subject");
        var (name, code) = CleanSubject(r);
        await EnsureSubjectFreeAsync(id, name, code, ct);
        subject.Name = name;
        subject.Code = code;
        await db.SaveChangesAsync(ct);
        return ToDto(subject);
    }

    public async Task DeleteSubjectAsync(Guid id, CancellationToken ct)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Subject");
        if (await db.ClassSubjects.AnyAsync(cs => cs.SubjectId == id, ct))
            throw new ConflictException("This subject is taught in a class. Remove it from the class first.", "subject_in_use");
        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(ct);
    }

    internal static (string Name, string Code) CleanSubject(SaveSubjectRequest r)
    {
        var name = CleanName(r.Name, "name", "subject name", 80);
        var code = (r.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 10 || !code.All(char.IsLetterOrDigit))
            throw BadRequestException.Field("code", "Enter a code of up to 10 letters or digits, such as MTH.");
        return (name, code);
    }

    private async Task EnsureSubjectFreeAsync(Guid? ignoreId, string name, string code, CancellationToken ct)
    {
        if (await db.Subjects.AnyAsync(s => s.Id != ignoreId && s.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("A subject with that name already exists.", "duplicate_subject");
        if (await db.Subjects.AnyAsync(s => s.Id != ignoreId && s.Code == code, ct))
            throw new ConflictException("A subject with that code already exists.", "duplicate_subject_code");
    }

    // ---------- Class subjects ----------

    public async Task<IReadOnlyList<ClassSubjectDto>> ListClassSubjectsAsync(Guid classId, CancellationToken ct)
    {
        if (!await db.SchoolClasses.AnyAsync(c => c.Id == classId, ct)) throw new NotFoundException("Class");
        var rows = await db.ClassSubjects.AsNoTracking().Where(cs => cs.ClassId == classId)
            .Join(db.Subjects, cs => cs.SubjectId, s => s.Id, (cs, s) => new { Link = cs, Subject = s })
            .OrderBy(x => x.Subject.Name).ToListAsync(ct);
        return rows.Select(x => new ClassSubjectDto(x.Link.Id, x.Link.ClassId, x.Link.SubjectId, x.Subject.Name, x.Subject.Code, x.Link.TeacherStaffId)).ToList();
    }

    public async Task<ClassSubjectDto> AddClassSubjectAsync(Guid classId, AddClassSubjectRequest r, CancellationToken ct)
    {
        if (!await db.SchoolClasses.AnyAsync(c => c.Id == classId, ct)) throw new NotFoundException("Class");
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == r.SubjectId, ct) ?? throw BadRequestException.Field("subjectId", "Choose a subject.");
        if (await db.ClassSubjects.AnyAsync(cs => cs.ClassId == classId && cs.SubjectId == subject.Id, ct))
            throw new ConflictException($"{subject.Name} is already taught in this class.", "duplicate_class_subject");
        await StaffService.EnsureStaffAsync(db, r.TeacherStaffId, ct);
        var link = new ClassSubject { TenantId = TenantId, ClassId = classId, SubjectId = subject.Id, TeacherStaffId = r.TeacherStaffId };
        db.ClassSubjects.Add(link);
        await db.SaveChangesAsync(ct);
        return new ClassSubjectDto(link.Id, classId, subject.Id, subject.Name, subject.Code, link.TeacherStaffId);
    }

    public async Task<ClassSubjectDto> SetTeacherAsync(Guid classId, Guid subjectId, SetTeacherRequest r, CancellationToken ct)
    {
        var link = await db.ClassSubjects.FirstOrDefaultAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId, ct)
            ?? throw new NotFoundException("Class subject");
        await StaffService.EnsureStaffAsync(db, r.TeacherStaffId, ct);
        link.TeacherStaffId = r.TeacherStaffId;
        await db.SaveChangesAsync(ct);
        var subject = await db.Subjects.AsNoTracking().FirstAsync(s => s.Id == subjectId, ct);
        return new ClassSubjectDto(link.Id, classId, subjectId, subject.Name, subject.Code, link.TeacherStaffId);
    }

    public async Task RemoveClassSubjectAsync(Guid classId, Guid subjectId, CancellationToken ct)
    {
        var link = await db.ClassSubjects.FirstOrDefaultAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId, ct)
            ?? throw new NotFoundException("Class subject");
        if (await db.Scores.AnyAsync(s => s.ClassSubjectId == link.Id, ct))
            throw new ConflictException("Scores have been entered for this subject. Keep it in the class, or clear its scores first.", "class_subject_has_scores");
        if (await db.SubjectResults.AnyAsync(r => r.ClassSubjectId == link.Id, ct) || await db.TermResultLocks.AnyAsync(l => l.ClassId == classId, ct))
            throw new ConflictException("Results exist for this subject. It can't be removed.", "class_subject_has_results");
        // Homework, tests, lesson plans and sittings would be deleted with the link, so they must go first.
        if (await db.Assignments.AnyAsync(a => a.ClassSubjectId == link.Id, ct) || await db.CbtPapers.AnyAsync(p => p.ClassSubjectId == link.Id, ct)
            || await db.LessonPlans.AnyAsync(p => p.ClassSubjectId == link.Id, ct) || await db.ExamSittings.AnyAsync(s => s.ClassSubjectId == link.Id, ct))
            throw new ConflictException(ClassSubjectHasWork, "class_subject_has_work");
        db.ClassSubjects.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Assessment scheme ----------

    /// <summary>The components for a level: its own override, else the school default, else the built-in CA1, CA2 and Exam.</summary>
    public async Task<IReadOnlyList<AssessmentComponent>> SchemeFor(Guid levelId)
    {
        var own = await db.AssessmentComponents.AsNoTracking().Where(c => c.LevelId == levelId).OrderBy(c => c.Order).ToListAsync();
        if (own.Count > 0) return own;
        return await SchoolSchemeAsync(default);
    }

    private async Task<IReadOnlyList<AssessmentComponent>> SchoolSchemeAsync(CancellationToken ct)
    {
        var saved = await db.AssessmentComponents.AsNoTracking().Where(c => c.LevelId == null).OrderBy(c => c.Order).ToListAsync(ct);
        if (saved.Count > 0) return saved;
        return SchoolTemplates.Components.Select((t, i) => new AssessmentComponent { TenantId = TenantId, Name = t.Name, MaxScore = t.MaxScore, Order = i + 1 }).ToList();
    }

    public async Task<AssessmentDto> GetAssessmentAsync(Guid? levelId, CancellationToken ct)
    {
        if (levelId is { } id)
        {
            await EnsureLevelAsync(id, ct);
            var own = await db.AssessmentComponents.AsNoTracking().Where(c => c.LevelId == id).OrderBy(c => c.Order).ToListAsync(ct);
            if (own.Count > 0) return new AssessmentDto(id, own.Select(ToDto).ToList());
        }
        return new AssessmentDto(null, (await EnsureDefaultSchemeAsync(ct)).Select(ToDto).ToList());
    }

    public async Task<AssessmentDto> SaveAssessmentAsync(SaveAssessmentRequest r, CancellationToken ct)
    {
        if (r.LevelId is { } levelId) await EnsureLevelAsync(levelId, ct);
        var inputs = r.Components ?? [];
        if (inputs.Count is 0 or > 10) throw BadRequestException.Field("components", "Add between 1 and 10 components, such as CA1 and Exam.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in inputs)
        {
            var name = (c.Name ?? "").Trim();
            if (name.Length is 0 or > 40) throw BadRequestException.Field("components", "Give every component a name of up to 40 characters.");
            if (!names.Add(name)) throw BadRequestException.Field("components", $"{name} appears twice. Give each component a different name.");
            if (c.MaxScore <= 0 || c.MaxScore > 100 || decimal.Round(c.MaxScore, 1) != c.MaxScore)
                throw BadRequestException.Field("components", $"The maximum for {name} must be more than 0 and no more than 100, with at most one decimal place.");
        }
        var total = inputs.Sum(c => c.MaxScore);
        if (total != 100) throw BadRequestException.Field("components", $"The components add up to {total:0.#}. They must total 100.");

        // Components are matched by id, never by position, so the scores entered against a column stay with it.
        var tenantId = TenantId;
        var given = inputs.Where(c => c.Id is not null).Select(c => c.Id).ToList();
        if (given.Count != given.Distinct().Count()) throw BadRequestException.Field("components", "Each part can appear once.");

        // Score saves and term locks hold this lock shared, so the checks below stay true until the save commits.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AssessmentLockKey(tenantId), ct);
        var own = await db.AssessmentComponents.Where(c => c.LevelId == r.LevelId).OrderBy(c => c.Order).ToListAsync(ct);
        var creatingOverride = r.LevelId is not null && own.Count == 0;
        var effective = creatingOverride ? await db.AssessmentComponents.Where(c => c.LevelId == null).OrderBy(c => c.Order).ToListAsync(ct) : own;
        var effectiveIds = effective.Select(c => c.Id).ToHashSet();
        if (inputs.Any(c => c.Id is { } id && !effectiveIds.Contains(id)))
            throw BadRequestException.Field("components", "One of the components is not part of this assessment. Reload the page and try again.");

        if (creatingOverride)
        {
            // Classes of the level already have scores under the school default; a new scheme would hide them.
            if (await db.Scores.Join(db.ClassSubjects, s => s.ClassSubjectId, cs => cs.Id, (s, cs) => cs.ClassId)
                    .Join(db.SchoolClasses, classId => classId, c => c.Id, (classId, c) => c.LevelId).AnyAsync(l => l == r.LevelId, ct))
                throw new ConflictException(ScoresEntered, "assessment_has_scores");
        }
        else if (effective.Count > 0 && await db.Scores.AnyAsync(s => effectiveIds.Contains(s.ComponentId), ct))
        {
            // With scores in place, parts can be renamed but not added, removed or reordered, and no maximum drops below a stored score.
            if (!inputs.Select(c => c.Id).SequenceEqual(effective.Select(c => (Guid?)c.Id)))
                throw new ConflictException(ScoresEntered, "assessment_has_scores");
            for (var i = 0; i < effective.Count; i++)
            {
                if (inputs[i].MaxScore >= effective[i].MaxScore) continue;
                var id = effective[i].Id;
                var highest = await db.Scores.Where(s => s.ComponentId == id).MaxAsync(s => (decimal?)s.Value, ct);
                if (highest > inputs[i].MaxScore)
                    throw new ConflictException("Some scores are above the new maximum. Change those scores first.", "scores_above_maximum");
            }
            var levels = r.LevelId is { } only
                ? [only]
                : await db.Levels.Where(l => !db.AssessmentComponents.Any(c => c.LevelId == l.Id)).Select(l => l.Id).ToListAsync(ct);
            if (await db.TermResultLocks.Join(db.SchoolClasses, l => l.ClassId, c => c.Id, (l, c) => c.LevelId).AnyAsync(l => levels.Contains(l), ct))
                throw new ConflictException("Results are locked for some classes. Unlock them before changing the assessment.", "term_locked");
        }

        var saved = new List<AssessmentComponent>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var name = inputs[i].Name.Trim();
            var row = !creatingOverride && inputs[i].Id is { } id ? own.First(c => c.Id == id) : null;
            if (row is not null)
            {
                row.Name = name;
                row.MaxScore = inputs[i].MaxScore;
                row.Order = i + 1;
            }
            else
            {
                row = new AssessmentComponent { TenantId = tenantId, LevelId = r.LevelId, Name = name, MaxScore = inputs[i].MaxScore, Order = i + 1 };
                db.AssessmentComponents.Add(row);
            }
            saved.Add(row);
        }
        var keptIds = saved.Select(c => c.Id).ToHashSet();
        db.AssessmentComponents.RemoveRange(own.Where(c => !keptIds.Contains(c.Id)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AssessmentDto(r.LevelId, saved.Select(ToDto).ToList());
    }

    private const string ScoresEntered = "Scores have been entered with this assessment. You can rename parts, but not add, remove or reorder them.";

    /// <summary>Removes a level's own scheme so it follows the school default again.</summary>
    public async Task ResetAssessmentAsync(Guid levelId, CancellationToken ct)
    {
        await EnsureLevelAsync(levelId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AssessmentLockKey(TenantId), ct);
        var own = await db.AssessmentComponents.Where(c => c.LevelId == levelId).ToListAsync(ct);
        await EnsureNoScoresAsync(own, ct);
        db.AssessmentComponents.RemoveRange(own);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// The per-school lock for the assessment. Editing it takes the lock exclusively; saving scores and locking a term take it shared
    /// (<see cref="IAppDbContext.LockSharedAsync"/>), so many teachers save at once but an edit waits for them and blocks them.
    /// </summary>
    public static long AssessmentLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x4153534D;

    private async Task EnsureNoScoresAsync(IReadOnlyList<AssessmentComponent> components, CancellationToken ct)
    {
        if (components.Count == 0) return;
        var ids = components.Select(c => c.Id).ToList();
        if (await db.Scores.AnyAsync(s => ids.Contains(s.ComponentId), ct))
            throw new ConflictException(ScoresEntered, "assessment_has_scores");
    }

    /// <summary>
    /// The components for a level as saved rows. The built-in default (CA1, CA2, Exam) is saved the first time scores need it,
    /// so every column has a stable id.
    /// </summary>
    public async Task<IReadOnlyList<AssessmentComponent>> EnsureSchemeAsync(Guid levelId, CancellationToken ct)
    {
        var own = await db.AssessmentComponents.AsNoTracking().Where(c => c.LevelId == levelId).OrderBy(c => c.Order).ToListAsync(ct);
        if (own.Count > 0) return own;
        return await EnsureDefaultSchemeAsync(ct);
    }

    /// <summary>The school default components as saved rows, saving CA1, CA2 and Exam the first time.</summary>
    public async Task<IReadOnlyList<AssessmentComponent>> EnsureDefaultSchemeAsync(CancellationToken ct)
    {
        var saved = await db.AssessmentComponents.AsNoTracking().Where(c => c.LevelId == null).OrderBy(c => c.Order).ToListAsync(ct);
        if (saved.Count > 0) return saved;

        // Two first requests wait for each other, so the defaults are saved once. Inside a caller's transaction, join it.
        var outer = db.Database.CurrentTransaction;
        await using var own = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(BitConverter.ToInt64(TenantId.ToByteArray(), 8) ^ 0x5C0E5C4E, ct);
        saved = await db.AssessmentComponents.AsNoTracking().Where(c => c.LevelId == null).OrderBy(c => c.Order).ToListAsync(ct);
        if (saved.Count > 0) return saved;
        var defaults = SchoolTemplates.Components.Select((t, i) => new AssessmentComponent { TenantId = TenantId, Name = t.Name, MaxScore = t.MaxScore, Order = i + 1 }).ToList();
        db.AssessmentComponents.AddRange(defaults);
        await db.SaveChangesAsync(ct);
        if (own is not null) await own.CommitAsync(ct);
        return defaults;
    }

    private static ComponentDto ToDto(AssessmentComponent c) => new(c.Id, c.Name, c.MaxScore, c.Order);

    // ---------- Grading bands ----------

    /// <summary>The bands for a level, best grade first: its own override, else the school default, else the built-in Nigerian scale.</summary>
    public async Task<IReadOnlyList<GradingBand>> BandsFor(Guid levelId)
    {
        var own = await db.GradingBands.AsNoTracking().Where(b => b.LevelId == levelId).OrderByDescending(b => b.Min).ToListAsync();
        if (own.Count > 0) return own;
        return await SchoolBandsAsync(default);
    }

    private async Task<IReadOnlyList<GradingBand>> SchoolBandsAsync(CancellationToken ct)
    {
        var saved = await db.GradingBands.AsNoTracking().Where(b => b.LevelId == null).OrderByDescending(b => b.Min).ToListAsync(ct);
        if (saved.Count > 0) return saved;
        return SchoolTemplates.Bands.Select(t => new GradingBand { TenantId = TenantId, Min = t.Min, Max = t.Max, Grade = t.Grade, Remark = t.Remark }).ToList();
    }

    public async Task<GradingDto> GetGradingAsync(Guid? levelId, CancellationToken ct)
    {
        if (levelId is { } id)
        {
            await EnsureLevelAsync(id, ct);
            var own = await db.GradingBands.AsNoTracking().Where(b => b.LevelId == id).OrderByDescending(b => b.Min).ToListAsync(ct);
            if (own.Count > 0) return new GradingDto(id, own.Select(ToDto).ToList());
        }
        return new GradingDto(null, (await SchoolBandsAsync(ct)).Select(ToDto).ToList());
    }

    /// <summary>
    /// Bands must cover 0 to 100 exactly once. Totals have one decimal place, so a band runs from its minimum up to the next band's
    /// minimum: 74.9 belongs to the 70 to 74 band. Each band's maximum therefore sits within 1 below the next minimum.
    /// </summary>
    public async Task<GradingDto> SaveGradingAsync(SaveGradingRequest r, CancellationToken ct)
    {
        if (r.LevelId is { } levelId) await EnsureLevelAsync(levelId, ct);
        var inputs = r.Bands ?? [];
        if (inputs.Count is 0 or > 20) throw BadRequestException.Field("bands", "Add between 1 and 20 grade bands.");

        var grades = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in inputs)
        {
            var grade = (b.Grade ?? "").Trim();
            if (grade.Length is 0 or > 10) throw BadRequestException.Field("bands", "Give every band a grade of up to 10 characters, such as A1.");
            if (!grades.Add(grade)) throw BadRequestException.Field("bands", $"The grade {grade} appears twice. Give each band a different grade.");
            if ((b.Remark ?? "").Trim().Length is 0 or > 60) throw BadRequestException.Field("bands", $"Give grade {grade} a remark of up to 60 characters.");
            if (b.Min < 0 || b.Max > 100 || b.Min > b.Max || decimal.Round(b.Min, 1) != b.Min || decimal.Round(b.Max, 1) != b.Max)
                throw BadRequestException.Field("bands", $"Grade {grade} needs a minimum and maximum between 0 and 100, with at most one decimal place, and the minimum cannot exceed the maximum.");
        }

        var ordered = inputs.OrderBy(b => b.Min).ToList();
        if (ordered[0].Min != 0) throw BadRequestException.Field("bands", "The lowest band must start at 0.");
        if (ordered[^1].Max != 100) throw BadRequestException.Field("bands", "The highest band must end at 100.");
        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = ordered[i].Min - ordered[i - 1].Max;
            if (gap <= 0)
                throw BadRequestException.Field("bands", $"Grades {ordered[i - 1].Grade.Trim()} and {ordered[i].Grade.Trim()} overlap. Each score must fall in one band.");
            if (gap > 1)
                throw BadRequestException.Field("bands", $"There is a gap between grades {ordered[i - 1].Grade.Trim()} and {ordered[i].Grade.Trim()}. Every score from 0 to 100 needs a band.");
        }

        db.GradingBands.RemoveRange(await db.GradingBands.Where(b => b.LevelId == r.LevelId).ToListAsync(ct));
        var tenantId = TenantId;
        var saved = ordered.OrderByDescending(b => b.Min).Select(b => new GradingBand
        {
            TenantId = tenantId, LevelId = r.LevelId, Min = b.Min, Max = b.Max, Grade = b.Grade.Trim(), Remark = b.Remark.Trim(),
        }).ToList();
        db.GradingBands.AddRange(saved);
        await db.SaveChangesAsync(ct);
        await ResultStaleness.MarkLevelAsync(db, r.LevelId, ct);
        return new GradingDto(r.LevelId, saved.Select(ToDto).ToList());
    }

    /// <summary>Removes a level's own bands so it follows the school default again.</summary>
    public async Task ResetGradingAsync(Guid levelId, CancellationToken ct)
    {
        await EnsureLevelAsync(levelId, ct);
        db.GradingBands.RemoveRange(await db.GradingBands.Where(b => b.LevelId == levelId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        await ResultStaleness.MarkLevelAsync(db, levelId, ct);
    }

    private static BandDto ToDto(GradingBand b) => new(b.Min, b.Max, b.Grade, b.Remark);

    // ---------- Helpers ----------

    private async Task EnsureLevelAsync(Guid levelId, CancellationToken ct)
    {
        if (!await db.Levels.AnyAsync(l => l.Id == levelId, ct)) throw new NotFoundException("Level");
    }

    internal static string CleanName(string? value, string field, string label, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw BadRequestException.Field(field, $"Enter the {label}.");
        if (text.Length > max) throw BadRequestException.Field(field, $"The {label} can have at most {max} characters.");
        return text;
    }
}
