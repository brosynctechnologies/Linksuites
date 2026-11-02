using Saas.Domain.Payouts;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

public sealed class MediaOptions
{
    public const string Section = "Media";
    /// <summary>Secret for signing video links (32+ characters).</summary>
    public string SigningKey { get; set; } = "";
    public int LinkLifetimeMinutes { get; set; } = 120;
}

/// <summary>
/// Short-lived signed video links. The signature covers the video, the viewer and the expiry, and sits in the path
/// (/api/media/{video}/{viewer}/{expires}/{signature}/{file}), so HLS playlists can use relative segment URLs.
/// </summary>
public sealed class MediaSigner(MediaOptions options, AppUrls urls, TimeProvider clock)
{
    public string Url(Guid videoId, Guid viewerId, string file)
    {
        var expires = clock.GetUtcNow().AddMinutes(options.LinkLifetimeMinutes).ToUnixTimeSeconds();
        return $"{urls.ApiBaseUrl.TrimEnd('/')}/api/media/{videoId:N}/{viewerId:N}/{expires}/{Sign(videoId, viewerId, expires)}/{file}";
    }

    public bool IsValid(Guid videoId, Guid viewerId, long expires, string signature) =>
        expires >= clock.GetUtcNow().ToUnixTimeSeconds()
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(videoId, viewerId, expires)), Encoding.ASCII.GetBytes(signature));

    private string Sign(Guid videoId, Guid viewerId, long expires)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningKey), Encoding.UTF8.GetBytes($"{videoId:N}.{viewerId:N}.{expires}"));
        return Convert.ToBase64String(mac)[..32].Replace('+', '-').Replace('/', '_');
    }
}

public interface ICertificateRenderer
{
    Task<byte[]> RenderAsync(CertificateDto certificate, string brandColor, CancellationToken ct);
}

