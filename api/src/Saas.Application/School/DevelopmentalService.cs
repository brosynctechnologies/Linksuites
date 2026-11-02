using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record DevelopmentalAreaDto(Guid Id, string Name, int Order);
public sealed record DevelopmentalAreasDto(Guid? LevelId, IReadOnlyList<DevelopmentalAreaDto> Areas);
public sealed record SaveDevelopmentalArea(Guid? Id, string? Name);
public sealed record SaveDevelopmentalAreasRequest(IReadOnlyList<SaveDevelopmentalArea>? Areas);

public sealed record DevelopmentalScaleItemDto(Guid Id, string Code, string Label, int Order);
public sealed record DevelopmentalScaleDto(Guid? LevelId, IReadOnlyList<DevelopmentalScaleItemDto> Scale);
public sealed record SaveDevelopmentalScaleItem(Guid? Id, string? Code, string? Label);
public sealed record SaveDevelopmentalScaleRequest(IReadOnlyList<SaveDevelopmentalScaleItem>? Scale);

public sealed record DevelopmentalRatingItemDto(Guid AreaId, string Name, int Order, Guid? ScaleId, string? Scale, string? Code, string? Comment);
public sealed record DevelopmentalRatingsDto(Guid StudentId, Guid TermId, IReadOnlyList<DevelopmentalRatingItemDto> Ratings);
public sealed record SaveDevelopmentalRating(Guid AreaId, Guid ScaleId, string? Comment);
public sealed record SaveDevelopmentalRatingsRequest(IReadOnlyList<SaveDevelopmentalRating>? Ratings);

/// <summary>
/// Developmental areas and the scale a nursery or creche class is rated on. A rating is not an exam mark.
/// A level with its own rows overrides the school default, the same way an assessment scheme does.
/// The school default is stored the first time it is read. Applying the school template does not insert these rows.
/// </summary>
public sealed class DevelopmentalService(IAppDbContext db, ITenantContext tenant, SchoolAccess access, IDbErrors dbErrors)
{
    public const int MaxNameLength = 40;
    public const int MaxItems = 20;
    public const int MaxCommentLength = 500;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public async Task<DevelopmentalAreasDto> GetAreasAsync(Guid? levelId, CancellationToken ct)
    {
        if (levelId is { } id) await EnsureLevelAsync(id, ct);
        var rows = await AreasForAsync(levelId, ct);
        var showingOverride = levelId is not null && rows.Count > 0 && rows[0].LevelId == levelId;
        return ToAreas(showingOverride ? levelId : null, rows);
    }

