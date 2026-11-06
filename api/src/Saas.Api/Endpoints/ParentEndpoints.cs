using Saas.Api.Infrastructure;
using Saas.Application.Auth;
using Saas.Application.School;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Endpoints;

/// <summary>
/// SchoolLink parents: customer accounts (no school in the token) linked to guardian records by invite. Every route under
/// <c>/api/parent</c> reaches a child only through <see cref="ParentAccess"/>.
/// </summary>
public static class ParentEndpoints
{
    public static void MapParentEndpoints(this IEndpointRouteBuilder app)
    {
        // Parent accounts are the shared customer accounts (the same ones learners and home-seekers use): password or emailed code.
        var parentAuth = app.MapGroup("/api/auth/parent").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
        parentAuth.MapPost("/signup", (LearnerSignupRequest r, AuthService s, CancellationToken ct) => s.LearnerSignupAsync(r, Saas.Application.Billing.Products.School, ct)).Validate<LearnerSignupRequest>();
        // Final fix pass B: the sign-up answers { confirmationRequired } and emails a code; this route (code + password) issues the session.
        parentAuth.MapPost("/signup/confirm", (ConfirmSignupRequest r, AuthService s, CancellationToken ct) => s.ConfirmSignupAsync(r, customer: true, ct))
            .Validate<ConfirmSignupRequest>().RequireRateLimiting(RateLimits.Otp);
        parentAuth.MapPost("/login", (LoginRequest r, AuthService s, CancellationToken ct) => s.LearnerLoginAsync(r, ct)).Validate<LoginRequest>();
        // The code itself is requested with POST /api/auth/otp/request.
        parentAuth.MapPost("/otp/verify", (OtpVerifyRequest r, AuthService s, CancellationToken ct) => s.CustomerVerifyOtpAsync(r, ct))
            .Validate<OtpVerifyRequest>().RequireRateLimiting(RateLimits.Otp);

        // The invite page: public preview; accepting needs a signed-in parent account.
        var invites = app.MapGroup("/api/school-invites").WithTags("School: parents");
        invites.MapGet("/{token}", (string token, GuardianInviteService s, CancellationToken ct) => s.PreviewAsync(token, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Preview);
        invites.MapPost("/{token}/accept", (string token, GuardianInviteService s, CancellationToken ct) => s.AcceptAsync(token, ct))
            .RequireAuthorization().RequireRateLimiting(RateLimits.PublicWrite);

        // The parent portal. Business tokens (staff, support's view-as) are refused by ParentAccess.
        var parent = app.MapGroup("/api/parent").RequireAuthorization().WithTags("School: parents");
        parent.MapGet("/children", (ParentAccess p, CancellationToken ct) => p.ChildrenAsync(ct));
        parent.MapGet("/children/{studentId:guid}", (Guid studentId, ParentAccess p, CancellationToken ct) => p.RequireChildAsync(studentId, ct));

        // Overview, results, report cards, attendance and the timetable: published results only, and the results hold applies.
        parent.MapGet("/children/{studentId:guid}/overview", (Guid studentId, PortalService s, CancellationToken ct) => s.ParentOverviewAsync(studentId, ct));
        parent.MapGet("/children/{studentId:guid}/results/{termId:guid}", (Guid studentId, Guid termId, PortalService s, CancellationToken ct) => s.ParentResultsAsync(studentId, termId, ct));
        parent.MapGet("/children/{studentId:guid}/report-card/{termId:guid}.pdf", async (Guid studentId, Guid termId, Guid? classId, PortalService s, HttpContext http, CancellationToken ct) =>
        {
            var pdf = await s.ParentReportCardAsync(studentId, termId, classId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", "report-card.pdf");
        });
        parent.MapGet("/children/{studentId:guid}/attendance", (Guid studentId, PortalService s, CancellationToken ct) => s.ParentAttendanceAsync(studentId, ct));
        parent.MapGet("/children/{studentId:guid}/timetable", (Guid studentId, PortalService s, CancellationToken ct) => s.ParentTimetableAsync(studentId, ct));
        parent.MapGet("/children/{studentId:guid}/exam-timetable", (Guid studentId, ExamTimetableService s, CancellationToken ct) => s.ForChildAsync(studentId, ct));

        // Fees: invoices with Pay now (the school's own Paystack account), the return verify and receipts.
        parent.MapGet("/children/{studentId:guid}/invoices", (Guid studentId, FeePaymentService s, CancellationToken ct) => s.ParentInvoicesAsync(studentId, ct));
        parent.MapPost("/children/{studentId:guid}/invoices/{invoiceId:guid}/pay", (Guid studentId, Guid invoiceId, PayOnlineRequest r, FeePaymentService s, CancellationToken ct) =>
            s.PayAsync(studentId, invoiceId, r, ct)).RequireRateLimiting(RateLimits.PublicWrite);
        parent.MapPost("/payments/{reference}/verify", (string reference, FeePaymentService s, CancellationToken ct) => s.VerifyAsync(reference, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        parent.MapGet("/payments/{id:guid}/receipt.pdf", async (Guid id, FeePaymentService s, HttpContext http, CancellationToken ct) =>
        {
            var (pdf, name) = await s.ParentReceiptAsync(id, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });

        // Announcements that reach the child, and conversations with the class teacher or the school office (text and one private image).
        parent.MapGet("/children/{studentId:guid}/announcements", (Guid studentId, AnnouncementService s, CancellationToken ct) => s.ForChildAsync(studentId, ct));
        parent.MapGet("/children/{studentId:guid}/assignments", (Guid studentId, AssignmentService s, CancellationToken ct) => s.ForChildAsync(studentId, ct));
        parent.MapGet("/children/{studentId:guid}/threads", (Guid studentId, MessageService s, CancellationToken ct) => s.ParentThreadsAsync(studentId, ct));
        parent.MapPost("/children/{studentId:guid}/threads", (Guid studentId, StartThreadRequest r, MessageService s, CancellationToken ct) => s.StartAsync(studentId, r, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        parent.MapGet("/threads/{id:guid}/messages", (Guid id, MessageService s, CancellationToken ct) => s.ParentMessagesAsync(id, ct));
        parent.MapPost("/threads/{id:guid}/messages", (Guid id, SendMessageRequest r, MessageService s, CancellationToken ct) => s.ParentSendAsync(id, r.Body, null, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        parent.MapPost("/threads/{id:guid}/messages/image", (Guid id, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? body, MessageService s, CancellationToken ct) =>
            s.ParentSendAsync(id, body, file.ToUpload(), ct)).RequireRateLimiting(RateLimits.PublicWrite).DisableAntiforgery();
    }
}
