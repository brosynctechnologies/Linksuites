using Saas.Application.Payouts;
using Saas.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

/// <summary>The tutor's learners and money: dashboard, students, Q&amp;A, reviews, coupons, earnings, payouts and refunds.</summary>
public sealed class StudioPeopleService(IAppDbContext db, CourseCore core, PlanService plans, IPaymentGateway gateway, ICurrentUser user, PayoutService payouts)
{
    public async Task<StudioDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var academy = await core.AcademyAsync(ct);
        var monthStart = new DateTimeOffset(core.Now.Year, core.Now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var ledger = await LedgerAsync(ct);
        var students = await db.Enrollments.Where(e => !e.Revoked).Select(e => e.UserId).Distinct().CountAsync(ct);
        var thisMonth = await db.Enrollments.CountAsync(e => e.CreatedAt >= monthStart, ct);
        var revenue = await db.Earnings.Where(e => e.Source == EarningSource.CourseOrder && !e.Reversed && e.CreatedAt >= monthStart).SumAsync(e => (long?)e.NetMinor, ct) ?? 0;
        var ratings = await db.Courses.Where(c => c.RatingCount > 0).Select(c => new { c.RatingAverage, c.RatingCount }).ToListAsync(ct);
        var rating = ratings.Sum(r => r.RatingCount) == 0 ? 0 : Math.Round(ratings.Sum(r => r.RatingAverage * r.RatingCount) / ratings.Sum(r => r.RatingCount), 1);

        var recent = await db.Enrollments.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(8).ToListAsync(ct);
        var names = await NamesAsync(recent.Select(e => e.UserId), ct);
        var titles = await db.Courses.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var orderIds = recent.Where(e => e.OrderId != null).Select(e => e.OrderId!.Value).ToList();
        var orders = await db.CourseOrders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        return new StudioDashboardDto(academy.Currency, students, thisMonth, revenue, ledger.AvailableMinor, ledger.PendingMinor, rating,
            recent.Select(e => new RecentEnrolmentDto(names.GetValueOrDefault(e.UserId, "Learner"), titles.GetValueOrDefault(e.CourseId, ""),
                e.CreatedAt, e.OrderId is { } o && orders.TryGetValue(o, out var order) ? order.TotalMinor : 0,
                e.OrderId is { } o2 && orders.TryGetValue(o2, out var order2) ? order2.Channel : null)).ToList());
    }

    public async Task<Paged<StudentDto>> StudentsAsync(Guid? courseId, string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = from e in db.Enrollments.AsNoTracking()
                    join u in db.Users on e.UserId equals u.Id
                    join c in db.Courses on e.CourseId equals c.Id
                    where !e.Revoked
                    // Where the sale was made; free and 100%-coupon enrolments have no order.
                    let channel = db.CourseOrders.Where(o => o.Id == e.OrderId).Select(o => (SaleChannel?)o.Channel).FirstOrDefault()
                    select new { e, u.FullName, u.Email, c.Title, Channel = channel };
        if (courseId is { } cid) query = query.Where(x => x.e.CourseId == cid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.Email!.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.e.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<StudentDto>(rows.Select(x => new StudentDto(x.e.Id, x.e.UserId, x.FullName, x.Email ?? "", x.e.CourseId, x.Title, x.e.CreatedAt,
            x.e.ProgressPercent, x.e.CompletedAt, x.e.Source, x.Channel)).ToList(), page, pageSize, total);
    }

    // Q&A and reviews

    public async Task<IReadOnlyList<QuestionDto>> QuestionsAsync(Guid? courseId, bool unanswered, CancellationToken ct)
    {
        var query = db.LessonQuestions.AsNoTracking().Include(q => q.Answers).AsQueryable();
        if (courseId is { } cid) query = query.Where(q => q.CourseId == cid);
        if (unanswered) query = query.Where(q => !q.Answers.Any(a => a.IsTutor));
        return await ToDtosAsync(await query.OrderByDescending(q => q.CreatedAt).Take(200).ToListAsync(ct), db, ct);
    }

    public async Task<QuestionDto> AnswerAsync(Guid questionId, BodyRequest r, CancellationToken ct)
    {
        var question = await db.LessonQuestions.Include(q => q.Answers).FirstOrDefaultAsync(q => q.Id == questionId, ct) ?? throw new NotFoundException("Question");
        question.Answers.Add(new LessonAnswer { QuestionId = question.Id, UserId = user.UserId!.Value, IsTutor = true, Body = r.Body.Trim() });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([question], db, ct))[0];
    }

