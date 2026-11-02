using Saas.Api.Infrastructure;
using Saas.Application.School;

namespace Saas.Api.Endpoints;

/// <summary>
/// The SchoolLink student portal (JSS and SSS). A student is a customer account (no school in the token) linked to one student record;
/// every route under <c>/api/student</c> reaches the student only through <see cref="StudentAccess"/>.
/// </summary>
public static class StudentEndpoints
{
    public static void MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        var student = app.MapGroup("/api/student").RequireAuthorization().WithTags("School: students");
        student.MapGet("/me", async (StudentAccess a, CancellationToken ct) =>
        {
            var me = await a.RequireSelfAsync(ct);
            return new { name = me.Name, admissionNo = me.AdmissionNo, className = me.ClassName, schoolName = me.SchoolName };
        });
        student.MapGet("/announcements", (AnnouncementService s, CancellationToken ct) => s.ForSelfAsync(ct));
        student.MapGet("/assignments", (AssignmentService s, CancellationToken ct) => s.ForSelfAsync(ct));
        student.MapPost("/assignments/{id:guid}/submit", async (Guid id, HttpRequest request, AssignmentService s, CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            return await s.SubmitAsync(id, form["body"], file is null ? null : file.ToUpload(), ct);
        }).DisableAntiforgery();

        // Published results only, and the results hold applies, as in the parent portal.
        student.MapGet("/overview", (PortalService s, CancellationToken ct) => s.StudentOverviewAsync(ct));
        student.MapGet("/results/{termId:guid}", (Guid termId, PortalService s, CancellationToken ct) => s.StudentResultsAsync(termId, ct));
        student.MapGet("/report-card/{termId:guid}.pdf", async (Guid termId, Guid? classId, PortalService s, HttpContext http, CancellationToken ct) =>
        {
            var pdf = await s.StudentReportCardAsync(termId, classId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", "report-card.pdf");
        });
        student.MapGet("/timetable", (PortalService s, CancellationToken ct) => s.StudentTimetableAsync(ct));
        student.MapGet("/exam-timetable", (ExamTimetableService s, CancellationToken ct) => s.ForSelfAsync(ct));

        // A published paper for the student's own class, inside its window. The payload never includes the answer key.
        student.MapGet("/cbt/papers", (CbtService s, CancellationToken ct) => s.StudentPapersAsync(ct));
        student.MapPost("/cbt/papers/{id:guid}/start", (Guid id, CbtService s, CancellationToken ct) => s.StartAsync(id, ct));
        student.MapGet("/cbt/attempts/{id:guid}", (Guid id, CbtService s, CancellationToken ct) => s.GetAttemptAsync(id, ct));
        student.MapPut("/cbt/attempts/{id:guid}/answers", (Guid id, SaveCbtAnswerRequest r, CbtService s, CancellationToken ct) => s.AnswerAsync(id, r, ct));
        student.MapPost("/cbt/attempts/{id:guid}/submit", (Guid id, CbtService s, CancellationToken ct) => s.SubmitAsync(id, ct));

        // Play: a word search and brain teasers. 404 when the school has turned Play off. A teaser's answer is never sent, and Play writes no score.
        student.MapGet("/play/word-search", (Guid? classSubjectId, PlayService s, CancellationToken ct) => s.WordSearchAsync(classSubjectId, ct));
        student.MapGet("/play/teasers", (PlayService s, CancellationToken ct) => s.TeasersAsync(ct));
    }
}
