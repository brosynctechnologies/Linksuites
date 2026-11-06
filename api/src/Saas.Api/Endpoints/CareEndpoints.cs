using Microsoft.AspNetCore.Mvc;
using Saas.Api.Infrastructure;
using Saas.Application.Common;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.Api.Endpoints;

/// <summary>CareLink: the clinic's own area. Staff roles are the shared team roles plus a clinical role; later tasks add their routes to this group.</summary>
public static class CareEndpoints
{
    public static void MapCareEndpoints(this IEndpointRouteBuilder app)
    {
        var care = app.MapGroup("/api/care").RequireAuthorization(Policies.CareProduct).WithTags("Care");

        // The emergency-access reason travels only in the X-Care-Reason header: a ?reason= query is refused, so a client that sends it
        // learns at once instead of leaking reasons into URLs and request logs.
        care.AddEndpointFilter((context, next) =>
        {
            if (context.HttpContext.Request.Query.ContainsKey("reason"))
                throw BadRequestException.Field("reason", $"Send the reason in the {RecordGuard.ReasonHeader} header, not the address.");
            return next(context);
        });

        // Owner and Admin manage settings and staff; the services check again through CareAccess.
        const string leadership = Policies.Admin;

        care.MapGet("/setup", (CareSetupService s, CancellationToken ct) => s.GetAsync(ct));
        care.MapPut("/settings/profile", (SaveClinicProfileRequest r, CareSetupService s, CancellationToken ct) => s.SaveProfileAsync(r, ct))
            .RequireAuthorization(leadership);

        care.MapGet("/staff", (CareStaffService s, CancellationToken ct) => s.ListAsync(ct)).RequireAuthorization(leadership);
        care.MapGet("/staff/me", (CareStaffService s, CancellationToken ct) => s.MeAsync(ct));
        care.MapPut("/staff/{userId:guid}", (Guid userId, SaveCareStaffRequest r, CareStaffService s, CancellationToken ct) => s.UpdateAsync(userId, r, ct))
            .RequireAuthorization(leadership);

        // Price list: everyone reads; leadership or the cashier writes (checked in the service).
        care.MapGet("/price-list", (bool? active, PriceCategory? category, PriceListService s, CancellationToken ct) => s.ListAsync(active, category, ct));
        care.MapPost("/price-list", (SavePriceItemRequest r, PriceListService s, CancellationToken ct) => s.CreateAsync(r, ct));
        care.MapPut("/price-list/{id:guid}", (Guid id, SavePriceItemRequest r, PriceListService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));

        // Patients: everyone searches and reads demographics; front desk and above register and edit; clinical facts are clinical only.
        // There is deliberately no DELETE: a patient is made inactive instead.
        care.MapGet("/patients", (string? q, PatientService s, CancellationToken ct) => s.SearchAsync(q, ct));
        care.MapPost("/patients", (SavePatientRequest r, PatientService s, CancellationToken ct) => s.RegisterAsync(r, ct));
        // A clinical caller opens the record through RecordGuard (logged); the X-Care-Reason header carries the emergency-access reason
        // when there's no care relationship. The access log is the medical director's only.
        care.MapGet("/patients/{id:guid}", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, PatientService s, CancellationToken ct) =>
            s.GetAsync(id, reason, ct));
        care.MapGet("/patients/{id:guid}/access-log", (Guid id, RecordGuard g, CancellationToken ct) => g.LogAsync(id, ct))
            .RequireAuthorization(Policies.Owner);
        care.MapPut("/patients/{id:guid}", (Guid id, SavePatientRequest r, PatientService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));
        care.MapPut("/patients/{id:guid}/clinical-facts", (Guid id, SaveClinicalFactsRequest r, PatientService s, CancellationToken ct) =>
            s.SaveClinicalFactsAsync(id, r, ct));
        care.MapPost("/patients/{id:guid}/inactive", (Guid id, PatientService s, CancellationToken ct) => s.SetInactiveAsync(id, true, ct));
        care.MapPost("/patients/{id:guid}/active", (Guid id, PatientService s, CancellationToken ct) => s.SetInactiveAsync(id, false, ct));
        // Patient portal (Task 15): front desk and leadership invite (a single-use 14-day link, emailed and offered as WhatsApp) and see or
        // revoke the portal accounts linked to the patient.
        care.MapPost("/patients/{id:guid}/invites", (Guid id, PatientInviteRequest r, PatientInviteService s, CancellationToken ct) => s.InviteAsync(id, r, ct));
        care.MapGet("/patients/{id:guid}/invites", (Guid id, PatientInviteService s, CancellationToken ct) => s.PendingAsync(id, ct));
        care.MapPost("/patients/{id:guid}/invites/{inviteId:guid}/cancel", async (Guid id, Guid inviteId, PatientInviteService s, CancellationToken ct) =>
        {
            await s.CancelInviteAsync(id, inviteId, ct);
            return Results.NoContent();
        });
        care.MapGet("/patients/{id:guid}/portal-links", (Guid id, PatientInviteService s, CancellationToken ct) => s.LinksAsync(id, ct));
        care.MapDelete("/patients/{id:guid}/portal-links/{linkId:guid}", async (Guid id, Guid linkId, PatientInviteService s, CancellationToken ct) =>
        {
            await s.RevokeAsync(id, linkId, ct);
            return Results.NoContent();
        });