    public static async Task<IReadOnlyList<QuestionDto>> ToDtosAsync(IReadOnlyList<LessonQuestion> questions, IAppDbContext db, CancellationToken ct)
    {
        var userIds = questions.Select(q => q.UserId).Concat(questions.SelectMany(q => q.Answers).Select(a => a.UserId)).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var lessonIds = questions.Select(q => q.LessonId).Distinct().ToList();
        var lessons = await db.Lessons.IgnoreQueryFilters().Where(l => lessonIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, ct);
        var courseIds = questions.Select(q => q.CourseId).Distinct().ToList();
        var courses = await db.Courses.IgnoreQueryFilters().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        return questions.Select(q => new QuestionDto(q.Id, q.CourseId, courses.GetValueOrDefault(q.CourseId, ""), q.LessonId,
            lessons.GetValueOrDefault(q.LessonId, ""), names.GetValueOrDefault(q.UserId, "Learner"), q.Body, q.CreatedAt,
            q.Answers.OrderBy(a => a.CreatedAt).Select(a => new AnswerDto(a.Id, names.GetValueOrDefault(a.UserId, a.IsTutor ? "Tutor" : "Learner"),
                a.IsTutor, a.Body, a.CreatedAt)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<StudioReviewDto>> ReviewsAsync(Guid? courseId, CancellationToken ct)
    {
        var query = db.CourseReviews.AsNoTracking();
        if (courseId is { } cid) query = query.Where(r => r.CourseId == cid);
        var rows = await query.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.UserId), ct);
        var titles = await db.Courses.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        return rows.Select(r => new StudioReviewDto(r.Id, titles.GetValueOrDefault(r.CourseId, ""), names.GetValueOrDefault(r.UserId, "Learner"),
            r.Rating, r.Comment, r.CreatedAt)).ToList();
    }

    // Coupons

    public async Task<IReadOnlyList<CouponDto>> CouponsAsync(CancellationToken ct) =>
        await db.CourseCoupons.AsNoTracking().OrderByDescending(c => c.CreatedAt)
            .Select(c => new CouponDto(c.Id, c.Code, c.CourseId, c.DiscountType, c.Value, c.MaxUses, c.Uses, c.ExpiresAt, c.IsActive)).ToListAsync(ct);

    public async Task<CouponDto> SaveCouponAsync(Guid? id, SaveCouponRequest r, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.Coupons, ct)) throw new PlanLimitException("Coupons are part of the Pro plan. Upgrade to use them.");
        if (r.CourseId is { } cid && !await db.Courses.AnyAsync(c => c.Id == cid, ct)) throw BadRequestException.Field("courseId", "That course does not exist.");
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.CourseCoupons.AnyAsync(c => c.Code == code && c.Id != id, ct)) throw BadRequestException.Field("code", "You already have a coupon with this code.");
        CourseCoupon coupon;
        if (id is null)
        {
            coupon = new CourseCoupon { Code = code };
            db.CourseCoupons.Add(coupon);
        }
        else
        {
            coupon = await db.CourseCoupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Coupon");
        }
        coupon.Code = code;
        coupon.CourseId = r.CourseId;
        coupon.DiscountType = r.DiscountType;
        coupon.Value = r.Value;
        coupon.MaxUses = r.MaxUses;
        coupon.ExpiresAt = r.ExpiresAt is { } end ? CourseRules.EndOfDayInLagos(DateOnly.FromDateTime(end.DateTime)) : null;
        coupon.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return new CouponDto(coupon.Id, coupon.Code, coupon.CourseId, coupon.DiscountType, coupon.Value, coupon.MaxUses, coupon.Uses, coupon.ExpiresAt, coupon.IsActive);
    }

    public async Task DeleteCouponAsync(Guid id, CancellationToken ct)
    {
        var deleted = await db.CourseCoupons.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Coupon");
    }

    // Money

    public async Task<EarningsDto> EarningsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var academy = await core.AcademyAsync(ct);
        var ledger = await LedgerAsync(ct);
        var total = await db.Earnings.CountAsync(e => e.Source == EarningSource.CourseOrder, ct);
        var rows = await db.Earnings.AsNoTracking().Where(e => e.Source == EarningSource.CourseOrder).OrderByDescending(e => e.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(e => e.UserId), ct);
        var titles = await db.Courses.ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var now = core.Now;
        return new EarningsDto(academy.Currency, ledger.AvailableMinor, ledger.PendingMinor, ledger.PaidOutMinor, new Paged<EarningDto>(
            rows.Select(e => new EarningDto(e.Id, e.SourceId, titles.GetValueOrDefault(e.CourseId ?? Guid.Empty, e.Description), names.GetValueOrDefault(e.UserId, "Learner"),
                e.Channel ?? SaleChannel.Academy, e.GrossMinor, e.FeeMinor, e.NetMinor, e.AvailableAt, e.CreatedAt,
                ledger.States.GetValueOrDefault(e.Id, LedgerRules.EarningState.Pending).ToString(),
                !e.Reversed && now - e.CreatedAt <= CourseRules.RefundWindow)).ToList(), page, pageSize, total));
    }

    public Task<IReadOnlyList<PayoutDto>> PayoutsAsync(CancellationToken ct) => payouts.ListAsync(ct);

    public async Task<PayoutDto> RequestPayoutAsync(PayoutRequest r, CancellationToken ct)
    {
        var academy = await core.AcademyAsync(ct);
        return await payouts.RequestAsync(r.AmountMinor, academy.PayoutAccount, academy.Currency, ct);
    }

    /// <summary>Refunds a learner within the refund window through the platform's Paystack, removing access and the earning.</summary>
    public async Task RefundAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.CourseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new NotFoundException("Order");
        if (order.Status != OrderState.Paid) throw new ConflictException("Only paid orders can be refunded.");
        if (core.Now - (order.PaidAt ?? order.CreatedAt) > CourseRules.RefundWindow)
            throw new ConflictException("Refunds are only possible within 7 days of purchase.");
        if (order.TotalMinor > 0) await gateway.RefundAsync(order.Reference, order.TotalMinor, ct);
        order.Status = OrderState.Refunded;
        order.RefundedAt = core.Now;
        await db.Earnings.Where(e => e.Source == EarningSource.CourseOrder && e.SourceId == order.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Reversed, true), ct);
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == order.CourseId && e.UserId == order.UserId, ct);
        if (enrollment is not null && !enrollment.Revoked)
        {
            enrollment.Revoked = true;
            await db.Courses.Where(c => c.Id == order.CourseId).ExecuteUpdateAsync(s => s.SetProperty(c => c.StudentsCount, c => c.StudentsCount - 1), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<LedgerRules.Ledger> LedgerAsync(CancellationToken ct) => payouts.LedgerAsync(ct);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }


}
