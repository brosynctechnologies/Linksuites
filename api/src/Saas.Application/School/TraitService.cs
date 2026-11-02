using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record TraitCriterionDto(Guid Id, string Name, int Order);
public sealed record TraitCriteriaDto(Guid? LevelId, TraitKind Kind, IReadOnlyList<TraitCriterionDto> Criteria);
public sealed record SaveTraitCriterion(Guid? Id, string? Name);
public sealed record SaveTraitCriteriaRequest(IReadOnlyList<SaveTraitCriterion>? Criteria);

public sealed record TraitRatingItemDto(Guid CriterionId, string Name, int Order, int? Value);
public sealed record TraitRatingsDto(Guid StudentId, Guid TermId, TraitKind Kind, IReadOnlyList<TraitRatingItemDto> Ratings);
public sealed record SaveTraitRating(Guid CriterionId, int Value);
public sealed record SaveTraitRatingsRequest(IReadOnlyList<SaveTraitRating>? Ratings);

/// <summary>
/// Psychomotor and affective criteria, and the class teacher's 1–5 ratings. Ratings are not scores: saving one does not
/// change <see cref="Score"/> or <see cref="TermResult.Total"/>, and it stays allowed after the term is published.
/// A level with its own rows overrides the school default for that kind, the same way an assessment scheme does.
/// </summary>
public sealed class TraitService(IAppDbContext db, ITenantContext tenant, SchoolAccess access, IDbErrors dbErrors)
{
    public const int MaxNameLength = 40;
    public const int MaxCriteria = 20;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>The items for one kind. A level with its own rows is returned as that level; otherwise the school default.</summary>
    public async Task<TraitCriteriaDto> GetCriteriaAsync(TraitKind? kind, Guid? levelId, CancellationToken ct)
    {
        var traitKind = RequireKind(kind);
        if (levelId is { } id) await EnsureLevelAsync(id, ct);
        var rows = await CriteriaForAsync(levelId, traitKind, ct);
        var showingOverride = levelId is not null && rows.Count > 0 && rows[0].LevelId == levelId;
        return ToDto(showingOverride ? levelId : null, traitKind, rows);
    }

