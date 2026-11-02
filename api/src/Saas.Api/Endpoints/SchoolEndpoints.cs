using Saas.Api.Infrastructure;
using Saas.Application.School;
using Saas.Application.School.Imports;
using Saas.Domain.School;
using Saas.Infrastructure.Schools;

namespace Saas.Api.Endpoints;

/// <summary>SchoolLink: the school's own area. Staff roles are the shared team roles; later tasks add their routes to this group.</summary>
public static class SchoolEndpoints
{
    public static void MapSchoolEndpoints(this IEndpointRouteBuilder app)
    {
        var school = app.MapGroup("/api/school").RequireAuthorization(Policies.SchoolProduct).WithTags("School");

        // "Book a demo" on the public site: emailed to the platform's super admins.
        app.MapPost("/api/school-demo-requests", async (SchoolDemoRequest r, SchoolDemoService s, CancellationToken ct) => { await s.RequestAsync(r, ct); return Results.Accepted(); })
            .Validate<SchoolDemoRequest>().AllowAnonymous().RequireRateLimiting(RateLimits.PublicWrite).WithTags("School");

        school.MapGet("/setup", (SchoolSetupService s, CancellationToken ct) => s.GetAsync(ct));

        // Everyone on the staff reads the academic setup; only Owner and Admin change it.
        const string admin = Policies.Admin;

        school.MapPost("/setup/apply-template", (ApplyTemplateRequest r, AcademicService s, CancellationToken ct) => s.ApplyTemplateAsync(r, ct))
            .RequireAuthorization(admin);

        school.MapGet("/levels", (AcademicService s, CancellationToken ct) => s.ListLevelsAsync(ct));
        school.MapPost("/levels", (SaveLevelRequest r, AcademicService s, CancellationToken ct) => s.CreateLevelAsync(r, ct)).RequireAuthorization(admin);
        school.MapPut("/levels/{id:guid}", (Guid id, SaveLevelRequest r, AcademicService s, CancellationToken ct) => s.UpdateLevelAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapDelete("/levels/{id:guid}", async (Guid id, AcademicService s, CancellationToken ct) => { await s.DeleteLevelAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        school.MapGet("/classes", (Guid? sessionId, AcademicService s, CancellationToken ct) => s.ListClassesAsync(sessionId, ct));
        school.MapPost("/classes", (SaveClassRequest r, AcademicService s, CancellationToken ct) => s.CreateClassAsync(r, ct)).RequireAuthorization(admin);
        school.MapPut("/classes/{id:guid}", (Guid id, SaveClassRequest r, AcademicService s, CancellationToken ct) => s.UpdateClassAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapDelete("/classes/{id:guid}", async (Guid id, AcademicService s, CancellationToken ct) => { await s.DeleteClassAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        school.MapGet("/subjects", (AcademicService s, CancellationToken ct) => s.ListSubjectsAsync(ct));
        school.MapPost("/subjects", (SaveSubjectRequest r, AcademicService s, CancellationToken ct) => s.CreateSubjectAsync(r, ct)).RequireAuthorization(admin);
        school.MapPut("/subjects/{id:guid}", (Guid id, SaveSubjectRequest r, AcademicService s, CancellationToken ct) => s.UpdateSubjectAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapDelete("/subjects/{id:guid}", async (Guid id, AcademicService s, CancellationToken ct) => { await s.DeleteSubjectAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        school.MapGet("/classes/{id:guid}/subjects", (Guid id, AcademicService s, CancellationToken ct) => s.ListClassSubjectsAsync(id, ct));
        school.MapPost("/classes/{id:guid}/subjects", (Guid id, AddClassSubjectRequest r, AcademicService s, CancellationToken ct) => s.AddClassSubjectAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapPut("/classes/{id:guid}/subjects/{subjectId:guid}", (Guid id, Guid subjectId, SetTeacherRequest r, AcademicService s, CancellationToken ct) => s.SetTeacherAsync(id, subjectId, r, ct)).RequireAuthorization(admin);
        school.MapDelete("/classes/{id:guid}/subjects/{subjectId:guid}", async (Guid id, Guid subjectId, AcademicService s, CancellationToken ct) => { await s.RemoveClassSubjectAsync(id, subjectId, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        // Staff: Owner and Admin manage them; every member sees their own classes.
        school.MapGet("/staff", (StaffService s, CancellationToken ct) => s.ListAsync(ct)).RequireAuthorization(admin);
        school.MapPut("/staff/{membershipId:guid}", (Guid membershipId, SaveStaffRequest r, StaffService s, CancellationToken ct) => s.UpdateAsync(membershipId, r, ct)).RequireAuthorization(admin);
        school.MapPut("/classes/{id:guid}/class-teacher", (Guid id, SetClassTeacherRequest r, StaffService s, CancellationToken ct) => s.SetClassTeacherAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapPut("/class-subjects/{id:guid}/teacher", (Guid id, SetSubjectTeacherRequest r, StaffService s, CancellationToken ct) => s.SetSubjectTeacherAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapGet("/my-classes", (StaffService s, CancellationToken ct) => s.MyClassesAsync(ct));
        school.MapGet("/staff/me", (StaffService s, CancellationToken ct) => s.MeAsync(ct));

        // Timetable: leadership saves a class's whole week; leadership and the class's teachers read it (warnings flag teacher clashes).
        school.MapGet("/timetable/{classId:guid}", (Guid classId, TimetableService s, CancellationToken ct) => s.GetAsync(classId, ct));
        school.MapPut("/timetable/{classId:guid}", (Guid classId, List<TimetableSlotInput>? slots, TimetableService s, CancellationToken ct) => s.SaveAsync(classId, slots, ct))
            .RequireAuthorization(admin);

        // Students: Owner and Admin write; the bursar reads everyone; a teacher reads the students of classes they teach.
        school.MapGet("/students", (string? q, Guid? classId, string? status, int? skip, int? take, StudentService s, CancellationToken ct) => s.ListAsync(q, classId, status, skip, take, ct));
        school.MapPost("/students", (CreateStudentRequest r, StudentService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequireAuthorization(admin);
        school.MapGet("/students/{id:guid}", (Guid id, StudentService s, CancellationToken ct) => s.GetAsync(id, ct));
        school.MapPut("/students/{id:guid}", (Guid id, UpdateStudentRequest r, StudentService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapDelete("/students/{id:guid}", async (Guid id, StudentService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(admin);
        school.MapPost("/students/{id:guid}/photo", (Guid id, IFormFile file, StudentService s, CancellationToken ct) => s.UploadPhotoAsync(id, file.ToUpload(), ct))
            .RequireAuthorization(admin).DisableAntiforgery();
        school.MapPost("/students/{id:guid}/transfer", (Guid id, TransferStudentRequest r, StudentService s, CancellationToken ct) => s.TransferAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapPost("/students/{id:guid}/status", (Guid id, ChangeStudentStatusRequest r, StudentService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapPost("/students/{id:guid}/guardians", (Guid id, SaveGuardianRequest r, StudentService s, CancellationToken ct) => s.AddGuardianAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapPut("/students/{id:guid}/guardians/{guardianId:guid}", (Guid id, Guid guardianId, SaveGuardianRequest r, StudentService s, CancellationToken ct) => s.UpdateGuardianAsync(id, guardianId, r, ct)).RequireAuthorization(admin);
        school.MapPost("/students/{id:guid}/guardians/{guardianId:guid}/primary", (Guid id, Guid guardianId, StudentService s, CancellationToken ct) => s.SetPrimaryGuardianAsync(id, guardianId, ct)).RequireAuthorization(admin);
        school.MapDelete("/students/{id:guid}/guardians/{guardianId:guid}", async (Guid id, Guid guardianId, StudentService s, CancellationToken ct) => { await s.RemoveGuardianAsync(id, guardianId, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        // Guardian invites: leadership only. The parent's side (invite page, accept, portal) is in ParentEndpoints.
        school.MapPost("/guardians/{id:guid}/invite", (Guid id, GuardianInviteService s, CancellationToken ct) => s.InviteAsync(id, ct)).RequireAuthorization(admin);
        school.MapPost("/guardians/invite-all", (GuardianInviteService s, CancellationToken ct) => s.InviteAllAsync(ct)).RequireAuthorization(admin);
        // The invites page: guardians of a class's active students (optionally only those with no email), with their phone and children. Leadership and the bursar.
        school.MapGet("/guardians", (bool? withoutEmail, Guid? classId, StudentService s, CancellationToken ct) => s.GuardiansAsync(withoutEmail == true, classId, ct))
            .RequireAuthorization(Policies.Reports);

        // Import from Excel or CSV: leadership only. The preview holds the rows for 30 minutes; the confirm saves them all or none.
        school.MapGet("/students/import/template.xlsx", async (StudentImportService s, CancellationToken ct) =>
            Results.File(await s.XlsxTemplateAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "schoollink-students-template.xlsx")).RequireAuthorization(admin);
        school.MapGet("/students/import/template.csv", async (StudentImportService s, CancellationToken ct) =>
            Results.File(await s.CsvTemplateAsync(ct), "text/csv", "schoollink-students-template.csv")).RequireAuthorization(admin);
        school.MapPost("/students/import/preview", (IFormFile file, StudentImportService s, CancellationToken ct) => s.PreviewAsync(file.ToUpload(), ct))
            .RequireAuthorization(admin).DisableAntiforgery();
        school.MapPost("/students/import/{token}/confirm", (string token, StudentImportService s, CancellationToken ct) => s.ConfirmAsync(token, ct)).RequireAuthorization(admin);

        // Every other list input imports through one engine. The kind decides who may use it (its access check runs before anything is looked
        // up); the scope (class, term, date...) is the query string. A preview is held 30 minutes for the user who uploaded it.
        school.MapGet("/imports/{kind}/template.xlsx", async (string kind, HttpRequest req, ImportEngine s, CancellationToken ct) =>
        {
            var file = await s.TemplateAsync(kind, Scope(req), csv: false, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });
        school.MapGet("/imports/{kind}/template.csv", async (string kind, HttpRequest req, ImportEngine s, CancellationToken ct) =>
        {
            var file = await s.TemplateAsync(kind, Scope(req), csv: true, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });
        school.MapPost("/imports/{kind}/preview", (string kind, IFormFile? file, HttpRequest req, ImportEngine s, CancellationToken ct) =>
            s.PreviewAsync(kind, Scope(req), file?.ToUpload(), ct)).DisableAntiforgery();
        school.MapPost("/imports/{token}/confirm", (string token, ImportEngine s, CancellationToken ct) => s.ConfirmAsync(token, ct));

        // ID cards: leadership and the bursar. The photo bytes are read on the server; the PDF carries no links.
        school.MapGet("/students/id-cards.pdf", async (Guid? classId, string? studentIds, IdCardService s, CancellationToken ct) =>
            Results.File(await s.RenderAsync(classId, studentIds, ct), "application/pdf", "student-id-cards.pdf")).RequireAuthorization(Policies.Reports);

        // Start next session: leadership only. The preview proposes each class's next class; the post saves everything or nothing.
        school.MapGet("/promotion/preview", (PromotionService s, CancellationToken ct) => s.PreviewAsync(ct)).RequireAuthorization(admin);
        school.MapPost("/promotion", (PromotionRequest r, PromotionService s, CancellationToken ct) => s.PromoteAsync(r, ct)).RequireAuthorization(admin);

        // Student logins: leadership sets or resets the password (JSS/SSS only). The student signs in on the school's public page.
        school.MapPost("/students/{id:guid}/login", (Guid id, SetStudentPasswordRequest r, StudentLoginService s, CancellationToken ct) => s.SetPasswordAsync(id, r, ct)).RequireAuthorization(admin);
        app.MapPost("/api/school/{slug}/student-login", (string slug, StudentLoginRequest r, StudentLoginService s, CancellationToken ct) => s.LoginAsync(slug, r, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Auth).WithTags("Auth");
        app.MapGet("/api/school/{slug}/public", (string slug, SchoolPublicService s, CancellationToken ct) => s.GetAsync(slug, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Preview).WithTags("School");

        // Signed photo links carry their own proof, so they sit outside the signed-in group. Short-lived; never cached.
        app.MapGet("/api/school/photos/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, StudentService s, HttpContext http, CancellationToken ct) =>
        {
            var photo = await s.PhotoByLinkAsync(id, expires, signature, ct);
            if (photo is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return photo.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(photo.Content!, "image/webp");
        }).AllowAnonymous().ExcludeFromDescription();

        // Attendance: leadership and the teachers of a class take its register; the summary is leadership only.
        school.MapGet("/attendance/summary", (DateOnly? date, AttendanceService s, CancellationToken ct) => s.SummaryAsync(date, ct)).RequireAuthorization(admin);
        school.MapGet("/attendance/{classId:guid}/{date}", (Guid classId, DateOnly date, AttendanceService s, CancellationToken ct) => s.GetAsync(classId, date, ct));
        school.MapPut("/attendance/{classId:guid}/{date}", (Guid classId, DateOnly date, SaveRegisterRequest r, AttendanceService s, CancellationToken ct) => s.SaveAsync(classId, date, r, ct));
        school.MapGet("/students/{id:guid}/attendance", (Guid id, Guid? termId, AttendanceService s, CancellationToken ct) => s.StudentAsync(id, termId, ct));

        // Trait ratings: the class teacher of the child's class, or leadership. The bursar is refused before the student is looked up.
        school.MapGet("/students/{id:guid}/traits", (Guid id, Guid? termId, TraitKind? kind, TraitService s, CancellationToken ct) => s.GetRatingsAsync(id, termId, kind, ct));
        school.MapPut("/students/{id:guid}/traits", (Guid id, Guid? termId, TraitKind? kind, SaveTraitRatingsRequest r, TraitService s, CancellationToken ct) => s.SaveRatingsAsync(id, termId, kind, r, ct));

        // Developmental ratings: the class teacher of the child's class, or leadership. The bursar is refused before the student is looked up.
        school.MapGet("/students/{id:guid}/developmental", (Guid id, Guid? termId, DevelopmentalService s, CancellationToken ct) => s.GetRatingsAsync(id, termId, ct));
        school.MapPut("/students/{id:guid}/developmental", (Guid id, Guid? termId, SaveDevelopmentalRatingsRequest r, DevelopmentalService s, CancellationToken ct) => s.SaveRatingsAsync(id, termId, r, ct));

        // Assignments: the subject teacher and leadership set and mark them. A counted mark writes the same score cell as the sheet.
        // The file is private and is read through a signed link.
        school.MapGet("/assignments", (Guid classSubjectId, Guid termId, AssignmentService s, CancellationToken ct) => s.ListAsync(classSubjectId, termId, ct));
        school.MapPost("/assignments", (CreateAssignmentRequest r, AssignmentService s, CancellationToken ct) => s.CreateAsync(r, ct));
        school.MapPut("/assignments/{id:guid}/submissions/{enrolmentId:guid}", (Guid id, Guid enrolmentId, MarkAssignmentRequest r, AssignmentService s, CancellationToken ct) => s.MarkAsync(id, enrolmentId, r, ct));

        // Lesson plans: the subject teacher writes a draft, and leadership may write one too. A draft is visible only to its author.
        // Submit asks leadership to review, unless the school has turned review off. The bursar is refused before the plan is looked up.
        school.MapGet("/lesson-plans", (Guid classSubjectId, Guid termId, LessonPlanService s, CancellationToken ct) => s.ListAsync(classSubjectId, termId, ct));
        school.MapPost("/lesson-plans", (SaveLessonPlanRequest r, LessonPlanService s, CancellationToken ct) => s.CreateAsync(r, ct));
        school.MapGet("/lesson-plans/{id:guid}", (Guid id, LessonPlanService s, CancellationToken ct) => s.GetAsync(id, ct));
        school.MapPut("/lesson-plans/{id:guid}", (Guid id, SaveLessonPlanRequest r, LessonPlanService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));
        school.MapPost("/lesson-plans/{id:guid}/submit", (Guid id, LessonPlanService s, CancellationToken ct) => s.SubmitAsync(id, ct));
        school.MapPost("/lesson-plans/{id:guid}/accept", (Guid id, LessonPlanService s, CancellationToken ct) => s.AcceptAsync(id, ct));
        school.MapPost("/lesson-plans/{id:guid}/return", (Guid id, ReturnLessonPlanRequest r, LessonPlanService s, CancellationToken ct) => s.ReturnAsync(id, r, ct));

        // Exam timetable: leadership writes the period and its sittings, then publishes. Teachers read published sittings for
        // subjects they teach. The bursar is refused before a period is looked up. A class clash or a venue clash is refused;
        // an invigilator who is already booked is saved and warned about.
        school.MapGet("/exam-periods", (Guid termId, ExamTimetableService s, CancellationToken ct) => s.ListAsync(termId, ct));
        school.MapPost("/exam-periods", (SaveExamPeriodRequest r, ExamTimetableService s, CancellationToken ct) => s.CreatePeriodAsync(r, ct)).RequireAuthorization(admin);
        school.MapGet("/exam-periods/{id:guid}", (Guid id, ExamTimetableService s, CancellationToken ct) => s.GetAsync(id, ct));
        school.MapPost("/exam-periods/{id:guid}/sittings", (Guid id, SaveExamSittingRequest r, ExamTimetableService s, CancellationToken ct) => s.AddSittingAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapPost("/exam-periods/{id:guid}/publish", (Guid id, ExamTimetableService s, CancellationToken ct) => s.PublishAsync(id, ct)).RequireAuthorization(admin);
        school.MapGet("/exam-sittings/{id:guid}", (Guid id, ExamTimetableService s, CancellationToken ct) => s.GetSittingAsync(id, ct));
        school.MapPut("/exam-sittings/{id:guid}", (Guid id, SaveExamSittingRequest r, ExamTimetableService s, CancellationToken ct) => s.UpdateSittingAsync(id, r, ct)).RequireAuthorization(admin);
        school.MapDelete("/exam-sittings/{id:guid}", async (Guid id, ExamTimetableService s, CancellationToken ct) => { await s.DeleteSittingAsync(id, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        // Question bank and CBT papers: the subject teacher, or leadership, writes them. A teacher of another subject gets 404.
        // The bursar is refused before a subject, a paper, or an attempt is looked up. Students sit a published paper on the student routes.
        school.MapGet("/questions", (Guid? subjectId, Guid? levelId, CbtService s, CancellationToken ct) => s.ListQuestionsAsync(subjectId, levelId, ct));
        school.MapPost("/questions", (SaveBankQuestionRequest r, CbtService s, CancellationToken ct) => s.CreateQuestionAsync(r, ct));
        school.MapPost("/questions/{id:guid}/archive", (Guid id, CbtService s, CancellationToken ct) => s.SetArchivedAsync(id, true, ct));
        school.MapPost("/questions/{id:guid}/unarchive", (Guid id, CbtService s, CancellationToken ct) => s.SetArchivedAsync(id, false, ct));
        school.MapGet("/cbt/papers", (Guid classSubjectId, Guid termId, CbtService s, CancellationToken ct) => s.ListPapersAsync(classSubjectId, termId, ct));
        school.MapGet("/cbt/papers/{id:guid}/attempts", (Guid id, CbtService s, CancellationToken ct) => s.AttemptsAsync(id, ct));
        school.MapPost("/cbt/papers", async (CreateCbtPaperRequest r, CbtService s, CancellationToken ct) =>
        {
            var paper = await s.CreatePaperAsync(r, ct);
            return Results.Created($"/api/school/cbt/papers/{paper.Id}", paper);
        });
        school.MapPost("/cbt/papers/{id:guid}/publish", (Guid id, CbtService s, CancellationToken ct) => s.PublishAsync(id, ct));
        school.MapPost("/cbt/papers/{id:guid}/release", (Guid id, CbtService s, CancellationToken ct) => s.ReleaseAsync(id, ct));
        school.MapPost("/cbt/attempts/{id:guid}/reopen", (Guid id, CbtService s, CancellationToken ct) => s.ReopenAsync(id, ct));

        // Play: a teacher or leadership writes word lists and brain teasers; a class-subject list needs a teacher of that class subject.
        // The bursar is refused before a class subject or a level is looked up. Leadership turns Play on or off for the school.
        school.MapGet("/play/words", (PlayService s, CancellationToken ct) => s.ListWordListsAsync(ct));
        school.MapPost("/play/words", (SaveWordListRequest r, PlayService s, CancellationToken ct) => s.CreateWordListAsync(r, ct));
        school.MapGet("/play/teasers", (PlayService s, CancellationToken ct) => s.ListTeasersAsync(ct));
        school.MapPost("/play/teasers", (SaveBrainTeaserRequest r, PlayService s, CancellationToken ct) => s.CreateTeaserAsync(r, ct));
        school.MapGet("/settings/play", (PlayService s, CancellationToken ct) => s.GetSettingsAsync(ct)).RequireAuthorization(admin);
        school.MapPut("/settings/play", (PlaySettingsRequest r, PlayService s, CancellationToken ct) => s.SetEnabledAsync(r, ct)).RequireAuthorization(admin);
        app.MapGet("/api/school/assignment-files/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, AssignmentService s, HttpContext http, CancellationToken ct) =>
        {
            var file = await s.FileByLinkAsync(id, expires, signature, ct);
            if (file is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return file.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(file.Content!, file.ContentType, file.FileName);
        }).AllowAnonymous().ExcludeFromDescription();

        // Scores: the subject teacher and leadership enter them (the class teacher reads every subject of their class); only leadership locks a class's term.
        school.MapGet("/scores", (Guid classSubjectId, Guid termId, ScoreService s, CancellationToken ct) => s.GetAsync(classSubjectId, termId, ct));
        school.MapPut("/scores", (SaveScoresRequest r, ScoreService s, CancellationToken ct) => s.SaveAsync(r, ct));
        school.MapPost("/results/{classId:guid}/{termId:guid}/lock", (Guid classId, Guid termId, ScoreService s, CancellationToken ct) => s.LockAsync(classId, termId, ct)).RequireAuthorization(admin);
        school.MapPost("/results/{classId:guid}/{termId:guid}/unlock", (Guid classId, Guid termId, ScoreService s, CancellationToken ct) => s.UnlockAsync(classId, termId, ct)).RequireAuthorization(admin);

        // Comment bank: every staff member reads it; leadership replaces it. The first read gives a school with none the template lines.
        school.MapGet("/comment-bank", (CommentBankService s, CancellationToken ct) => s.GetAsync(ct));
        school.MapPut("/comment-bank", (SaveCommentBankRequest r, CommentBankService s, CancellationToken ct) => s.SaveAsync(r, ct)).RequireAuthorization(admin);

        // Results: leadership, the class teacher, and a subject teacher of the class read the grid (a subject teacher sees only their subjects).
        // The class teacher writes the teacher remark, leadership both remarks; only leadership computes, publishes and unpublishes.
        // A subject teacher (or leadership) writes the subject comment for a class subject they teach.
        // One child: the same readers. A teacher who does not teach that child gets 404. The bursar is refused before the child is looked up.
        // The broadsheet PDF and CSV are leadership only. A class teacher still reads the grid.
        school.MapGet("/results/{classId:guid}/{termId:guid}", (Guid classId, Guid termId, ResultService s, CancellationToken ct) => s.GetAsync(classId, termId, ct));
        school.MapGet("/results/{classId:guid}/{termId:guid}/broadsheet.pdf", async (Guid classId, Guid termId, BroadsheetService s, HttpContext http, CancellationToken ct) =>
        {
            var pdf = await s.PdfAsync(classId, termId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", "broadsheet.pdf");
        }).RequireAuthorization(admin);
        school.MapGet("/results/{classId:guid}/{termId:guid}/broadsheet.csv", async (Guid classId, Guid termId, BroadsheetService s, HttpContext http, CancellationToken ct) =>
        {
            var csv = await s.CsvAsync(classId, termId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(csv, "text/csv", "broadsheet.csv");
        }).RequireAuthorization(admin);
        school.MapGet("/results/students/{studentId:guid}", async (Guid studentId, Guid? termId, Guid? classId, bool? suggestions, ResultService results, CommentBankService comments, CancellationToken ct) =>
        {
            var child = await results.GetChildAsync(studentId, termId, classId, ct);
            if (suggestions != true) return child;
            return child with { Suggestions = await comments.SuggestAsync(child.ClassId, child.TermId, child.EnrolmentId, null, ct) };
        });
        school.MapGet("/results/{classId:guid}/{termId:guid}/suggestions", (Guid classId, Guid termId, Guid enrolmentId, Guid? classSubjectId, CommentBankService s, CancellationToken ct) => s.SuggestAsync(classId, termId, enrolmentId, classSubjectId, ct));
        school.MapPut("/results/{classId:guid}/{termId:guid}/remarks", (Guid classId, Guid termId, SetRemarksRequest r, ResultService s, CancellationToken ct) => s.SetRemarksAsync(classId, termId, r, ct));
        school.MapPut("/results/{classId:guid}/{termId:guid}/subject-comments", (Guid classId, Guid termId, SetSubjectCommentRequest r, ResultService s, CancellationToken ct) => s.SetSubjectCommentAsync(classId, termId, r, ct));
        school.MapPost("/results/{classId:guid}/{termId:guid}/compute", async (Guid classId, Guid termId, ResultService s, CancellationToken ct) => { await s.ComputeAsync(classId, termId, ct); return Results.NoContent(); }).RequireAuthorization(admin);
        school.MapPost("/results/{classId:guid}/{termId:guid}/publish", (Guid classId, Guid termId, ResultService s, CancellationToken ct) => s.PublishAsync(classId, termId, ct)).RequireAuthorization(admin);
        school.MapPost("/results/{classId:guid}/{termId:guid}/unpublish", (Guid classId, Guid termId, ResultService s, CancellationToken ct) => s.UnpublishAsync(classId, termId, ct)).RequireAuthorization(admin);

        // Report cards: leadership and the class teacher. The bundle is queued here and built by the report card job; only leadership and the requester read it.
        school.MapGet("/report-cards/{enrolmentId:guid}/{termId:guid}.pdf", async (Guid enrolmentId, Guid termId, Guid? classId, ReportCardService s, HttpContext http, CancellationToken ct) =>
        {
            var pdf = await s.RenderAsync(enrolmentId, termId, classId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", "report-card.pdf");
        });
        school.MapPost("/report-cards/{classId:guid}/{termId:guid}/bundle", (Guid classId, Guid termId, ReportCardService s, CancellationToken ct) => s.RequestBundleAsync(classId, termId, ct));
        school.MapGet("/report-cards/bundles/{id:guid}", (Guid id, ReportCardService s, CancellationToken ct) => s.GetBundleAsync(id, ct));
        app.MapGet("/api/school/report-cards/bundles/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, ReportCardService s, HttpContext http, CancellationToken ct) =>
        {
            var file = await s.BundleByLinkAsync(id, expires, signature, ct);
            if (file is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return file.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(file.Content!, "application/pdf", "report-cards.pdf");
        }).AllowAnonymous().ExcludeFromDescription();

        // Branding: leadership sets the name, motto, address, logo and the principal's signature (private images, resized on upload).
        school.MapGet("/settings/lesson-plans", (LessonPlanService s, CancellationToken ct) => s.GetSettingsAsync(ct)).RequireAuthorization(admin);
        school.MapPut("/settings/lesson-plans", (LessonPlanSettingsRequest r, LessonPlanService s, CancellationToken ct) => s.SetNeedReviewAsync(r, ct)).RequireAuthorization(admin);
        school.MapGet("/settings/profile", (SchoolBrandingService s, CancellationToken ct) => s.GetProfileAsync(ct)).RequireAuthorization(admin);
        school.MapPut("/settings/profile",(SaveSchoolProfileRequest r, SchoolBrandingService s, CancellationToken ct) => s.UpdateProfileAsync(r, ct)).RequireAuthorization(admin);
        school.MapPut("/settings/logo", (IFormFile file, SchoolBrandingService s, CancellationToken ct) => s.UploadLogoAsync(file.ToUpload(), ct))
            .RequireAuthorization(admin).DisableAntiforgery();
        school.MapDelete("/settings/logo", async (SchoolBrandingService s, CancellationToken ct) => { await s.DeleteLogoAsync(ct); return Results.NoContent(); }).RequireAuthorization(admin);
        school.MapPut("/settings/signature", (IFormFile file, SchoolBrandingService s, CancellationToken ct) => s.UploadSignatureAsync(file.ToUpload(), ct))
            .RequireAuthorization(admin).DisableAntiforgery();
        school.MapDelete("/settings/signature", async (SchoolBrandingService s, CancellationToken ct) => { await s.DeleteSignatureAsync(ct); return Results.NoContent(); }).RequireAuthorization(admin);
        app.MapGet("/api/school/logos/{tenantId:guid}/{expires:long}/{signature}", async (Guid tenantId, long expires, string signature, SchoolPublicService s, HttpContext http, CancellationToken ct) =>
        {
            var logo = await s.LogoByLinkAsync(tenantId, expires, signature, ct);
            if (logo is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return logo.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(logo.Content!, "image/webp");
        }).AllowAnonymous().ExcludeFromDescription();

        // Fees: Owner, Admin and the bursar (Accountant) only.
        var fees = school.MapGroup("").RequireAuthorization(Policies.Reports);
        fees.MapGet("/fee-items", (FeeService s, CancellationToken ct) => s.ListItemsAsync(ct));
        fees.MapPost("/fee-items", (SaveFeeItemRequest r, FeeService s, CancellationToken ct) => s.CreateItemAsync(r, ct));
        fees.MapPut("/fee-items/{id:guid}", (Guid id, SaveFeeItemRequest r, FeeService s, CancellationToken ct) => s.UpdateItemAsync(id, r, ct));
        fees.MapDelete("/fee-items/{id:guid}", async (Guid id, FeeService s, CancellationToken ct) => { await s.DeleteItemAsync(id, ct); return Results.NoContent(); });
        fees.MapGet("/fee-structure/{termId:guid}", (Guid termId, FeeService s, CancellationToken ct) => s.GetStructureAsync(termId, ct));
        fees.MapPut("/fee-structure/{termId:guid}/{levelId:guid}", (Guid termId, Guid levelId, SaveFeeStructureRequest r, FeeService s, CancellationToken ct) => s.SaveStructureAsync(termId, levelId, r, ct));
        fees.MapPost("/fee-structure/{termId:guid}/copy-from/{fromTermId:guid}", (Guid termId, Guid fromTermId, FeeService s, CancellationToken ct) => s.CopyStructureAsync(termId, fromTermId, ct));
        fees.MapPost("/invoices/generate", (GenerateInvoicesRequest r, FeeService s, CancellationToken ct) => s.GenerateAsync(r, ct));
        fees.MapGet("/invoices", (Guid? termId, Guid? classId, string? status, int? skip, int? take, FeeService s, CancellationToken ct) => s.ListAsync(termId, classId, status, skip, take, ct));
        fees.MapGet("/invoices/{id:guid}", (Guid id, FeeService s, CancellationToken ct) => s.GetAsync(id, ct));
        fees.MapPut("/invoices/{id:guid}/optional-items", (Guid id, OptionalItemsRequest r, FeeService s, CancellationToken ct) => s.OptionalItemsAsync(id, r, ct));
        fees.MapPost("/invoices/{id:guid}/discount", (Guid id, DiscountRequest r, FeeService s, CancellationToken ct) => s.DiscountAsync(id, r, ct));
        fees.MapPut("/invoices/{id:guid}/instalments", (Guid id, List<InstalmentInput>? r, FeeService s, CancellationToken ct) => s.InstalmentsAsync(id, r, ct));

        // Payments: the same fee roles record manual payments, reverse payments and print receipts. Payments are never edited.
        fees.MapPost("/invoices/{id:guid}/payments", (Guid id, RecordPaymentRequest r, FeePaymentService s, CancellationToken ct) => s.RecordAsync(id, r, ct));
        fees.MapPost("/payments/{id:guid}/reverse", (Guid id, ReversePaymentRequest r, FeePaymentService s, CancellationToken ct) => s.ReverseAsync(id, r, ct));
        fees.MapGet("/payments/{id:guid}/receipt.pdf", async (Guid id, FeePaymentService s, HttpContext http, CancellationToken ct) =>
        {
            var (pdf, name) = await s.StaffReceiptAsync(id, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });

        // Cancel an invoice (refused while it has payments) and clear its review flag: same fee roles, under the fee lock, audited.
        fees.MapPost("/invoices/{id:guid}/cancel", (Guid id, CancelInvoiceRequest r, FinanceService s, CancellationToken ct) => s.CancelInvoiceAsync(id, r, ct));
        fees.MapPost("/invoices/{id:guid}/reviewed", (Guid id, FinanceService s, CancellationToken ct) => s.MarkReviewedAsync(id, ct));

        // Debtors and the finance reports: Owner, Admin and the bursar. The results hold and reminder schedule are read by them and changed by leadership.
        fees.MapGet("/debtors", (Guid? termId, Guid? classId, int? minDaysOverdue, FinanceService s, CancellationToken ct) => s.DebtorsAsync(termId, classId, minDaysOverdue, ct));
        fees.MapGet("/debtors/whatsapp", (Guid? termId, int? minDaysOverdue, FinanceService s, CancellationToken ct) => s.DebtorsWhatsAppAsync(termId, minDaysOverdue, ct));
        fees.MapGet("/debtors.csv", async (Guid? termId, Guid? classId, int? minDaysOverdue, FinanceService s, HttpContext http, CancellationToken ct) =>
        {
            var csv = await s.DebtorsCsvAsync(termId, classId, minDaysOverdue, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(csv, "text/csv", "debtors.csv");
        });
        fees.MapGet("/finance/summary", (Guid? termId, FinanceService s, CancellationToken ct) => s.SummaryAsync(termId, ct));
        fees.MapGet("/finance/collections", (Guid? termId, string? by, FinanceService s, CancellationToken ct) => s.CollectionsAsync(termId, by, ct));
        fees.MapGet("/settings/finance", (FinanceService s, CancellationToken ct) => s.GetSettingsAsync(ct));
        school.MapPut("/settings/finance", (SaveFinanceSettingsRequest r, FinanceService s, CancellationToken ct) => s.UpdateSettingsAsync(r, ct)).RequireAuthorization(admin);

        // Expenses: the same fee roles. The receipt photo is private and read through a signed link.
        fees.MapGet("/expenses", (Guid? termId, DateOnly? from, DateOnly? to, string? category, int? skip, int? take, ExpenseService s, CancellationToken ct) => s.ListAsync(termId, from, to, category, skip, take, ct));
        fees.MapGet("/expenses/categories", (ExpenseService s, CancellationToken ct) => s.CategoriesAsync(ct));
        fees.MapGet("/expenses/{id:guid}", (Guid id, ExpenseService s, CancellationToken ct) => s.GetAsync(id, ct));
        fees.MapPost("/expenses", (SaveExpenseRequest r, ExpenseService s, CancellationToken ct) => s.CreateAsync(r, ct));
        fees.MapPut("/expenses/{id:guid}", (Guid id, SaveExpenseRequest r, ExpenseService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));
        fees.MapDelete("/expenses/{id:guid}", async (Guid id, ExpenseService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });
        fees.MapPost("/expenses/{id:guid}/receipt", (Guid id, IFormFile file, ExpenseService s, CancellationToken ct) => s.UploadReceiptAsync(id, file.ToUpload(), ct)).DisableAntiforgery();
        fees.MapDelete("/expenses/{id:guid}/receipt", async (Guid id, ExpenseService s, CancellationToken ct) => { await s.DeleteReceiptAsync(id, ct); return Results.NoContent(); });
        app.MapGet("/api/school/expense-receipts/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, ExpenseService s, HttpContext http, CancellationToken ct) =>
        {
            var receipt = await s.ReceiptByLinkAsync(id, expires, signature, ct);
            if (receipt is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return receipt.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(receipt.Content!, "image/webp");
        }).AllowAnonymous().ExcludeFromDescription();

        // The school's own Paystack key (money goes straight to the school): the proprietor only. Stored encrypted, never returned.
        school.MapGet("/settings/paystack", (FeePaymentService s, CancellationToken ct) => s.GetPaystackAsync(ct)).RequireAuthorization(Policies.Owner);
        school.MapPut("/settings/paystack", (SchoolPaystackKeyRequest r, FeePaymentService s, CancellationToken ct) => s.SetPaystackKeyAsync(r, ct)).RequireAuthorization(Policies.Owner);
        school.MapDelete("/settings/paystack", async (bool? force, FeePaymentService s, CancellationToken ct) => { await s.RemovePaystackKeyAsync(force == true, ct); return Results.NoContent(); }).RequireAuthorization(Policies.Owner);

        // Each school points its own Paystack webhook here; it is signed with that school's key.
        app.MapPost("/api/webhooks/paystack/school/{tenantId:guid}", async (Guid tenantId, HttpRequest request, FeePaymentService s, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var ok = await s.HandleWebhookAsync(tenantId, body, request.Headers["x-paystack-signature"],
                Saas.Infrastructure.Payments.PaystackClient.IsValidSignature, Saas.Infrastructure.Payments.PaystackClient.ParseChargeSuccess, ct);
            return ok ? Results.Ok() : Results.Unauthorized();
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Webhook).WithTags("Webhooks").ExcludeFromDescription();

        // Announcements: every member reads them; leadership and the bursar post to any audience, a teacher to their own classes (checked in the service).
        school.MapGet("/announcements", (int? skip, int? take, AnnouncementService s, CancellationToken ct) => s.ListAsync(skip, take, ct));
        school.MapPost("/announcements", (PostAnnouncementRequest r, AnnouncementService s, CancellationToken ct) => s.PostAsync(r, ct));
        school.MapDelete("/announcements/{id:guid}", async (Guid id, AnnouncementService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });
        school.MapPost("/announcements/{id:guid}/attachment", (Guid id, IFormFile file, AnnouncementService s, CancellationToken ct) => s.UploadAttachmentAsync(id, file.ToUpload(), ct))
            .DisableAntiforgery();
        app.MapGet("/api/school/announcement-files/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, AnnouncementService s, HttpContext http, CancellationToken ct) =>
        {
            var file = await s.AttachmentByLinkAsync(id, expires, signature, ct);
            if (file is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return file.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(file.Content!, file.ContentType, file.FileName);
        }).AllowAnonymous().ExcludeFromDescription();

        // Messages: leadership reads every thread and answers the office's; the bursar the office's; a teacher the threads of their classes.
        school.MapGet("/threads", (bool? mine, MessageService s, CancellationToken ct) => s.ListAsync(mine == true, ct));
        school.MapGet("/threads/{id:guid}/messages", (Guid id, MessageService s, CancellationToken ct) => s.StaffMessagesAsync(id, ct));
        school.MapPost("/threads/{id:guid}/messages", (Guid id, SendMessageRequest r, MessageService s, CancellationToken ct) => s.StaffSendAsync(id, r.Body, null, ct));
        school.MapPost("/threads/{id:guid}/messages/image", (Guid id, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? body, MessageService s, CancellationToken ct) =>
            s.StaffSendAsync(id, body, file.ToUpload(), ct)).DisableAntiforgery();
        app.MapGet("/api/school/message-images/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, MessageService s, HttpContext http, CancellationToken ct) =>
        {
            var image = await s.ImageByLinkAsync(id, expires, signature, ct);
            if (image is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return image.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(image.Content!, "image/webp");
        }).AllowAnonymous().ExcludeFromDescription();

        school.MapGet("/assessment",(Guid? levelId, AcademicService s, CancellationToken ct) => s.GetAssessmentAsync(levelId, ct));
        school.MapPut("/assessment", (SaveAssessmentRequest r, AcademicService s, CancellationToken ct) => s.SaveAssessmentAsync(r, ct)).RequireAuthorization(admin);
        school.MapDelete("/assessment/{levelId:guid}", async (Guid levelId, AcademicService s, CancellationToken ct) => { await s.ResetAssessmentAsync(levelId, ct); return Results.NoContent(); }).RequireAuthorization(admin);

        // Trait criteria: any staff member reads them; leadership replaces a kind. Null levelId is the school default, seeded on the first read.
        school.MapGet("/traits", (TraitKind? kind, Guid? levelId, TraitService s, CancellationToken ct) => s.GetCriteriaAsync(kind, levelId, ct));
        school.MapPut("/traits", (TraitKind? kind, Guid? levelId, SaveTraitCriteriaRequest r, TraitService s, CancellationToken ct) => s.SaveCriteriaAsync(kind, levelId, r, ct)).RequireAuthorization(admin);

        // Developmental areas and scale: any staff member reads them; leadership replaces them. Null levelId is the school default, seeded on the first read.
        school.MapGet("/developmental/areas", (Guid? levelId, DevelopmentalService s, CancellationToken ct) => s.GetAreasAsync(levelId, ct));
        school.MapPut("/developmental/areas", (Guid? levelId, SaveDevelopmentalAreasRequest r, DevelopmentalService s, CancellationToken ct) => s.SaveAreasAsync(levelId, r, ct)).RequireAuthorization(admin);
        school.MapGet("/developmental/scale", (Guid? levelId, DevelopmentalService s, CancellationToken ct) => s.GetScaleAsync(levelId, ct));
        school.MapPut("/developmental/scale", (Guid? levelId, SaveDevelopmentalScaleRequest r, DevelopmentalService s, CancellationToken ct) => s.SaveScaleAsync(levelId, r, ct)).RequireAuthorization(admin);

        school.MapGet("/grading", (Guid? levelId, AcademicService s, CancellationToken ct) => s.GetGradingAsync(levelId, ct));
        school.MapPut("/grading", (SaveGradingRequest r, AcademicService s, CancellationToken ct) => s.SaveGradingAsync(r, ct)).RequireAuthorization(admin);
        school.MapDelete("/grading/{levelId:guid}", async (Guid levelId, AcademicService s, CancellationToken ct) => { await s.ResetGradingAsync(levelId, ct); return Results.NoContent(); }).RequireAuthorization(admin);
    }

    /// <summary>An import's scope is its whole query string.</summary>
    private static ImportScope Scope(HttpRequest req) =>
        new(req.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase));
}