        // Doctors, schedules and appointments. Hours are Lagos wall-clock times; slots and appointments travel as UTC instants. The schedule is
        // leadership's or that doctor's; slots, the doctor list and appointments are front desk and above (the cashier gets 403).
        care.MapGet("/doctors", (AppointmentService s, CancellationToken ct) => s.DoctorsAsync(ct));
        care.MapGet("/doctors/{userId:guid}/schedule", (Guid userId, AppointmentService s, CancellationToken ct) => s.GetScheduleAsync(userId, ct));
        care.MapPut("/doctors/{userId:guid}/schedule", (Guid userId, SaveScheduleRequest r, AppointmentService s, CancellationToken ct) =>
            s.SaveScheduleAsync(userId, r, ct));
        care.MapGet("/doctors/{userId:guid}/slots", (Guid userId, DateOnly? date, AppointmentService s, CancellationToken ct) =>
            s.SlotsForDeskAsync(userId, date, ct));
        care.MapGet("/appointments", (DateOnly? date, Guid? doctorUserId, Guid? patientId, AppointmentStatus? status, AppointmentService s, CancellationToken ct) =>
            s.ListAsync(date, doctorUserId, patientId, status, ct));
        care.MapPost("/appointments", (BookRequest r, AppointmentService s, CancellationToken ct) => s.BookForDeskAsync(r, ct));
        care.MapPut("/appointments/{id:guid}", (Guid id, RescheduleRequest r, AppointmentService s, CancellationToken ct) => s.RescheduleAsync(id, r, ct));
        care.MapPost("/appointments/{id:guid}/cancel", (Guid id, AppointmentService s, CancellationToken ct) => s.CancelAsync(id, ct));