    /// <summary>Leadership replaces one kind. A level id writes that level's override and leaves the school default as it is.</summary>
    public async Task<TraitCriteriaDto> SaveCriteriaAsync(TraitKind? kind, Guid? levelId, SaveTraitCriteriaRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can change the trait list.");
        var traitKind = RequireKind(kind);
        if (levelId is { } id) await EnsureLevelAsync(id, ct);
        var inputs = Clean(r.Criteria);

        var tenantId = TenantId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(SeedLockKey(tenantId), ct);
        await EnsureDefaultsAsync(ct);

        var own = await db.TraitCriteria.Where(c => c.LevelId == levelId && c.Kind == traitKind).OrderBy(c => c.Order).ToListAsync(ct);
        var creatingOverride = levelId is not null && own.Count == 0;
        var effective = creatingOverride
            ? await db.TraitCriteria.Where(c => c.LevelId == null && c.Kind == traitKind).OrderBy(c => c.Order).ToListAsync(ct)
            : own;
        var effectiveIds = effective.Select(c => c.Id).ToHashSet();
        if (inputs.Any(c => c.Id is { } given && !effectiveIds.Contains(given)))
            throw BadRequestException.Field("criteria", "One of the items is not part of this list. Reload the page and try again.");
        var givenIds = inputs.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToList();
        if (givenIds.Count != givenIds.Distinct().Count())
            throw BadRequestException.Field("criteria", "Each item can appear once.");

        if (creatingOverride)
        {
            if (await LevelHasRatingsAsync(levelId!.Value, effectiveIds, ct))
                throw new ConflictException("Teachers have already rated these items for this level. The level keeps the school's list.", "trait_has_ratings");
        }
        else
        {
            var removed = own.Where(c => inputs.All(i => i.Id != c.Id)).Select(c => c.Id).ToList();
            if (removed.Count > 0 && await db.TraitRatings.AnyAsync(x => removed.Contains(x.CriterionId), ct))
                throw new ConflictException("Teachers have already rated one of these items. You can rename it, but not remove it.", "trait_has_ratings");
        }

        var saved = new List<TraitCriterion>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var row = !creatingOverride && inputs[i].Id is { } existing ? own.First(c => c.Id == existing) : null;
            if (row is not null)
            {
                row.Name = inputs[i].Name;
                row.Order = i + 1;
            }
            else
            {
                row = new TraitCriterion { TenantId = tenantId, LevelId = levelId, Kind = traitKind, Name = inputs[i].Name, Order = i + 1 };
                db.TraitCriteria.Add(row);
            }
            saved.Add(row);
        }
        var kept = saved.Select(c => c.Id).ToHashSet();
        db.TraitCriteria.RemoveRange(own.Where(c => !kept.Contains(c.Id)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(levelId, traitKind, saved);
    }

    /// <summary>Ratings for a child the caller has already been allowed to read. Seeds the list. Does not check who is asking.</summary>
    public async Task<IReadOnlyList<TraitRatingItemDto>> RatingsForAsync(Guid levelId, Guid enrolmentId, Guid studentId, Guid termId, TraitKind kind, CancellationToken ct)
    {
        var criteria = await CriteriaForAsync(levelId, kind, ct);
        return (await ViewAsync(studentId, termId, kind, enrolmentId, criteria, ct)).Ratings;
    }

    /// <summary>The class teacher of the child's class, or leadership, reads the ratings. The bursar is refused before the student is looked up.</summary>
    public async Task<TraitRatingsDto> GetRatingsAsync(Guid studentId, Guid? termId, TraitKind? kind, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not rate traits.");
        var traitKind = RequireKind(kind);
        if (termId is not { } term) throw BadRequestException.Field("termId", "Choose a term.");
        var child = await LoadChildAsync(studentId, term, ct);
        var criteria = await CriteriaForAsync(child.Class.LevelId, traitKind, ct);
        return await ViewAsync(studentId, term, traitKind, child.Enrolment.Id, criteria, ct);
    }

    /// <summary>
    /// Saves the ratings that were sent and leaves the others as they are. A value outside 1 to 5 is refused.
    /// This does not write a score or a term total, and published results do not freeze it.
    /// </summary>
    public async Task<TraitRatingsDto> SaveRatingsAsync(Guid studentId, Guid? termId, TraitKind? kind, SaveTraitRatingsRequest r, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not rate traits.");
        var traitKind = RequireKind(kind);
        if (termId is not { } term) throw BadRequestException.Field("termId", "Choose a term.");
        var child = await LoadChildAsync(studentId, term, ct);

        var ratings = r.Ratings ?? throw BadRequestException.Field("ratings", "Send at least one rating.");
        if (ratings.Count == 0) throw BadRequestException.Field("ratings", "Send at least one rating.");
        if (ratings.Any(x => x.Value is < 1 or > 5))
            throw BadRequestException.Field("value", "Rate each item from 1 to 5.");
        if (ratings.Select(x => x.CriterionId).Distinct().Count() != ratings.Count)
            throw BadRequestException.Field("ratings", "Each item can be rated once.");

        var criteria = await CriteriaForAsync(child.Class.LevelId, traitKind, ct);
        var allowed = criteria.Select(c => c.Id).ToHashSet();
        if (ratings.Any(x => !allowed.Contains(x.CriterionId)))
            throw BadRequestException.Field("ratings", "One of the items is not on this list. Reload the page and try again.");

        var tenantId = TenantId;
        // An import's confirm already has a transaction open: join it (it commits), otherwise open our own.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(RatingLockKey(child.Enrolment.Id), ct);
        var ids = ratings.Select(x => x.CriterionId).ToList();
        var existing = await db.TraitRatings.Where(x => x.EnrolmentId == child.Enrolment.Id && x.TermId == term && ids.Contains(x.CriterionId)).ToListAsync(ct);
        foreach (var rating in ratings)
        {
            var row = existing.FirstOrDefault(x => x.CriterionId == rating.CriterionId);
            if (row is null)
            {
                db.TraitRatings.Add(new TraitRating
                {
                    TenantId = tenantId, EnrolmentId = child.Enrolment.Id, TermId = term, CriterionId = rating.CriterionId, Value = rating.Value,
                });
            }
            else if (row.Value != rating.Value)
            {
                row.Value = rating.Value;
            }
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("These ratings were saved from another device at the same moment. Reload the page and try again.", "traits_conflict");
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return await ViewAsync(studentId, term, traitKind, child.Enrolment.Id, criteria, ct);
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

    private async Task<List<TraitCriterion>> CriteriaForAsync(Guid? levelId, TraitKind kind, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        if (levelId is { } id)
        {
            var own = await db.TraitCriteria.AsNoTracking().Where(c => c.LevelId == id && c.Kind == kind).OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync(ct);
            if (own.Count > 0) return own;
        }
        return await db.TraitCriteria.AsNoTracking().Where(c => c.LevelId == null && c.Kind == kind).OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync(ct);
    }

    /// <summary>Stores the template the first time a school has no default row for a kind. Safe when two reads arrive together.</summary>
    private async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        if (await HasDefaultAsync(TraitKind.Psychomotor, ct) && await HasDefaultAsync(TraitKind.Affective, ct)) return;

        var outer = db.Database.CurrentTransaction;
        await using var own = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(SeedLockKey(TenantId), ct);
        var tenantId = TenantId;
        if (!await HasDefaultAsync(TraitKind.Psychomotor, ct))
            db.TraitCriteria.AddRange(Rows(tenantId, TraitKind.Psychomotor, SchoolTemplates.Psychomotor));
        if (!await HasDefaultAsync(TraitKind.Affective, ct))
            db.TraitCriteria.AddRange(Rows(tenantId, TraitKind.Affective, SchoolTemplates.Affective));
        if (db.ChangeTracker.Entries<TraitCriterion>().Any(e => e.State == EntityState.Added))
            await db.SaveChangesAsync(ct);
        if (own is not null) await own.CommitAsync(ct);
    }

    private Task<bool> HasDefaultAsync(TraitKind kind, CancellationToken ct) =>
        db.TraitCriteria.AnyAsync(c => c.LevelId == null && c.Kind == kind, ct);

    private static IEnumerable<TraitCriterion> Rows(Guid tenantId, TraitKind kind, IReadOnlyList<string> names) =>
        names.Select((name, i) => new TraitCriterion { TenantId = tenantId, Kind = kind, Name = name, Order = i + 1 });

    private async Task<bool> LevelHasRatingsAsync(Guid levelId, HashSet<Guid> criterionIds, CancellationToken ct)
    {
        if (criterionIds.Count == 0) return false;
        return await db.TraitRatings.AnyAsync(r => criterionIds.Contains(r.CriterionId)
            && db.Enrolments.Any(e => e.Id == r.EnrolmentId && db.SchoolClasses.Any(c => c.Id == e.ClassId && c.LevelId == levelId)), ct);
    }

    private async Task<TraitRatingsDto> ViewAsync(Guid studentId, Guid termId, TraitKind kind, Guid enrolmentId, IReadOnlyList<TraitCriterion> criteria, CancellationToken ct)
    {
        var ids = criteria.Select(c => c.Id).ToList();
        var values = await db.TraitRatings.AsNoTracking()
            .Where(r => r.EnrolmentId == enrolmentId && r.TermId == termId && ids.Contains(r.CriterionId))
            .ToDictionaryAsync(r => r.CriterionId, r => r.Value, ct);
        var ratings = criteria.Select(c => new TraitRatingItemDto(c.Id, c.Name, c.Order, values.TryGetValue(c.Id, out var value) ? value : null)).ToList();
        return new TraitRatingsDto(studentId, termId, kind, ratings);
    }

    private static TraitCriteriaDto ToDto(Guid? levelId, TraitKind kind, IReadOnlyList<TraitCriterion> rows) =>
        new(levelId, kind, rows.Select(c => new TraitCriterionDto(c.Id, c.Name, c.Order)).ToList());

    private static TraitKind RequireKind(TraitKind? kind) =>
        kind ?? throw BadRequestException.Field("kind", "Choose Psychomotor or Affective.");

    private async Task EnsureLevelAsync(Guid levelId, CancellationToken ct)
    {
        if (!await db.Levels.AnyAsync(l => l.Id == levelId, ct)) throw new NotFoundException("Level");
    }

    private static List<CleanCriterion> Clean(IReadOnlyList<SaveTraitCriterion>? criteria)
    {
        if (criteria is null || criteria.Count is 0 or > MaxCriteria)
            throw BadRequestException.Field("criteria", "Add between 1 and 20 items.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<CleanCriterion>(criteria.Count);
        foreach (var criterion in criteria)
        {
            var name = (criterion.Name ?? "").Trim();
            if (name.Length is 0 or > MaxNameLength) throw BadRequestException.Field("criteria", "Give every item a name of up to 40 characters.");
            if (!names.Add(name)) throw BadRequestException.Field("criteria", $"{name} appears twice. Give each item a different name.");
            cleaned.Add(new CleanCriterion(criterion.Id, name));
        }
        return cleaned;
    }

    private sealed record CleanCriterion(Guid? Id, string Name);
    private sealed record Child(Enrolment Enrolment, SchoolClass Class);

    private static long SeedLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x7A17C0DE;
    private static long RatingLockKey(Guid enrolmentId) => BitConverter.ToInt64(enrolmentId.ToByteArray(), 8) ^ 0x7A17A7E5;
}