    /// <summary>Leadership replaces the areas. A level id writes that level's override and leaves the school default as it is.</summary>
    public async Task<DevelopmentalAreasDto> SaveAreasAsync(Guid? levelId, SaveDevelopmentalAreasRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can change the developmental areas.");
        if (levelId is { } id) await EnsureLevelAsync(id, ct);
        var inputs = CleanAreas(r.Areas);

        var tenantId = TenantId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(SeedLockKey(tenantId), ct);
        await EnsureDefaultsAsync(ct);

        var own = await db.DevelopmentalAreas.Where(a => a.LevelId == levelId).OrderBy(a => a.Order).ToListAsync(ct);
        var creatingOverride = levelId is not null && own.Count == 0;
        var effective = creatingOverride
            ? await db.DevelopmentalAreas.Where(a => a.LevelId == null).OrderBy(a => a.Order).ToListAsync(ct)
            : own;
        var effectiveIds = effective.Select(a => a.Id).ToHashSet();
        if (inputs.Any(a => a.Id is { } given && !effectiveIds.Contains(given)))
            throw BadRequestException.Field("areas", "One of the areas is not part of this list. Reload the page and try again.");
        var givenIds = inputs.Where(a => a.Id is not null).Select(a => a.Id!.Value).ToList();
        if (givenIds.Count != givenIds.Distinct().Count())
            throw BadRequestException.Field("areas", "Each area can appear once.");

        if (creatingOverride)
        {
            if (await LevelHasAreaRatingsAsync(levelId!.Value, effectiveIds, ct))
                throw new ConflictException("Teachers have already rated these areas for this level. The level keeps the school's list.", "developmental_has_ratings");
        }
        else
        {
            var removed = own.Where(a => inputs.All(i => i.Id != a.Id)).Select(a => a.Id).ToList();
            if (removed.Count > 0 && await db.DevelopmentalRatings.AnyAsync(x => removed.Contains(x.AreaId), ct))
                throw new ConflictException("Teachers have already rated one of these areas. You can rename it, but not remove it.", "developmental_has_ratings");
        }

        var saved = new List<DevelopmentalArea>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var row = !creatingOverride && inputs[i].Id is { } existing ? own.First(a => a.Id == existing) : null;
            if (row is not null)
            {
                row.Name = inputs[i].Name;
                row.Order = i + 1;
            }
            else
            {
                row = new DevelopmentalArea { TenantId = tenantId, LevelId = levelId, Name = inputs[i].Name, Order = i + 1 };
                db.DevelopmentalAreas.Add(row);
            }
            saved.Add(row);
        }
        var kept = saved.Select(a => a.Id).ToHashSet();
        db.DevelopmentalAreas.RemoveRange(own.Where(a => !kept.Contains(a.Id)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToAreas(levelId, saved);
    }

    public async Task<DevelopmentalScaleDto> GetScaleAsync(Guid? levelId, CancellationToken ct)
    {
        if (levelId is { } id) await EnsureLevelAsync(id, ct);
        var rows = await ScaleForAsync(levelId, ct);
        var showingOverride = levelId is not null && rows.Count > 0 && rows[0].LevelId == levelId;
        return ToScale(showingOverride ? levelId : null, rows);
    }

    /// <summary>Leadership replaces the scale. A level id writes that level's override and leaves the school default as it is.</summary>
    public async Task<DevelopmentalScaleDto> SaveScaleAsync(Guid? levelId, SaveDevelopmentalScaleRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can change the developmental scale.");
        if (levelId is { } id) await EnsureLevelAsync(id, ct);
        var inputs = CleanScaleItems(r.Scale);

        var tenantId = TenantId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(SeedLockKey(tenantId), ct);
        await EnsureDefaultsAsync(ct);

        var own = await db.DevelopmentalScales.Where(s => s.LevelId == levelId).OrderBy(s => s.Order).ToListAsync(ct);
        var creatingOverride = levelId is not null && own.Count == 0;
        var effective = creatingOverride
            ? await db.DevelopmentalScales.Where(s => s.LevelId == null).OrderBy(s => s.Order).ToListAsync(ct)
            : own;
        var effectiveIds = effective.Select(s => s.Id).ToHashSet();
        if (inputs.Any(s => s.Id is { } given && !effectiveIds.Contains(given)))
            throw BadRequestException.Field("scale", "One of the points is not part of this scale. Reload the page and try again.");
        var givenIds = inputs.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToList();
        if (givenIds.Count != givenIds.Distinct().Count())
            throw BadRequestException.Field("scale", "Each point can appear once.");

        if (creatingOverride)
        {
            if (await LevelHasScaleRatingsAsync(levelId!.Value, effectiveIds, ct))
                throw new ConflictException("Teachers have already used this scale for this level. The level keeps the school's scale.", "developmental_has_ratings");
        }
        else
        {
            var removed = own.Where(s => inputs.All(i => i.Id != s.Id)).Select(s => s.Id).ToList();
            if (removed.Count > 0 && await db.DevelopmentalRatings.AnyAsync(x => removed.Contains(x.ScaleId), ct))
                throw new ConflictException("Teachers have already used one of these labels. You can rename it, but not remove it.", "developmental_has_ratings");
        }

        var saved = new List<DevelopmentalScale>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var row = !creatingOverride && inputs[i].Id is { } existing ? own.First(s => s.Id == existing) : null;
            if (row is not null)
            {
                row.Code = inputs[i].Code;
                row.Label = inputs[i].Label;
                row.Order = i + 1;
            }
            else
            {
                row = new DevelopmentalScale { TenantId = tenantId, LevelId = levelId, Code = inputs[i].Code, Label = inputs[i].Label, Order = i + 1 };
                db.DevelopmentalScales.Add(row);
            }
            saved.Add(row);
        }
        var kept = saved.Select(s => s.Id).ToHashSet();
        db.DevelopmentalScales.RemoveRange(own.Where(s => !kept.Contains(s.Id)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToScale(levelId, saved);
    }

    /// <summary>The areas for a level, or the school default when the level has none. Seeds the school default on first use.</summary>
    public async Task<IReadOnlyList<DevelopmentalArea>> AreasForAsync(Guid? levelId, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        if (levelId is { } id)
        {
            var own = await db.DevelopmentalAreas.AsNoTracking().Where(a => a.LevelId == id).OrderBy(a => a.Order).ThenBy(a => a.Name).ToListAsync(ct);
            if (own.Count > 0) return own;
        }
        return await db.DevelopmentalAreas.AsNoTracking().Where(a => a.LevelId == null).OrderBy(a => a.Order).ThenBy(a => a.Name).ToListAsync(ct);
    }

    /// <summary>Area ratings for a child the caller has already been allowed to read. Seeds the list. Does not check who is asking.</summary>
    public async Task<IReadOnlyList<DevelopmentalRatingItemDto>> RatingsForAsync(Guid levelId, Guid enrolmentId, Guid studentId, Guid termId, CancellationToken ct)
    {
        var areas = await AreasForAsync(levelId, ct);
        var scale = await ScaleForAsync(levelId, ct);
        return (await ViewAsync(studentId, termId, enrolmentId, areas, scale, ct)).Ratings;
    }

    /// <summary>The class teacher of the child's class, or leadership, reads the ratings. The bursar is refused before the student is looked up.</summary>
    public async Task<DevelopmentalRatingsDto> GetRatingsAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not rate developmental areas.");
        if (termId is not { } term) throw BadRequestException.Field("termId", "Choose a term.");
        var child = await LoadChildAsync(studentId, term, ct);
        var areas = await AreasForAsync(child.Class.LevelId, ct);
        var scale = await ScaleForAsync(child.Class.LevelId, ct);
        return await ViewAsync(studentId, term, child.Enrolment.Id, areas, scale, ct);
    }

    /// <summary>
    /// Saves the ratings that were sent and leaves the others as they are. A numeric class is refused.
    /// This does not write a score or a term total.
    /// </summary>
    public async Task<DevelopmentalRatingsDto> SaveRatingsAsync(Guid studentId, Guid? termId, SaveDevelopmentalRatingsRequest r, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not rate developmental areas.");
        if (termId is not { } term) throw BadRequestException.Field("termId", "Choose a term.");
        var child = await LoadChildAsync(studentId, term, ct);
        if (await ModeAsync(child.Class.LevelId, ct) != AssessmentMode.Developmental)
            throw new BadRequestException("This class uses numeric assessment.");

        var ratings = r.Ratings ?? throw BadRequestException.Field("ratings", "Send at least one rating.");
        if (ratings.Count == 0) throw BadRequestException.Field("ratings", "Send at least one rating.");
        if (ratings.Select(x => x.AreaId).Distinct().Count() != ratings.Count)
            throw BadRequestException.Field("ratings", "Each area can be rated once.");
        var comments = ratings.ToDictionary(x => x.AreaId, x => CleanComment(x.Comment));

        var areas = await AreasForAsync(child.Class.LevelId, ct);
        var scale = await ScaleForAsync(child.Class.LevelId, ct);
        var allowedAreas = areas.Select(a => a.Id).ToHashSet();
        var allowedScale = scale.Select(s => s.Id).ToHashSet();
        if (ratings.Any(x => !allowedAreas.Contains(x.AreaId)))
            throw BadRequestException.Field("ratings", "One of the areas is not on this list. Reload the page and try again.");
        if (ratings.Any(x => !allowedScale.Contains(x.ScaleId)))
            throw BadRequestException.Field("ratings", "One of the ratings is not on this scale. Reload the page and try again.");

        var tenantId = TenantId;
        // An import's confirm already has a transaction open: join it (it commits), otherwise open our own.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // For a developmental class the ratings are the result, so a locked (or published) term freezes them like scores.
        await db.LockAsync(ScoreService.LockKey(child.Class.Id, term), ct);
        if (await db.TermResultLocks.AnyAsync(l => l.ClassId == child.Class.Id && l.TermId == term, ct))
            throw new ConflictException(ScoreService.TermLockedMessage, "term_locked");
        await db.LockAsync(RatingLockKey(child.Enrolment.Id), ct);
        var ids = ratings.Select(x => x.AreaId).ToList();
        var existing = await db.DevelopmentalRatings.Where(x => x.EnrolmentId == child.Enrolment.Id && x.TermId == term && ids.Contains(x.AreaId)).ToListAsync(ct);
        foreach (var rating in ratings)
        {
            var comment = comments[rating.AreaId];
            var row = existing.FirstOrDefault(x => x.AreaId == rating.AreaId);
            if (row is null)
            {
                db.DevelopmentalRatings.Add(new DevelopmentalRating
                {
                    TenantId = tenantId, EnrolmentId = child.Enrolment.Id, TermId = term, AreaId = rating.AreaId, ScaleId = rating.ScaleId, Comment = comment,
                });
            }
            else if (row.ScaleId != rating.ScaleId || row.Comment != comment)
            {
                row.ScaleId = rating.ScaleId;
                row.Comment = comment;
            }
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("These ratings were saved from another device at the same moment. Reload the page and try again.", "developmental_conflict");
        }
        if (tx is not null) await tx.CommitAsync(ct);
        await ResultStaleness.MarkAsync(db, child.Class.Id, term, ct);
        return await ViewAsync(studentId, term, child.Enrolment.Id, areas, scale, ct);
    }

    private async Task<List<DevelopmentalScale>> ScaleForAsync(Guid? levelId, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        if (levelId is { } id)
        {
            var own = await db.DevelopmentalScales.AsNoTracking().Where(s => s.LevelId == id).OrderBy(s => s.Order).ThenBy(s => s.Label).ToListAsync(ct);
            if (own.Count > 0) return own;
        }
        return await db.DevelopmentalScales.AsNoTracking().Where(s => s.LevelId == null).OrderBy(s => s.Order).ThenBy(s => s.Label).ToListAsync(ct);
    }

    /// <summary>Stores the template the first time a school has no default areas or scale. Safe when two reads arrive together.</summary>
    private async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        if (await HasDefaultAreasAsync(ct) && await HasDefaultScaleAsync(ct)) return;

        var outer = db.Database.CurrentTransaction;
        await using var own = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(SeedLockKey(TenantId), ct);
        var tenantId = TenantId;
        if (!await HasDefaultAreasAsync(ct))
            db.DevelopmentalAreas.AddRange(SchoolTemplates.DevelopmentalAreas.Select((name, i) =>
                new DevelopmentalArea { TenantId = tenantId, Name = name, Order = i + 1 }));
        if (!await HasDefaultScaleAsync(ct))
            db.DevelopmentalScales.AddRange(SchoolTemplates.DevelopmentalScale.Select((point, i) =>
                new DevelopmentalScale { TenantId = tenantId, Code = point.Code, Label = point.Label, Order = i + 1 }));
        if (db.ChangeTracker.Entries<DevelopmentalArea>().Any(e => e.State == EntityState.Added)
            || db.ChangeTracker.Entries<DevelopmentalScale>().Any(e => e.State == EntityState.Added))
            await db.SaveChangesAsync(ct);
        if (own is not null) await own.CommitAsync(ct);
    }

