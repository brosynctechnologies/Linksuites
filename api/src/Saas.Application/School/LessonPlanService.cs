using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SaveLessonPlanRequest(
    Guid ClassSubjectId, Guid TermId, int Week, DateOnly Date, string? Topic, string? Objectives, string? Materials,
    string? Methods, string? Activities, string? PreviousKnowledge, string? Introduction, string? Presentation,
    string? Conclusion, string? Evaluation, Guid? AssignmentId, string? TeacherRemarks);

public sealed record ReturnLessonPlanRequest(string? Note);

public sealed record LessonPlanSettingsRequest(bool NeedReview);

public sealed record LessonPlanSettingsDto(bool NeedReview);

public sealed record LessonPlanDto(
    Guid Id, Guid ClassSubjectId, Guid TermId, int Week, DateOnly Date, string Topic, string Objectives, string Materials,
    string Methods, string Activities, string PreviousKnowledge, string Introduction, string Presentation, string Conclusion,
    string Evaluation, Guid? AssignmentId, string? TeacherRemarks, LessonPlanStatus Status, string? ReviewerNote,
    DateTimeOffset? SubmittedAt, Guid? ReviewedBy, Guid CreatedBy);

/// <summary>
/// A weekly lesson plan for one class subject. The subject teacher, or leadership, writes a draft. Only the author can see a
/// draft. Submit tells leadership, unless the school has turned review off, in which case the plan is accepted at once.
/// Accept and return are for leadership. A plan does not create a timetable slot and does not appear on the report card.
/// </summary>
public sealed class LessonPlanService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, SchoolAccess access, SchoolFeed feed, TimeProvider clock)
{
    public const int MaxTopicLength = 200;
    public const int MaxTextLength = 4000;
    public const int MaxRemarksLength = 2000;
    public const int MaxWeek = 20;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();
    private Guid UserId => user.UserId ?? throw new ForbiddenException();

