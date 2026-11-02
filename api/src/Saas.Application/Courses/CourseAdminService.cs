using Saas.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

/// <summary>For the people who run the platform: marketplace review, categories and an overview (payouts: PayoutAdminService). Works across tenants.</summary>
public sealed class CourseAdminService(IAppDbContext db, CourseCore core, NotificationQueue notifications)
{
    public async Task<AdminOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var monthStart = new DateTimeOffset(core.Now.Year, core.Now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var earnings = await db.Earnings.IgnoreQueryFilters().Where(e => e.Source == EarningSource.CourseOrder && !e.Reversed && e.CreatedAt >= monthStart)
            .Select(e => new { e.GrossMinor, e.FeeMinor }).ToListAsync(ct);
        return new AdminOverviewDto(
            await db.Academies.IgnoreQueryFilters().CountAsync(ct),
            await db.Courses.IgnoreQueryFilters().CountAsync(c => c.Status == CourseStatus.Published, ct),
            await db.Enrollments.IgnoreQueryFilters().Select(e => e.UserId).Distinct().CountAsync(ct),
            earnings.Sum(e => e.GrossMinor), earnings.Sum(e => e.FeeMinor),
            await db.Courses.IgnoreQueryFilters().CountAsync(c => c.MarketplaceStatus == MarketplaceStatus.InReview, ct),
            await db.Payouts.IgnoreQueryFilters().CountAsync(p => p.Status == PayoutStatus.Requested, ct));
    }

    public async Task<IReadOnlyList<CourseReviewItemDto>> ReviewQueueAsync(CancellationToken ct)
    {
        var courses = await db.Courses.IgnoreQueryFilters().AsNoTracking().Where(c => c.MarketplaceStatus == MarketplaceStatus.InReview)
            .OrderBy(c => c.SubmittedAt).ToListAsync(ct);
        var tenantIds = courses.Select(c => c.TenantId).Distinct().ToList();
        var academies = await db.Academies.IgnoreQueryFilters().AsNoTracking().Where(a => tenantIds.Contains(a.TenantId)).ToDictionaryAsync(a => a.TenantId, ct);
        var ids = courses.Select(c => c.Id).ToList();
        var lessons = await db.Lessons.IgnoreQueryFilters().Where(l => ids.Contains(l.CourseId)).GroupBy(l => l.CourseId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return courses.Select(c => new CourseReviewItemDto(c.Id, c.Title, academies[c.TenantId].Name, c.SubmittedAt, lessons.GetValueOrDefault(c.Id),
            c.PriceMinor, core.CourseUrl(academies[c.TenantId].Slug, c.Slug))).ToList();
    }

    public async Task DecideAsync(Guid courseId, ReviewDecision r, CancellationToken ct)
    {
        var course = await core.CourseAcrossTenantsAsync(courseId, ct);
        var tracked = await db.Courses.FirstAsync(c => c.Id == course.Id, ct);
        if (tracked.MarketplaceStatus != MarketplaceStatus.InReview) throw new ConflictException("This course isn't waiting for review.");
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) throw BadRequestException.Field("note", "Tell the tutor what to change.");
        tracked.MarketplaceStatus = r.Approve ? MarketplaceStatus.Approved : MarketplaceStatus.Rejected;
        tracked.MarketplaceNote = r.Approve ? null : r.Note!.Trim();
        var owner = await db.Memberships.Where(m => m.Role == MemberRole.Owner).Select(m => m.User!.Email).FirstOrDefaultAsync(ct);
        if (owner is not null)
            notifications.Email(owner, r.Approve ? "course.approved" : "course.rejected", new()
            {
                ["course"] = tracked.Title, ["note"] = tracked.MarketplaceNote ?? "",
            }, tracked.TenantId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct) =>
        await db.CourseCategories.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId)).ToListAsync(ct);

    public async Task<CategoryDto> SaveCategoryAsync(Guid? id, SaveCategoryRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 60) throw BadRequestException.Field("name", "Enter a name of up to 60 characters.");
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? Slugs.From(r.Name) : Slugs.From(r.Slug);
        if (await db.CourseCategories.AnyAsync(c => c.Slug == slug && c.Id != id, ct)) throw BadRequestException.Field("slug", "That category link is taken.");
        if (r.ParentId == id && id is not null) throw BadRequestException.Field("parentId", "A category can't be its own parent.");
        CourseCategory category;
        if (id is null)
        {
            category = new CourseCategory { Name = r.Name.Trim(), Slug = slug };
            db.CourseCategories.Add(category);
        }
        else
        {
            category = await db.CourseCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category");
        }
        category.Name = r.Name.Trim();
        category.Slug = slug;
        category.ParentId = r.ParentId;
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Slug, category.ParentId);
    }


}