    private Task<bool> HasDefaultAreasAsync(CancellationToken ct) => db.DevelopmentalAreas.AnyAsync(a => a.LevelId == null, ct);
    private Task<bool> HasDefaultScaleAsync(CancellationToken ct) => db.DevelopmentalScales.AnyAsync(s => s.LevelId == null, ct);

    private async Task<bool> LevelHasAreaRatingsAsync(Guid levelId, HashSet<Guid> areaIds, CancellationToken ct)
    {
        if (areaIds.Count == 0) return false;
        return await db.DevelopmentalRatings.AnyAsync(r => areaIds.Contains(r.AreaId)
            && db.Enrolments.Any(e => e.Id == r.EnrolmentId && db.SchoolClasses.Any(c => c.Id == e.ClassId && c.LevelId == levelId)), ct);
    }

    private async Task<bool> LevelHasScaleRatingsAsync(Guid levelId, HashSet<Guid> scaleIds, CancellationToken ct)
    {
        if (scaleIds.Count == 0) return false;
        return await db.DevelopmentalRatings.AnyAsync(r => scaleIds.Contains(r.ScaleId)
            && db.Enrolments.Any(e => e.Id == r.EnrolmentId && db.SchoolClasses.Any(c => c.Id == e.ClassId && c.LevelId == levelId)), ct);
    }