    public async Task<IReadOnlyList<LessonPlanDto>> ListAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        RefuseBursar();
        var (cs, term) = await LoadSubjectAsync(classSubjectId, termId, ct);
        var userId = UserId;
        var query = db.LessonPlans.AsNoTracking().Where(p => p.ClassSubjectId == cs.Id && p.TermId == term.Id);
        query = access.IsLeadership
            ? query.Where(p => p.Status != LessonPlanStatus.Draft || p.CreatedBy == userId)
            : query.Where(p => p.CreatedBy == userId);
        var rows = await query.OrderBy(p => p.Week).ThenBy(p => p.Date).ThenBy(p => p.Topic).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<LessonPlanDto> GetAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var plan = await FindAsync(id, ct, track: false);
        if (!CanSee(plan)) throw new NotFoundException("This lesson plan");
        return ToDto(plan);
    }

    public async Task<LessonPlanDto> CreateAsync(SaveLessonPlanRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var text = Read(r);
        var (cs, term) = await LoadSubjectAsync(r.ClassSubjectId, r.TermId, ct);
        await EnsureAssignmentAsync(r.AssignmentId, cs.Id, term.Id, ct);
        var plan = new LessonPlan
        {
            TenantId = TenantId, ClassSubjectId = cs.Id, TermId = term.Id, Week = r.Week, Date = r.Date,
            Topic = text.Topic, Objectives = text.Objectives, Materials = text.Materials, Methods = text.Methods,
            Activities = text.Activities, PreviousKnowledge = text.PreviousKnowledge, Introduction = text.Introduction,
            Presentation = text.Presentation, Conclusion = text.Conclusion, Evaluation = text.Evaluation,
            AssignmentId = r.AssignmentId, TeacherRemarks = text.TeacherRemarks, Status = LessonPlanStatus.Draft, CreatedBy = UserId,
        };
        db.LessonPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<LessonPlanDto> UpdateAsync(Guid id, SaveLessonPlanRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var plan = await FindAsync(id, ct);
        if (plan.CreatedBy != UserId || !CanSee(plan)) throw new NotFoundException("This lesson plan");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(plan.ClassSubjectId, ct))
            throw new NotFoundException("This lesson plan");
        EnsureEditable(plan);
        if (r.ClassSubjectId != plan.ClassSubjectId || r.TermId != plan.TermId)
            throw BadRequestException.Field("classSubjectId", "A lesson plan stays with its class and term.");
        var text = Read(r);
        await EnsureAssignmentAsync(r.AssignmentId, plan.ClassSubjectId, plan.TermId, ct);
        plan.Week = r.Week;
        plan.Date = r.Date;
        plan.Topic = text.Topic;
        plan.Objectives = text.Objectives;
        plan.Materials = text.Materials;
        plan.Methods = text.Methods;
        plan.Activities = text.Activities;
        plan.PreviousKnowledge = text.PreviousKnowledge;
        plan.Introduction = text.Introduction;
        plan.Presentation = text.Presentation;
        plan.Conclusion = text.Conclusion;
        plan.Evaluation = text.Evaluation;
        plan.AssignmentId = r.AssignmentId;
        plan.TeacherRemarks = text.TeacherRemarks;
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<LessonPlanDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var plan = await FindAsync(id, ct);
        if (plan.CreatedBy != UserId) throw new NotFoundException("This lesson plan");
        if (plan.Status == LessonPlanStatus.Accepted)
            throw new ConflictException("This lesson plan has already been accepted.", "lesson_plan_accepted");
        if (plan.Status == LessonPlanStatus.Submitted)
            throw new ConflictException("This lesson plan has already been submitted.", "lesson_plan_submitted");

        var needReview = await db.SchoolProfiles.Select(p => p.LessonPlansNeedReview).FirstAsync(ct);
        plan.SubmittedAt = clock.GetUtcNow();
        plan.ReviewerNote = null;
        plan.ReviewedBy = null;
        if (needReview)
        {
            plan.Status = LessonPlanStatus.Submitted;
            var leaders = await feed.MembersAsync(ct, MemberRole.Owner, MemberRole.Admin);
            leaders.RemoveAll(leader => leader == UserId);
            feed.Notify(leaders, "school.lesson_plan_submitted", "A lesson plan was submitted",
                $"{plan.Topic} for week {plan.Week} is ready for review.", $"/app/lesson-plans/{plan.Id}");
        }
        else
            plan.Status = LessonPlanStatus.Accepted;
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<LessonPlanDto> AcceptAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var plan = await OpenForReviewAsync(id, ct);
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can accept a lesson plan.");
        if (plan.Status != LessonPlanStatus.Submitted)
            throw new ConflictException(plan.Status == LessonPlanStatus.Accepted
                ? "This lesson plan has already been accepted."
                : "This lesson plan is not waiting for review.", "lesson_plan_status");
        plan.Status = LessonPlanStatus.Accepted;
        plan.ReviewedBy = UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<LessonPlanDto> ReturnAsync(Guid id, ReturnLessonPlanRequest r, CancellationToken ct)
    {
        RefuseBursar();
        var plan = await OpenForReviewAsync(id, ct);
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can return a lesson plan.");
        var note = (r.Note ?? "").Trim();
        if (note.Length == 0) throw BadRequestException.Field("note", "Write a note so the teacher knows what to change.");
        if (note.Length > MaxRemarksLength) throw BadRequestException.Field("note", $"Use {MaxRemarksLength} characters or fewer for the note.");
        if (plan.Status != LessonPlanStatus.Submitted)
            throw new ConflictException(plan.Status == LessonPlanStatus.Accepted
                ? "This lesson plan has already been accepted."
                : "This lesson plan is not waiting for review.", "lesson_plan_status");
        plan.Status = LessonPlanStatus.Returned;
        plan.ReviewerNote = note;
        plan.ReviewedBy = UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<LessonPlanSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can change lesson plan review.");
        return new LessonPlanSettingsDto(await db.SchoolProfiles.AsNoTracking().Select(p => p.LessonPlansNeedReview).FirstAsync(ct));
    }

    public async Task<LessonPlanSettingsDto> SetNeedReviewAsync(LessonPlanSettingsRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can change lesson plan review.");
        var profile = await db.SchoolProfiles.FirstAsync(ct);
        profile.LessonPlansNeedReview = r.NeedReview;
        await db.SaveChangesAsync(ct);
        return new LessonPlanSettingsDto(profile.LessonPlansNeedReview);
    }

    private async Task<LessonPlan> OpenForReviewAsync(Guid id, CancellationToken ct)
    {
        var plan = await FindAsync(id, ct);
        if (!CanSee(plan)) throw new NotFoundException("This lesson plan");
        return plan;
    }

    private bool CanSee(LessonPlan plan) =>
        plan.CreatedBy == UserId || (access.IsLeadership && plan.Status != LessonPlanStatus.Draft);

    private void RefuseBursar()
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not write lesson plans.");
    }

    private static void EnsureEditable(LessonPlan plan)
    {
        if (plan.Status == LessonPlanStatus.Accepted)
            throw new ConflictException("This lesson plan has been accepted, so it can't be changed.", "lesson_plan_accepted");
        if (plan.Status is not (LessonPlanStatus.Draft or LessonPlanStatus.Returned))
            throw new ConflictException("This lesson plan can only be changed while it is a draft or has been returned.", "lesson_plan_status");
    }

    private async Task<LessonPlan> FindAsync(Guid id, CancellationToken ct, bool track = true)
    {
        var query = track ? db.LessonPlans : db.LessonPlans.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("This lesson plan");
    }

    private async Task<(ClassSubject Cs, Term Term)> LoadSubjectAsync(Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        var cs = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classSubjectId, ct)
            ?? throw new NotFoundException("Class subject");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(cs.Id, ct))
            throw new NotFoundException("Class subject");
        var sessionId = await db.SchoolClasses.Where(c => c.Id == cs.ClassId).Select(c => c.SessionId).FirstAsync(ct);
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != sessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");
        return (cs, term);
    }

    private async Task EnsureAssignmentAsync(Guid? assignmentId, Guid classSubjectId, Guid termId, CancellationToken ct)
    {
        if (assignmentId is not { } id) return;
        var row = await db.Assignments.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.ClassSubjectId, a.TermId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("This assignment");
        if (row.ClassSubjectId != classSubjectId || row.TermId != termId)
            throw BadRequestException.Field("assignmentId", "Choose an assignment for this class and term.");
    }

    private static LessonPlanDto ToDto(LessonPlan plan) => new(
        plan.Id, plan.ClassSubjectId, plan.TermId, plan.Week, plan.Date, plan.Topic, plan.Objectives, plan.Materials,
        plan.Methods, plan.Activities, plan.PreviousKnowledge, plan.Introduction, plan.Presentation, plan.Conclusion,
        plan.Evaluation, plan.AssignmentId, plan.TeacherRemarks, plan.Status, plan.ReviewerNote, plan.SubmittedAt,
        plan.ReviewedBy, plan.CreatedBy);

    private readonly record struct PlanText(
        string Topic, string Objectives, string Materials, string Methods, string Activities, string PreviousKnowledge,
        string Introduction, string Presentation, string Conclusion, string Evaluation, string? TeacherRemarks);

    private static PlanText Read(SaveLessonPlanRequest r)
    {
        if (r.Week is < 1 or > MaxWeek) throw BadRequestException.Field("week", $"Week must be from 1 to {MaxWeek}.");
        if (r.Date == default) throw BadRequestException.Field("date", "Choose the date of the lesson.");
        return new PlanText(
            Required(r.Topic, "topic", "Enter a topic.", MaxTopicLength),
            Required(r.Objectives, "objectives", "Write the objectives.", MaxTextLength),
            Required(r.Materials, "materials", "Write the materials.", MaxTextLength),
            Required(r.Methods, "methods", "Write the methods.", MaxTextLength),
            Required(r.Activities, "activities", "Write the activities.", MaxTextLength),
            Required(r.PreviousKnowledge, "previousKnowledge", "Write the previous knowledge.", MaxTextLength),
            Required(r.Introduction, "introduction", "Write the introduction.", MaxTextLength),
            Required(r.Presentation, "presentation", "Write the presentation.", MaxTextLength),
            Required(r.Conclusion, "conclusion", "Write the conclusion.", MaxTextLength),
            Required(r.Evaluation, "evaluation", "Write the evaluation.", MaxTextLength),
            Optional(r.TeacherRemarks, "teacherRemarks", MaxRemarksLength));
    }

    private static string Required(string? value, string field, string empty, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw BadRequestException.Field(field, empty);
        if (text.Length > max) throw BadRequestException.Field(field, $"Use {max} characters or fewer.");
        return text;
    }

    private static string? Optional(string? value, string field, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return null;
        if (text.Length > max) throw BadRequestException.Field(field, $"Use {max} characters or fewer.");
        return text;
    }
}
