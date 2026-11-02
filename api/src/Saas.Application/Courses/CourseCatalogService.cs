using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

/// <summary>What anyone can see: the marketplace, academy pages, course pages, previews and certificate checks.</summary>
public sealed class CourseCatalogService(IAppDbContext db, CourseCore core, PlanService plans, ICurrentUser user, ICertificateRenderer certificates)
{
    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct) =>
        await db.CourseCategories.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId)).ToListAsync(ct);

    /// <summary>The marketplace: published courses that their tutor listed and an admin approved.</summary>
    public async Task<Paged<CourseCardDto>> SearchAsync(string? q, string? category, CourseLevel? level, string? price, string? sort,
        int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 60);
        var query = Live().Where(c => c.ListOnMarketplace && c.MarketplaceStatus == MarketplaceStatus.Approved);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c => c.Title.ToLower().Contains(term) || (c.Subtitle != null && c.Subtitle.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = await db.CourseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == category, ct);
            if (cat is null) return new Paged<CourseCardDto>([], page, pageSize, 0);
            var ids = await db.CourseCategories.Where(c => c.Id == cat.Id || c.ParentId == cat.Id).Select(c => c.Id).ToListAsync(ct);
            query = query.Where(c => c.CategoryId != null && ids.Contains(c.CategoryId.Value));
        }
        if (level is { } lv) query = query.Where(c => c.Level == lv);
        if (price == "free") query = query.Where(c => c.IsFree);
        if (price == "paid") query = query.Where(c => !c.IsFree);
        query = sort switch
        {
            "newest" => query.OrderByDescending(c => c.PublishedAt),
            "rating" => query.OrderByDescending(c => c.RatingAverage).ThenByDescending(c => c.RatingCount),
            "price" => query.OrderBy(c => c.PriceMinor),
            _ => query.OrderByDescending(c => c.StudentsCount).ThenByDescending(c => c.RatingAverage),
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<CourseCardDto>(await CardsAsync(rows, ct), page, pageSize, total);
    }

    public async Task<AcademyPageDto> AcademyAsync(string slug, CancellationToken ct)
    {
        var academy = await db.Academies.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug.ToLowerInvariant() && db.Tenants.Any(t => t.Id == a.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Academy");
        core.UseTenantOf(academy.TenantId);
        var courses = await Live().Where(c => c.TenantId == academy.TenantId).OrderByDescending(c => c.StudentsCount).ToListAsync(ct);
        return new AcademyPageDto(academy.Name, academy.Slug, academy.Headline, academy.Bio, academy.LogoUrl, academy.BrandColor,
            !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct), await CardsAsync(courses, ct));
    }

    public async Task<CourseDetailDto> CourseAsync(string academySlug, string courseSlug, CancellationToken ct)
    {
        var (course, academy) = await ResolveAsync(academySlug, courseSlug, ct);
        var card = (await CardsAsync([course], ct))[0];
        var sections = await db.CourseSections.AsNoTracking().Include(s => s.Lessons).Where(s => s.CourseId == course.Id).OrderBy(s => s.Position).ToListAsync(ct);
        var category = course.CategoryId is { } cid ? await db.CourseCategories.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var enrolled = user.UserId is { } uid && await db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.UserId == uid && !e.Revoked, ct);
        return new CourseDetailDto(card.Id, card.Slug, card.AcademySlug, card.AcademyName, card.Title, card.Subtitle, card.ThumbnailUrl, card.PriceMinor,
            card.Currency, card.IsFree, card.Level, card.RatingAverage, card.RatingCount, card.StudentsCount, card.LessonsCount, card.DurationSec,
            category, course.Description, course.Language, course.Outcomes, course.Requirements, course.UpdatedAt ?? course.CreatedAt,
            new TutorDto(academy.Name, academy.Headline, academy.Bio, academy.LogoUrl), academy.BrandColor,
            sections.Select(s => new PublicSectionDto(s.Id, s.Title, s.Lessons.OrderBy(l => l.Position)
                .Select(l => new PublicLessonDto(l.Id, l.Title, l.Type, l.DurationSec, l.IsPreview)).ToList())).ToList(), enrolled);
    }

    public async Task<Paged<PublicReviewDto>> ReviewsAsync(string academySlug, string courseSlug, int page, CancellationToken ct)
    {
        var (course, _) = await ResolveAsync(academySlug, courseSlug, ct);
        page = Math.Max(1, page);
        var query = db.CourseReviews.AsNoTracking().Where(r => r.CourseId == course.Id);
        var total = await query.CountAsync(ct);
        var rows = await (from r in query join u in db.Users on r.UserId equals u.Id
                          orderby r.CreatedAt descending
                          select new { r.Rating, r.Comment, r.CreatedAt, u.FullName }).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new Paged<PublicReviewDto>(rows.Select(r => new PublicReviewDto(FirstNameAndInitial(r.FullName), r.Rating, r.Comment, r.CreatedAt)).ToList(),
            page, 20, total);
    }

    /// <summary>Lessons marked as free previews can be played by anyone.</summary>
    public async Task<LessonPlaybackDto> PreviewAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await db.Lessons.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == lessonId && l.IsPreview && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Preview");
        core.UseTenantOf(lesson.TenantId);
        var course = await db.Courses.AsNoTracking().FirstAsync(c => c.Id == lesson.CourseId, ct);
        if (course.Status != CourseStatus.Published) throw new NotFoundException("Preview");
        var video = await db.Videos.AsNoTracking().FirstOrDefaultAsync(v => v.LessonId == lesson.Id, ct);
        return new LessonPlaybackDto(lesson.Id, lesson.CourseId, lesson.Title, lesson.Type, lesson.Content, lesson.DurationSec,
            core.Playback(video, Guid.Empty), [], null, 0, false, null, null, null);
    }

    public async Task<CertificateDto> CertificateAsync(string code, CancellationToken ct)
    {
        var (dto, _) = await CertificateWithBrandAsync(code, ct);
        return dto;
    }

    public async Task<(byte[] Pdf, string FileName)> CertificatePdfAsync(string code, CancellationToken ct)
    {
        var (dto, brand) = await CertificateWithBrandAsync(code, ct);
        return (await certificates.RenderAsync(dto, brand, ct), $"certificate-{dto.Code}.pdf");
    }

    private async Task<(CertificateDto, string)> CertificateWithBrandAsync(string code, CancellationToken ct)
    {
        var cert = await db.Certificates.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code.Trim().ToUpperInvariant(), ct)
            ?? throw new NotFoundException("Certificate");
        core.UseTenantOf(cert.TenantId);
        var academy = await db.Academies.AsNoTracking().FirstAsync(ct);
        var tutor = await db.Memberships.Where(m => m.Role == MemberRole.Owner).Select(m => m.User!.FullName).FirstOrDefaultAsync(ct) ?? academy.Name;
        return (new CertificateDto(cert.Code, cert.StudentName, cert.CourseTitle, tutor, academy.Name, cert.IssuedAt), academy.BrandColor);
    }

    // Helpers

    /// <summary>Published courses of active tenants, across all academies.</summary>
    private IQueryable<Course> Live() => db.Courses.IgnoreQueryFilters().AsNoTracking()
        .Where(c => c.Status == CourseStatus.Published && db.Tenants.Any(t => t.Id == c.TenantId && t.Status == TenantStatus.Active));

    private async Task<(Course, Academy)> ResolveAsync(string academySlug, string courseSlug, CancellationToken ct)
    {
        var academy = await db.Academies.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Slug == academySlug.ToLowerInvariant() && db.Tenants.Any(t => t.Id == a.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Course");
        core.UseTenantOf(academy.TenantId);
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == courseSlug.ToLowerInvariant() && c.Status == CourseStatus.Published, ct)
            ?? throw new NotFoundException("Course");
        return (course, academy);
    }

    public async Task<IReadOnlyList<CourseCardDto>> CardsAsync(IReadOnlyList<Course> courses, CancellationToken ct)
    {
        var ids = courses.Select(c => c.Id).ToList();
        var tenantIds = courses.Select(c => c.TenantId).Distinct().ToList();
        var academies = await db.Academies.IgnoreQueryFilters().AsNoTracking().Where(a => tenantIds.Contains(a.TenantId)).ToDictionaryAsync(a => a.TenantId, ct);
        var stats = await db.Lessons.IgnoreQueryFilters().Where(l => ids.Contains(l.CourseId)).GroupBy(l => l.CourseId)
            .Select(g => new { g.Key, Count = g.Count(), Duration = g.Sum(l => l.DurationSec) }).ToDictionaryAsync(x => x.Key, ct);
        var categories = await db.CourseCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return courses.Select(c =>
        {
            var a = academies[c.TenantId];
            var s = stats.GetValueOrDefault(c.Id);
            return new CourseCardDto(c.Id, c.Slug, a.Slug, a.Name, c.Title, c.Subtitle, c.ThumbnailUrl, c.PriceMinor, c.Currency, c.IsFree, c.Level,
                Math.Round(c.RatingAverage, 1), c.RatingCount, c.StudentsCount, s?.Count ?? 0, s?.Duration ?? 0,
                c.CategoryId is { } cid ? categories.GetValueOrDefault(cid) : null);
        }).ToList();
    }

    public static string FirstNameAndInitial(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => "Learner", 1 => parts[0], _ => $"{parts[0]} {parts[^1][0]}." };
    }
}