    private async Task<DevelopmentalRatingsDto> ViewAsync(
        Guid studentId, Guid termId, Guid enrolmentId, IReadOnlyList<DevelopmentalArea> areas, IReadOnlyList<DevelopmentalScale> scale, CancellationToken ct)
    {
        var areaIds = areas.Select(a => a.Id).ToList();
        var ratings = await db.DevelopmentalRatings.AsNoTracking()
            .Where(r => r.EnrolmentId == enrolmentId && r.TermId == termId && areaIds.Contains(r.AreaId))
            .ToListAsync(ct);
        var byArea = ratings.ToDictionary(r => r.AreaId);
        var byScale = scale.ToDictionary(s => s.Id);
        var rows = areas.Select(a =>
        {
            if (!byArea.TryGetValue(a.Id, out var rating))
                return new DevelopmentalRatingItemDto(a.Id, a.Name, a.Order, null, null, null, null);
            byScale.TryGetValue(rating.ScaleId, out var point);
            return new DevelopmentalRatingItemDto(a.Id, a.Name, a.Order, rating.ScaleId, point?.Label, point?.Code, rating.Comment);
        }).ToList();
        return new DevelopmentalRatingsDto(studentId, termId, rows);
    }

    private async Task<Child> LoadChildAsync(Guid studentId, Guid termId, CancellationToken ct)
    {
        var termRow = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student");
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.StudentId == studentId && e.SessionId == termRow.SessionId, ct)
            ?? throw new NotFoundException("Student");
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == enrolment.ClassId, ct) ?? throw new NotFoundException("Student");
        if (!access.IsLeadership)
        {
            var staffId = await access.StaffIdAsync(ct);
            if (staffId is null || cls.ClassTeacherStaffId != staffId) throw new NotFoundException("Student");
        }
        return new Child(enrolment, cls);
    }

    private Task<AssessmentMode> ModeAsync(Guid levelId, CancellationToken ct) =>
        db.Levels.AsNoTracking().Where(l => l.Id == levelId).Select(l => l.AssessmentMode).FirstAsync(ct);

    private async Task EnsureLevelAsync(Guid levelId, CancellationToken ct)
    {
        if (!await db.Levels.AnyAsync(l => l.Id == levelId, ct)) throw new NotFoundException("Level");
    }

    private static DevelopmentalAreasDto ToAreas(Guid? levelId, IReadOnlyList<DevelopmentalArea> rows) =>
        new(levelId, rows.Select(a => new DevelopmentalAreaDto(a.Id, a.Name, a.Order)).ToList());

    private static DevelopmentalScaleDto ToScale(Guid? levelId, IReadOnlyList<DevelopmentalScale> rows) =>
        new(levelId, rows.Select(s => new DevelopmentalScaleItemDto(s.Id, s.Code, s.Label, s.Order)).ToList());

    private static string? CleanComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        var text = comment.Trim();
        if (text.Length > MaxCommentLength) throw BadRequestException.Field("comment", $"A comment can have at most {MaxCommentLength} characters.");
        return text;
    }

    private static List<CleanArea> CleanAreas(IReadOnlyList<SaveDevelopmentalArea>? areas)
    {
        if (areas is null || areas.Count is 0 or > MaxItems)
            throw BadRequestException.Field("areas", "Add between 1 and 20 areas.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<CleanArea>(areas.Count);
        foreach (var area in areas)
        {
            var name = (area.Name ?? "").Trim();
            if (name.Length is 0 or > MaxNameLength) throw BadRequestException.Field("areas", "Give every area a name of up to 40 characters.");
            if (!names.Add(name)) throw BadRequestException.Field("areas", $"{name} appears twice. Give each area a different name.");
            cleaned.Add(new CleanArea(area.Id, name));
        }
        return cleaned;
    }

    private static List<CleanScale> CleanScaleItems(IReadOnlyList<SaveDevelopmentalScaleItem>? scale)
    {
        if (scale is null || scale.Count is 0 or > MaxItems)
            throw BadRequestException.Field("scale", "Add between 1 and 20 points on the scale.");
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<CleanScale>(scale.Count);
        foreach (var point in scale)
        {
            var code = (point.Code ?? "").Trim();
            var label = (point.Label ?? "").Trim();
            if (code.Length is 0 or > MaxNameLength || label.Length is 0 or > MaxNameLength)
                throw BadRequestException.Field("scale", "Give every point a code and a label of up to 40 characters.");
            if (!codes.Add(code)) throw BadRequestException.Field("scale", $"{code} appears twice. Give each point a different code.");
            if (!labels.Add(label)) throw BadRequestException.Field("scale", $"{label} appears twice. Give each point a different label.");
            cleaned.Add(new CleanScale(point.Id, code, label));
        }
        return cleaned;
    }

    private sealed record CleanArea(Guid? Id, string Name);
    private sealed record CleanScale(Guid? Id, string Code, string Label);
    private sealed record Child(Enrolment Enrolment, SchoolClass Class);

    private static long SeedLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0DE7E10A;
    private static long RatingLockKey(Guid enrolmentId) => BitConverter.ToInt64(enrolmentId.ToByteArray(), 8) ^ 0x0DE7A7E5;
}
