using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

/// <summary>A signed-in learner: buying, the lesson player, progress, quizzes, Q&amp;A, notes, reviews and certificates.</summary>
public sealed class LearnService(IAppDbContext db, CourseCore core, IPaymentGateway gateway, ICurrentUser user, AppUrls urls)
{
    private static readonly TimeSpan QuizGrace = TimeSpan.FromSeconds(60);

    private Guid UserId => user.UserId ?? throw new ForbiddenException();

    // Buying

    public async Task<CourseQuoteDto> QuoteAsync(QuoteCourseRequest r, CancellationToken ct)
    {
        var course = await PublishedCourseAsync(r.CourseId, ct);
        var (discount, message) = await CouponAsync(course, r.CouponCode, ct);
        return new CourseQuoteDto(course.PriceMinor, discount, course.PriceMinor - discount, message);
    }

    public async Task<CheckoutResultDto> CheckoutAsync(CheckoutRequest r, CancellationToken ct)
    {
        var userId = UserId;
        var course = await PublishedCourseAsync(r.CourseId, ct);
        if (await db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.UserId == userId && !e.Revoked, ct))
            return new CheckoutResultDto(true, null, null, course.PriceMinor, 0, 0);

        var (discount, message) = await CouponAsync(course, r.CouponCode, ct);
        if (!string.IsNullOrWhiteSpace(r.CouponCode) && message is not null) throw BadRequestException.Field("couponCode", message);
        var total = course.IsFree ? 0 : course.PriceMinor - discount;

        if (total == 0)
        {
            await core.EnrollAsync(course, userId, course.IsFree ? EnrollmentSource.Free : EnrollmentSource.Coupon, null, ct);
            if (!course.IsFree) await UseCouponAsync(r.CouponCode!, ct);
            await db.SaveChangesAsync(ct);
            return new CheckoutResultDto(true, null, null, course.PriceMinor, discount, 0);
        }

