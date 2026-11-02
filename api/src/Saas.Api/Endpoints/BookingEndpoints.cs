using Saas.Api.Infrastructure;
using Saas.Application.Bookings;
using Saas.Domain.Bookings;
using Saas.Domain.Core;
using Saas.Infrastructure.Payments;

namespace Saas.Api.Endpoints;

/// <summary>Booking System: settings, services, staff, calendar, bookings, customers, reports, and the public booking page.</summary>
public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Policies.BookingsProduct);
        MemberRole[] settingsRoles = [MemberRole.Owner, MemberRole.Admin];
        MemberRole[] deskRoles = [MemberRole.Owner, MemberRole.Admin, MemberRole.Staff];

        var business = api.MapGroup("/booking-business").WithTags("Bookings: settings").WritableBy(settingsRoles);
        business.MapGet("/", (BookingSetupService s, CancellationToken ct) => s.GetBusinessAsync(ct));
        business.MapPut("/", (UpdateBookingBusinessRequest r, BookingSetupService s, CancellationToken ct) => s.UpdateBusinessAsync(r, ct))
            .Validate<UpdateBookingBusinessRequest>();
        business.MapPost("/logo", (IFormFile file, BookingSetupService s, CancellationToken ct) => s.UploadImageAsync(file.ToUpload(), false, ct))
            .DisableAntiforgery();
        business.MapPost("/cover", (IFormFile file, BookingSetupService s, CancellationToken ct) => s.UploadImageAsync(file.ToUpload(), true, ct))
            .DisableAntiforgery();
        business.MapPut("/paystack", (PaystackKey r, BookingSetupService s, CancellationToken ct) => s.SetPaystackKeyAsync(r.SecretKey, ct))
            .RequireAuthorization(Policies.Owner);
        business.MapDelete("/paystack", (BookingSetupService s, CancellationToken ct) => s.RemovePaystackKeyAsync(ct))
            .RequireAuthorization(Policies.Owner);
        business.MapGet("/hours", (BookingSetupService s, CancellationToken ct) => s.GetHoursAsync(ct));
        business.MapPut("/hours", (List<HoursDto> r, BookingSetupService s, CancellationToken ct) => s.SetHoursAsync(r, ct));

        var services = api.MapGroup("/services").WithTags("Bookings: services").WritableBy(settingsRoles);
        services.MapGet("/", (BookingSetupService s, CancellationToken ct) => s.ListServicesAsync(ct));
        services.MapPost("/", (SaveServiceRequest r, BookingSetupService s, CancellationToken ct) => s.SaveServiceAsync(null, r, ct))
            .Validate<SaveServiceRequest>();
        services.MapPut("/{id:guid}", (Guid id, SaveServiceRequest r, BookingSetupService s, CancellationToken ct) => s.SaveServiceAsync(id, r, ct))
            .Validate<SaveServiceRequest>();
        services.MapDelete("/{id:guid}", async (Guid id, BookingSetupService s, CancellationToken ct) =>
        {
            await s.DeleteServiceAsync(id, ct);
            return Results.NoContent();
        });

        var staff = api.MapGroup("/staff").WithTags("Bookings: staff").WritableBy(settingsRoles);
        staff.MapGet("/", (BookingSetupService s, CancellationToken ct) => s.ListStaffAsync(ct));
        staff.MapGet("/{id:guid}", (Guid id, BookingSetupService s, CancellationToken ct) => s.GetStaffAsync(id, ct));
        staff.MapPost("/", (SaveStaffRequest r, BookingSetupService s, CancellationToken ct) => s.SaveStaffAsync(null, r, ct)).Validate<SaveStaffRequest>();
        staff.MapPut("/{id:guid}", (Guid id, SaveStaffRequest r, BookingSetupService s, CancellationToken ct) => s.SaveStaffAsync(id, r, ct))
            .Validate<SaveStaffRequest>();
        staff.MapDelete("/{id:guid}", async (Guid id, BookingSetupService s, CancellationToken ct) =>
        {
            await s.DeleteStaffAsync(id, ct);
            return Results.NoContent();
        });
        staff.MapPost("/{id:guid}/photo", (Guid id, IFormFile file, BookingSetupService s, CancellationToken ct) =>
            s.UploadStaffPhotoAsync(id, file.ToUpload(), ct)).DisableAntiforgery();
        staff.MapPut("/{id:guid}/schedule", (Guid id, List<IntervalDto> r, BookingSetupService s, CancellationToken ct) => s.SetScheduleAsync(id, r, ct));
        staff.MapGet("/{id:guid}/time-off", (Guid id, BookingSetupService s, CancellationToken ct) => s.ListTimeOffAsync(id, ct));
        staff.MapPost("/{id:guid}/time-off", (Guid id, SaveTimeOffRequest r, BookingSetupService s, CancellationToken ct) => s.AddTimeOffAsync(id, r, ct))
            .Validate<SaveTimeOffRequest>();
        staff.MapDelete("/{id:guid}/time-off/{timeOffId:guid}", async (Guid id, Guid timeOffId, BookingSetupService s, CancellationToken ct) =>
        {
            await s.DeleteTimeOffAsync(id, timeOffId, ct);
            return Results.NoContent();
        });

        api.MapGet("/availability", (Guid serviceId, Guid? staffId, DateOnly date, BookingService s, CancellationToken ct) =>
            s.AvailabilityAsync(serviceId, staffId, date, ct)).WithTags("Bookings");
        api.MapGet("/calendar", (DateOnly from, DateOnly to, Guid? staffId, BookingService s, CancellationToken ct) =>
            s.CalendarAsync(from, to, staffId, ct)).WithTags("Bookings");
        api.MapGet("/booking-dashboard", (BookingService s, CancellationToken ct) => s.DashboardAsync(ct)).WithTags("Bookings");
        api.MapGet("/booking-reports", (DateOnly? from, DateOnly? to, BookingService s, CancellationToken ct) => s.ReportAsync(from, to, ct))
            .WithTags("Bookings").RequireAuthorization(Policies.Reports);

        var bookings = api.MapGroup("/bookings").WithTags("Bookings").WritableBy(deskRoles);
        bookings.MapGet("/", (DateOnly? from, DateOnly? to, Guid? staffId, BookingStatus? status, string? q, int? page, int? pageSize,
            BookingService s, CancellationToken ct) => s.ListAsync(from, to, staffId, status, q, page ?? 1, pageSize ?? 50, ct));
        bookings.MapGet("/{id:guid}", (Guid id, BookingService s, CancellationToken ct) => s.GetAsync(id, ct));
        bookings.MapPost("/", (CreateBookingRequest r, BookingService s, CancellationToken ct) => s.CreateAsync(r, ct)).Validate<CreateBookingRequest>();
        bookings.MapPatch("/{id:guid}", (Guid id, RescheduleRequest r, BookingService s, CancellationToken ct) => s.RescheduleAsync(id, r, ct));
        bookings.MapPost("/{id:guid}/status", (Guid id, ChangeStatusRequest r, BookingService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct));
        bookings.MapPost("/{id:guid}/payments", (Guid id, RecordBookingPaymentRequest r, BookingService s, CancellationToken ct) =>
            s.RecordPaymentAsync(id, r, ct)).Validate<RecordBookingPaymentRequest>();
        bookings.MapPost("/{id:guid}/refund", (Guid id, RefundRequest r, BookingService s, CancellationToken ct) => s.RefundAsync(id, r, ct))
            .RequireAuthorization(Policies.Admin);

        var customers = api.MapGroup("/booking-customers").WithTags("Bookings: customers").WritableBy(deskRoles);
        customers.MapGet("/", (string? q, int? page, int? pageSize, BookingService s, CancellationToken ct) =>
            s.CustomersAsync(q, page ?? 1, pageSize ?? 50, ct));
        customers.MapGet("/{id:guid}", (Guid id, BookingService s, CancellationToken ct) => s.CustomerAsync(id, ct));
        customers.MapPut("/{id:guid}", (Guid id, UpdateCustomerRequest r, BookingService s, CancellationToken ct) => s.UpdateCustomerAsync(id, r, ct))
            .Validate<UpdateCustomerRequest>();

        // Public: booking page and the customer's manage link.
        var book = app.MapGroup("/api/public/book/{slug}").WithTags("Bookings: public");
        book.MapGet("/", (string slug, PublicBookingService s, CancellationToken ct) => s.PageAsync(slug, ct));
        book.MapGet("/days", (string slug, Guid serviceId, Guid? staffId, string month, PublicBookingService s, CancellationToken ct) =>
            s.DaysAsync(slug, serviceId, staffId, month, ct));
        book.MapGet("/availability", (string slug, Guid serviceId, Guid? staffId, DateOnly date, PublicBookingService s, CancellationToken ct) =>
            s.AvailabilityAsync(slug, serviceId, staffId, date, ct));
        book.MapPost("/bookings", (string slug, PublicBookRequest r, PublicBookingService s, CancellationToken ct) => s.BookAsync(slug, r, ct))
            .Validate<PublicBookRequest>().RequireRateLimiting(RateLimits.PublicWrite);

        var manage = app.MapGroup("/api/public/bookings/{token}").WithTags("Bookings: public");
        manage.MapGet("/", (string token, PublicBookingService s, CancellationToken ct) => s.GetAsync(token, ct));
        manage.MapPost("/pay", (string token, BookingPayRequest r, PublicBookingService s, CancellationToken ct) => s.PayAsync(token, r, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        manage.MapPost("/verify", (string token, ReferenceRequest r, PublicBookingService s, CancellationToken ct) => s.VerifyAsync(token, r.Reference, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        manage.MapPost("/cancel", (string token, PublicBookingService s, CancellationToken ct) => s.CancelAsync(token, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        manage.MapPost("/reschedule", (string token, PublicRescheduleRequest r, PublicBookingService s, CancellationToken ct) =>
            s.RescheduleAsync(token, r, ct)).RequireRateLimiting(RateLimits.PublicWrite);
        manage.MapPost("/review", (string token, ReviewRequest r, PublicBookingService s, CancellationToken ct) => s.ReviewAsync(token, r, ct))
            .Validate<ReviewRequest>().RequireRateLimiting(RateLimits.PublicWrite);
        manage.MapGet("/ics", async (string token, PublicBookingService s, CancellationToken ct) =>
            Results.Text(await s.IcsAsync(token, ct), "text/calendar; charset=utf-8"));

        app.MapGet("/api/public/staff-calendar/{feed}", async (string feed, PublicBookingService s, CancellationToken ct) =>
            Results.Text(await s.StaffFeedAsync(feed.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) ? feed[..^4] : feed, ct),
                "text/calendar; charset=utf-8")).WithTags("Bookings: public");

        app.MapPost("/api/webhooks/paystack/bookings/{tenantId:guid}", async (Guid tenantId, HttpRequest request,
            PublicBookingService s, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var ok = await s.HandleWebhookAsync(tenantId, body, request.Headers["x-paystack-signature"],
                PaystackClient.IsValidSignature, PaystackClient.ParseChargeSuccess, ct);
            return ok ? Results.Ok() : Results.Unauthorized();
        }).WithTags("Webhooks").ExcludeFromDescription();
    }

    public sealed record PaystackKey(string SecretKey);
    public sealed record ReferenceRequest(string Reference);
}
