using Saas.Application.Payouts;
using Saas.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

/// <summary>The tutor's studio: academy settings, courses, curriculum, lessons, video, files and quizzes.</summary>
public sealed class StudioService(IAppDbContext db, CourseCore core, PlanService plans, IFileStorage storage, ICurrentUser user)
{
    public const long MaxVideoBytes = 2L * 1024 * 1024 * 1024;
    public const long MaxResourceBytes = 50L * 1024 * 1024;
    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".webm", ".mkv", ".m4v"];

    // Academy

    public async Task<AcademyDto> GetAcademyAsync(CancellationToken ct) => ToDto(await core.AcademyAsync(ct));

    public async Task<AcademyDto> UpdateAcademyAsync(UpdateAcademyRequest r, CancellationToken ct)
    {
        var a = await core.AcademyAsync(ct);
        var slug = r.Slug.Trim().ToLowerInvariant();
        Slugs.EnsureNotNewlyReserved(slug, a.Slug);
        if (slug != a.Slug && await db.Academies.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug, ct))
            throw BadRequestException.Field("slug", "That academy link is already taken.");
        a.Name = r.Name.Trim();
        a.Slug = slug;
        a.Headline = Clean(r.Headline);
        a.Bio = Clean(r.Bio);
        a.BrandColor = r.BrandColor;
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<AcademyDto> UploadLogoAsync(Upload upload, CancellationToken ct)
    {
        var a = await core.AcademyAsync(ct);
        a.LogoUrl = await SaveImageAsync(upload, $"{a.TenantId}/academy/logo", ct);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<AcademyDto> SetPayoutAccountAsync(PayoutAccountDto r, CancellationToken ct)
    {
        var a = await core.AcademyAsync(ct);
        a.PayoutAccount = new PayoutAccount { BankName = r.BankName.Trim(), AccountNumber = r.AccountNumber.Trim(), AccountName = r.AccountName.Trim() };
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    // Courses

    public async Task<IReadOnlyList<StudioCourseSummaryDto>> ListAsync(CancellationToken ct)
    {
        var courses = await db.Courses.AsNoTracking().OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt).ToListAsync(ct);
        var ids = courses.Select(c => c.Id).ToList();
        var lessons = await db.Lessons.Where(l => ids.Contains(l.CourseId)).GroupBy(l => l.CourseId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var revenue = await db.Earnings.Where(e => e.Source == EarningSource.CourseOrder && !e.Reversed && e.CourseId != null).GroupBy(e => e.CourseId!.Value)
            .Select(g => new { g.Key, Net = g.Sum(e => e.NetMinor) }).ToDictionaryAsync(x => x.Key, x => x.Net, ct);
        return courses.Select(c => new StudioCourseSummaryDto(c.Id, c.Title, c.Slug, c.ThumbnailUrl, c.Status, c.MarketplaceStatus, c.PriceMinor,
            c.IsFree, lessons.GetValueOrDefault(c.Id), c.StudentsCount, c.RatingAverage, c.RatingCount, revenue.GetValueOrDefault(c.Id),
            c.UpdatedAt ?? c.CreatedAt)).ToList();
    }

    public async Task<StudioCourseDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadCourseAsync(id, ct), ct);

    public async Task<StudioCourseDto> SaveAsync(Guid? id, SaveCourseRequest r, CancellationToken ct)
    {
        if (r.CategoryId is { } cat && !await db.CourseCategories.AnyAsync(c => c.Id == cat, ct))
            throw BadRequestException.Field("categoryId", "That category does not exist.");
        var academy = await core.AcademyAsync(ct);
        Course course;
        if (id is null)
        {
            await plans.EnsureCanAddAsync(LimitKeys.Courses, await db.Courses.CountAsync(c => c.Status != CourseStatus.Archived, ct), "Courses", ct);
            course = new Course { Title = r.Title.Trim(), Slug = await UniqueSlugAsync(r.Title, null, ct), Currency = academy.Currency };
            db.Courses.Add(course);
            course.Sections.Add(new CourseSection { CourseId = course.Id, Title = "Getting started", Position = 0 });
        }
        else
        {
            course = await LoadCourseAsync(id.Value, ct);
            if (!string.Equals(course.Title, r.Title.Trim(), StringComparison.Ordinal) && course.Status == CourseStatus.Draft)
                course.Slug = await UniqueSlugAsync(r.Title, course.Id, ct); // links only change while unpublished
        }
        course.Title = r.Title.Trim();
        course.Subtitle = Clean(r.Subtitle);
        course.Description = Clean(r.Description);
        course.CategoryId = r.CategoryId;
        course.Level = r.Level;
        course.Language = string.IsNullOrWhiteSpace(r.Language) ? "English" : r.Language.Trim();
        course.IsFree = r.IsFree;
        course.PriceMinor = r.IsFree ? 0 : r.PriceMinor;
        course.Outcomes = (r.Outcomes ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        course.Requirements = (r.Requirements ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        course.ListOnMarketplace = r.ListOnMarketplace;
        if (!r.ListOnMarketplace) course.MarketplaceStatus = MarketplaceStatus.NotListed;
        await db.SaveChangesAsync(ct);
        return await GetAsync(course.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var course = await LoadCourseAsync(id, ct);
        if (await db.Enrollments.AnyAsync(e => e.CourseId == id, ct))
            throw new ConflictException("Learners are enrolled in this course. Unpublish it instead so they keep access.");
        var lessonIds = course.Sections.SelectMany(s => s.Lessons).Select(l => l.Id).ToList();
        await db.Videos.Where(v => lessonIds.Contains(v.LessonId)).ExecuteDeleteAsync(ct);
        await db.LessonResources.Where(r => lessonIds.Contains(r.LessonId)).ExecuteDeleteAsync(ct);
        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
    }

    public async Task<StudioCourseDto> UploadThumbnailAsync(Guid id, Upload upload, CancellationToken ct)
    {
        var course = await LoadCourseAsync(id, ct);
        course.ThumbnailUrl = await SaveImageAsync(upload, $"{course.TenantId}/courses/{course.Id:N}/thumb", ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<StudioCourseDto> PublishAsync(Guid id, bool publish, CancellationToken ct)
    {
        var course = await LoadCourseAsync(id, ct);
        if (publish)
        {
            if (!course.Sections.Any(s => s.Lessons.Count > 0)) throw new ConflictException("Add at least one lesson before publishing.");
            course.Status = CourseStatus.Published;
            course.PublishedAt ??= core.Now;
        }
        else
        {
            course.Status = CourseStatus.Draft;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<StudioCourseDto> SubmitToMarketplaceAsync(Guid id, CancellationToken ct)
    {
        var course = await LoadCourseAsync(id, ct);
        if (course.Status != CourseStatus.Published) throw new ConflictException("Publish the course before sending it for marketplace review.");
        if (string.IsNullOrWhiteSpace(course.Description) || course.ThumbnailUrl is null || course.CategoryId is null)
            throw new ConflictException("Add a description, a thumbnail and a category before sending it for review.");
        course.ListOnMarketplace = true;
        course.MarketplaceStatus = MarketplaceStatus.InReview;
        course.MarketplaceNote = null;
        course.SubmittedAt = core.Now;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // Curriculum

    public async Task<StudioCourseDto> AddSectionAsync(Guid courseId, TitleRequest r, CancellationToken ct)
    {
        var course = await LoadCourseAsync(courseId, ct);
        course.Sections.Add(new CourseSection
        {
            CourseId = course.Id, Title = Required(r.Title, 120), Position = course.Sections.Count == 0 ? 0 : course.Sections.Max(s => s.Position) + 1,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(courseId, ct);
    }

    public async Task<StudioCourseDto> RenameSectionAsync(Guid sectionId, TitleRequest r, CancellationToken ct)
    {
        var section = await db.CourseSections.FirstOrDefaultAsync(s => s.Id == sectionId, ct) ?? throw new NotFoundException("Section");
        section.Title = Required(r.Title, 120);
        await db.SaveChangesAsync(ct);
        return await GetAsync(section.CourseId, ct);
    }

    public async Task<StudioCourseDto> DeleteSectionAsync(Guid sectionId, CancellationToken ct)
    {
        var section = await db.CourseSections.Include(s => s.Lessons).FirstOrDefaultAsync(s => s.Id == sectionId, ct) ?? throw new NotFoundException("Section");
        if (section.Lessons.Count > 0) throw new ConflictException("Move or delete this section's lessons first.");
        db.CourseSections.Remove(section);
        await db.SaveChangesAsync(ct);
        return await GetAsync(section.CourseId, ct);
    }

    public async Task<StudioCourseDto> AddLessonAsync(Guid sectionId, NewLessonRequest r, CancellationToken ct)
    {
        var section = await db.CourseSections.Include(s => s.Lessons).FirstOrDefaultAsync(s => s.Id == sectionId, ct) ?? throw new NotFoundException("Section");
        var lesson = new Lesson
        {
            CourseId = section.CourseId, SectionId = section.Id, Title = Required(r.Title, 160), Type = r.Type,
            Position = section.Lessons.Count == 0 ? 0 : section.Lessons.Max(l => l.Position) + 1,
            Quiz = r.Type == LessonType.Quiz ? new QuizDefinition() : null,
        };
        // One transaction: the new lesson and every learner's progress (no longer complete) change together.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.Lessons.Add(lesson);
            await db.SaveChangesAsync(ct);
            await core.RecomputeCourseProgressAsync(section.CourseId, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(section.CourseId, ct);
    }

    /// <summary>Saves the complete order of sections and lessons; lessons may move between sections.</summary>
    public async Task<StudioCourseDto> ReorderAsync(Guid courseId, ReorderRequest r, CancellationToken ct)
    {
        var course = await LoadCourseAsync(courseId, ct);
        var sections = course.Sections.ToDictionary(s => s.Id);
        var lessons = course.Sections.SelectMany(s => s.Lessons).ToDictionary(l => l.Id);
        if (r.Sections.Count != sections.Count || r.Sections.Any(s => !sections.ContainsKey(s.Id))
            || r.Sections.SelectMany(s => s.LessonIds).Order().SequenceEqual(lessons.Keys.Order()) is false)
            throw BadRequestException.Field("sections", "Send every section and lesson of the course exactly once.");
        for (var i = 0; i < r.Sections.Count; i++)
        {
            sections[r.Sections[i].Id].Position = i;
            for (var j = 0; j < r.Sections[i].LessonIds.Count; j++)
            {
                var lesson = lessons[r.Sections[i].LessonIds[j]];
                lesson.SectionId = r.Sections[i].Id;
                lesson.Position = j;
            }
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(courseId, ct);
    }

    // Lessons

    public async Task<StudioLessonDto> GetLessonAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadLessonAsync(id, ct), ct);

    public async Task<StudioLessonDto> SaveLessonAsync(Guid id, SaveLessonRequest r, CancellationToken ct)
    {
        var lesson = await LoadLessonAsync(id, ct);
        lesson.Title = r.Title.Trim();
        lesson.Type = r.Type;
        lesson.IsPreview = r.IsPreview;
        lesson.Content = Clean(r.Content);
        if (r.Type == LessonType.Quiz) lesson.Quiz ??= new QuizDefinition();
        await db.SaveChangesAsync(ct);
        return await GetLessonAsync(id, ct);
    }

    public async Task DeleteLessonAsync(Guid id, CancellationToken ct)
    {
        var lesson = await LoadLessonAsync(id, ct);
        var keys = await db.Videos.Where(v => v.LessonId == id).Select(v => v.SourceKey).ToListAsync(ct);
        keys.AddRange(await db.LessonResources.Where(r => r.LessonId == id).Select(r => r.StorageKey).ToListAsync(ct));
        // One transaction: the lesson goes, nobody resumes into it, and every learner's progress and completion
        // (and certificate, if this finishes the course for them) are recomputed against the lessons left.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Videos.Where(v => v.LessonId == id).ExecuteDeleteAsync(ct);
            await db.LessonResources.Where(r => r.LessonId == id).ExecuteDeleteAsync(ct);
            await db.LessonProgress.Where(p => p.LessonId == id).ExecuteDeleteAsync(ct);
            await db.Enrollments.Where(e => e.LastLessonId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.LastLessonId, (Guid?)null), ct);
            db.Lessons.Remove(lesson);
            await db.SaveChangesAsync(ct);
            await core.RecomputeCourseProgressAsync(lesson.CourseId, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        foreach (var key in keys) await storage.DeleteAsync(key, ct);
    }

    /// <summary>Stores the original and queues it for processing (HLS when FFmpeg is available, otherwise played as MP4).</summary>
    public async Task<StudioLessonDto> UploadVideoAsync(Guid lessonId, Upload upload, CancellationToken ct)
    {
        var lesson = await LoadLessonAsync(lessonId, ct);
        var ext = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (!VideoExtensions.Contains(ext)) throw BadRequestException.Field("file", "Upload an MP4, MOV, WebM or MKV video.");
        if (upload.Length <= 0 || upload.Length > MaxVideoBytes) throw BadRequestException.Field("file", "Videos must be 2 GB or smaller.");
        await EnsureStorageAsync(upload.Length, ct);

        var old = await db.Videos.Where(v => v.LessonId == lessonId).ToListAsync(ct);
        var video = new Video { LessonId = lessonId, SourceKey = "", SourceSizeBytes = upload.Length };
        video.SourceKey = $"{lesson.TenantId}/videos/{video.Id:N}/source{ext}";
        await storage.SaveAsync(video.SourceKey, upload.Content, string.IsNullOrWhiteSpace(upload.ContentType) ? "video/mp4" : upload.ContentType, ct);
        db.Videos.RemoveRange(old);
        db.Videos.Add(video);
        if (lesson.Type != LessonType.Video) lesson.Type = LessonType.Video;
        await db.SaveChangesAsync(ct);
        foreach (var v in old)
        {
            await storage.DeleteAsync(v.SourceKey, ct);
            if (v.PlaybackKey is { } playback && playback != v.SourceKey) await storage.DeleteAsync(playback, ct);
        }
        return await GetLessonAsync(lessonId, ct);
    }

    public async Task<VideoDto?> VideoStatusAsync(Guid lessonId, CancellationToken ct)
    {
        var v = await db.Videos.AsNoTracking().FirstOrDefaultAsync(x => x.LessonId == lessonId, ct);
        return v is null ? null : ToDto(v);
    }

    public async Task<StudioLessonDto> AddResourceAsync(Guid lessonId, Upload upload, string? title, CancellationToken ct)
    {
        var lesson = await LoadLessonAsync(lessonId, ct);
        if (upload.Length <= 0 || upload.Length > MaxResourceBytes) throw BadRequestException.Field("file", "Files must be 50 MB or smaller.");
        await EnsureStorageAsync(upload.Length, ct);
        var safeName = string.Concat(Path.GetFileName(upload.FileName).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-'));
        var key = $"{lesson.TenantId}/resources/{lesson.Id:N}/{Guid.CreateVersion7():N}-{safeName}";
        var url = await storage.SaveAsync(key, upload.Content, string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType, ct);
        db.LessonResources.Add(new LessonResource
        {
            LessonId = lessonId, Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(upload.FileName) : title.Trim(),
            FileName = Path.GetFileName(upload.FileName), StorageKey = key, Url = url, SizeBytes = upload.Length,
        });
        await db.SaveChangesAsync(ct);
        return await GetLessonAsync(lessonId, ct);
    }

    public async Task<StudioLessonDto> DeleteResourceAsync(Guid lessonId, Guid resourceId, CancellationToken ct)
    {
        var resource = await db.LessonResources.FirstOrDefaultAsync(r => r.Id == resourceId && r.LessonId == lessonId, ct)
            ?? throw new NotFoundException("File");
        db.LessonResources.Remove(resource);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(resource.StorageKey, ct);
        return await GetLessonAsync(lessonId, ct);
    }

    public async Task<StudioLessonDto> SaveQuizAsync(Guid lessonId, SaveQuizRequest r, CancellationToken ct)
    {
        var lesson = await LoadLessonAsync(lessonId, ct);
        lesson.Type = LessonType.Quiz;
        lesson.Quiz = new QuizDefinition
        {
            TimeLimitMinutes = r.TimeLimitMinutes, PassMarkPercent = r.PassMarkPercent, MaxAttempts = r.MaxAttempts, Shuffle = r.Shuffle,
            Questions = r.Questions.Select(q => new QuizQuestion
            {
                Id = q.Id ?? Guid.CreateVersion7(), Type = q.Type, Prompt = q.Prompt.Trim(), Points = q.Points, Explanation = Clean(q.Explanation),
                Options = q.Type == QuestionType.ShortAnswer ? [] : (q.Options ?? []).Select(o => new QuizOption
                {
                    Id = o.Id ?? Guid.CreateVersion7(), Text = o.Text.Trim(), IsCorrect = o.IsCorrect,
                }).ToList(),
                AcceptedAnswers = q.Type == QuestionType.ShortAnswer ? (q.AcceptedAnswers ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).ToList() : [],
            }).ToList(),
        };
        await db.SaveChangesAsync(ct);
        return await GetLessonAsync(lessonId, ct);
    }

    // Helpers

    private async Task EnsureStorageAsync(long addBytes, CancellationToken ct)
    {
        var limitMb = (await plans.GetCurrentPlanAsync(ct)).Limit(LimitKeys.VideoStorageMb);
        if (limitMb < 0) return;
        var used = (await db.Videos.SumAsync(v => (long?)v.SourceSizeBytes, ct) ?? 0) + (await db.LessonResources.SumAsync(r => (long?)r.SizeBytes, ct) ?? 0);
        if (used + addBytes > limitMb * 1024 * 1024)
            throw new PlanLimitException($"This would use more than your plan's {limitMb / 1024.0:0.#} GB of storage. Upgrade or remove old videos.");
    }

    private async Task<Course> LoadCourseAsync(Guid id, CancellationToken ct) =>
        await db.Courses.Include(c => c.Sections).ThenInclude(s => s.Lessons).FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Course");

    private async Task<Lesson> LoadLessonAsync(Guid id, CancellationToken ct) =>
        await db.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lesson");

    private async Task<string> UniqueSlugAsync(string title, Guid? excludeId, CancellationToken ct)
    {
        var baseSlug = Slugs.From(title);
        var slug = baseSlug;
        for (var n = 2; await db.Courses.AnyAsync(c => c.Slug == slug && c.Id != excludeId, ct); n++) slug = $"{baseSlug}-{n}";
        return slug;
    }

    private async Task<string> SaveImageAsync(Upload upload, string prefix, CancellationToken ct)
    {
        var (buffer, ext, mime) = await ImageUploads.ReadAsync(upload, ct);
        await using var _ = buffer;
        var key = $"{prefix}-{Guid.CreateVersion7():N}{ext}";
        return await storage.SaveAsync(key, buffer, mime, ct);
    }

    private AcademyDto ToDto(Academy a) => new(a.Name, a.Slug, a.Headline, a.Bio, a.LogoUrl, a.BrandColor, a.Currency,
        a.PayoutAccount is { } p && user.Role is MemberRole.Owner or MemberRole.Admin ? new PayoutAccountDto(p.BankName, user.IsSupportView ? AccountMask.Last4(p.AccountNumber) : p.AccountNumber, p.AccountName) : null, core.AcademyUrl(a.Slug));

    private async Task<StudioCourseDto> ToDtoAsync(Course c, CancellationToken ct)
    {
        var academy = await core.AcademyAsync(ct);
        var lessonIds = c.Sections.SelectMany(s => s.Lessons).Select(l => l.Id).ToList();
        var videos = await db.Videos.AsNoTracking().Where(v => lessonIds.Contains(v.LessonId)).ToDictionaryAsync(v => v.LessonId, v => v.Status, ct);
        return new StudioCourseDto(c.Id, c.Title, c.Subtitle, c.Description, c.CategoryId, c.Level, c.Language, c.PriceMinor, c.IsFree, c.Outcomes,
            c.Requirements, c.ListOnMarketplace, c.Slug, c.ThumbnailUrl, c.Status, c.MarketplaceStatus, c.MarketplaceNote, c.PublishedAt,
            core.CourseUrl(academy.Slug, c.Slug),
            c.Sections.OrderBy(s => s.Position).Select(s => new SectionDto(s.Id, s.Title, s.Position,
                s.Lessons.OrderBy(l => l.Position).Select(l => new LessonSummaryDto(l.Id, l.Title, l.Type, l.Position, l.IsPreview, l.DurationSec,
                    videos.TryGetValue(l.Id, out var vs) ? vs.ToString() : "None")).ToList())).ToList());
    }

    private async Task<StudioLessonDto> ToDtoAsync(Lesson l, CancellationToken ct)
    {
        var video = await db.Videos.AsNoTracking().FirstOrDefaultAsync(v => v.LessonId == l.Id, ct);
        var resources = await db.LessonResources.AsNoTracking().Where(r => r.LessonId == l.Id).OrderBy(r => r.CreatedAt)
            .Select(r => new ResourceDto(r.Id, r.Title, r.FileName, r.SizeBytes, r.Url)).ToListAsync(ct);
        return new StudioLessonDto(l.Id, l.Title, l.Type, l.IsPreview, l.Content, l.SectionId, l.CourseId, l.DurationSec,
            video is null ? null : ToDto(video), resources, l.Quiz is { } q ? ToDto(q) : null);
    }

    private static VideoDto ToDto(Video v) => new(v.Status, v.Format, v.DurationSec, v.ThumbnailUrl, v.Error, v.Renditions);

    public static SaveQuizRequest ToDto(QuizDefinition q) => new(q.TimeLimitMinutes, q.PassMarkPercent, q.MaxAttempts, q.Shuffle,
        q.Questions.Select(x => new QuizQuestionDto(x.Id, x.Type, x.Prompt, x.Points, x.Explanation,
            x.Options.Select(o => new QuizOptionDto(o.Id, o.Text, o.IsCorrect)).ToList(), x.AcceptedAnswers)).ToList());

    private static string Required(string? value, int max)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0 || v.Length > max) throw BadRequestException.Field("title", $"Enter a title of up to {max} characters.");
        return v;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