        // Visits and the queue: front desk and above check in; everyone reads the queue and a visit (demographics, stage, urgency, wait);
        // vital signs are clinical (triage by doctors and nurses; reads through RecordGuard, with the X-Care-Reason header for emergencies).
        care.MapPost("/visits", (CheckInRequest r, VisitService s, CancellationToken ct) => s.CheckInAsync(r, ct));
        care.MapGet("/queue", (DateOnly? date, bool? mine, VisitService s, CancellationToken ct) => s.QueueAsync(date, mine, ct));
        care.MapGet("/visits/{id:guid}", (Guid id, VisitService s, CancellationToken ct) => s.GetAsync(id, ct));
        care.MapGet("/visits/{id:guid}/vitals", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, VisitService s, CancellationToken ct) =>
            s.VitalsAsync(id, reason, ct));
        care.MapPost("/visits/{id:guid}/triage", (Guid id, TriageRequest r, VisitService s, CancellationToken ct) => s.TriageAsync(id, r, ct));
        care.MapPost("/visits/{id:guid}/stage", (Guid id, StageRequest r, VisitService s, CancellationToken ct) => s.SetStageAsync(id, r, ct));

        // Consultations: the visit's doctor (or the medical director) opens one per visit through RecordGuard; only the author saves or
        // signs the draft; a signed note is final and any clinical staff member adds addenda (X-Care-Reason header for emergency access).
        care.MapPost("/visits/{id:guid}/consultation", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, ConsultationService s, CancellationToken ct) =>
            s.OpenAsync(id, reason, ct));
        care.MapPut("/consultations/{id:guid}", (Guid id, SaveConsultationRequest r, ConsultationService s, CancellationToken ct) => s.SaveAsync(id, r, ct));
        care.MapPost("/consultations/{id:guid}/sign", (Guid id, ConsultationService s, CancellationToken ct) => s.SignAsync(id, ct));
        care.MapPost("/notes/{kind}/{id:guid}/addenda", (string kind, Guid id, AddAddendumRequest r, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason,
            ConsultationService s, CancellationToken ct) => s.AddAddendumAsync(kind, id, r, reason, ct));
        care.MapGet("/diagnosis-codes", (string? q, ConsultationService s, CancellationToken ct) => s.CodesAsync(q, ct));
        // The patient record timeline: clinical only, through RecordGuard (area record).
        care.MapGet("/patients/{id:guid}/record", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, RecordTimelineService s, CancellationToken ct) =>
            s.GetAsync(id, reason, ct));

        // Prescriptions and tests: doctors prescribe and request from their own visit's consultation (writes, not logged opens); an allergy
        // match needs allergyAcknowledged. Doctors and nurses record results (text and/or a file, JSON or multipart). The PDF and the
        // result file's signed link are clinical reads through RecordGuard (X-Care-Reason header for emergency access).
        care.MapPost("/prescriptions", (CreatePrescriptionRequest r, PrescriptionService s, CancellationToken ct) => s.CreateAsync(r, ct));
        care.MapGet("/prescriptions/{id:guid}/pdf", async (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, PrescriptionService s,
            HttpContext http, CancellationToken ct) =>
        {
            var (pdf, name) = await s.PdfAsync(id, reason, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });
        care.MapPost("/tests", (CreateTestRequest r, TestService s, CancellationToken ct) => s.RequestAsync(r, ct));
        care.MapPost("/tests/{id:guid}/result", async (Guid id, HttpRequest request, TestService s, CancellationToken ct) =>
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                return await s.RecordResultAsync(id, form["text"].ToString(), file?.ToUpload(), ct);
            }
            var body = request.ContentLength is 0 ? null : await request.ReadFromJsonAsync<RecordResultRequest>(ct);
            return await s.RecordResultAsync(id, body?.Text, null, ct);
        }).DisableAntiforgery();
        care.MapGet("/tests/{id:guid}/file", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, TestService s, CancellationToken ct) =>
            s.FileLinkAsync(id, reason, ct));
        // Post-build: a doctor or the director reviews a result before the portal shows it (and the patient is told); the list of results
        // waiting for review (no description or result text).
        care.MapPost("/tests/{id:guid}/review", (Guid id, TestService s, CancellationToken ct) => s.ReviewAsync(id, ct));
        care.MapGet("/tests/awaiting-review", (TestService s, CancellationToken ct) => s.AwaitingReviewAsync(ct));
        // The signed result-file link carries its own proof (issued after a logged clinical read), so it sits outside the signed-in group.
        app.MapGet("/api/care/result-files/{id:guid}/{expires:long}/{signature}", async (Guid id, long expires, string signature, TestService s,
            HttpContext http, CancellationToken ct) =>
        {
            var file = await s.FileByLinkAsync(id, expires, signature, ct);
            if (file is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return file.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(file.Content!, file.ContentType);
        }).AllowAnonymous().ExcludeFromDescription().WithTags("Care");

        // Bills: everyone reads (clinical references only for clinical staff); the cashier or leadership adds manual lines and removes lines.
        care.MapGet("/bills", (Guid? patientId, BillStatus? status, BillingService s, CancellationToken ct) => s.ListAsync(patientId, status, ct));
        care.MapGet("/bills/{id:guid}", (Guid id, BillingService s, CancellationToken ct) => s.GetAsync(id, ct));
        care.MapPost("/bills/{id:guid}/lines", (Guid id, AddBillLineRequest r, BillingService s, CancellationToken ct) => s.AddManualLineAsync(id, r, ct));
        care.MapPost("/bills/{id:guid}/lines/{lineId:guid}/remove", (Guid id, Guid lineId, RemoveBillLineRequest r, BillingService s, CancellationToken ct) =>
            s.RemoveLineAsync(id, lineId, r, ct));

        // Wards and beds: everyone reads (bed status and demographics only); leadership sets them up (active beds count against the plan,
        // 402 above it). Front desk and clinical staff mark a cleaned bed ready.
        care.MapGet("/wards", (WardService s, CancellationToken ct) => s.ListAsync(ct));
        care.MapPost("/wards", (SaveWardRequest r, WardService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequireAuthorization(leadership);
        care.MapPut("/wards/{id:guid}", (Guid id, SaveWardRequest r, WardService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .RequireAuthorization(leadership);
        care.MapDelete("/wards/{id:guid}", async (Guid id, WardService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(leadership);
        care.MapGet("/wards/{id:guid}/beds", (Guid id, WardService s, CancellationToken ct) => s.BedsAsync(id, ct));
        care.MapPost("/wards/{id:guid}/beds", (Guid id, SaveBedRequest r, WardService s, CancellationToken ct) => s.AddBedAsync(id, r, ct))
            .RequireAuthorization(leadership);
        care.MapPut("/wards/{id:guid}/beds/{bedId:guid}", (Guid id, Guid bedId, SaveBedRequest r, WardService s, CancellationToken ct) =>
            s.UpdateBedAsync(id, bedId, r, ct)).RequireAuthorization(leadership);
        care.MapDelete("/wards/{id:guid}/beds/{bedId:guid}", async (Guid id, Guid bedId, WardService s, CancellationToken ct) =>
        {
            await s.DeleteBedAsync(id, bedId, ct);
            return Results.NoContent();
        }).RequireAuthorization(leadership);
        care.MapPost("/beds/{id:guid}/ready", (Guid id, WardService s, CancellationToken ct) => s.ReadyAsync(id, ct));
        care.MapGet("/bed-board", (WardService s, CancellationToken ct) => s.BedBoardAsync(ct));
        care.MapGet("/wards/{id:guid}/patients", (Guid id, WardService s, CancellationToken ct) => s.PatientsAsync(id, ct));

        // Admissions: a doctor admits (reason and admitting diagnosis are clinical); the desk assigns the bed and records the deposit (a
        // real payment with a receipt); the desk or clinical staff transfer. One admission's clinical fields are a RecordGuard read.
        care.MapPost("/admissions", (AdmitRequest r, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, AdmissionService s, CancellationToken ct) =>
            s.AdmitAsync(r, reason, ct));
        care.MapGet("/admissions/{id:guid}", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, AdmissionService s, CancellationToken ct) =>
            s.GetAsync(id, reason, ct));
        care.MapPost("/admissions/{id:guid}/bed", (Guid id, AssignBedRequest r, AdmissionService s, CancellationToken ct) => s.AssignBedAsync(id, r, ct));
        care.MapPost("/admissions/{id:guid}/transfer", (Guid id, TransferRequest r, AdmissionService s, CancellationToken ct) => s.TransferAsync(id, r, ct));
        // Task 16: the admitting doctor or the director adds a covering doctor to the care team (a live care relationship, ending like the
        // admitting doctor's) or ends one. Audited with ids only.
        // Task 19: clinical staff read the care team (doctors' names only; not a record open).
        care.MapGet("/admissions/{id:guid}/care-team", (Guid id, AdmissionService s, CancellationToken ct) => s.CareTeamAsync(id, ct));
        care.MapPost("/admissions/{id:guid}/care-team", (Guid id, CareTeamRequest r, AdmissionService s, CancellationToken ct) =>
            s.AddCoveringDoctorAsync(id, r, ct));
        care.MapDelete("/admissions/{id:guid}/care-team/{userId:guid}", async (Guid id, Guid userId, AdmissionService s, CancellationToken ct) =>
        {
            await s.RemoveCoveringDoctorAsync(id, userId, ct);
            return Results.NoContent();
        });

        // Ward care: nurses, doctors and the director write nursing notes, fluids and doses; doctors and the director write ward rounds
        // (signed as consultations are). Writes need a care relationship with the admission (or the director) and an admitted patient;
        // reads are clinical opens through RecordGuard (X-Care-Reason header for emergency access).
        care.MapPost("/admissions/{id:guid}/nursing-notes", (Guid id, NursingNoteRequest r, WardCareService s, CancellationToken ct) =>
            s.AddNursingNoteAsync(id, r, ct));
        care.MapGet("/admissions/{id:guid}/nursing-notes", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, WardCareService s,
            CancellationToken ct) => s.NursingNotesAsync(id, reason, ct));
        care.MapPost("/admissions/{id:guid}/fluids", (Guid id, FluidRequest r, WardCareService s, CancellationToken ct) => s.AddFluidAsync(id, r, ct));
        care.MapGet("/admissions/{id:guid}/fluids", (Guid id, DateOnly? date, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, WardCareService s,
            CancellationToken ct) => s.FluidsAsync(id, date, reason, ct));
        care.MapPost("/admissions/{id:guid}/ward-rounds", (Guid id, WardRoundRequest r, WardCareService s, CancellationToken ct) => s.StartRoundAsync(id, r, ct));
        care.MapGet("/admissions/{id:guid}/ward-rounds", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, WardCareService s,
            CancellationToken ct) => s.WardRoundsAsync(id, reason, ct));
        care.MapPut("/ward-rounds/{id:guid}", (Guid id, WardRoundRequest r, WardCareService s, CancellationToken ct) => s.SaveRoundAsync(id, r, ct));
        care.MapPost("/ward-rounds/{id:guid}/sign", (Guid id, WardCareService s, CancellationToken ct) => s.SignRoundAsync(id, ct));
        care.MapGet("/admissions/{id:guid}/medication-chart", (Guid id, DateOnly? date, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason,
            WardCareService s, CancellationToken ct) => s.MedicationChartAsync(id, date, reason, ct));
        care.MapPost("/doses/{id:guid}", (Guid id, RecordDoseRequest r, WardCareService s, CancellationToken ct) => s.RecordDoseAsync(id, r, ct));
        // The author discards an unsigned ward-round draft (before discharge).
        care.MapPost("/ward-rounds/{id:guid}/discard", async (Guid id, WardCareService s, CancellationToken ct) =>
        {
            await s.DiscardRoundAsync(id, ct);
            return Results.NoContent();
        });

        // Discharge: the admission's doctor writes and signs the discharge summary (clinical text, read through RecordGuard) and discharges
        // the patient (bed to Cleaning, bill final, assignments ended, future doses stopped). The admissions list (current and recent) is for
        // leadership and clinical staff, demographics, bed and status only. The PDF is a clinical read (area pdf).
        care.MapGet("/admissions", (AdmissionStatus? status, DischargeService s, CancellationToken ct) => s.ListAsync(status, ct));
        care.MapGet("/admissions/{id:guid}/discharge-summary", (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, DischargeService s,
            CancellationToken ct) => s.GetSummaryAsync(id, reason, ct));
        care.MapPut("/admissions/{id:guid}/discharge-summary", (Guid id, SaveDischargeSummaryRequest r, Guid? draftId, DischargeService s, CancellationToken ct) =>
            s.SaveSummaryAsync(id, r, draftId, ct)); // ?draftId= the draft being edited: refused once it was discarded
        care.MapPost("/admissions/{id:guid}/discharge-summary/sign", (Guid id, DischargeService s, CancellationToken ct) => s.SignSummaryAsync(id, ct));
        // The director takes over an unsigned draft; the author or the director discards one.
        care.MapPost("/admissions/{id:guid}/discharge-summary/take-over", (Guid id, DischargeService s, CancellationToken ct) => s.TakeOverSummaryAsync(id, ct));
        care.MapPost("/admissions/{id:guid}/discharge-summary/discard", async (Guid id, DischargeService s, CancellationToken ct) =>
        {
            await s.DiscardSummaryAsync(id, ct);
            return Results.NoContent();
        });
        care.MapPost("/admissions/{id:guid}/discharge", (Guid id, DischargeRequest r, DischargeService s, CancellationToken ct) => s.DischargeAsync(id, r, ct));
        care.MapGet("/admissions/{id:guid}/discharge.pdf", async (Guid id, [FromHeader(Name = RecordGuard.ReasonHeader)] string? reason, DischargeService s,
            HttpContext http, CancellationToken ct) =>
        {
            var (pdf, name) = await s.PdfAsync(id, reason, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });

        // Payments (Task 11). The cashier and leadership record desk payments (an Idempotency-Key header or body field makes a retry return
        // the first payment) and reverse them with a reason; leadership grants discounts and waivers; every member prints a receipt (no bill
        // lines, nothing clinical). The cashier or leadership can ask Paystack about an online checkout.
        care.MapPost("/bills/{id:guid}/payments", (Guid id, DeskPaymentRequest r, [FromHeader(Name = "Idempotency-Key")] string? key, PaymentService s,
            CancellationToken ct) => s.RecordAtDeskAsync(id, r, key, ct));
        care.MapPost("/payments/{id:guid}/reverse", (Guid id, ReverseCarePaymentRequest r, PaymentService s, CancellationToken ct) => s.ReverseForStaffAsync(id, r, ct));
        care.MapGet("/payments/{id:guid}/receipt.pdf", async (Guid id, PaymentService s, HttpContext http, CancellationToken ct) =>
        {
            var (pdf, name) = await s.ReceiptAsync(id, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });
        care.MapPost("/bills/{id:guid}/adjustments", (Guid id, AdjustBillRequest r, BillingService s, CancellationToken ct) => s.AdjustAsync(id, r, ct))
            .RequireAuthorization(leadership);
        care.MapPost("/bills/{id:guid}/reviewed", (Guid id, BillingService s, CancellationToken ct) => s.MarkReviewedAsync(id, ct));
        care.MapPost("/payments/online/{reference}/verify", (string reference, OnlinePaymentService s, CancellationToken ct) => s.VerifyForStaffAsync(reference, ct));

        // The clinic's own Paystack key (money goes straight to the clinic): the medical director only, on the Clinic plan or higher (402
        // otherwise). Stored encrypted, never returned.
        care.MapGet("/settings/paystack", (OnlinePaymentService s, CancellationToken ct) => s.GetPaystackAsync(ct)).RequireAuthorization(Policies.Owner);
        care.MapPut("/settings/paystack", (CarePaystackKeyRequest r, OnlinePaymentService s, CancellationToken ct) => s.SetPaystackKeyAsync(r, ct))
            .RequireAuthorization(Policies.Owner);
        care.MapDelete("/settings/paystack", async (bool? force, OnlinePaymentService s, CancellationToken ct) =>
        {
            await s.RemovePaystackKeyAsync(force == true, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Owner);

        // Reports (Task 12): the cashier and leadership (checked in the service). Money, methods, price categories and demographics only;
        // each has a CSV (formula-safe cells, a byte order mark, local phone numbers).
        care.MapGet("/reports/takings", (DateOnly? from, DateOnly? to, CareReportService s, CancellationToken ct) => s.TakingsAsync(from, to, ct));
        care.MapGet("/reports/takings.csv", async (DateOnly? from, DateOnly? to, CareReportService s, CancellationToken ct) =>
            Results.File(await s.TakingsCsvAsync(from, to, ct), "text/csv", "takings.csv"));
        care.MapGet("/reports/debtors", (CareReportService s, CancellationToken ct) => s.DebtorsAsync(ct));
        care.MapGet("/reports/debtors.csv", async (CareReportService s, CancellationToken ct) =>
            Results.File(await s.DebtorsCsvAsync(ct), "text/csv", "debtors.csv"));
        care.MapGet("/reports/revenue", (DateOnly? from, DateOnly? to, CareReportService s, CancellationToken ct) => s.RevenueAsync(from, to, ct));
        care.MapGet("/reports/revenue.csv", async (DateOnly? from, DateOnly? to, CareReportService s, CancellationToken ct) =>
            Results.File(await s.RevenueCsvAsync(from, to, ct), "text/csv", "revenue.csv"));
        care.MapGet("/reports/deposits", (CareReportService s, CancellationToken ct) => s.DepositsAsync(ct));
        care.MapGet("/reports/deposits.csv", async (CareReportService s, CancellationToken ct) =>
            Results.File(await s.DepositsCsvAsync(ct), "text/csv", "deposits.csv"));
        // Task 16: the medical director's list of unsigned drafts (consultations on Done visits, ward rounds, discharge summaries): patient
        // number, doctor, date and age only, never the note text.
        care.MapGet("/reports/unsigned-drafts", (CareReportService s, CancellationToken ct) => s.UnsignedDraftsAsync(ct));

        // Excel/CSV import through the shared engine (care kinds only). Each kind's access check is its screen's; the preview holds the
        // demographic columns for 30 minutes for the uploader, and the confirm saves every row or none (a repeat confirm returns the result).
        care.MapGet("/imports/{kind}/template.xlsx", async (string kind, Saas.Application.School.Imports.ImportEngine s, CancellationToken ct) =>
        {
            var file = await s.TemplateAsync(Saas.Application.Billing.Products.Care, kind, Saas.Application.School.Imports.ImportScope.Empty, csv: false, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });
        care.MapGet("/imports/{kind}/template.csv", async (string kind, Saas.Application.School.Imports.ImportEngine s, CancellationToken ct) =>
        {
            var file = await s.TemplateAsync(Saas.Application.Billing.Products.Care, kind, Saas.Application.School.Imports.ImportScope.Empty, csv: true, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });
        care.MapPost("/imports/{kind}/preview", (string kind, IFormFile? file, Saas.Application.School.Imports.ImportEngine s, CancellationToken ct) =>
            s.PreviewAsync(Saas.Application.Billing.Products.Care, kind, Saas.Application.School.Imports.ImportScope.Empty, file?.ToUpload(), ct))
            .DisableAntiforgery();
        care.MapPost("/imports/{token}/confirm", (string token, Saas.Application.School.Imports.ImportEngine s, CancellationToken ct) =>
            s.ConfirmAsync(Saas.Application.Billing.Products.Care, token, ct));

        // Each clinic points its own Paystack webhook here; it is signed with that clinic's key.
        app.MapPost("/api/webhooks/paystack/care/{tenantId:guid}", async (Guid tenantId, HttpRequest request, OnlinePaymentService s, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var ok = await s.HandleWebhookAsync(tenantId, body, request.Headers["x-paystack-signature"],
                Saas.Infrastructure.Payments.PaystackClient.IsValidSignature, Saas.Infrastructure.Payments.PaystackClient.ParseChargeSuccess, ct);
            return ok ? Results.Ok() : Results.Unauthorized();
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Webhook).WithTags("Webhooks").ExcludeFromDescription();
    }
}
