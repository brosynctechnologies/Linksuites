using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School;

/// <summary>
/// Marks results stale so they are rebuilt before they are next viewed or published. Published results are frozen and never marked:
/// they change only after an unpublish.
/// </summary>
internal static class ResultStaleness
{
    /// <summary>One class's results for one term (scores or the lock changed).</summary>
    public static Task MarkAsync(IAppDbContext db, Guid classId, Guid termId, CancellationToken ct) =>
        db.TermResults.Where(r => r.ClassId == classId && r.TermId == termId && r.PublishedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stale, true), ct);

    /// <summary>One class's results for the term that contains the date (a register changed).</summary>
    public static Task MarkForDateAsync(IAppDbContext db, Guid classId, DateOnly date, CancellationToken ct) =>
        db.TermResults.Where(r => r.ClassId == classId && r.PublishedAt == null
                && db.Terms.Any(t => t.Id == r.TermId && t.StartDate <= date && t.EndDate >= date))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stale, true), ct);

    /// <summary>Every unpublished result of the level's classes (its assessment mode changed).</summary>
    public static Task MarkLevelModeAsync(IAppDbContext db, Guid levelId, CancellationToken ct) =>
        db.TermResults.Where(r => r.PublishedAt == null && db.SchoolClasses.Any(c => c.Id == r.ClassId && c.LevelId == levelId))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stale, true), ct);

    /// <summary>
    /// Every class of the current session whose grading scale changed: the level's classes, or, for the school default (null), the
    /// classes of every level that has no scale of its own.
    /// </summary>
    public static Task MarkLevelAsync(IAppDbContext db, Guid? levelId, CancellationToken ct) =>
        db.TermResults.Where(r => r.PublishedAt == null && db.SchoolClasses.Any(c => c.Id == r.ClassId
                && db.AcademicSessions.Any(s => s.Id == c.SessionId && s.IsCurrent)
                && (levelId != null ? c.LevelId == levelId : !db.GradingBands.Any(b => b.LevelId == c.LevelId))))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stale, true), ct);
}
