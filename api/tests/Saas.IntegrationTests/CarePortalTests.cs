using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 15: the patient portal. Portal users are customer accounts linked to patients by a single-use, 14-day invite. Every
/// portal route reaches a patient only through <c>PatientPortalAccess.RequirePatientAsync</c>: 404 for a patient the account isn't linked
/// to (another family's child, another clinic's patient), then 403 "This clinic's portal is unavailable." for a suspended clinic, then the
/// request acts as that patient's clinic. The record shows signed notes and addenda only, never drafts or staff-only fields, and reading
/// it writes a RecordAccess row marked as a portal read (final review C1). Bills are paid through the clinic's own Paystack key; patient notices name the patient's
/// first name so a guardian with several linked patients can tell them apart.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CarePortalTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string CareKey = "sk_test_integration_portal_key";
    private const string LateCancel = "This appointment starts soon. Call the clinic to change it.";
    private const string PortalUnavailable = "This clinic's portal is unavailable.";
    private const string InviteUsed = "This invitation was already used.";
    private const string InviteExpired = "This invitation has expired. Ask the clinic for a new one.";
    private const string ReasonLabel = "Reason for visit (no medical details)";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {r.RequestMessage?.Method} {r.RequestMessage?.RequestUri} {text}");
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"expected {(int)status}, got {(int)r.StatusCode} {r.RequestMessage?.Method} {r.RequestMessage?.RequestUri} {text}");
        var body = string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
        return body;
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);

    private static string TokenOf(string link) => link[(link.LastIndexOf('/') + 1)..];

    // ---------- Portal accounts and links ----------

    /// <summary>A customer account (the shared accounts parents and learners use), signed in.</summary>
    private async Task<(HttpClient Client, Guid UserId, string Email)> AccountAsync(string name = "Portal Family")
    {
        var email = $"portal-{Guid.NewGuid():N}@example.com";
        var client = api.CreateClient();
        var auth = await Json(await api.SignUpAsync(client, "/api/auth/parent/signup", new { fullName = name, email, password = "password123" }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth.GetProperty("user").GetProperty("id").GetGuid(), email);
    }

    private static async Task<JsonElement> InviteAsync(HttpClient staff, Guid patientId, string? email = null, string? phone = null) =>
        await Json(await staff.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { email, phone }, Ct));

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient portal, string token, string relationship = "Self") =>
        portal.PostAsJsonAsync($"/api/care-portal/invites/{token}/accept", new { relationship }, Ct);

    /// <summary>The clinic invites, the account accepts: the account is linked to the patient.</summary>
    private static async Task LinkAsync(HttpClient portal, CareKit.Clinic clinic, Guid patientId, string relationship = "Self")
    {
        var token = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        await Json(await AcceptAsync(portal, token, relationship));
    }

    private async Task<(HttpClient Portal, Guid UserId)> LinkedAsync(CareKit.Clinic clinic, Guid patientId, string relationship = "Self")
    {
        var (portal, userId, _) = await AccountAsync();
        await LinkAsync(portal, clinic, patientId, relationship);
        return (portal, userId);
    }

    private static string P(Guid patientId) => $"/api/care-portal/patients/{patientId}";

    // ---------- Clinic helpers ----------

    private static object[] EveryDay() => Enum.GetValues<DayOfWeek>()
        .Select(d => (object)new { weekday = d.ToString(), start = "07:00", end = "21:00", slotMinutes = 15 }).ToArray();

    private async Task<Guid> DoctorWithHoursAsync(CareKit.Clinic clinic)
    {
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = EveryDay() }, Ct));
        return doctorId;
    }

    private static async Task<List<DateTimeOffset>> PortalSlotsAsync(HttpClient portal, Guid patientId, Guid doctorId, DateOnly date) =>
        (await Json(await portal.GetAsync($"{P(patientId)}/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();

    private Task<Guid> BillAsync(CareKit.Clinic clinic, Guid patientId, Guid itemId) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b =>
        {
            var bill = await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct);
            await b.AddLineAsync(bill.Id, itemId, 1, null, Ct);
            return bill.Id;
        });

    private Task<int> AccessRowsAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId, Ct));

    private Task SetPatientStatusAsync(Guid patientId, PatientStatus status) =>
        api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == patientId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status), Ct));

    /// <summary>Sends a patient notice in the clinic's scope and saves it, as the services do.</summary>
    private async Task NoticeAsync(Guid tenantId, Func<CareNotices, Task> send)
    {
        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        await send(scope.ServiceProvider.GetRequiredService<CareNotices>());
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
    }

    private Task<List<UserNotification>> NoticesForAsync(Guid userId) =>
        api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == userId && n.Product == "care").ToListAsync(Ct));

    /// <summary>Checks the patient in, has the director open and save a consultation, and signs it when <paramref name="sign"/>.</summary>
    private async Task<Guid> ConsultationAsync(CareKit.Clinic clinic, Guid patientId, string complaint, bool sign, string diagnosis = "Signeddiagnosis")
    {
        var visitId = Id(await kit.CheckInAsync(clinic.Owner, patientId));
        var consultationId = Id(await Json(await clinic.Owner.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/consultations/{consultationId}", new
        {
            complaint, history = $"{complaint} history", examination = $"{complaint} examination", plan = $"{complaint} plan",
            diagnoses = new[] { new { text = $"{diagnosis} {complaint}", code = (string?)null, primary = true } },
        }, Ct));
        if (sign)
        {
            await Json(await clinic.Owner.PostAsync($"/api/care/consultations/{consultationId}/sign", null, Ct));
            await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "Done" }, Ct));
        }
        return consultationId;
    }

    // ---------- The brief's six tests ----------

    /// <summary>The record shows signed consultations and their addenda; a draft consultation never appears, nor does its text.</summary>
    [Fact]
    public async Task Portal_never_shows_drafts()
    {
        var clinic = await kit.ClinicAsync("Portal Draft Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Notes", "Patient"));
        var signed = await ConsultationAsync(clinic, patientId, "Signedcomplaint", sign: true);
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/notes/consultation/{signed}/addenda", new { text = "Addendummarker after review" }, Ct));
        await ConsultationAsync(clinic, patientId, "Draftcomplaint", sign: false, diagnosis: "Draftdiagnosis");
        var (portal, _) = await LinkedAsync(clinic, patientId);

        var response = await portal.GetAsync($"{P(patientId)}/record", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var record = await Json(response);
        Assert.Contains("Signedcomplaint", text);
        Assert.Contains("Addendummarker", text);
        Assert.Contains("Signeddiagnosis", text);
        Assert.DoesNotContain("Draftcomplaint", text);
        Assert.DoesNotContain("Draftdiagnosis", text);
        Assert.DoesNotContain("\"Draft\"", text);
        var note = Assert.Single(record.GetProperty("consultations").EnumerateArray());
        Assert.Equal(signed, Id(note));
        Assert.Equal("Addendummarker after review", Assert.Single(note.GetProperty("addenda").EnumerateArray()).GetProperty("text").GetString());
    }

    /// <summary>A parent linked to one child gets 404 for a sibling they aren't linked to, on every portal route, and the list leaves them out.</summary>
    [Fact]
    public async Task Unlinked_child_is_404()
    {
        var clinic = await kit.ClinicAsync("Portal Sibling Clinic");
        var linked = Id(await kit.PatientAsync(clinic, "Linked", "Child", "2015-01-01"));
        var other = Id(await kit.PatientAsync(clinic, "Other", "Child", "2016-01-01"));
        var (portal, _) = await LinkedAsync(clinic, linked, "Parent");

        foreach (var path in new[] { "record", "access-log", "bills", "appointments", "doctors", "notifications" })
            await ProblemAsync(await portal.GetAsync($"{P(other)}/{path}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await portal.PostAsJsonAsync($"{P(other)}/appointments", new { doctorUserId = Guid.NewGuid(), start = DateTimeOffset.UtcNow.AddDays(1) }, Ct),
            HttpStatusCode.NotFound);
        await ProblemAsync(await portal.PutAsJsonAsync($"{P(other)}/contact", new { phone = "08031234567" }, Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await portal.PostAsJsonAsync($"{P(other)}/bills/{Guid.NewGuid()}/pay", new { }, Ct), HttpStatusCode.NotFound);

        var list = await Json(await portal.GetAsync("/api/care-portal/patients", Ct));
        Assert.Equal(linked, Id(Assert.Single(list.EnumerateArray())));
        await Json(await portal.GetAsync($"{P(linked)}/record", Ct));
    }

    /// <summary>Another clinic's patient is 404 to an account linked only in this clinic; once linked there too, each opens under its own clinic.</summary>
    [Fact]
    public async Task Other_clinic_record_is_404()
    {
        var first = await kit.ClinicAsync("Portal First Clinic");
        var second = await kit.ClinicAsync("Portal Second Clinic");
        var mine = Id(await kit.PatientAsync(first, "Home", "Patient"));
        var theirs = Id(await kit.PatientAsync(second, "Away", "Patient"));
        await ConsultationAsync(second, theirs, "Awaycomplaint", sign: true);
        var (portal, _) = await LinkedAsync(first, mine);

        await ProblemAsync(await portal.GetAsync($"{P(theirs)}/record", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await portal.GetAsync($"{P(theirs)}/bills", Ct), HttpStatusCode.NotFound);

        await LinkAsync(portal, second, theirs);
        var away = await (await portal.GetAsync($"{P(theirs)}/record", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Awaycomplaint", away);
        var home = await (await portal.GetAsync($"{P(mine)}/record", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Awaycomplaint", home);
    }

    /// <summary>A parent linked to two children in one clinic and a third in another sees all three, with each clinic and the relationship.</summary>
    [Fact]
    public async Task Parent_sees_both_children()
    {
        var clinic = await kit.ClinicAsync("Portal Family Clinic");
        var elsewhere = await kit.ClinicAsync("Portal Family Annex");
        var ada = Id(await kit.PatientAsync(clinic, "Ada", "Family", "2014-03-03"));
        var tolu = Id(await kit.PatientAsync(clinic, "Tolu", "Family", "2017-05-05"));
        var chidi = Id(await kit.PatientAsync(elsewhere, "Chidi", "Family", "2019-07-07"));
        var (portal, _, _) = await AccountAsync("Family Parent");
        await LinkAsync(portal, clinic, ada, "Parent");
        await LinkAsync(portal, clinic, tolu, "Parent");
        await LinkAsync(portal, elsewhere, chidi, "Guardian");

        var list = (await Json(await portal.GetAsync("/api/care-portal/patients", Ct))).EnumerateArray().ToList();
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { ada, chidi, tolu }.Order(), list.Select(Id).Order());
        var adaRow = list.Single(p => Id(p) == ada);
        Assert.Equal("Ada", adaRow.GetProperty("firstName").GetString());
        Assert.Equal("Portal Family Clinic", adaRow.GetProperty("clinicName").GetString());
        Assert.Equal("Parent", adaRow.GetProperty("relationship").GetString());
        Assert.Equal("Guardian", list.Single(p => Id(p) == chidi).GetProperty("relationship").GetString());
        Assert.Equal("Portal Family Annex", list.Single(p => Id(p) == chidi).GetProperty("clinicName").GetString());
        foreach (var id in new[] { ada, tolu, chidi })
            Assert.Equal(id, Id((await Json(await portal.GetAsync($"{P(id)}/record", Ct))).GetProperty("patient")));
    }

    /// <summary>
    /// The portal pays a bill through the clinic's Paystack key: checkout returns to /portal/pay/return?reference=, the return verify
    /// records the payment once, the bill is paid, the receipt downloads, and the "payment received" notice names the patient's first name.
    /// </summary>
    [Fact]
    public async Task Portal_pays_bill_online()
    {
        var clinic = await kit.ClinicAsync("Portal Pay Clinic");
        await kit.StaffAsync(clinic, "Cashier"); // the Clinic plan has online payments
        await Json(await clinic.Owner.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = CareKey }, Ct));
        var patientId = Id(await kit.PatientAsync(clinic, "Payer", "Patient"));
        var itemId = await kit.PriceItemAsync(clinic, "Portal consultation", "Consultation", 500_000);
        var billId = await BillAsync(clinic, patientId, itemId);
        var (portal, userId) = await LinkedAsync(clinic, patientId);

        var bills = await Json(await portal.GetAsync($"{P(patientId)}/bills", Ct));
        var bill = Assert.Single(bills.EnumerateArray());
        Assert.Equal(500_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("Portal consultation", Assert.Single(bill.GetProperty("lines").EnumerateArray()).GetProperty("description").GetString());

        var checkout = await Json(await portal.PostAsJsonAsync($"{P(patientId)}/bills/{billId}/pay", new { }, Ct));
        var reference = checkout.GetProperty("reference").GetString()!;
        Assert.StartsWith("care_", reference);
        Assert.False(string.IsNullOrEmpty(checkout.GetProperty("authorizationUrl").GetString()));
        var init = api.Merchant.Initialized.Last(i => i.Reference == reference);
        Assert.EndsWith($"/portal/pay/return?reference={reference}", init.CallbackUrl);
        Assert.Equal(500_000, init.AmountMinor);

        api.Merchant.Charges[reference] = new Saas.Application.Billing.VerifiedCharge(reference, 500_000, "NGN", DateTimeOffset.UtcNow);
        var verified = await Json(await portal.PostAsync($"/api/care-portal/payments/{reference}/verify", null, Ct));
        Assert.Equal("Succeeded", verified.GetProperty("status").GetString());
        Assert.Equal(0, verified.GetProperty("billBalanceMinor").GetInt64());
        var again = await Json(await portal.PostAsync($"/api/care-portal/payments/{reference}/verify", null, Ct));
        Assert.Equal(verified.GetProperty("receiptNumber").GetString(), again.GetProperty("receiptNumber").GetString());
        Assert.Equal(1, await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().CountAsync(p => p.BillId == billId && p.Status == CarePaymentStatus.Succeeded, Ct)));

        var paid = Assert.Single((await Json(await portal.GetAsync($"{P(patientId)}/bills", Ct))).EnumerateArray());
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        var paymentId = Id(Assert.Single(paid.GetProperty("payments").EnumerateArray()));
        var receipt = await portal.GetAsync($"{P(patientId)}/receipts/{paymentId}.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Equal("application/pdf", receipt.Content.Headers.ContentType?.MediaType);

        var notice = Assert.Single(await NoticesForAsync(userId), n => n.Kind == "care.payment_received");
        Assert.Contains("Payer", notice.Body);
        Assert.DoesNotContain("Patient", notice.Body);
        Assert.Contains($"patient={patientId}", notice.Link);
    }

    /// <summary>Cancelling less than 2 hours before the start is refused with the exact sentence; an appointment days away cancels.</summary>
    [Fact]
    public async Task Late_cancel_refused()
    {
        var clinic = await kit.ClinicAsync("Portal Cancel Clinic");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var patientId = Id(await kit.PatientAsync(clinic, "Cancel", "Patient"));
        var sibling = Id(await kit.PatientAsync(clinic, "Sibling", "Patient", "2001-01-01"));
        var (portal, _) = await LinkedAsync(clinic, patientId);

        var soon = DateTimeOffset.UtcNow.AddMinutes(90);
        var soonId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        await api.WithDbAsync(db =>
        {
            db.Appointments.Add(new Appointment
            {
                Id = soonId, TenantId = clinic.TenantId, PatientId = patientId, DoctorUserId = doctorId, Start = soon, End = soon.AddMinutes(15),
                Status = AppointmentStatus.Booked, Source = AppointmentSource.Portal,
            });
            db.Appointments.Add(new Appointment
            {
                Id = siblingId, TenantId = clinic.TenantId, PatientId = sibling, DoctorUserId = doctorId, Start = soon.AddDays(5), End = soon.AddDays(5).AddMinutes(15),
                Status = AppointmentStatus.Booked, Source = AppointmentSource.Desk,
            });
            return db.SaveChangesAsync(Ct);
        });

        await ProblemAsync(await portal.PostAsync($"{P(patientId)}/appointments/{soonId}/cancel", null, Ct), HttpStatusCode.Conflict, LateCancel);
        Assert.Equal(AppointmentStatus.Booked, await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.Id == soonId).Select(a => a.Status).FirstAsync(Ct)));
        // Another patient's appointment can't be reached through this patient.
        await ProblemAsync(await portal.PostAsync($"{P(patientId)}/appointments/{siblingId}/cancel", null, Ct), HttpStatusCode.NotFound);

        var slots = await PortalSlotsAsync(portal, patientId, doctorId, Today.AddDays(3));
        var booked = await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[0], reason = "Check-up" }, Ct));
        var cancelled = await Json(await portal.PostAsync($"{P(patientId)}/appointments/{Id(booked)}/cancel", null, Ct));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
    }

    // ---------- Carries ----------

    /// <summary>A suspended clinic: the link check comes first (404 for an unlinked patient), then 403 with the portal sentence.</summary>
    [Fact]
    public async Task Suspended_clinic_is_403_after_the_link_check()
    {
        var clinic = await kit.ClinicAsync("Portal Suspended Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Suspended", "Patient"));
        var stranger = Id(await kit.PatientAsync(clinic, "Stranger", "Patient", "1980-01-01"));
        var (portal, _) = await LinkedAsync(clinic, patientId);
        var spare = TokenOf((await InviteAsync(clinic.Owner, stranger)).GetProperty("link").GetString()!);
        await Json(await portal.GetAsync($"{P(patientId)}/record", Ct));

        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == clinic.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Suspended), Ct));

        foreach (var path in new[] { "record", "bills", "appointments", "access-log" })
            await ProblemAsync(await portal.GetAsync($"{P(patientId)}/{path}", Ct), HttpStatusCode.Forbidden, PortalUnavailable);
        await ProblemAsync(await portal.GetAsync($"{P(stranger)}/record", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await AcceptAsync(portal, spare), HttpStatusCode.Forbidden, PortalUnavailable);
        Assert.Empty((await Json(await portal.GetAsync("/api/care-portal/patients", Ct))).EnumerateArray());
    }

    /// <summary>
    /// Final review C1 (ruling): every portal read of clinical data (the record, the bills with their item names) writes a RecordAccess row
    /// marked as a portal read, carrying the portal account and its relationship. The director's access log and the patient's "who opened"
    /// page both show it as "Nkem W. (parent), patient portal". Reads with nothing clinical (appointments, notices, the log itself) write none.
    /// </summary>
    [Fact]
    public async Task Portal_read_writes_a_portal_access_row_shown_in_both_logs()
    {
        var clinic = await kit.ClinicAsync("Portal Access Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Reader", "Patient"));
        await ConsultationAsync(clinic, patientId, "Readercomplaint", sign: true);
        var drug = await kit.PriceItemAsync(clinic, "Portaldrugname tablets", "Drug", 2_500);
        await BillAsync(clinic, patientId, drug);
        var (portal, portalUserId, _) = await AccountAsync("Nkem Williams");
        await LinkAsync(portal, clinic, patientId, "Parent");
        var before = await AccessRowsAsync(patientId);

        foreach (var path in new[] { "access-log", "appointments", "notifications" })
            await Json(await portal.GetAsync($"{P(patientId)}/{path}", Ct));
        Assert.Equal(before, await AccessRowsAsync(patientId));

        await Json(await portal.GetAsync($"{P(patientId)}/record", Ct));
        var bills = await portal.GetAsync($"{P(patientId)}/bills", Ct);
        Assert.Contains("Portaldrugname tablets", await bills.Content.ReadAsStringAsync(Ct)); // the patient's own bill keeps the item name
        var rows = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters()
            .Where(a => a.PatientId == patientId && a.StaffUserId == portalUserId).OrderBy(a => a.At).ToListAsync(Ct));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Portal));
        Assert.All(rows, r => Assert.Equal("parent", r.Role));
        Assert.All(rows, r => Assert.False(r.Emergency));
        Assert.Equal(new[] { "record", "bills" }, rows.Select(r => r.Area));

        // The director's access log.
        var staffLog = (await Json(await clinic.Owner.GetAsync($"/api/care/patients/{patientId}/access-log", Ct))).EnumerateArray()
            .Where(e => e.GetProperty("portal").GetBoolean()).ToList();
        Assert.Equal(2, staffLog.Count);
        Assert.All(staffLog, e => Assert.Equal("Nkem W.", e.GetProperty("staffName").GetString()));
        Assert.All(staffLog, e => Assert.Equal("parent", e.GetProperty("relationship").GetString()));
        Assert.All(staffLog, e => Assert.Equal("Nkem W. (parent), patient portal", e.GetProperty("who").GetString()));
        Assert.Contains(staffLog, e => e.GetProperty("areaLabel").GetString() == "Bills");

        // The patient's "who opened" page.
        var patientLog = (await Json(await portal.GetAsync($"{P(patientId)}/access-log", Ct))).EnumerateArray()
            .Where(e => e.GetProperty("portal").GetBoolean()).ToList();
        Assert.Equal(2, patientLog.Count);
        Assert.All(patientLog, e => Assert.Equal("Nkem W. (parent), patient portal", e.GetProperty("who").GetString()));
        Assert.DoesNotContain(portalUserId.ToString(), (await (await portal.GetAsync($"{P(patientId)}/access-log", Ct)).Content.ReadAsStringAsync(Ct)));
    }

    /// <summary>Final review C1 (ruling): front desk sends only emailed invites to the email on file; link-only invites are leadership's.</summary>
    [Fact]
    public async Task Front_desk_cannot_create_a_link_only_invite()
    {
        var clinic = await kit.ClinicAsync("Portal Link Only Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var patientId = Id(await kit.PatientAsync(clinic, "Noemail", "Patient", phone: "08031234567"));

        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct), HttpStatusCode.Forbidden, PatientInviteService.LinkOnlyLeadership);
        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { phone = "08031234567" }, Ct), HttpStatusCode.Forbidden,
            PatientInviteService.LinkOnlyLeadership);
        Assert.Equal(0, await PendingInvitesAsync(patientId));
        // Leadership may.
        await InviteAsync(admin, patientId);
        await InviteAsync(clinic.Owner, patientId);
        Assert.Equal(2, await PendingInvitesAsync(patientId));

        // With an email on file (set by the director), the desk invites that address, and only that address.
        var onFile = $"onfile-{Guid.NewGuid():N}@example.com";
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}",
            new { firstName = "Noemail", lastName = "Patient", sex = "Female", dateOfBirth = "1990-04-12", phone = "08031234567", email = onFile }, Ct));
        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { email = "clerk@example.com" }, Ct), HttpStatusCode.Forbidden,
            PatientInviteService.DeskEmailOnFile);
        await InviteAsync(desk, patientId);
        await InviteAsync(desk, patientId, email: onFile.ToUpperInvariant());
        var sent = await api.WithDbAsync(db => db.PatientInvites.IgnoreQueryFilters().Where(i => i.PatientId == patientId && i.Email != null).Select(i => i.Email).ToListAsync(Ct));
        Assert.Equal(2, sent.Count);
        Assert.All(sent, e => Assert.Equal(onFile, e));
    }

    /// <summary>Final review C1 (ruling): front desk that changed the patient's email in the last 24 hours can't invite (409); others can.</summary>
    [Fact]
    public async Task Front_desk_email_change_then_invite_is_refused()
    {
        var clinic = await kit.ClinicAsync("Portal Email Change Clinic");
        var (desk, deskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var (otherDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Changed", "Patient"));
        var clerk = $"clerk-{Guid.NewGuid():N}@example.com";

        await Json(await desk.PutAsJsonAsync($"/api/care/patients/{patientId}",
            new { firstName = "Changed", lastName = "Patient", sex = "Female", dateOfBirth = "1990-04-12", email = clerk }, Ct));
        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct), HttpStatusCode.Conflict,
            PatientInviteService.EmailRecentlyChanged);
        Assert.Equal(0, await PendingInvitesAsync(patientId));
        // A later edit that leaves the email alone doesn't clear it: the email change is still within 24 hours.
        await Json(await desk.PutAsJsonAsync($"/api/care/patients/{patientId}",
            new { firstName = "Changed", lastName = "Patient", sex = "Female", dateOfBirth = "1990-04-12", email = clerk, phone = "08031234567" }, Ct));
        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct), HttpStatusCode.Conflict);

        // Another clerk and the director are not refused.
        await InviteAsync(otherDesk, patientId);
        await InviteAsync(clinic.Owner, patientId);

        // After 24 hours the same clerk may invite again.
        await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == clinic.TenantId && a.ActorId == deskId && a.Action == "patient.updated")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedAt, DateTimeOffset.UtcNow.AddHours(-25)), Ct));
        await InviteAsync(desk, patientId);
    }

    /// <summary>The access log shows who and in what role (and what was opened), never the reason, the emergency flag or staff ids. Every area has a label.</summary>
    [Fact]
    public async Task Access_log_shows_staff_name_and_role_only()
    {
        var clinic = await kit.ClinicAsync("Portal Log Clinic");
        var (_, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var patientId = Id(await kit.PatientAsync(clinic, "Logged", "Patient"));
        var at = DateTimeOffset.UtcNow.AddMinutes(-30);
        await api.WithDbAsync(db =>
        {
            foreach (var area in RecordGuard.Areas.All)
                db.RecordAccesses.Add(new RecordAccess
                {
                    TenantId = clinic.TenantId, StaffUserId = nurseId, Role = "Nurse", PatientId = patientId, Area = area,
                    Reason = area == RecordGuard.Areas.Record ? "Logreasonmarker" : null, Emergency = area == RecordGuard.Areas.Record, At = at,
                });
            return db.SaveChangesAsync(Ct);
        });
        var (portal, _) = await LinkedAsync(clinic, patientId);

        var response = await portal.GetAsync($"{P(patientId)}/access-log", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var log = (await Json(response)).EnumerateArray().ToList();
        Assert.Equal(RecordGuard.Areas.All.Count, log.Count);
        Assert.DoesNotContain("Logreasonmarker", text);
        Assert.DoesNotContain(nurseId.ToString(), text);
        foreach (var entry in log)
        {
            Assert.Equal(new[] { "at", "portal", "role", "staffName", "what", "who" }, entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal("Nurse Staff", entry.GetProperty("staffName").GetString());
            Assert.Equal("Nurse Staff, Nurse", entry.GetProperty("who").GetString());
            Assert.False(entry.GetProperty("portal").GetBoolean());
            Assert.Equal("Nurse", entry.GetProperty("role").GetString());
        }
        var labels = log.Select(e => e.GetProperty("what").GetString()!).ToList();
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(labels.Count, labels.Distinct().Count()); // each area, triage included, reads as its own label
        Assert.DoesNotContain("ward-round", labels);
    }

    /// <summary>An account not linked to the bill's patient can't start a checkout, verify someone else's reference or download their receipt.</summary>
    [Fact]
    public async Task Unlinked_payer_cannot_pay_or_get_receipt()
    {
        var clinic = await kit.ClinicAsync("Portal Payer Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        await Json(await clinic.Owner.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = CareKey }, Ct));
        var itemId = await kit.PriceItemAsync(clinic, "Payer consultation", "Consultation", 500_000);
        var patientId = Id(await kit.PatientAsync(clinic, "Billed", "Patient"));
        var otherId = Id(await kit.PatientAsync(clinic, "Neighbour", "Patient", "1975-05-05"));
        var billId = await BillAsync(clinic, patientId, itemId);
        var otherBill = await BillAsync(clinic, otherId, itemId);
        var desk = await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 100_000, method = "Cash" }, Ct));
        var deskPayment = Id(desk);
        var (owner, _) = await LinkedAsync(clinic, patientId);
        var (neighbour, _) = await LinkedAsync(clinic, otherId);

        // Not linked to the patient: 404 on the patient's routes.
        await ProblemAsync(await neighbour.PostAsJsonAsync($"{P(patientId)}/bills/{billId}/pay", new { }, Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await neighbour.GetAsync($"{P(patientId)}/receipts/{deskPayment}.pdf", Ct), HttpStatusCode.NotFound);
        // Linked to a different patient: the bill and the receipt aren't that patient's.
        await ProblemAsync(await neighbour.PostAsJsonAsync($"{P(otherId)}/bills/{billId}/pay", new { }, Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await neighbour.GetAsync($"{P(otherId)}/receipts/{deskPayment}.pdf", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await owner.PostAsJsonAsync($"{P(patientId)}/bills/{otherBill}/pay", new { }, Ct), HttpStatusCode.NotFound);

        // The owner's own checkout can't be verified by the neighbour.
        var reference = (await Json(await owner.PostAsJsonAsync($"{P(patientId)}/bills/{billId}/pay", new { }, Ct))).GetProperty("reference").GetString()!;
        api.Merchant.Charges[reference] = new Saas.Application.Billing.VerifiedCharge(reference, 400_000, "NGN", DateTimeOffset.UtcNow);
        await ProblemAsync(await neighbour.PostAsync($"/api/care-portal/payments/{reference}/verify", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(CarePaymentStatus.Pending, await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters()
            .Where(p => p.ProviderRef == reference).Select(p => p.Status).FirstAsync(Ct)));
        await ProblemAsync(await neighbour.PostAsync("/api/care-portal/payments/care_unknown/verify", null, Ct), HttpStatusCode.NotFound);

        // The linked account gets its receipt.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{P(patientId)}/receipts/{deskPayment}.pdf", Ct)).StatusCode);
    }

    /// <summary>The invite lasts 14 days, is stored hashed, is single use, and an expired one is refused; accepting links with the relationship.</summary>
    [Fact]
    public async Task Invite_expires_and_is_single_use()
    {
        var clinic = await kit.ClinicAsync("Portal Invite Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Invited", "Patient"));
        var link = (await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!;
        Assert.Contains("/portal/invite/", link);
        var token = TokenOf(link);
        var stored = await api.WithDbAsync(db => db.Database.SqlQuery<string>(
            $"SELECT token_hash AS \"Value\" FROM patient_invites WHERE patient_id = {patientId}").ToListAsync(Ct));
        Assert.DoesNotContain(token, Assert.Single(stored));
        var expires = await api.WithDbAsync(db => db.Database.SqlQuery<DateTimeOffset>(
            $"SELECT expires_at AS \"Value\" FROM patient_invites WHERE patient_id = {patientId}").FirstAsync(Ct));
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(14).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(14).AddMinutes(5));

        var preview = await Json(await api.CreateClient().GetAsync($"/api/care-portal/invites/{token}", Ct));
        Assert.Equal("Portal Invite Clinic", preview.GetProperty("clinicName").GetString());

        var (first, _, _) = await AccountAsync();
        var (second, _, _) = await AccountAsync();
        await ProblemAsync(await AcceptAsync(first, token, "Cousin"), HttpStatusCode.BadRequest, field: "relationship");
        var linked = await Json(await AcceptAsync(first, token, "Parent"));
        Assert.Equal("Parent", Assert.Single(linked.EnumerateArray()).GetProperty("relationship").GetString());
        await ProblemAsync(await AcceptAsync(second, token), HttpStatusCode.Conflict, InviteUsed);
        await ProblemAsync(await AcceptAsync(first, token), HttpStatusCode.Conflict, InviteUsed);
        Assert.Empty((await Json(await second.GetAsync("/api/care-portal/patients", Ct))).EnumerateArray());

        var late = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        await api.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE patient_invites SET expires_at = now() - interval '1 minute' WHERE patient_id = {patientId} AND accepted_at IS NULL", Ct));
        await ProblemAsync(await AcceptAsync(second, late), HttpStatusCode.Conflict, InviteExpired);
        await ProblemAsync(await AcceptAsync(second, "not-a-real-token"), HttpStatusCode.NotFound);
        Assert.Empty((await Json(await second.GetAsync("/api/care-portal/patients", Ct))).EnumerateArray());
    }

    /// <summary>Only front desk and leadership create invites; the WhatsApp message carries the link and the clinic, never clinical text.</summary>
    [Fact]
    public async Task Only_front_desk_and_leadership_invite()
    {
        var clinic = await kit.ClinicAsync("Portal Inviter Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Whats", "App", phone: "08031234567"));
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { "Allergymarker" }, conditions = new[] { "Conditionmarker" }, bloodGroup = "O+", genotype = "AA" }, Ct));
        await ConsultationAsync(clinic, patientId, "Complaintmarker", sign: true);

        foreach (var role in new[] { "Doctor", "Nurse", "Cashier" })
        {
            var (client, _) = await kit.StaffAsync(clinic, role);
            await ProblemAsync(await client.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct), HttpStatusCode.Forbidden);
        }
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        // Final review C1: front desk invites only the email on file, so the director records it first.
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}",
            new { firstName = "Whats", lastName = "App", sex = "Female", dateOfBirth = "1990-04-12", phone = "08031234567", email = "family@example.com" }, Ct));
        foreach (var client in new[] { desk, admin, clinic.Owner })
        {
            var invite = await InviteAsync(client, patientId, email: "family@example.com");
            var url = invite.GetProperty("whatsAppUrl").GetString()!;
            Assert.StartsWith("https://wa.me/234", url);
            var message = Uri.UnescapeDataString(url[(url.IndexOf("text=", StringComparison.Ordinal) + 5)..]);
            Assert.Contains(invite.GetProperty("link").GetString()!, message);
            Assert.Contains("Portal Inviter Clinic", message);
            foreach (var marker in new[] { "Allergymarker", "Conditionmarker", "Complaintmarker", "Signeddiagnosis", "O+" })
                Assert.DoesNotContain(marker, message);
        }
        // The emailed invite carries no clinical text either.
        var emails = await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == clinic.TenantId && n.TemplateKey == "care.patient_invite").ToListAsync(Ct));
        Assert.Equal(3, emails.Count);
        Assert.All(emails, e => Assert.Equal("family@example.com", e.Recipient));
        foreach (var e in emails)
            foreach (var value in e.Payload.Values)
                foreach (var marker in new[] { "Allergymarker", "Conditionmarker", "Complaintmarker", "Signeddiagnosis" })
                    Assert.DoesNotContain(marker, value);

        // A deceased patient gets no invite.
        await SetPatientStatusAsync(patientId, PatientStatus.Deceased);
        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct), HttpStatusCode.Conflict,
            "This patient is recorded as deceased.");
    }

    /// <summary>A deceased patient can't be booked from the portal (the shared booking rule, after the link check).</summary>
    [Fact]
    public async Task Deceased_patient_cannot_book()
    {
        var clinic = await kit.ClinicAsync("Portal Deceased Clinic");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var patientId = Id(await kit.PatientAsync(clinic, "Late", "Patient"));
        var (portal, _) = await LinkedAsync(clinic, patientId, "Parent");
        var slots = await PortalSlotsAsync(portal, patientId, doctorId, Today.AddDays(3));

        await SetPatientStatusAsync(patientId, PatientStatus.Deceased);
        await ProblemAsync(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[0] }, Ct),
            HttpStatusCode.Conflict, "This patient is recorded as deceased.");
        Assert.Equal(0, await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId, Ct)));
    }

    /// <summary>Portal bookings carry source Portal; the list shows the reason under the "no medical details" label.</summary>
    [Fact]
    public async Task Appointments_show_reason_with_label()
    {
        var clinic = await kit.ClinicAsync("Portal Booking Clinic");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var patientId = Id(await kit.PatientAsync(clinic, "Booker", "Patient"));
        var (portal, userId) = await LinkedAsync(clinic, patientId);

        var doctors = await Json(await portal.GetAsync($"{P(patientId)}/doctors", Ct));
        Assert.Contains(doctors.EnumerateArray(), d => d.GetProperty("userId").GetGuid() == doctorId);
        var slots = await PortalSlotsAsync(portal, patientId, doctorId, Today.AddDays(4));
        await ProblemAsync(await portal.PostAsJsonAsync($"{P(patientId)}/appointments",
            new { doctorUserId = doctorId, start = slots[0], reason = new string('x', 121) }, Ct), HttpStatusCode.BadRequest, field: "reason");
        var booked = await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[0], reason = "Check-up" }, Ct));
        Assert.Equal("Portal", booked.GetProperty("source").GetString());

        var list = await Json(await portal.GetAsync($"{P(patientId)}/appointments", Ct));
        Assert.Equal(ReasonLabel, list.GetProperty("reasonLabel").GetString());
        var item = Assert.Single(list.GetProperty("appointments").EnumerateArray());
        Assert.Equal("Check-up", item.GetProperty("reason").GetString());
        Assert.True(item.GetProperty("canCancel").GetBoolean());
        Assert.Equal(AppointmentSource.Portal, await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.PatientId == patientId).Select(a => a.Source).FirstAsync(Ct)));

        var notice = Assert.Single(await NoticesForAsync(userId), n => n.Kind == "care.appointment_booked");
        Assert.Contains("Booker", notice.Body);
        Assert.DoesNotContain("Check-up", notice.Body);
    }

    /// <summary>
    /// A guardian linked to two children: each notice names that child's first name (never the last name), links to that child, and the
    /// portal feed is scoped by clinic and patient. A revoked link and a deceased patient get nothing.
    /// </summary>
    [Fact]
    public async Task Guardian_notices_carry_the_first_name()
    {
        var clinic = await kit.ClinicAsync("Portal Notice Clinic");
        var other = await kit.ClinicAsync("Portal Notice Annex");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var ada = Id(await kit.PatientAsync(clinic, "Ada", "Okonkwo", "2014-01-01"));
        var tolu = Id(await kit.PatientAsync(clinic, "Tolu", "Okonkwo", "2016-01-01"));
        var annexChild = Id(await kit.PatientAsync(other, "Bisi", "Okonkwo", "2018-01-01"));
        var (guardian, guardianId, _) = await AccountAsync("Okonkwo Guardian");
        await LinkAsync(guardian, clinic, ada, "Guardian");
        await LinkAsync(guardian, clinic, tolu, "Guardian");
        await LinkAsync(guardian, other, annexChild, "Guardian");

        var slots = (await Json(await desk.GetAsync($"/api/care/doctors/{doctorId}/slots?date={Today.AddDays(3):yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = ada, doctorUserId = doctorId, start = slots[0], reason = "Review" }, Ct));
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = tolu, doctorUserId = doctorId, start = slots[1], reason = "Review" }, Ct));
        await NoticeAsync(clinic.TenantId, n => n.ResultReadyAsync(ada, Ct));
        await NoticeAsync(other.TenantId, n => n.BillDueAsync(annexChild, "BILL-2026-00001", 100_000, Ct));

        var notices = await NoticesForAsync(guardianId);
        var adaBooked = Assert.Single(notices, n => n.Kind == "care.appointment_booked" && n.Link.Contains($"patient={ada}"));
        var toluBooked = Assert.Single(notices, n => n.Kind == "care.appointment_booked" && n.Link.Contains($"patient={tolu}"));
        Assert.Contains("Ada", adaBooked.Body);
        Assert.Contains("Tolu", toluBooked.Body);
        Assert.Equal("A new result for Ada is ready in your CareLink portal.", Assert.Single(notices, n => n.Kind == "care.result_ready").Body);
        Assert.Contains("Bisi", Assert.Single(notices, n => n.Kind == "care.bill_due").Body);
        Assert.All(notices, n => Assert.DoesNotContain("Okonkwo", n.Body));
        Assert.All(notices, n => Assert.DoesNotContain("Review", n.Body));

        // The feed for one patient holds only that patient's notices from that clinic.
        var adaFeed = (await Json(await guardian.GetAsync($"{P(ada)}/notifications", Ct))).EnumerateArray().ToList();
        Assert.Equal(2, adaFeed.Count);
        Assert.All(adaFeed, n => Assert.Contains("Ada", n.GetProperty("body").GetString()));
        Assert.Contains("Tolu", Assert.Single((await Json(await guardian.GetAsync($"{P(tolu)}/notifications", Ct))).EnumerateArray()).GetProperty("body").GetString());
        Assert.Contains("Bisi", Assert.Single((await Json(await guardian.GetAsync($"{P(annexChild)}/notifications", Ct))).EnumerateArray()).GetProperty("body").GetString());

        // The clinic revokes the guardian's link to Tolu: no more notices, and the portal no longer reaches Tolu.
        var links = await Json(await desk.GetAsync($"/api/care/patients/{tolu}/portal-links", Ct));
        var linkId = Id(Assert.Single(links.EnumerateArray()));
        Assert.Equal(HttpStatusCode.NoContent, (await desk.DeleteAsync($"/api/care/patients/{tolu}/portal-links/{linkId}", Ct)).StatusCode);
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = tolu, doctorUserId = doctorId, start = slots[2], reason = "Review" }, Ct));
        Assert.Single(await NoticesForAsync(guardianId), n => n.Kind == "care.appointment_booked" && n.Link.Contains($"patient={tolu}"));
        await ProblemAsync(await guardian.GetAsync($"{P(tolu)}/record", Ct), HttpStatusCode.NotFound);

        // A deceased patient's portal users hear nothing.
        var before = (await NoticesForAsync(guardianId)).Count;
        await SetPatientStatusAsync(ada, PatientStatus.Deceased);
        await NoticeAsync(clinic.TenantId, n => n.BillDueAsync(ada, "BILL-2026-00002", 100_000, Ct));
        await NoticeAsync(clinic.TenantId, n => n.ResultReadyAsync(ada, Ct));
        Assert.Equal(before, (await NoticesForAsync(guardianId)).Count);
    }

    /// <summary>PUT contact changes only the patient's phone, email and address, validated as on the patient screen, audited with ids only.</summary>
    [Fact]
    public async Task Contact_update_changes_only_contact_fields()
    {
        var clinic = await kit.ClinicAsync("Portal Contact Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Contact", "Patient"));
        var (portal, userId) = await LinkedAsync(clinic, patientId);

        await ProblemAsync(await portal.PutAsJsonAsync($"{P(patientId)}/contact", new { email = "not-an-email" }, Ct), HttpStatusCode.BadRequest, field: "email");
        await ProblemAsync(await portal.PutAsJsonAsync($"{P(patientId)}/contact", new { phone = "12" }, Ct), HttpStatusCode.BadRequest, field: "phone");
        await Json(await portal.PutAsJsonAsync($"{P(patientId)}/contact", new
        {
            phone = "08031234567", email = "Contact.New@Example.com", address = "5 Contactmarker Road",
            firstName = "Changed", lastName = "Changed", nextOfKinName = "Changed", bloodGroup = "AB-", allergies = new[] { "Changed" },
        }, Ct));

        var patient = await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == patientId, Ct));
        Assert.Equal("Contact", patient.FirstName);
        Assert.Equal("Patient", patient.LastName);
        Assert.Null(patient.NextOfKinName);
        Assert.Null(patient.BloodGroup);
        Assert.Empty(patient.Allergies);
        Assert.Equal("contact.new@example.com", patient.Email);
        Assert.Equal("5 Contactmarker Road", patient.Address);
        Assert.NotNull(patient.Phone);

        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityId == patientId.ToString() && a.Action == "patient.contact_updated").ToListAsync(Ct)));
        Assert.Equal(userId, audit.ActorId);
        foreach (var json in new[] { audit.AfterJson, audit.BeforeJson })
            foreach (var value in new[] { "Contactmarker", "contact.new", "8031234567" })
                Assert.DoesNotContain(value, json ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Portal accounts sign up and sign in under /api/auth/patient (password or emailed code); staff tokens can't use the portal; invite accept,
    /// the code routes and portal writes are rate limited like the other portals.
    /// </summary>
    [Fact]
    public async Task Patient_sign_in_and_rate_limits()
    {
        var clinic = await kit.ClinicAsync("Portal Auth Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Auth", "Patient"));
        var email = $"patient-{Guid.NewGuid():N}@example.com";
        var client = api.CreateClient();
        await Json(await api.SignUpAsync(client, "/api/auth/patient/signup", new { fullName = "Auth Patient", email, password = "password123" }, Ct));
        var login = await Json(await client.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        await LinkAsync(client, clinic, patientId);
        await Json(await client.GetAsync($"{P(patientId)}/record", Ct));

        // A staff token is refused: the portal is for customer accounts.
        await ProblemAsync(await clinic.Owner.GetAsync("/api/care-portal/patients", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await clinic.Owner.GetAsync($"{P(patientId)}/record", Ct), HttpStatusCode.Forbidden);
        // Anonymous: 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/care-portal/patients", Ct)).StatusCode);

        _ = api.CreateClient();
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        string? Policy(string method, string route) => endpoints
            .Where(e => e.RoutePattern.RawText == route && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .Select(e => e.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().LastOrDefault()?.PolicyName).Single();
        Assert.Equal("public-write", Policy("POST", "/api/care-portal/invites/{token}/accept"));
        Assert.Equal("preview", Policy("GET", "/api/care-portal/invites/{token}"));
        Assert.Equal("otp", Policy("POST", "/api/auth/patient/otp/verify"));
        Assert.Equal("otp", Policy("POST", "/api/auth/patient/signup/confirm")); // final fix pass B: the sign-up's emailed code
        Assert.Equal("auth", Policy("POST", "/api/auth/patient/login"));
        Assert.Equal("otp", Policy("POST", "/api/auth/otp/request")); // the emailed code itself
        Assert.Equal("public-write", Policy("POST", "/api/care-portal/patients/{id:guid}/appointments"));
        Assert.Equal("public-write", Policy("POST", "/api/care-portal/patients/{id:guid}/bills/{billId:guid}/pay"));
        Assert.Equal("public-write", Policy("POST", "/api/care-portal/payments/{reference}/verify"));

        // Fix round 1: and a portal (customer) token is refused on the staff routes.
        await ProblemAsync(await client.GetAsync("/api/care/patients", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.GetAsync($"/api/care/patients/{patientId}", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.GetAsync($"/api/care/patients/{patientId}/record", Ct), HttpStatusCode.Forbidden);
    }

    // ---------- Fix round 1 ----------

    private const string StaffRefused = "Staff accounts can't be linked to patients. Use a personal account.";
    private const string OtherEmail = "This invitation was sent to another email address.";

    private Task<string> EmailOfAsync(Guid userId) => api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).FirstAsync(Ct));

    private Task<Guid> OwnerIdAsync(CareKit.Clinic clinic) => api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
        .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));

    /// <summary>Signs in at the patient portal's login with an existing account's email and password: a customer (tenantless) token.</summary>
    private async Task<HttpClient> PortalLoginAsync(string email)
    {
        var client = api.CreateClient();
        var auth = await Json(await client.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Signs in with the emailed code (which confirms the address), creating the account first if needed: a customer client.</summary>
    private async Task<HttpClient> ConfirmByCodeAsync(string email)
    {
        var anonymous = api.CreateClient();
        if (!await api.WithDbAsync(db => db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct)))
            await Json(await api.SignUpAsync(anonymous, "/api/auth/patient/signup", new { fullName = "Code Family", email, password = "password123" }, Ct));
        (await anonymous.PostAsJsonAsync("/api/auth/otp/request", new { email }, Ct)).EnsureSuccessStatusCode();
        var code = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "otp" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["code"];
        var signedIn = await Json(await anonymous.PostAsJsonAsync("/api/auth/patient/otp/verify", new { email, code }, Ct));
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>
    /// R2 (fix round 3): a refused business code sign-in doesn't confirm the email, so it can't disarm the customer takeover. The squatter
    /// registers the address; the owner's code on the business sign-in is refused (no business) and the account stays unconfirmed; the owner's
    /// customer code sign-in then takes the account over and the squatter's password fails. Final fix pass B: a sign-up made now has no
    /// password before its code, so the squatter here is an account from before codes at sign-up (password set, never confirmed).
    /// </summary>
    [Fact]
    public async Task Refused_business_code_sign_in_leaves_the_account_unconfirmed()
    {
        var email = $"squatted-{Guid.NewGuid():N}@example.com";
        await api.LegacyUnconfirmedAccountAsync(email, name: "Squatter");
        Task<(bool Confirmed, DateTimeOffset? At)> StateAsync() => api.WithDbAsync(db => db.Users.Where(u => u.NormalizedEmail == email.ToUpperInvariant())
            .Select(u => new ValueTuple<bool, DateTimeOffset?>(u.EmailConfirmed, u.EmailConfirmedAt)).FirstAsync(Ct));

        var anonymous = api.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/auth/otp/request", new { email }, Ct)).EnsureSuccessStatusCode();
        var code = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "otp" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["code"];
        await ProblemAsync(await anonymous.PostAsJsonAsync("/api/auth/otp/verify", new { email, code }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal((false, (DateTimeOffset?)null), await StateAsync());
        // Still the squatter's password at this point.
        Assert.True((await anonymous.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);

        await ConfirmByCodeAsync(email);
        var (confirmed, at) = await StateAsync();
        Assert.True(confirmed);
        Assert.NotNull(at);
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);
    }

    /// <summary>The emailed sign-in code most recently sent to <paramref name="email"/>.</summary>
    private async Task<string> RequestCodeAsync(string email)
    {
        (await api.CreateClient().PostAsJsonAsync("/api/auth/otp/request", new { email }, Ct)).EnsureSuccessStatusCode();
        return (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "otp" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["code"];
    }

    /// <summary>
    /// R2/R3: a business owner's code sign-in signs in to the business with a valid session (rotated stamp, working refresh). Because the
    /// owner signed up with a password and had never confirmed the address, this first confirmation is a takeover (R3): the old password
    /// fails, and a password reset restores one. An already-confirmed owner's later code sign-in keeps the password. Final fix pass B: the
    /// takeover is left only for accounts from before codes at sign-up, so the owner is made one (confirmed sign-ups never lose a password).
    /// </summary>
    [Fact]
    public async Task Business_code_sign_in_confirms_after_the_session()
    {
        var clinic = await kit.ClinicAsync("Portal Business Code Clinic");
        var ownerId = await OwnerIdAsync(clinic);
        var email = await EmailOfAsync(ownerId);
        await api.WithDbAsync(db => db.Users.Where(u => u.Id == ownerId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.EmailConfirmed, false).SetProperty(u => u.EmailConfirmedAt, (DateTimeOffset?)null), Ct));
        var anonymous = api.CreateClient();
        var session = await Json(await anonymous.PostAsJsonAsync("/api/auth/otp/verify", new { email, code = await RequestCodeAsync(email) }, Ct));
        Assert.Equal(clinic.TenantId, session.GetProperty("tenant").GetProperty("id").GetGuid());
        var state = await api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => new { u.EmailConfirmed, u.EmailConfirmedAt }).FirstAsync(Ct));
        Assert.True(state.EmailConfirmed);
        Assert.NotNull(state.EmailConfirmedAt);

        // The session issued after the takeover is valid, and so is its refresh token.
        var business = api.CreateClient();
        business.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        await Json(await business.GetAsync("/api/care/setup", Ct));
        await Json(await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.GetProperty("refreshToken").GetString() }, Ct));

        // First confirmation: the password set before it no longer signs in; a reset (a mailbox proof too) restores one.
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);
        (await anonymous.PostAsJsonAsync("/api/auth/password/forgot", new { email }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "password.reset" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["link"];
        var fragment = System.Web.HttpUtility.ParseQueryString(link[(link.IndexOf('#') + 1)..]);
        (await anonymous.PostAsJsonAsync("/api/auth/password/reset", new { email = fragment["email"], token = fragment["token"], newPassword = "password456" }, Ct))
            .EnsureSuccessStatusCode();
        await Json(await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "password456" }, Ct));

        // Already confirmed: a later code sign-in is no takeover, so the password stays.
        await Json(await anonymous.PostAsJsonAsync("/api/auth/otp/verify", new { email, code = await RequestCodeAsync(email) }, Ct));
        await Json(await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "password456" }, Ct));
    }

    /// <summary>
    /// R3: a squatter signs up a free business on the invitee's address with a password. The owner's business code sign-in succeeds and takes
    /// the account over: the squatter's password and refresh fail. The owner's later customer code sign-in accepts the invite; the squatter's
    /// old customer token can't. Final fix pass B: a squatter's sign-up made now never gets a session without the code
    /// (AuthConfirmTests), so the squatter here is an account from before codes at sign-up, which can still sign in with its password.
    /// </summary>
    [Fact]
    public async Task Business_squatter_loses_the_account_to_the_owners_code_sign_in()
    {
        var clinic = await kit.ClinicAsync("Portal Business Squat Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Squatted", "Patient"));
        var email = $"biz-squat-{Guid.NewGuid():N}@example.com";
        var token = TokenOf((await InviteAsync(clinic.Owner, patientId, email: email)).GetProperty("link").GetString()!);

        var anonymous = api.CreateClient();
        await api.LegacyUnconfirmedAccountAsync(email, name: "Squatter");
        var pending = await Json(await anonymous.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Squatter", email, password = "password123", country = "NG", product = "care", businessName = $"Squat Clinic {Guid.NewGuid():N}",
        }, Ct));
        Assert.True(pending.GetProperty("confirmationRequired").GetBoolean()); // still no session from the sign-up itself
        var squatterBusiness = await Json(await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "password123", product = "care" }, Ct));
        var squatterLogin = await Json(await anonymous.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct));
        var squatterPortal = api.CreateClient();
        squatterPortal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", squatterLogin.GetProperty("accessToken").GetString());
        await ProblemAsync(await AcceptAsync(squatterPortal, token), HttpStatusCode.Conflict, OtherEmail); // not confirmed

        await Task.Delay(TimeSpan.FromMilliseconds(1100), Ct); // the squatter's tokens fall in an earlier second than the confirmation
        await Json(await anonymous.PostAsJsonAsync("/api/auth/otp/verify", new { email, code = await RequestCodeAsync(email) }, Ct));
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);
        foreach (var refresh in new[] { squatterBusiness, squatterLogin })
            Assert.False((await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh.GetProperty("refreshToken").GetString() }, Ct)).IsSuccessStatusCode);

        var owner = await ConfirmByCodeAsync(email);
        await ProblemAsync(await AcceptAsync(squatterPortal, token), HttpStatusCode.Unauthorized, "Please sign in again.");
        await Json(await AcceptAsync(owner, token, "Self"));
        Assert.Equal(1, await LinksOfAsync(patientId));
        await ProblemAsync(await squatterPortal.GetAsync($"{P(patientId)}/record", Ct), HttpStatusCode.NotFound);
    }

    private Task<int> LinksOfAsync(Guid patientId) =>
        api.WithDbAsync(db => db.PatientLinks.IgnoreQueryFilters().CountAsync(l => l.PatientId == patientId, Ct));

    private Task<int> PendingInvitesAsync(Guid patientId) =>
        api.WithDbAsync(db => db.PatientInvites.IgnoreQueryFilters().CountAsync(i => i.PatientId == patientId && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow, Ct));

    /// <summary>
    /// I1: a front-desk clerk invites a patient and accepts with their own work login at the portal sign-in: 403, no link. The same for the
    /// cashier. A member of another clinic is not this clinic's staff and may accept.
    /// </summary>
    [Fact]
    public async Task Staff_accounts_cannot_accept_their_clinics_invites()
    {
        var clinic = await kit.ClinicAsync("Portal Self Invite Clinic");
        var other = await kit.ClinicAsync("Portal Self Invite Elsewhere");
        var (desk, deskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var (_, cashierId) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = Id(await kit.PatientAsync(clinic, "Targeted", "Patient"));

        foreach (var memberId in new[] { deskId, cashierId, await OwnerIdAsync(clinic) })
        {
            // Link-only invites are leadership's (final review C1); the desk can't make one, so the director does.
            var token = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
            var asCustomer = await PortalLoginAsync(await EmailOfAsync(memberId));
            await ProblemAsync(await AcceptAsync(asCustomer, token), HttpStatusCode.Forbidden, StaffRefused);
            Assert.Empty((await Json(await asCustomer.GetAsync("/api/care-portal/patients", Ct))).EnumerateArray());
            await ProblemAsync(await asCustomer.GetAsync($"{P(patientId)}/record", Ct), HttpStatusCode.NotFound);
        }
        Assert.Equal(0, await LinksOfAsync(patientId));
        Assert.Equal(3, await PendingInvitesAsync(patientId)); // a refused accept doesn't use the invite up

        // Another clinic's director is a member there, not here.
        var outsider = await PortalLoginAsync(await EmailOfAsync(await OwnerIdAsync(other)));
        var spare = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        await Json(await AcceptAsync(outsider, spare, "Parent"));
        await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct), HttpStatusCode.Forbidden);
        Assert.Equal(1, await LinksOfAsync(patientId));
    }

    /// <summary>I1: an emailed invite accepts only from an account whose confirmed email matches (any case); a link-only invite from anyone.</summary>
    [Fact]
    public async Task Emailed_invite_accepts_only_its_confirmed_email()
    {
        var clinic = await kit.ClinicAsync("Portal Bound Invite Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Bound", "Patient"));
        var local = $"bound-{Guid.NewGuid():N}";
        var token = TokenOf((await InviteAsync(clinic.Owner, patientId, email: $"{local.ToUpperInvariant()}@Example.COM")).GetProperty("link").GetString()!);

        var (stranger, _, _) = await AccountAsync();
        await ProblemAsync(await AcceptAsync(stranger, token), HttpStatusCode.Conflict, OtherEmail);

        // The right address, but never confirmed. Final fix pass B: a sign-up made now gets no session before its code, so this is an account
        // from before codes at sign-up (a password, never confirmed), signed in with that password.
        var email = $"{local}@example.com";
        var unconfirmed = api.CreateClient();
        await api.LegacyUnconfirmedAccountAsync(email, name: "Bound Family");
        var auth = await Json(await unconfirmed.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct));
        unconfirmed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        await ProblemAsync(await AcceptAsync(unconfirmed, token), HttpStatusCode.Conflict, OtherEmail);
        Assert.Equal(1, await PendingInvitesAsync(patientId));

        // Signing in with the emailed code confirms the address; then the invite accepts. (A second passes first, so the squatter's token
        // below is issued in an earlier second than the confirmation: the iat check works in whole seconds.)
        await Task.Delay(TimeSpan.FromMilliseconds(1100), Ct);
        var confirmed = await ConfirmByCodeAsync(email);
        await Json(await AcceptAsync(confirmed, token, "Guardian"));
        Assert.Equal(1, await LinksOfAsync(patientId));
        // Fix round 2: the invite address isn't on the patient's file (the patient has no recorded email), so the director's notice says so.
        Assert.Contains("sent to an address that isn't on the patient's file",
            Assert.Single(await NoticesForAsync(await OwnerIdAsync(clinic)), n => n.Kind == "care.portal_linked").Body);

        // R1: whoever registered the address first with a password has lost the account to the mailbox's owner: the password no longer
        // signs in, the refresh token is revoked, and the old (still unexpired) access token can neither accept an invite nor read the record.
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = auth.GetProperty("refreshToken").GetString() }, Ct)).IsSuccessStatusCode);
        var second = TokenOf((await InviteAsync(clinic.Owner, patientId, email: email)).GetProperty("link").GetString()!);
        await ProblemAsync(await AcceptAsync(unconfirmed, second), HttpStatusCode.Unauthorized, "Please sign in again.");
        await ProblemAsync(await unconfirmed.GetAsync($"{P(patientId)}/record", Ct), HttpStatusCode.NotFound);
        Assert.Empty((await Json(await unconfirmed.GetAsync("/api/care-portal/patients", Ct))).EnumerateArray());
        await Json(await confirmed.GetAsync($"{P(patientId)}/record", Ct));
        await Json(await AcceptAsync(confirmed, second, "Guardian")); // the new session accepts

        // A link-only invite (no email anywhere) still accepts from any non-member account.
        var open = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        await Json(await AcceptAsync(stranger, open, "Parent"));
        Assert.Equal(2, await LinksOfAsync(patientId));
    }

    /// <summary>
    /// Final fix pass B (final review I3): a patient who creates an account from an emailed invite (sign-up, then the emailed code) accepts it
    /// and keeps the password they chose: it signs in a week later, and a later code sign-in doesn't remove it.
    /// </summary>
    [Fact]
    public async Task Patient_signing_up_from_an_emailed_invite_keeps_a_working_password()
    {
        var clinic = await kit.ClinicAsync("Portal Signup Invite Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Signup", "Patient"));
        var email = $"signup-invite-{Guid.NewGuid():N}@example.com";
        var token = TokenOf((await InviteAsync(clinic.Owner, patientId, email: email)).GetProperty("link").GetString()!);

        var anonymous = api.CreateClient();
        var pending = await Json(await anonymous.PostAsJsonAsync("/api/auth/patient/signup", new { fullName = "Signup Family", email, password = "family-pass-1" }, Ct));
        Assert.True(pending.GetProperty("confirmationRequired").GetBoolean());
        var session = await Json(await anonymous.PostAsJsonAsync("/api/auth/patient/signup/confirm",
            new { email, code = await api.LatestCodeAsync(email), password = "family-pass-1" }, Ct));
        var portal = api.CreateClient();
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        await Json(await AcceptAsync(portal, token, "Self"));
        await Json(await portal.GetAsync($"{P(patientId)}/record", Ct));

        await ConfirmByCodeAsync(email); // a later code sign-in
        var again = await Json(await anonymous.PostAsJsonAsync("/api/auth/patient/login", new { email, password = "family-pass-1" }, Ct));
        var later = api.CreateClient();
        later.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", again.GetProperty("accessToken").GetString());
        await Json(await later.GetAsync($"{P(patientId)}/record", Ct));
        await Json(await portal.GetAsync($"{P(patientId)}/record", Ct)); // the first session wasn't ended either
    }

    /// <summary>I1: the medical director is told in-app whenever a link is created or re-activated: number, relationship and inviter only.</summary>
    [Fact]
    public async Task Director_is_told_when_a_portal_link_is_made()
    {
        var clinic = await kit.ClinicAsync("Portal Link Notice Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var ownerId = await OwnerIdAsync(clinic);
        var patient = await kit.PatientAsync(clinic, "Noticed", "Patientname");
        var patientId = Id(patient);
        var number = patient.GetProperty("number").GetString()!;
        // Final review C1: the desk sends emailed invites to the email on file only, so the patient has one and the account confirms it.
        var guardianEmail = $"guardian-{Guid.NewGuid():N}@example.com";
        await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == patientId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Email, guardianEmail), Ct));
        var portal = await ConfirmByCodeAsync(guardianEmail);

        await Json(await AcceptAsync(portal, TokenOf((await InviteAsync(desk, patientId)).GetProperty("link").GetString()!), "Guardian"));
        var first = Assert.Single(await NoticesForAsync(ownerId), n => n.Kind == "care.portal_linked");
        Assert.Contains(number, first.Body);
        Assert.Contains("guardian", first.Body);
        Assert.Contains("FrontDesk Staff", first.Body);
        Assert.DoesNotContain("Noticed", first.Body);
        Assert.DoesNotContain("Patientname", first.Body);
        Assert.Equal(clinic.TenantId, first.TenantId);

        // Revoked, then linked again: told again.
        var linkId = Id(Assert.Single((await Json(await desk.GetAsync($"/api/care/patients/{patientId}/portal-links", Ct))).EnumerateArray()));
        Assert.Equal(HttpStatusCode.NoContent, (await desk.DeleteAsync($"/api/care/patients/{patientId}/portal-links/{linkId}", Ct)).StatusCode);
        await Json(await AcceptAsync(portal, TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!), "Parent"));
        Assert.Equal(2, (await NoticesForAsync(ownerId)).Count(n => n.Kind == "care.portal_linked"));
        Assert.Contains(await NoticesForAsync(ownerId), n => n.Kind == "care.portal_linked" && n.Body.Contains("parent"));
        // Link-only invites name no address, so nothing about the address is said.
        Assert.All((await NoticesForAsync(ownerId)).Where(n => n.Kind == "care.portal_linked"), n => Assert.DoesNotContain("address", n.Body));

        // Fix round 2: an invite to the patient's recorded email reads plainly; one to another address says so.
        var onFile = $"onfile-{Guid.NewGuid():N}@example.com";
        var filed = Id(await kit.PatientAsync(clinic, "Filed", "Patientname", "1999-09-09"));
        await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == filed).ExecuteUpdateAsync(s => s.SetProperty(p => p.Email, onFile), Ct));
        var onFileClient = await ConfirmByCodeAsync(onFile);
        await Json(await AcceptAsync(onFileClient, TokenOf((await InviteAsync(desk, filed)).GetProperty("link").GetString()!))); // defaults to the file's email
        var offFile = $"offfile-{Guid.NewGuid():N}@example.com";
        var offFileClient = await ConfirmByCodeAsync(offFile);
        // An address off the file is leadership's to send (final review C1).
        await Json(await AcceptAsync(offFileClient, TokenOf((await InviteAsync(clinic.Owner, filed, email: offFile)).GetProperty("link").GetString()!), "Parent"));
        var filedNotices = (await NoticesForAsync(ownerId)).Where(n => n.Kind == "care.portal_linked" && n.Link.Contains(filed.ToString())).ToList();
        Assert.Equal(2, filedNotices.Count);
        Assert.Single(filedNotices, n => n.Body.Contains("sent to an address that isn't on the patient's file"));
        Assert.Single(filedNotices, n => n.Body.Contains("as self.") && !n.Body.Contains("address"));
        Assert.All(filedNotices, n => Assert.DoesNotContain(offFile, n.Body));
    }

    /// <summary>I2: revoking a link expires the patient's pending invites; pending invites are listed (no token) and can be cancelled.</summary>
    [Fact]
    public async Task Pending_invites_are_listed_cancelled_and_expired_by_a_revoke()
    {
        var clinic = await kit.ClinicAsync("Portal Withdraw Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Withdrawn", "Patient"));
        var (portal, _, _) = await AccountAsync();

        // Link-only and off-file invites are leadership's (final review C1); the desk lists and cancels them.
        var used = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        var leftover = TokenOf((await InviteAsync(clinic.Owner, patientId, email: "withdrawn-family@example.com")).GetProperty("link").GetString()!);
        await Json(await AcceptAsync(portal, used));

        var pending = await desk.GetAsync($"/api/care/patients/{patientId}/invites", Ct);
        var pendingText = await pending.Content.ReadAsStringAsync(Ct);
        var row = Assert.Single((await Json(pending)).EnumerateArray());
        Assert.DoesNotContain(leftover, pendingText);
        Assert.DoesNotContain("token", pendingText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("withdrawn-family@example.com", pendingText);
        Assert.Contains("example.com", row.GetProperty("email").GetString());
        foreach (var client in new[] { cashier, doctor })
            await ProblemAsync(await client.GetAsync($"/api/care/patients/{patientId}/invites", Ct), HttpStatusCode.Forbidden);

        // Revoking the link withdraws the leftover invite too.
        var linkId = Id(Assert.Single((await Json(await desk.GetAsync($"/api/care/patients/{patientId}/portal-links", Ct))).EnumerateArray()));
        Assert.Equal(HttpStatusCode.NoContent, (await desk.DeleteAsync($"/api/care/patients/{patientId}/portal-links/{linkId}", Ct)).StatusCode);
        Assert.Empty((await Json(await desk.GetAsync($"/api/care/patients/{patientId}/invites", Ct))).EnumerateArray());
        var (family, _, _) = await AccountAsync();
        await ProblemAsync(await AcceptAsync(family, leftover), HttpStatusCode.Conflict, InviteExpired);

        // A single invite cancelled by the desk: gone from the list, refused on preview and accept.
        var mistake = await InviteAsync(clinic.Owner, patientId);
        var mistakeToken = TokenOf(mistake.GetProperty("link").GetString()!);
        var inviteId = Id(Assert.Single((await Json(await desk.GetAsync($"/api/care/patients/{patientId}/invites", Ct))).EnumerateArray()));
        foreach (var client in new[] { cashier, doctor })
            await ProblemAsync(await client.PostAsync($"/api/care/patients/{patientId}/invites/{inviteId}/cancel", null, Ct), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await desk.PostAsync($"/api/care/patients/{patientId}/invites/{inviteId}/cancel", null, Ct)).StatusCode);
        Assert.Empty((await Json(await desk.GetAsync($"/api/care/patients/{patientId}/invites", Ct))).EnumerateArray());
        await ProblemAsync(await api.CreateClient().GetAsync($"/api/care-portal/invites/{mistakeToken}", Ct), HttpStatusCode.Conflict, InviteExpired);
        await ProblemAsync(await AcceptAsync(family, mistakeToken), HttpStatusCode.Conflict, InviteExpired);
        await ProblemAsync(await desk.PostAsync($"/api/care/patients/{patientId}/invites/{Guid.NewGuid()}/cancel", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(0, await api.WithDbAsync(db => db.PatientLinks.IgnoreQueryFilters().CountAsync(l => l.PatientId == patientId && l.Active, Ct)));
    }

    /// <summary>An invite sent while the patient was alive can't be accepted once the patient is recorded deceased, and isn't used up.</summary>
    [Fact]
    public async Task Invite_for_a_deceased_patient_is_refused()
    {
        var clinic = await kit.ClinicAsync("Portal Deceased Invite Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Gone", "Patient"));
        var token = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        await SetPatientStatusAsync(patientId, PatientStatus.Deceased);
        var (portal, _, _) = await AccountAsync();
        await ProblemAsync(await AcceptAsync(portal, token), HttpStatusCode.Conflict, "This patient is recorded as deceased.");
        Assert.Equal(0, await LinksOfAsync(patientId));
        Assert.Equal(1, await PendingInvitesAsync(patientId));
    }

    /// <summary>
    /// Two accounts accept one invite at the same moment: both pass the first checks and wait on the accept lock (held here), then exactly
    /// one is linked and the other gets 409 "This invitation was already used."
    /// </summary>
    [Fact]
    public async Task Concurrent_accepts_link_exactly_one_account()
    {
        var clinic = await kit.ClinicAsync("Portal Race Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Raced", "Patient"));
        var token = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        var (first, _, _) = await AccountAsync();
        var (second, _, _) = await AccountAsync();

        HttpResponseMessage[] answers;
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var key = PatientInviteService.AcceptLockKey(patientId);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", Ct);
            var pending = new[] { AcceptAsync(first, token, "Parent"), AcceptAsync(second, token, "Guardian") };
            var deadline = DateTime.UtcNow.AddSeconds(20);
            var waiting = 0;
            // Only waiters on this key count (fix round 2): a bigint advisory lock is classid = high 32 bits, objid = low 32 bits, objsubid 1.
            var high = (long)((ulong)key >> 32);
            var low = key & 0xFFFFFFFFL;
            while (DateTime.UtcNow < deadline && waiting < 2 && !pending.Any(p => p.IsCompleted))
            {
                waiting = await api.WithDbAsync(d => d.Database.SqlQuery<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND classid::bigint = {high} AND objid::bigint = {low} AND objsubid = 1").FirstAsync(Ct));
                if (waiting < 2) await Task.Delay(50, Ct);
            }
            Assert.True(waiting >= 2, $"Both accepts should wait on the lock; {waiting} did.");
            await tx.CommitAsync(Ct);
            answers = await Task.WhenAll(pending);
        }

        Assert.Single(answers, a => a.IsSuccessStatusCode);
        await ProblemAsync(Assert.Single(answers, a => !a.IsSuccessStatusCode), HttpStatusCode.Conflict, InviteUsed);
        Assert.Equal(1, await LinksOfAsync(patientId));
    }

    // ---------- The portal route walk ----------

    /// <summary>Values that must never reach a portal response: draft note text, staff-only text (nursing notes, emergency reasons, bill reasons).</summary>
    private static readonly string[] HiddenMarkers =
    [
        "Draftcomplaint", "Draftdiagnosis", "Draftround", "Draftsummary", "Nursenotemarker", "Emergencymarker", "Manualmarker", "Removedmarker",
        "Adjustmarker", "Reversalmarker", "Draftdrugmarker", "Pendingtestmarker", "Unreviewedtestmarker", "Unreviewedresultmarker",
    ];

    /// <summary>Fields that are staff-only: who did what by id, review flags, allergy overrides, reasons on bills and discharges, ward-care charts.</summary>
    private static readonly HashSet<string> StaffOnlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "staffUserId", "emergency", "allergyAcknowledged", "overriddenAllergens", "needsReview", "depositOverrideBy", "amaReason", "deathCause",
        "deathTime", "removedReason", "removedAt", "reversalReason", "reversedByUserId", "clinicalRef", "priceItemId", "recordedByUserId", "takenBy",
        "authorUserId", "dischargedByUserId", "bookedByUserId", "nursingNotes", "medicationSummary", "doses", "fluids", "urgency", "stage",
        "idempotencyKey", "payerUserId", "unsignedRoundsAtDischarge", "adjustments", "byUserId", "resultFileKey", "requestedBy", "resultedBy",
        "lowDepositNotifiedAt", "addedByUserId", "removedByUserId", "checkedInByUserId", "isDeposit",
    };

    private sealed record PortalWorld(Guid PatientId, Guid PaymentId, Guid DoctorId, string InviteToken);

    /// <summary>Every GET under /api/care-portal, with the path to call in the walk world.</summary>
    private static readonly Dictionary<string, Func<PortalWorld, string>> PortalRoutes = new()
    {
        ["GET /api/care-portal/patients"] = _ => "/api/care-portal/patients",
        ["GET /api/care-portal/patients/{id:guid}/record"] = w => $"{P(w.PatientId)}/record",
        ["GET /api/care-portal/patients/{id:guid}/access-log"] = w => $"{P(w.PatientId)}/access-log",
        ["GET /api/care-portal/patients/{id:guid}/bills"] = w => $"{P(w.PatientId)}/bills",
        ["GET /api/care-portal/patients/{id:guid}/receipts/{paymentId:guid}.pdf"] = w => $"{P(w.PatientId)}/receipts/{w.PaymentId}.pdf",
        ["GET /api/care-portal/patients/{id:guid}/appointments"] = w => $"{P(w.PatientId)}/appointments",
        ["GET /api/care-portal/patients/{id:guid}/doctors"] = w => $"{P(w.PatientId)}/doctors",
        ["GET /api/care-portal/patients/{id:guid}/doctors/{doctorId:guid}/slots"] = w => $"{P(w.PatientId)}/doctors/{w.DoctorId}/slots?date={Today.AddDays(2):yyyy-MM-dd}",
        ["GET /api/care-portal/patients/{id:guid}/notifications"] = w => $"{P(w.PatientId)}/notifications",
        ["GET /api/care-portal/invites/{token}"] = w => $"/api/care-portal/invites/{w.InviteToken}",
    };

    private static IEnumerable<string> PropertyNames(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    yield return p.Name;
                    foreach (var n in PropertyNames(p.Value)) yield return n;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var n in PropertyNames(item)) yield return n;
                break;
        }
    }

    private static IEnumerable<string> StringValues(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: yield return e.GetString()!; break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var v in StringValues(p.Value)) yield return v;
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var v in StringValues(item)) yield return v;
                break;
        }
    }

    /// <summary>
    /// Walks every portal GET route as a linked account over a patient with signed and draft notes (consultation, ward round, discharge
    /// summary), a nursing note, an emergency open with a reason, and a bill with a manual line, a removed line, a discount, a reversed and a
    /// kept payment. No response carries draft text, staff-only text, a "Draft" status or a staff-only field; the signed text is there.
    /// </summary>
    [Fact]
    public async Task Portal_routes_carry_no_drafts_or_staff_fields()
    {
        _ = api.CreateClient();
        var routes = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && p.StartsWith("/api/care-portal/", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Where(m => m is "GET" or "ANY").Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Distinct().ToList();
        Assert.DoesNotContain(routes, r => r.StartsWith("ANY ", StringComparison.Ordinal));
        var missing = routes.Where(r => !PortalRoutes.ContainsKey(r)).ToList();
        Assert.True(missing.Count == 0, "Add these portal GET routes to the walk: " + string.Join(", ", missing));
        var stale = PortalRoutes.Keys.Where(k => !routes.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "These walk rows match no route: " + string.Join(", ", stale));

        var clinic = await kit.ClinicAsync("Portal Walk Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (stranger, _) = await kit.StaffAsync(clinic, "Doctor");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var patientId = Id(await kit.PatientAsync(clinic, "Walker", "Patient"));
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { "Penicillin" }, conditions = new[] { "Asthma" }, bloodGroup = "O+", genotype = "AS" }, Ct));

        var signed = await ConsultationAsync(clinic, patientId, "Signedcomplaint", sign: true);
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/notes/consultation/{signed}/addenda", new { text = "Addendummarker" }, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId = signed, allergyAcknowledged = false,
            items = new[] { new { drug = "Salbutamol", strength = "2 mg", dose = "1 tablet", frequency = "TDS", durationDays = 5, route = "Oral" } },
        }, Ct));
        var draft = await ConsultationAsync(clinic, patientId, "Draftcomplaint", sign: false, diagnosis: "Draftdiagnosis");
        // Fix round 1: a prescription on the draft consultation (hidden until the note is signed), a requested test with no result yet
        // (hidden), and a resulted test (shown, whatever the note's state: the patient was told a result is ready).
        await Json(await clinic.Owner.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId = draft, allergyAcknowledged = false,
            items = new[] { new { drug = "Draftdrugmarker", strength = "5 mg", dose = "1 tablet", frequency = "OD", durationDays = 3, route = "Oral" } },
        }, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync("/api/care/tests", new { patientId, consultationId = draft, kind = "Lab", description = "Pendingtestmarker screen" }, Ct));
        var resulted = Id(await Json(await clinic.Owner.PostAsJsonAsync("/api/care/tests",
            new { patientId, consultationId = draft, kind = "Lab", description = "Resultedtest smear" }, Ct)));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/tests/{resulted}/result", new { text = "Resultedmarker negative" }, Ct));
        // Post-build (user decision): a result shows only once a doctor (here the director) has reviewed it; a recorded, unreviewed result
        // stays hidden.
        await Json(await clinic.Owner.PostAsync($"/api/care/tests/{resulted}/review", null, Ct));
        var unreviewed = Id(await Json(await clinic.Owner.PostAsJsonAsync("/api/care/tests",
            new { patientId, consultationId = draft, kind = "Lab", description = "Unreviewedtestmarker swab" }, Ct)));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/tests/{unreviewed}/result", new { text = "Unreviewedresultmarker seen" }, Ct));

        var admissionId = await kit.AdmitAsync(clinic.Owner, patientId, "Walk admit reason", "Walk admitting diagnosis", emergencyReason: null);
        var round = Id(await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{admissionId}/ward-rounds", new { note = "Signedround" }, Ct)));
        await Json(await clinic.Owner.PostAsync($"/api/care/ward-rounds/{round}/sign", null, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{admissionId}/ward-rounds", new { note = "Draftround" }, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{admissionId}/nursing-notes", new { note = "Nursenotemarker" }, Ct));
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/admissions/{admissionId}/discharge-summary", new
        {
            diagnoses = "Draftsummary diagnoses", treatment = "Draftsummary treatment", condition = "Stable", takeHome = "Draftsummary home",
        }, Ct));
        using (var emergency = new HttpRequestMessage(HttpMethod.Get, $"/api/care/patients/{patientId}/record"))
        {
            emergency.Headers.Add("X-Care-Reason", Uri.EscapeDataString("Emergencymarker call"));
            await Json(await stranger.SendAsync(emergency, Ct));
        }

        var itemId = await kit.PriceItemAsync(clinic, "Walk consultation", "Consultation", 500_000);
        var billId = await BillAsync(clinic, patientId, itemId);
        var manual = await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/lines", new { priceItemId = itemId, quantity = 1, reason = "Manualmarker" }, Ct));
        var manualLine = manual.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("reason").GetString() == "Manualmarker");
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/lines/{Id(manualLine)}/remove", new { reason = "Removedmarker" }, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/adjustments", new { kind = "Discount", amountMinor = 10_000, reason = "Adjustmarker" }, Ct));
        var reversed = Id(await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 10_000, method = "Cash" }, Ct)));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/payments/{reversed}/reverse", new { reason = "Reversalmarker" }, Ct));
        var kept = Id(await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 20_000, method = "Pos" }, Ct)));

        var (portal, _) = await LinkedAsync(clinic, patientId);
        var slots = await PortalSlotsAsync(portal, patientId, doctorId, Today.AddDays(2));
        await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[0], reason = "Walk booking" }, Ct));
        var spare = TokenOf((await InviteAsync(clinic.Owner, patientId)).GetProperty("link").GetString()!);
        var world = new PortalWorld(patientId, kept, doctorId, spare);

        // The seed is there to be leaked: the director's record holds every hidden marker.
        var staffView = await (await clinic.Owner.GetAsync($"/api/care/patients/{patientId}/record", Ct)).Content.ReadAsStringAsync(Ct);
        foreach (var marker in new[] { "Draftcomplaint", "Draftround", "Draftsummary", "Nursenotemarker", "Draftdrugmarker", "Pendingtestmarker", "Unreviewedresultmarker" })
            Assert.Contains(marker, staffView);

        var failures = new List<string>();
        var seen = new System.Text.StringBuilder();
        foreach (var route in routes)
        {
            var response = await portal.GetAsync(PortalRoutes[route](world), Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            if (!response.IsSuccessStatusCode)
            {
                failures.Add($"{route} → {(int)response.StatusCode}: {body}");
                continue;
            }
            if (response.Content.Headers.ContentType?.MediaType == "application/pdf") continue; // the receipt prints no lines or reasons (Task 11)
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                failures.Add($"{route} → {response.Content.Headers.ContentType?.MediaType}");
                continue;
            }
            seen.Append(body);
            var root = JsonDocument.Parse(body).RootElement;
            foreach (var name in PropertyNames(root).Where(StaffOnlyNames.Contains).Distinct()) failures.Add($"{route} → staff-only field {name}");
            foreach (var marker in HiddenMarkers.Where(m => body.Contains(m, StringComparison.OrdinalIgnoreCase))) failures.Add($"{route} → hidden text {marker}");
            if (StringValues(root).Contains("Draft")) failures.Add($"{route} → a Draft status");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        var all = seen.ToString();
        foreach (var marker in new[] { "Signedcomplaint", "Addendummarker", "Signedround", "Salbutamol", "Walk admit reason", "Walk booking", "Walk consultation",
                     "Resultedmarker", "Resultedtest smear" })
            Assert.Contains(marker, all);
    }
}