        if (!gateway.IsConfigured) throw new ServiceUnavailableException("Online payments aren't set up yet. Please try again later.");
        // A sale only counts as a marketplace sale if the course is actually in the marketplace.
        var channel = r.Channel == SaleChannel.Academy || course.MarketplaceStatus != MarketplaceStatus.Approved ? SaleChannel.Academy : SaleChannel.Marketplace;
        var order = new CourseOrder
        {
            UserId = userId, CourseId = course.Id, PriceMinor = course.PriceMinor, DiscountMinor = discount, TotalMinor = total,
            Currency = course.Currency, CouponCode = discount > 0 ? r.CouponCode!.Trim().ToUpperInvariant() : null, Channel = channel, Reference = "",
        };
        order.Reference = $"crs_{order.Id:N}";
        db.CourseOrders.Add(order);
        await db.SaveChangesAsync(ct);

        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstAsync(ct);
        var url = await gateway.InitializeAsync(new PaymentInit(email!, total, course.Currency, order.Reference,
            $"{urls.Web(Products.Courses)}/checkout/{order.Id}", new() { ["course"] = course.Title, ["tenantId"] = course.TenantId.ToString() }), ct);
        return new CheckoutResultDto(false, url, order.Id, course.PriceMinor, discount, total);
    }

    public async Task<VerifyOrderResult> VerifyAsync(Guid orderId, VerifyOrderRequest r, CancellationToken ct)
    {
        var userId = UserId;
        var order = await db.CourseOrders.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct)
            ?? throw new NotFoundException("Order");
        if (order.Status == OrderState.Pending && r.Reference == order.Reference
            && await gateway.VerifyAsync(order.Reference, ct) is { } charge)
            await core.ConfirmOrderAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
        var course = await core.CourseAcrossTenantsAsync(order.CourseId, ct);
        var enrolled = await db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.UserId == userId && !e.Revoked, ct);
        if (enrolled) return new VerifyOrderResult(true, $"{urls.Web(Products.Courses)}/learn/{course.Id}/{await FirstLessonAsync(course.Id, ct)}");
        var academy = await db.Academies.AsNoTracking().FirstAsync(ct);
        return new VerifyOrderResult(false, core.CourseUrl(academy.Slug, course.Slug));
    }

    // My learning

    public async Task<IReadOnlyList<MyCourseDto>> MyCoursesAsync(CancellationToken ct)
    {
        var userId = UserId;
        var enrollments = await db.Enrollments.IgnoreQueryFilters().AsNoTracking().Where(e => e.UserId == userId && !e.Revoked)
            .OrderByDescending(e => e.UpdatedAt ?? e.CreatedAt).ToListAsync(ct);
        var courseIds = enrollments.Select(e => e.CourseId).ToList();
        var courses = await db.Courses.IgnoreQueryFilters().AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var tenantIds = courses.Values.Select(c => c.TenantId).Distinct().ToList();
        var academies = await db.Academies.IgnoreQueryFilters().AsNoTracking().Where(a => tenantIds.Contains(a.TenantId)).ToDictionaryAsync(a => a.TenantId, ct);
        var totals = await db.Lessons.IgnoreQueryFilters().Where(l => courseIds.Contains(l.CourseId)).GroupBy(l => l.CourseId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var done = await db.LessonProgress.IgnoreQueryFilters().Where(p => enrollmentIds.Contains(p.EnrollmentId) && p.CompletedAt != null)
            .GroupBy(p => p.EnrollmentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var certs = await db.Certificates.IgnoreQueryFilters().Where(c => c.UserId == userId).ToDictionaryAsync(c => c.EnrollmentId, c => c.Code, ct);
        // A last-viewed lesson the tutor has since deleted doesn't count.
        var lastIds = enrollments.Where(e => e.LastLessonId != null).Select(e => e.LastLessonId!.Value).ToList();
        var existing = (await db.Lessons.IgnoreQueryFilters().Where(l => lastIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct)).ToHashSet();
        return enrollments.Where(e => courses.ContainsKey(e.CourseId)).Select(e =>
        {
            var c = courses[e.CourseId];
            var a = academies[c.TenantId];
            return new MyCourseDto(c.Id, c.Slug, a.Slug, c.Title, c.ThumbnailUrl, a.Name, e.ProgressPercent, done.GetValueOrDefault(e.Id),
                totals.GetValueOrDefault(c.Id), e.LastLessonId is { } last && existing.Contains(last) ? last : null, e.CompletedAt,
                certs.GetValueOrDefault(e.Id));
        }).ToList();
    }

    public async Task<LearnCourseDto> CourseAsync(Guid courseId, CancellationToken ct)
    {
        var (course, enrollment) = await EnrolledAsync(courseId, ct);
        var academy = await db.Academies.AsNoTracking().FirstAsync(ct);
        var sections = await db.CourseSections.AsNoTracking().Include(s => s.Lessons).Where(s => s.CourseId == course.Id).OrderBy(s => s.Position).ToListAsync(ct);
        var completed = await db.LessonProgress.Where(p => p.EnrollmentId == enrollment.Id && p.CompletedAt != null).Select(p => p.LessonId).ToListAsync(ct);
        var passed = await db.QuizAttempts.Where(a => a.CourseId == course.Id && a.UserId == enrollment.UserId && a.Passed).Select(a => a.LessonId).Distinct().ToListAsync(ct);
        var ordered = sections.SelectMany(s => s.Lessons.OrderBy(l => l.Position)).ToList();
        // A last-viewed lesson the tutor has since deleted doesn't count.
        var last = ordered.Any(l => l.Id == enrollment.LastLessonId) ? enrollment.LastLessonId : null;
        var resume = last ?? ordered.FirstOrDefault(l => !completed.Contains(l.Id))?.Id ?? ordered.FirstOrDefault()?.Id;
        var cert = await db.Certificates.Where(c => c.EnrollmentId == enrollment.Id).Select(c => c.Code).FirstOrDefaultAsync(ct);
        return new LearnCourseDto(course.Id, course.Title, academy.Slug, course.Slug, academy.Name, enrollment.ProgressPercent, cert, academy.BrandColor,
            sections.Select(s => new LearnSectionDto(s.Id, s.Title, s.Lessons.OrderBy(l => l.Position).Select(l => new LearnLessonDto(l.Id, l.Title, l.Type,
                l.DurationSec, completed.Contains(l.Id), l.Type == LessonType.Quiz, passed.Contains(l.Id))).ToList())).ToList(), resume);
    }

    public async Task<LessonPlaybackDto> LessonAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        var (_, enrollment) = await EnrolledAsync(lesson.CourseId, ct);
        var video = await db.Videos.AsNoTracking().FirstOrDefaultAsync(v => v.LessonId == lesson.Id, ct);
        var resources = await db.LessonResources.AsNoTracking().Where(r => r.LessonId == lesson.Id).OrderBy(r => r.CreatedAt)
            .Select(r => new PlaybackResourceDto(r.Title, r.Url, r.SizeBytes)).ToListAsync(ct);
        var progress = await db.LessonProgress.AsNoTracking().FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lesson.Id, ct);
        var order = await db.Lessons.AsNoTracking().Where(l => l.CourseId == lesson.CourseId)
            .Join(db.CourseSections, l => l.SectionId, s => s.Id, (l, s) => new { l.Id, SectionPos = s.Position, l.Position })
            .OrderBy(x => x.SectionPos).ThenBy(x => x.Position).Select(x => x.Id).ToListAsync(ct);
        var index = order.IndexOf(lesson.Id);

        PlaybackQuizDto? quiz = null;
        if (lesson.Quiz is { } q)
        {
            var attempts = await db.QuizAttempts.AsNoTracking().Where(a => a.LessonId == lesson.Id && a.UserId == enrollment.UserId && a.SubmittedAt != null).ToListAsync(ct);
            quiz = new PlaybackQuizDto(q.Questions.Count, q.TimeLimitMinutes, q.PassMarkPercent, q.MaxAttempts, attempts.Count,
                attempts.Count == 0 ? null : attempts.Max(a => a.ScorePercent), attempts.Any(a => a.Passed));
        }
        var email = await db.Users.Where(u => u.Id == enrollment.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return new LessonPlaybackDto(lesson.Id, lesson.CourseId, lesson.Title, lesson.Type, lesson.Content, lesson.DurationSec,
            core.Playback(video, enrollment.UserId), resources, quiz, progress?.PositionSec ?? 0, progress?.CompletedAt is not null,
            index > 0 ? order[index - 1] : null, index >= 0 && index < order.Count - 1 ? order[index + 1] : null, email);
    }

    public async Task<ProgressResult> SaveProgressAsync(Guid lessonId, ProgressRequest r, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        var (_, enrollment) = await EnrolledAsync(lesson.CourseId, ct, track: true);
        var progress = await db.LessonProgress.FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lessonId, ct);
        if (progress is null)
        {
            progress = new LessonProgress { EnrollmentId = enrollment.Id, LessonId = lessonId };
            db.LessonProgress.Add(progress);
        }
        progress.PositionSec = Math.Max(0, r.PositionSec);
        // Quizzes complete only by passing an attempt.
        if (r.Completed && lesson.Type != LessonType.Quiz) progress.CompletedAt ??= core.Now;
        if (lesson.Type == LessonType.Video && lesson.DurationSec == 0 && r.PositionSec > 0 && r.Completed)
        {
            var tracked = await db.Lessons.FirstAsync(l => l.Id == lessonId, ct);
            tracked.DurationSec = r.PositionSec; // learn the length from the first full watch when the server couldn't probe it
        }
        enrollment.LastLessonId = lessonId;
        await db.SaveChangesAsync(ct);
        var result = await core.RefreshProgressAsync(enrollment, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    // Notes and Q&A

    public async Task<NoteDto> NoteAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        await EnrolledAsync(lesson.CourseId, ct);
        var note = await db.LessonNotes.AsNoTracking().FirstOrDefaultAsync(n => n.LessonId == lessonId && n.UserId == UserId, ct);
        return new NoteDto(note?.Body ?? "", note?.UpdatedAt ?? note?.CreatedAt);
    }

    public async Task<NoteDto> SaveNoteAsync(Guid lessonId, BodyRequest r, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        await EnrolledAsync(lesson.CourseId, ct);
        var note = await db.LessonNotes.FirstOrDefaultAsync(n => n.LessonId == lessonId && n.UserId == UserId, ct);
        if (note is null) db.LessonNotes.Add(note = new LessonNote { LessonId = lessonId, UserId = UserId });
        note.Body = r.Body;
        await db.SaveChangesAsync(ct);
        return new NoteDto(note.Body, note.UpdatedAt ?? note.CreatedAt);
    }

    public async Task<IReadOnlyList<QuestionDto>> QuestionsAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        await EnrolledAsync(lesson.CourseId, ct);
        var questions = await db.LessonQuestions.AsNoTracking().Include(q => q.Answers).Where(q => q.LessonId == lessonId)
            .OrderByDescending(q => q.CreatedAt).Take(100).ToListAsync(ct);
        return await StudioPeopleService.ToDtosAsync(questions, db, ct);
    }

    public async Task<QuestionDto> AskAsync(Guid lessonId, BodyRequest r, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        await EnrolledAsync(lesson.CourseId, ct);
        var question = new LessonQuestion { CourseId = lesson.CourseId, LessonId = lessonId, UserId = UserId, Body = r.Body.Trim() };
        db.LessonQuestions.Add(question);
        await db.SaveChangesAsync(ct);
        return (await StudioPeopleService.ToDtosAsync([question], db, ct))[0];
    }

    public async Task<QuestionDto> ReplyAsync(Guid questionId, BodyRequest r, CancellationToken ct)
    {
        var tenantId = await db.LessonQuestions.IgnoreQueryFilters().Where(q => q.Id == questionId).Select(q => (Guid?)q.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Question");
        core.UseTenantOf(tenantId);
        var question = await db.LessonQuestions.Include(q => q.Answers).FirstAsync(q => q.Id == questionId, ct);
        await EnrolledAsync(question.CourseId, ct);
        question.Answers.Add(new LessonAnswer { QuestionId = question.Id, UserId = UserId, Body = r.Body.Trim() });
        await db.SaveChangesAsync(ct);
        return (await StudioPeopleService.ToDtosAsync([question], db, ct))[0];
    }

    // Quizzes

    public async Task<StartedAttemptDto> StartQuizAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        var (_, enrollment) = await EnrolledAsync(lesson.CourseId, ct);
        var quiz = lesson.Quiz is { Questions.Count: > 0 } q ? q : throw new ConflictException("This quiz has no questions yet.");
        var used = await db.QuizAttempts.CountAsync(a => a.LessonId == lessonId && a.UserId == enrollment.UserId && a.SubmittedAt != null, ct);
        if (quiz.MaxAttempts is { } max && used >= max) throw new ConflictException("You've used all your attempts for this quiz.");

        var questions = quiz.Questions.ToList();
        if (quiz.Shuffle) questions = questions.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();
        var attempt = new QuizAttempt
        {
            LessonId = lessonId, CourseId = lesson.CourseId, UserId = enrollment.UserId, StartedAt = core.Now,
            EndsAt = quiz.TimeLimitMinutes is { } minutes ? core.Now.AddMinutes(minutes) : null, QuestionOrder = questions.Select(x => x.Id).ToList(),
        };
        db.QuizAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return new StartedAttemptDto(attempt.Id, attempt.StartedAt, attempt.EndsAt, questions.Select(x => new AttemptQuestionDto(x.Id, x.Type, x.Prompt,
            x.Points, x.Options.Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList())).ToList());
    }

    public async Task<QuizResultDto> SubmitQuizAsync(Guid attemptId, SubmitAttemptRequest r, CancellationToken ct)
    {
        var userId = UserId;
        var tenantId = await db.QuizAttempts.IgnoreQueryFilters().Where(a => a.Id == attemptId && a.UserId == userId).Select(a => (Guid?)a.TenantId)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Attempt");
        core.UseTenantOf(tenantId);
        var attempt = await db.QuizAttempts.FirstAsync(a => a.Id == attemptId, ct);
        if (attempt.SubmittedAt is not null) throw new ConflictException("This attempt was already submitted.");
        var lesson = await db.Lessons.AsNoTracking().FirstAsync(l => l.Id == attempt.LessonId, ct);
        var quiz = lesson.Quiz ?? new QuizDefinition();

        // Answers after the time limit (plus a little grace for slow connections) don't count.
        var late = attempt.EndsAt is { } end && core.Now > end + QuizGrace;
        var answers = late ? new Dictionary<Guid, QuizAnswer>() : r.Answers.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => new QuizAnswer
        {
            QuestionId = g.Key, OptionIds = g.First().OptionIds ?? [], Text = g.First().Text,
        });
        var grade = CourseRules.Grade(quiz, answers);
        attempt.SubmittedAt = core.Now;
        attempt.ScorePercent = grade.ScorePercent;
        attempt.Passed = grade.Passed;
        attempt.Answers = quiz.Questions.Select(q => new QuizAnswer
        {
            QuestionId = q.Id, OptionIds = answers.GetValueOrDefault(q.Id)?.OptionIds ?? [], Text = answers.GetValueOrDefault(q.Id)?.Text,
            Correct = grade.Questions.First(g => g.QuestionId == q.Id).Correct,
        }).ToList();

        if (grade.Passed)
        {
            var enrollment = await db.Enrollments.FirstAsync(e => e.CourseId == attempt.CourseId && e.UserId == userId, ct);
            var progress = await db.LessonProgress.FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lesson.Id, ct);
            if (progress is null) db.LessonProgress.Add(progress = new LessonProgress { EnrollmentId = enrollment.Id, LessonId = lesson.Id });
            progress.CompletedAt ??= core.Now;
            await db.SaveChangesAsync(ct);
            await core.RefreshProgressAsync(enrollment, ct);
        }
        await db.SaveChangesAsync(ct);
        return Result(attempt, quiz);
    }

    public async Task<IReadOnlyList<QuizResultDto>> AttemptsAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await LessonAcrossTenantsAsync(lessonId, ct);
        await EnrolledAsync(lesson.CourseId, ct);
        var attempts = await db.QuizAttempts.AsNoTracking().Where(a => a.LessonId == lessonId && a.UserId == UserId && a.SubmittedAt != null)
            .OrderByDescending(a => a.SubmittedAt).ToListAsync(ct);
        return attempts.Select(a => Result(a, lesson.Quiz ?? new QuizDefinition())).ToList();
    }

    private static QuizResultDto Result(QuizAttempt a, QuizDefinition quiz) => new(a.Id, a.ScorePercent, a.Passed, a.SubmittedAt,
        quiz.Questions.Select(q =>
        {
            var answer = a.Answers.FirstOrDefault(x => x.QuestionId == q.Id);
            return new QuestionResultDto(q.Id, q.Prompt, answer?.Correct ?? false, q.Explanation, answer?.OptionIds ?? [], answer?.Text,
                q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList());
        }).ToList());

    // Reviews and certificates

    public async Task<ReviewInput> ReviewAsync(Guid courseId, ReviewInput r, CancellationToken ct)
    {
        var (course, enrollment) = await EnrolledAsync(courseId, ct);
        var review = await db.CourseReviews.FirstOrDefaultAsync(x => x.CourseId == courseId && x.UserId == enrollment.UserId, ct);
        if (review is null) db.CourseReviews.Add(review = new CourseReview { CourseId = courseId, UserId = enrollment.UserId });
        review.Rating = r.Rating;
        review.Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim();
        await db.SaveChangesAsync(ct);
        var ratings = await db.CourseReviews.Where(x => x.CourseId == courseId).Select(x => x.Rating).ToListAsync(ct);
        var tracked = await db.Courses.FirstAsync(c => c.Id == course.Id, ct);
        tracked.RatingCount = ratings.Count;
        tracked.RatingAverage = ratings.Count == 0 ? 0 : ratings.Average();
        await db.SaveChangesAsync(ct);
        return new ReviewInput(review.Rating, review.Comment);
    }

    public async Task<IReadOnlyList<MyCertificateDto>> CertificatesAsync(CancellationToken ct) =>
        (await db.Certificates.IgnoreQueryFilters().AsNoTracking().Where(c => c.UserId == UserId).OrderByDescending(c => c.IssuedAt).ToListAsync(ct))
            .Select(c => new MyCertificateDto(c.Code, c.CourseTitle, c.IssuedAt, core.CertificatePdfUrl(c.Code), core.CertificateVerifyUrl(c.Code))).ToList();

    // Helpers

    private async Task<Course> PublishedCourseAsync(Guid courseId, CancellationToken ct)
    {
        var course = await core.CourseAcrossTenantsAsync(courseId, ct);
        if (course.Status != CourseStatus.Published) throw new NotFoundException("Course");
        // A suspended business can't sell; learners who already enrolled keep their access to what they paid for.
        if (!await db.Tenants.AnyAsync(t => t.Id == course.TenantId && t.Status == Saas.Domain.Core.TenantStatus.Active, ct)) throw new NotFoundException("Course");
        return course;
    }

    private async Task<(long Discount, string? Message)> CouponAsync(Course course, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || course.IsFree) return (0, null);
        var normalized = code.Trim().ToUpperInvariant();
        var coupon = await db.CourseCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized, ct);
        return CourseRules.ApplyCoupon(coupon, course.Id, course.PriceMinor, core.Now);
    }

    private async Task UseCouponAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        await db.CourseCoupons.Where(c => c.Code == normalized).ExecuteUpdateAsync(s => s.SetProperty(c => c.Uses, c => c.Uses + 1), ct);
    }

    private async Task<Lesson> LessonAcrossTenantsAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await db.Lessons.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == lessonId, ct) ?? throw new NotFoundException("Lesson");
        core.UseTenantOf(lesson.TenantId);
        return lesson;
    }

    private async Task<(Course, Enrollment)> EnrolledAsync(Guid courseId, CancellationToken ct, bool track = false)
    {
        var userId = UserId;
        var course = await core.CourseAcrossTenantsAsync(courseId, ct);
        var query = db.Enrollments.Where(e => e.CourseId == courseId && e.UserId == userId && !e.Revoked);
        var enrollment = await (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Enrol in this course to see its lessons.");
        return (course, enrollment);
    }

    private async Task<Guid?> FirstLessonAsync(Guid courseId, CancellationToken ct) =>
        await db.Lessons.Where(l => l.CourseId == courseId)
            .Join(db.CourseSections, l => l.SectionId, s => s.Id, (l, s) => new { l.Id, SectionPos = s.Position, l.Position })
            .OrderBy(x => x.SectionPos).ThenBy(x => x.Position).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
}