/// <summary>Rules shared by the studio, catalogue, learning and admin services.</summary>
public sealed class CourseCore(
    IAppDbContext db, ITenantContext tenant, PlanService plans, NotificationQueue notifications, MediaSigner media, AppUrls urls,
    TimeProvider clock, ILogger<CourseCore> logger)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<Academy> AcademyAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        return await db.Academies.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Academy");
    }

    public string AcademyUrl(string slug) => $"{urls.Web(Products.Courses)}/a/{slug}";
    public string CourseUrl(string academySlug, string courseSlug) => $"{urls.Web(Products.Courses)}/courses/{academySlug}/{courseSlug}";
    public string CertificateVerifyUrl(string code) => $"{urls.Web(Products.Courses)}/certificates/{code}";
    public string CertificatePdfUrl(string code) => $"{urls.ApiBaseUrl.TrimEnd('/')}/api/certificates/{code}/pdf";

    /// <summary>Scopes the request to the tenant that owns a course (learner and admin requests start with no tenant).</summary>
    public void UseTenantOf(Guid tenantId)
    {
        if (tenant.TenantId != tenantId) tenant.SetTenant(tenantId);
    }

    public async Task<Course> CourseAcrossTenantsAsync(Guid courseId, CancellationToken ct)
    {
        var course = await db.Courses.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == courseId, ct) ?? throw new NotFoundException("Course");
        UseTenantOf(course.TenantId);
        return course;
    }

    public async Task<long> PlanLimitForTenantAsync(string key, CancellationToken ct) => (await plans.GetCurrentPlanAsync(ct)).Limit(key);

    // Video links

    public PlaybackVideoDto? Playback(Video? video, Guid viewerId) => video is null ? null : new PlaybackVideoDto(
        video.Status, video.Format,
        video.Status != VideoStatus.Ready ? null : media.Url(video.Id, viewerId, video.Format == "hls" ? "master.m3u8" : "video.mp4"),
        video.ThumbnailUrl);

    // Enrolment, completion, certificates

    public async Task<Enrollment> EnrollAsync(Course course, Guid userId, EnrollmentSource source, Guid? orderId, CancellationToken ct)
    {
        UseTenantOf(course.TenantId);
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == course.Id && e.UserId == userId, ct);
        if (enrollment is null)
        {
            enrollment = new Enrollment { UserId = userId, CourseId = course.Id, Source = source, OrderId = orderId };
            db.Enrollments.Add(enrollment);
            await db.Courses.Where(c => c.Id == course.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.StudentsCount, c => c.StudentsCount + 1), ct);
        }
        enrollment.Revoked = false;
        enrollment.OrderId ??= orderId;
        return enrollment;
    }

    /// <summary>Recomputes progress; when every lesson is done (quizzes passed) marks the course complete and issues a certificate.</summary>
    public async Task<ProgressResult> RefreshProgressAsync(Enrollment enrollment, CancellationToken ct)
    {
        var lessons = await db.Lessons.Where(l => l.CourseId == enrollment.CourseId).Select(l => new { l.Id, l.Type }).ToListAsync(ct);
        var completed = await db.LessonProgress.Where(p => p.EnrollmentId == enrollment.Id && p.CompletedAt != null)
            .Select(p => p.LessonId).ToListAsync(ct);
        var done = lessons.Count(l => completed.Contains(l.Id));
        string? certificateCode = await db.Certificates.Where(c => c.EnrollmentId == enrollment.Id).Select(c => c.Code).FirstOrDefaultAsync(ct);
        if (ApplyProgress(enrollment, done, lessons.Count) && certificateCode is null)
            certificateCode = (await IssueCertificatesAsync(enrollment.CourseId, [enrollment], ct)).GetValueOrDefault(enrollment.Id);
        return new ProgressResult(enrollment.ProgressPercent, enrollment.CompletedAt is not null, certificateCode);
    }

    /// <summary>
    /// Recomputes progress and completion for every enrolment in a course, after the tutor adds or deletes lessons,
    /// and issues certificates to anyone that change finishes. A fixed number of queries whatever the course size.
    /// Call it inside the same transaction as the lesson change, after that change is saved.
    /// </summary>
    public async Task RecomputeCourseProgressAsync(Guid courseId, CancellationToken ct)
    {
        var enrollments = await db.Enrollments.Where(e => e.CourseId == courseId && !e.Revoked).ToListAsync(ct);
        if (enrollments.Count == 0) return;
        var total = await db.Lessons.CountAsync(l => l.CourseId == courseId, ct);
        var done = await (from p in db.LessonProgress
                          join l in db.Lessons on p.LessonId equals l.Id
                          where l.CourseId == courseId && p.CompletedAt != null
                          group p by p.EnrollmentId into g
                          select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var finished = enrollments.Where(e => ApplyProgress(e, done.GetValueOrDefault(e.Id), total)).ToList();
        if (finished.Count == 0) return;
        var finishedIds = finished.Select(e => e.Id).ToList();
        var hasCertificate = (await db.Certificates.Where(c => finishedIds.Contains(c.EnrollmentId)).Select(c => c.EnrollmentId).ToListAsync(ct)).ToHashSet();
        await IssueCertificatesAsync(courseId, finished.Where(e => !hasCertificate.Contains(e.Id)).ToList(), ct);
    }

    /// <summary>
    /// Sets the stored percent and completion from live counts. Returns true when this call finished the course.
    /// A course that gains lessons is no longer complete (the certificate already issued stays valid); an empty course
    /// leaves completion as it was.
    /// </summary>
    private bool ApplyProgress(Enrollment enrollment, int done, int total)
    {
        enrollment.ProgressPercent = CourseRules.ProgressPercent(done, total);
        if (total == 0) return false;
        if (done < total)
        {
            enrollment.CompletedAt = null;
            return false;
        }
        if (enrollment.CompletedAt is not null) return false;
        enrollment.CompletedAt = Now;
        return true;
    }

    /// <summary>Certificates (and their emails) for enrolments that just finished, when the plan includes them.</summary>
    private async Task<Dictionary<Guid, string>> IssueCertificatesAsync(Guid courseId, IReadOnlyList<Enrollment> finished, CancellationToken ct)
    {
        var codes = new Dictionary<Guid, string>();
        if (finished.Count == 0 || !await plans.HasFeatureAsync(LimitKeys.Certificates, ct)) return codes;
        var userIds = finished.Select(e => e.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        var title = await db.Courses.Where(c => c.Id == courseId).Select(c => c.Title).FirstAsync(ct);
        foreach (var enrollment in finished)
        {
            var user = users[enrollment.UserId];
            var code = Codes.Random(10);
            codes[enrollment.Id] = code;
            db.Certificates.Add(new Certificate
            {
                EnrollmentId = enrollment.Id, CourseId = courseId, UserId = enrollment.UserId, Code = code,
                StudentName = user.FullName, CourseTitle = title, IssuedAt = Now,
            });
            if (user.Email is not null)
                notifications.Email(user.Email, "course.certificate", new()
                {
                    ["name"] = user.FullName, ["course"] = title, ["link"] = CertificateVerifyUrl(code),
                }, enrollment.TenantId, enrollment.UserId);
        }
        return codes;
    }

    // Orders

    /// <summary>
    /// Marks a paid order, enrols the learner and records the tutor's earning. Idempotent per order, and checks the
    /// amount and currency against what the provider charged.
    /// </summary>
    public async Task<bool> ConfirmOrderAsync(string reference, long amountMinor, string currency, CancellationToken ct)
    {
        var order = await db.CourseOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Reference == reference, ct);
        if (order is null) return false;
        UseTenantOf(order.TenantId);
        if (order.Status == OrderState.Paid) return true;
        if (order.Status != OrderState.Pending) return false;
        if (order.TotalMinor != amountMinor || !string.Equals(order.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Course order {Reference}: charged {Amount} {Currency}, expected {Expected} {ExpectedCurrency}",
                reference, amountMinor, currency, order.TotalMinor, order.Currency);
            order.Status = OrderState.Failed;
            await db.SaveChangesAsync(ct);
            return false;
        }

        var course = await db.Courses.FirstAsync(c => c.Id == order.CourseId, ct);
        order.Status = OrderState.Paid;
        order.PaidAt = Now;
        await EnrollAsync(course, order.UserId, order.CouponCode is null ? EnrollmentSource.Purchase : EnrollmentSource.Coupon, order.Id, ct);
        if (order.CouponCode is { } code)
            await db.CourseCoupons.Where(c => c.Code == code).ExecuteUpdateAsync(s => s.SetProperty(c => c.Uses, c => c.Uses + 1), ct);
        var (pct, fee, net) = CourseRules.Split(order.TotalMinor, order.Channel, await PlanLimitForTenantAsync(LimitKeys.AcademyFeePercent, ct));
        db.Earnings.Add(new Earning
        {
            Source = EarningSource.CourseOrder, SourceId = order.Id, Description = course.Title, CourseId = course.Id, UserId = order.UserId, Channel = order.Channel, GrossMinor = order.TotalMinor,
            FeePercent = pct, FeeMinor = fee, NetMinor = net, AvailableAt = Now.Add(CourseRules.RefundWindow),
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The return page and the webhook can race; a unique index on earnings per order makes the second one fail.
            logger.LogInformation(ex, "Course order {Reference} was already confirmed", reference);
        }
        return true;
    }
}

public sealed class CoursesProvisioner(IAppDbContext db) : Auth.IProductProvisioner
{
    public string Product => Products.Courses;

    public Task ProvisionAsync(Tenant tenant, Auth.SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        db.Academies.Add(new Academy
        {
            Name = tenant.Name, Slug = tenant.Slug, Currency = tenant.Currency,
            Headline = $"Courses by {tenant.Name}",
        });
        return Task.CompletedTask;
    }
}

public sealed class CoursesUsage(IAppDbContext db) : IUsageProvider
{
    public string Product => Products.Courses;

    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        var courses = await db.Courses.CountAsync(c => c.Status != CourseStatus.Archived, ct);
        var bytes = await db.Videos.SumAsync(v => (long?)v.SourceSizeBytes, ct) ?? 0;
        bytes += await db.LessonResources.SumAsync(r => (long?)r.SizeBytes, ct) ?? 0;
        return
        [
            new(LimitKeys.Courses, "Courses", courses, plan.Limit(LimitKeys.Courses)),
            new(LimitKeys.VideoStorageMb, "Storage (MB)", bytes / (1024 * 1024), plan.Limit(LimitKeys.VideoStorageMb)),
        ];
    }
}
