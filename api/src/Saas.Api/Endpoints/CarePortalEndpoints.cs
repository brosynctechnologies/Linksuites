using Saas.Api.Infrastructure;
using Saas.Application.Auth;
using Saas.Application.Care;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Endpoints;

/// <summary>
/// The CareLink patient portal (Task 15): customer accounts (no clinic in the token) linked to patients by invite, as SchoolLink links
/// parents. Every route under <c>/api/care-portal/patients/{id}</c> reaches the patient only through
/// <see cref="PatientPortalAccess.RequirePatientAsync"/> (404 unlinked, then 403 for a suspended clinic, then the clinic as the tenant).
/// </summary>
public static class CarePortalEndpoints
{
    public static void MapCarePortalEndpoints(this IEndpointRouteBuilder app)
    {
        // Portal accounts are the shared customer accounts (the ones parents and learners use): password or emailed code. The code itself is
        // requested with POST /api/auth/otp/request (rate limited as otp).
        var patientAuth = app.MapGroup("/api/auth/patient").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
        patientAuth.MapPost("/signup", (LearnerSignupRequest r, AuthService s, CancellationToken ct) => s.LearnerSignupAsync(r, ct)).Validate<LearnerSignupRequest>();
        patientAuth.MapPost("/login", (LoginRequest r, AuthService s, CancellationToken ct) => s.LearnerLoginAsync(r, ct)).Validate<LoginRequest>();
        patientAuth.MapPost("/otp/verify", (OtpVerifyRequest r, AuthService s, CancellationToken ct) => s.CustomerVerifyOtpAsync(r, ct))
            .Validate<OtpVerifyRequest>().RequireRateLimiting(RateLimits.Otp);

        var portal = app.MapGroup("/api/care-portal").WithTags("Care: portal");

        // The invite page: a public preview (the clinic and the patient's first name); accepting needs a signed-in portal account.
        portal.MapGet("/invites/{token}", (string token, PatientInviteService s, CancellationToken ct) => s.PreviewAsync(token, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Preview);
        portal.MapPost("/invites/{token}/accept", (string token, AcceptPatientInviteRequest r, PatientInviteService s, CancellationToken ct) =>
            s.AcceptAsync(token, r, ct)).RequireAuthorization().RequireRateLimiting(RateLimits.PublicWrite);

        // Business tokens (staff, support's view-as) are refused by PatientPortalAccess.
        var me = app.MapGroup("/api/care-portal").RequireAuthorization().WithTags("Care: portal");
        me.MapGet("/patients", (PatientPortalAccess p, CancellationToken ct) => p.PatientsAsync(ct));

        // The record (signed notes and addenda only) and who opened it (name and role only). Neither writes RecordAccess.
        me.MapGet("/patients/{id:guid}/record", (Guid id, PortalService s, CancellationToken ct) => s.RecordAsync(id, ct));
        me.MapGet("/patients/{id:guid}/access-log", (Guid id, PortalService s, CancellationToken ct) => s.AccessLogAsync(id, ct));

        // Bills: pay online through the clinic's own Paystack key (returning to /portal/pay/return?reference=), verify, receipts.
        me.MapGet("/patients/{id:guid}/bills", (Guid id, PortalService s, CancellationToken ct) => s.BillsAsync(id, ct));
        me.MapPost("/patients/{id:guid}/bills/{billId:guid}/pay", (Guid id, Guid billId, PortalPayRequest r, PortalService s, CancellationToken ct) =>
            s.PayAsync(id, billId, r, ct)).RequireRateLimiting(RateLimits.PublicWrite);
        me.MapPost("/payments/{reference}/verify", (string reference, PortalService s, CancellationToken ct) => s.VerifyAsync(reference, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        me.MapGet("/patients/{id:guid}/receipts/{paymentId:guid}.pdf", async (Guid id, Guid paymentId, PortalService s, HttpContext http, CancellationToken ct) =>
        {
            var (pdf, name) = await s.ReceiptAsync(id, paymentId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });

        // Appointments: the clinic's doctors and their open slots, book (source Portal), cancel at least 2 hours before.
        me.MapGet("/patients/{id:guid}/doctors", (Guid id, PortalService s, CancellationToken ct) => s.DoctorsAsync(id, ct));
        me.MapGet("/patients/{id:guid}/doctors/{doctorId:guid}/slots", (Guid id, Guid doctorId, DateOnly? date, PortalService s, CancellationToken ct) =>
            s.SlotsAsync(id, doctorId, date, ct));
        me.MapGet("/patients/{id:guid}/appointments", (Guid id, PortalService s, CancellationToken ct) => s.AppointmentsAsync(id, ct));
        me.MapPost("/patients/{id:guid}/appointments", (Guid id, PortalBookRequest r, PortalService s, CancellationToken ct) => s.BookAsync(id, r, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        me.MapPost("/patients/{id:guid}/appointments/{appointmentId:guid}/cancel", (Guid id, Guid appointmentId, PortalService s, CancellationToken ct) =>
            s.CancelAsync(id, appointmentId, ct)).RequireRateLimiting(RateLimits.PublicWrite);

        // The patient's own contact details, and the patient's notices at this clinic.
        me.MapPut("/patients/{id:guid}/contact", (Guid id, PortalContactRequest r, PortalService s, CancellationToken ct) => s.UpdateContactAsync(id, r, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        me.MapGet("/patients/{id:guid}/notifications", (Guid id, PortalService s, CancellationToken ct) => s.NoticesAsync(id, ct));
    }
}
