using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;
using Saas.Infrastructure.Notifications;
using Saas.Infrastructure.Persistence;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 14: every care notice goes through <see cref="CareNotices"/> (in-app and, where the spec asks, email through the outbox),
/// none carries clinical detail (drug, diagnosis, test, result, note text, the appointment's reason, an imported file's name), nothing is
/// sent before the caller commits, a Died discharge cancels the patient's appointments without telling anyone, and a suspended clinic is
/// closed to staff (the tenant status) and to the portal ("This clinic's portal is unavailable.").
/// <para>
/// Patient notices go to portal users (<see cref="ICarePortalRecipients"/>, nobody until Task 15), so these tests run on a host where a
/// test lookup links the patient to a portal account.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareNotificationTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    // Clinical markers seeded on the patient: none may appear in any notice or email.
    private const string Drug = "Zentoracil";
    private const string Diagnosis = "Quorvian";
    private const string TestName = "Plasmodex";
    private const string ResultText = "Resultmarker";
    private const string NoteText = "Complaintmarker";
    private const string BookingReason = "Haemorrhoid";
    private const string Allergy = "Allergenmarker";
    private const string Condition = "Conditionmarker";
    private const string AdmitReason = "Admitmarker";
    private const string EmergencyReason = "Emergencymarker";
    private const string ImportFile = "Kalvirex-cohort";
    private static readonly string[] Markers =
        [Drug, Diagnosis, TestName, ResultText, NoteText, BookingReason, Allergy, Condition, AdmitReason, EmergencyReason, ImportFile];

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        var text = await r.Content.ReadAsStringAsync(Ct);
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);

    private static DateTimeOffset Lagos(DateOnly day, int hour, int minute = 0) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), CareCalendar.LagosOffset).ToUniversalTime();

    // ---------- A host whose portal lookup links patients to portal accounts ----------

    /// <summary>Patient → portal accounts, read by <see cref="LinkedRecipients"/> on the test host (Task 15 replaces the real lookup).</summary>
    private static readonly ConcurrentDictionary<Guid, Guid[]> Links = new();

    private sealed class LinkedRecipients : ICarePortalRecipients
    {
        public Task<IReadOnlyList<Guid>> ForPatientAsync(Guid patientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Links.TryGetValue(patientId, out var users) ? users : []);
    }

    private WebApplicationFactory<Program> PortalHost() =>
        api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<ICarePortalRecipients, LinkedRecipients>()));

    /// <summary>The same signed-in user, calling the portal host.</summary>
    private static HttpClient On(WebApplicationFactory<Program> host, HttpClient client)
    {
        var moved = host.CreateClient();
        moved.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        return moved;
    }

    private Task<Guid> PortalUserAsync(string name) => api.WithDbAsync(async db =>
    {
        var email = $"portal-{Guid.NewGuid():N}@example.com";
        var account = new AppUser { FullName = name, Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
        db.Users.Add(account);
        await db.SaveChangesAsync(Ct);
        return account.Id;
    });

    private static async Task<T> AsClinicAsync<T>(WebApplicationFactory<Program> host, Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        return await action(scope.ServiceProvider);
    }

    private static object[] EveryDay() => Enum.GetValues<DayOfWeek>()
        .Select(d => (object)new { weekday = d.ToString(), start = "07:00", end = "21:00", slotMinutes = 15 }).ToArray();

    private static async Task<List<DateTimeOffset>> SlotsAsync(HttpClient client, Guid doctorId, DateOnly date) =>
        (await Json(await client.GetAsync($"/api/care/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();

    private Task<List<UserNotification>> InAppAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().Where(n => n.TenantId == tenantId && n.Product == "care").ToListAsync(Ct));

    private Task<List<Notification>> EmailsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().Where(n => n.TenantId == tenantId && n.TemplateKey.StartsWith("care.")).ToListAsync(Ct));

    private static MultipartFormDataContent Form(string csv, string fileName)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static void AssertNoMarker(string what, params string?[] texts)
    {
        foreach (var text in texts)
            foreach (var marker in Markers)
                Assert.False(text?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true, $"{what} carries '{marker}': {text}");
    }

    // ---------- The brief's tests ----------

    /// <summary>
    /// Drives every care notice through the real routes and jobs with clinical markers on the patient (allergy, condition, booking
    /// reason, note, diagnosis, drug, test, result, admission reason, emergency reason, imported file name), then renders every in-app
    /// notice and every email: every kind is there, and none carries a marker.
    /// </summary>
    [Fact]
    public async Task No_notice_contains_clinical_detail()
    {
        await using var host = PortalHost();
        var clinic = await kit.ClinicAsync("Notice Detail Clinic");
        var (doctorMain, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurseMain, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (deskMain, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashierMain, _) = await kit.StaffAsync(clinic, "Cashier");
        var (strangerMain, _) = await kit.StaffAsync(clinic, "Doctor");
        var (owner, doctor, nurse, desk, cashier, stranger) =
            (On(host, clinic.Owner), On(host, doctorMain), On(host, nurseMain), On(host, deskMain), On(host, cashierMain), On(host, strangerMain));
        var tenantId = clinic.TenantId;

        var drugItem = await kit.PriceItemAsync(clinic, $"{Drug} 500mg", "Drug", 200_000);
        var testItem = await kit.PriceItemAsync(clinic, $"{TestName} panel", "Test", 1_000_000);
        var wardId = await kit.WardAsync(clinic, "Notice ward", nurseUserIds: [nurseId]);
        var bedId = await kit.BedAsync(clinic, wardId, "N1");

        var patientId = Id(await kit.PatientAsync(clinic, "Notice", "Patient"));
        var portalUser = await PortalUserAsync("Notice Parent");
        Links[patientId] = [portalUser];
        await Json(await owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { Allergy }, conditions = new[] { Condition }, bloodGroup = "O+", genotype = "AA" }, Ct));

        // Appointments: booked, moved, cancelled (each with a reason), and tomorrow's reminder.
        await Json(await owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = EveryDay() }, Ct));
        var date = Today.AddDays(3);
        var slots = await SlotsAsync(desk, doctorId, date);
        var booked = Id(await Json(await desk.PostAsJsonAsync("/api/care/appointments",
            new { patientId, doctorUserId = doctorId, start = slots[0], reason = $"{BookingReason} review" }, Ct)));
        await Json(await desk.PutAsJsonAsync($"/api/care/appointments/{booked}", new { start = slots[1], reason = $"{BookingReason} again" }, Ct));
        var toCancel = Id(await Json(await desk.PostAsJsonAsync("/api/care/appointments",
            new { patientId, doctorUserId = doctorId, start = slots[4], reason = $"{BookingReason} check" }, Ct)));
        await Json(await desk.PostAsync($"/api/care/appointments/{toCancel}/cancel", null, Ct));
        await api.WithDbAsync(db =>
        {
            var start = Lagos(Today.AddDays(1), 10, 30);
            db.Appointments.Add(new Appointment
            {
                TenantId = tenantId, PatientId = patientId, DoctorUserId = doctorId, Start = start, End = start.AddMinutes(15),
                Status = AppointmentStatus.Booked, Reason = $"{BookingReason} reminder",
            });
            return db.SaveChangesAsync(Ct);
        });
        Assert.Equal(1, await AsClinicAsync(host, tenantId, sp => sp.GetRequiredService<CareJobSteps>().SendRemindersAsync(Lagos(Today, 9), Ct)));

        // The visit: triage with a critical reading (patient waiting, critical vital), a consultation with the markers, a drug, a test and
        // its result, then payment: bill due, a desk payment, an online payment, a mismatch and an unverified checkout.
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 38.2, systolic = 120, diastolic = 80, pulse = 88, respRate = 16, spO2 = 85 },
            urgency = "Urgent", doctorUserId = doctorId,
        }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        await Json(await doctor.PutAsJsonAsync($"/api/care/consultations/{consultationId}", new
        {
            complaint = $"{NoteText} for three days", history = "None", examination = "Pale", plan = $"Start {Drug}",
            diagnoses = new object[] { new { text = $"{Diagnosis} fever", code = (string?)null, primary = true } },
        }, Ct));
        await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId, allergyAcknowledged = false,
            items = new[] { new { drug = Drug, strength = "500 mg", dose = "1 capsule", frequency = "TDS", durationDays = 5, route = "Oral", priceItemId = (Guid?)drugItem } },
        }, Ct));
        var testId = Id(await Json(await doctor.PostAsJsonAsync("/api/care/tests",
            new { patientId, consultationId, kind = "Lab", description = $"{TestName} panel", priceItemId = testItem }, Ct)));
        await Json(await doctor.PostAsync($"/api/care/consultations/{consultationId}/sign", null, Ct));
        await Json(await nurse.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = $"{ResultText} positive" }, Ct));
        // Post-build (user decision): the patient's "result ready" notice is sent when a doctor reviews the result.
        await Json(await doctor.PostAsync($"/api/care/tests/{testId}/review", null, Ct));

        await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingPayment" }, Ct));
        var visitBill = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(b => b.VisitId == visitId).Select(b => b.Id).FirstAsync(Ct));
        await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{visitBill}/payments", new { amountMinor = 100_000, method = "Cash" }, Ct));

        const string careKey = "sk_test_integration_notice_key";
        await Json(await owner.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = careKey }, Ct));
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        Task<string> CheckoutAsync() => AsClinicAsync(host, tenantId, async sp =>
            (await sp.GetRequiredService<OnlinePaymentService>().StartCheckoutAsync(visitBill, ownerId, 100_000, Ct)).Reference);
        var paid = await CheckoutAsync();
        var wrong = await CheckoutAsync();
        var stranded = await CheckoutAsync();
        async Task WebhookAsync(string reference, long amount)
        {
            var body = SchoolKit.ChargeSuccessBody(reference, amount);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/paystack/care/{tenantId}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-paystack-signature", SchoolKit.Sign(body, careKey));
            Assert.Equal(HttpStatusCode.OK, (await host.CreateClient().SendAsync(request, Ct)).StatusCode);
        }
        await WebhookAsync(paid, 100_000);
        await WebhookAsync(wrong, 50_000);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/care/settings/paystack?force=true", Ct)).StatusCode);
        await Json(await cashier.PostAsync($"/api/care/payments/online/{stranded}/verify", null, Ct));

        // The admission: an admission drug (doses, a low deposit), a missed dose, emergency access, the summary and a home discharge.
        var admissionId = await kit.AdmitAsync(doctor, patientId, reason: $"{AdmitReason} and vomiting", admittingDiagnosis: $"{Diagnosis} fever", emergencyReason: null);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, admissionId, allergyAcknowledged = false,
            items = new[] { new { drug = Drug, strength = "500 mg", dose = "1 capsule", frequency = "QDS", durationDays = 2, route = "Oral", priceItemId = (Guid?)drugItem } },
        }, Ct));
        Assert.Equal(1, await AsClinicAsync(host, tenantId, sp => sp.GetRequiredService<CareJobSteps>().CheckDepositsAsync(DateTimeOffset.UtcNow, Ct)));
        var firstDose = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == admissionId)
            .OrderBy(d => d.DueAt).Select(d => d.Id).FirstAsync(Ct));
        await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == firstDose)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DueAt, DateTimeOffset.UtcNow.AddHours(-2)).SetProperty(d => d.Status, DoseStatus.Due), Ct));
        Assert.Equal(1, await AsClinicAsync(host, tenantId, sp => sp.GetRequiredService<CareJobSteps>().FlagMissedDosesAsync(DateTimeOffset.UtcNow, Ct)));

        using (var emergency = new HttpRequestMessage(HttpMethod.Get, $"/api/care/patients/{patientId}/record"))
        {
            emergency.Headers.Add("X-Care-Reason", Uri.EscapeDataString($"{EmergencyReason} call"));
            await Json(await stranger.SendAsync(emergency, Ct));
        }

        await Json(await doctor.PutAsJsonAsync($"/api/care/admissions/{admissionId}/discharge-summary", new
        {
            diagnoses = $"{Diagnosis} fever, resolved", treatment = $"{Drug} for five days", condition = "Stable",
            takeHome = $"Complete the {Drug}", followUp = Today.AddDays(7),
        }, Ct));
        await Json(await doctor.PostAsync($"/api/care/admissions/{admissionId}/discharge-summary/sign", null, Ct));
        await Json(await doctor.PostAsJsonAsync($"/api/care/admissions/{admissionId}/discharge", new { type = "Home" }, Ct));

        // Task 15: a portal invite, emailed to the family (the clinic, the patient's first name and the link only).
        await Json(await owner.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { email = "notice-family@example.com" }, Ct));
        // Fix round 1: a link-only invite accepted by a family account tells the director (care.portal_linked, in-app).
        var linkOnly = (await Json(await owner.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct))).GetProperty("link").GetString()!;
        var family = host.CreateClient();
        var familyAuth = await Json(await api.SignUpAsync(family, "/api/auth/patient/signup",
            new { fullName = "Notice Family", email = $"notice-family-{Guid.NewGuid():N}@example.com", password = "password123" }, Ct));
        family.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", familyAuth.GetProperty("accessToken").GetString());
        await Json(await family.PostAsJsonAsync($"/api/care-portal/invites/{linkOnly[(linkOnly.LastIndexOf('/') + 1)..]}/accept", new { relationship = "Parent" }, Ct));

        // An import whose file name names a condition.
        var csv = "patient_number,first_name,middle_name,last_name,sex,date_of_birth,phone,email,address,next_of_kin_name,next_of_kin_phone,next_of_kin_relationship\r\n" +
                  ",Imported,,Person,F,1985-01-02,,,,,,\r\n";
        var preview = await Json(await owner.PostAsync("/api/care/imports/care-patients/preview", Form(csv, $"{ImportFile}.csv"), Ct));
        await Json(await owner.PostAsync($"/api/care/imports/{preview.GetProperty("token").GetString()}/confirm", null, Ct));

        // Every kind was written, in-app; every email kind went to the outbox.
        var inApp = await InAppAsync(tenantId);
        var emails = await EmailsAsync(tenantId);
        foreach (var kind in CareNotices.Kinds.Where(k => k != "care.patient_invite")) // the portal invite is an email only (Task 15)
            Assert.True(inApp.Any(n => n.Kind == kind), $"no in-app {kind}");
        foreach (var kind in CareNotices.EmailKinds)
            Assert.True(emails.Any(n => n.TemplateKey == kind), $"no email {kind}");
        Assert.All(inApp, n => Assert.Equal("care", n.Product));
        // Ruling (Task 15): patient notices name the patient's first name, so a guardian of several patients can tell them apart.
        Assert.Equal("A new result for Notice is ready in your CareLink portal.", Assert.Single(inApp, n => n.Kind == CareNotices.ResultReady).Body);
        // Patient notices reach the portal account only; staff notices never do.
        Assert.All(inApp.Where(n => n.Kind is CareNotices.AppointmentBooked or CareNotices.AppointmentChanged or CareNotices.AppointmentReminder
            or CareNotices.ResultReady or CareNotices.SummaryReady or CareNotices.BillDue or CareNotices.PaymentReceived), n => Assert.Equal(portalUser, n.UserId));

        // Nothing clinical anywhere: the feed rows, and every email as the dispatcher renders it.
        foreach (var n in inApp) AssertNoMarker($"in-app {n.Kind}", n.Title, n.Body, n.Link);
        foreach (var n in emails)
        {
            var rendered = EmailTemplates.Render(n, "CareLink");
            AssertNoMarker($"email {n.TemplateKey}", rendered.Subject, rendered.Text, rendered.Html);
            AssertNoMarker($"email payload {n.TemplateKey}", n.Payload.Values.ToArray());
            // A care template of its own, not the fallback that lists the payload.
            Assert.DoesNotContain("message: ", rendered.Text, StringComparison.Ordinal);
            Assert.NotEqual("CareLink", rendered.Subject);
        }
    }

    /// <summary>
    /// A suspended clinic: staff routes get 403 through the existing tenant status; the portal gate (Task 15's portal routes call it) gives
    /// 403 "This clinic's portal is unavailable."; and the care jobs skip the clinic.
    /// </summary>
    [Fact]
    public async Task Suspended_clinic_blocks_staff_and_portal()
    {
        var clinic = await kit.ClinicAsync("Suspended Notice Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Suspended", "Patient"));
        var gate = (Guid tenantId) => api.AsTenantAsync<CarePortalGate, bool>(tenantId, async g => { await g.EnsureOpenAsync(tenantId, Ct); return true; });

        // Open: staff and the portal gate both pass.
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync($"/api/care/patients/{patientId}", Ct)).StatusCode);
        Assert.True(await gate(clinic.TenantId));

        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Suspended), Ct));

        foreach (var client in new[] { desk, clinic.Owner })
        {
            var refused = await client.GetAsync($"/api/care/patients/{patientId}", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("This business is suspended. Contact support.",
                (await refused.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("detail").GetString());
        }
        var portal = await Assert.ThrowsAsync<ForbiddenException>(() => gate(clinic.TenantId));
        Assert.Equal(CarePortalGate.PortalUnavailable, portal.Message);
        Assert.Equal("This clinic's portal is unavailable.", CarePortalGate.PortalUnavailable);
        // A clinic that doesn't exist (or another product's business) reads the same: nothing to see.
        await Assert.ThrowsAsync<ForbiddenException>(() => gate(Guid.NewGuid()));

        // The care jobs leave a suspended clinic alone: a reminder that is due is not sent.
        var start = Lagos(Today.AddDays(1), 10);
        await api.WithDbAsync(db =>
        {
            db.Appointments.Add(new Appointment
            {
                TenantId = clinic.TenantId, PatientId = patientId, DoctorUserId = doctorId, Start = start, End = start.AddMinutes(15), Status = AppointmentStatus.Booked,
            });
            return db.SaveChangesAsync(Ct);
        });
        await api.Services.GetRequiredService<CareJobs>().RunOnceAsync(Lagos(Today, 9), Ct);
        Assert.Null(await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.PatientId == patientId).Select(a => a.ReminderSentAt).FirstAsync(Ct)));
    }

    // ---------- Carries ----------

    /// <summary>
    /// Carry (Task 10): a Died discharge cancels the patient's booked appointments, and nobody is told: no appointment notice, no bill-due
    /// notice and no email reaches the patient's portal accounts.
    /// </summary>
    [Fact]
    public async Task Died_discharge_cancels_appointments_silently()
    {
        await using var host = PortalHost();
        var clinic = await kit.ClinicAsync("Died Notice Clinic");
        var (doctorMain, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (deskMain, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (owner, doctor, desk) = (On(host, clinic.Owner), On(host, doctorMain), On(host, deskMain));
        var drugItem = await kit.PriceItemAsync(clinic, "Died drug", "Drug", 300_000);
        var patientId = Id(await kit.PatientAsync(clinic, "Died", "Patient"));
        var other = Id(await kit.PatientAsync(clinic, "Living", "Patient", "1980-01-01"));
        var portalUser = await PortalUserAsync("Died Family");
        Links[patientId] = [portalUser];
        Links[other] = [portalUser];

        await Json(await owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = EveryDay() }, Ct));
        var slots = await SlotsAsync(desk, doctorId, Today.AddDays(2));
        async Task<Guid> BookAsync(Guid patient, DateTimeOffset start) => Id(await Json(await desk.PostAsJsonAsync("/api/care/appointments",
            new { patientId = patient, doctorUserId = doctorId, start, reason = "Review" }, Ct)));
        var first = await BookAsync(patientId, slots[0]);
        var second = await BookAsync(patientId, slots[2]);
        var others = await BookAsync(other, slots[4]);
        var cancelledBefore = await BookAsync(patientId, slots[6]);
        await Json(await desk.PostAsync($"/api/care/appointments/{cancelledBefore}/cancel", null, Ct));

        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, admissionId, allergyAcknowledged = false,
            items = new[] { new { drug = "Died drug", strength = "1", dose = "1", frequency = "OD", durationDays = 1, route = "Oral", priceItemId = (Guid?)drugItem } },
        }, Ct));
        var before = (await InAppAsync(clinic.TenantId)).Count(n => n.UserId == portalUser);
        var emailsBefore = (await EmailsAsync(clinic.TenantId)).Count;
        Assert.True(before > 0); // the bookings did reach the family, so silence below is the discharge's doing

        var died = await Json(await doctor.PostAsJsonAsync($"/api/care/admissions/{admissionId}/discharge",
            new { type = "Died", deathTime = DateTimeOffset.UtcNow, deathCause = "Cardiac arrest" }, Ct));
        Assert.True(died.GetProperty("balanceMinor").GetInt64() > 0); // money is owed: still no bill-due notice

        var appointments = await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == clinic.TenantId)
            .ToDictionaryAsync(a => a.Id, Ct));
        Assert.Equal(AppointmentStatus.Cancelled, appointments[first].Status);
        Assert.Equal(AppointmentStatus.Cancelled, appointments[second].Status);
        Assert.NotNull(appointments[first].CancelledAt);
        Assert.Equal(AppointmentStatus.Booked, appointments[others].Status); // another patient's booking is untouched
        Assert.Equal(before, (await InAppAsync(clinic.TenantId)).Count(n => n.UserId == portalUser));
        Assert.Equal(emailsBefore, (await EmailsAsync(clinic.TenantId)).Count);

        // Later events for the deceased patient tell nobody either (a summary signed after death, say).
        await AsClinicAsync(host, clinic.TenantId, async sp =>
        {
            var notices = sp.GetRequiredService<CareNotices>();
            await notices.SummaryReadyAsync(patientId, Ct);
            await notices.BillDueAsync(patientId, "BILL-1", 100_000, Ct);
            await sp.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
            return true;
        });
        Assert.Equal(before, (await InAppAsync(clinic.TenantId)).Count(n => n.UserId == portalUser));
    }

    /// <summary>Carry (Task 12): no email echoes an imported file's name; the importer's in-app notice gives the counts only.</summary>
    [Fact]
    public async Task No_email_echoes_the_import_file_name()
    {
        var clinic = await kit.ClinicAsync("Import Notice Clinic");
        var (desk, deskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var csv = "patient_number,first_name,middle_name,last_name,sex,date_of_birth,phone,email,address,next_of_kin_name,next_of_kin_phone,next_of_kin_relationship\r\n" +
                  ",Filed,,Person,M,1979-05-06,,,,,,\r\n";
        var preview = await Json(await desk.PostAsync("/api/care/imports/care-patients/preview", Form(csv, $"{ImportFile} HIV list.csv"), Ct));
        await Json(await desk.PostAsync($"/api/care/imports/{preview.GetProperty("token").GetString()}/confirm", null, Ct));

        var notice = Assert.Single(await InAppAsync(clinic.TenantId), n => n.UserId == deskId);
        Assert.Equal(CareNotices.ImportFinished, notice.Kind);
        Assert.Equal("1 added and 0 updated from your file.", notice.Body);
        AssertNoMarker("import notice", notice.Title, notice.Body, notice.Link);
        Assert.DoesNotContain("HIV", notice.Body, StringComparison.Ordinal);
        var all = await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().Where(n => n.TenantId == clinic.TenantId).ToListAsync(Ct));
        foreach (var n in all)
        {
            var rendered = EmailTemplates.Render(n, "CareLink");
            AssertNoMarker($"email {n.TemplateKey}", rendered.Subject, rendered.Text);
        }
    }

    /// <summary>
    /// Notices and emails are rows in the caller's transaction: until it commits, nothing is visible to the dispatcher (which reads committed
    /// outbox rows only), and a rollback leaves nothing at all.
    /// </summary>
    [Fact]
    public async Task Nothing_is_sent_before_commit()
    {
        await using var host = PortalHost();
        var clinic = await kit.ClinicAsync("Commit Notice Clinic");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Commit", "Patient"));
        var portalUser = await PortalUserAsync("Commit Parent");
        Links[patientId] = [portalUser];

        async Task<(int InApp, int Emails)> CountAsync() =>
            ((await InAppAsync(clinic.TenantId)).Count, (await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters()
                .CountAsync(n => n.TenantId == clinic.TenantId && n.TemplateKey.StartsWith("care.") && n.Status == NotificationStatus.Pending, Ct))));

        async Task WriteAsync(bool commit)
        {
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(clinic.TenantId);
            var db = sp.GetRequiredService<AppDbContext>();
            var notices = sp.GetRequiredService<CareNotices>();
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            await notices.ResultReadyAsync(patientId, Ct);
            await notices.EmergencyAccessAsync(patientId, Ct);
            notices.CriticalVitalFor(doctorId);
            await db.SaveChangesAsync(Ct);
            // Saved but not committed: another connection (the dispatcher's) sees none of it.
            Assert.Equal((0, 0), await CountAsync());
            if (commit) await tx.CommitAsync(Ct);
            else await tx.RollbackAsync(Ct);
        }

        await WriteAsync(commit: false);
        Assert.Equal((0, 0), await CountAsync());
        await WriteAsync(commit: true);
        var (inApp, emails) = await CountAsync();
        Assert.Equal(3, inApp); // the portal user, the director, the doctor
        Assert.Equal(2, emails); // the portal user and the director
    }

    // ---------- Fix round 1 ----------

    /// <summary>
    /// M1 (a): every <c>care.*</c> string constant in the CareLink application code (CareNotices' own kinds and the services' aliases) is
    /// in <see cref="CareNotices.Kinds"/>, the list <see cref="No_notice_contains_clinical_detail"/> requires and scans. A new kind can't
    /// slip past the no-clinical-detail test, and EmailKinds is a subset of Kinds.
    /// </summary>
    [Fact]
    public void Every_care_notice_kind_is_covered()
    {
        var constants = typeof(CareNotices).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Saas.Application.Care", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (Name: $"{t.Name}.{f.Name}", Value: (string)f.GetRawConstantValue()!)))
            .Where(c => c.Value.StartsWith("care.", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(constants);
        foreach (var (name, value) in constants)
            Assert.True(CareNotices.Kinds.Contains(value), $"{name} = \"{value}\" is not in CareNotices.Kinds, so the no-clinical-detail test doesn't cover it.");
        Assert.All(CareNotices.EmailKinds, k => Assert.Contains(k, CareNotices.Kinds));
        Assert.Equal(CareNotices.Kinds.Count, CareNotices.Kinds.Distinct().Count());
    }

    /// <summary>M1 (b): every email kind renders through a care template of its own, never the generic payload dump.</summary>
    [Fact]
    public void Every_email_kind_has_its_own_template()
    {
        foreach (var kind in CareNotices.EmailKinds)
        {
            var n = new Notification
            {
                Channel = NotificationChannel.Email, Recipient = "someone@example.com", TemplateKey = kind,
                Payload = new() { ["clinic"] = "Template Clinic", ["title"] = "Template title", ["message"] = "Template sentence.", ["link"] = "https://care.test/portal", ["reference"] = "care_ref" },
            };
            var rendered = EmailTemplates.Render(n, "CareLink");
            Assert.NotEqual("CareLink", rendered.Subject); // the fallback's subject is the app name
            Assert.DoesNotContain("message: ", rendered.Text, StringComparison.Ordinal); // the fallback lists "key: value"
            Assert.DoesNotContain("clinic: ", rendered.Text, StringComparison.Ordinal);
            Assert.Contains("Template sentence.", rendered.Text, StringComparison.Ordinal);
            Assert.Contains("Template Clinic", rendered.Subject, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// M1 (c): within the CareLink application code only CareNotices.cs touches <c>InAppNotifications</c> or <c>NotificationQueue</c>, so
    /// every care notice goes through the one helper that builds non-clinical sentences.
    /// </summary>
    [Fact]
    public void Only_CareNotices_writes_care_notifications()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Saas.Application", "Care"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var careDir = Path.Combine(dir.FullName, "src", "Saas.Application", "Care");
        var files = Directory.GetFiles(careDir, "*.cs", SearchOption.AllDirectories);
        Assert.Contains(files, f => Path.GetFileName(f) == "CareNotices.cs");
        var offenders = files.Where(f => Path.GetFileName(f) != "CareNotices.cs")
            .Where(f => File.ReadAllText(f) is var text && (text.Contains("InAppNotifications", StringComparison.Ordinal) || text.Contains("NotificationQueue", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(careDir, f)).ToList();
        Assert.True(offenders.Count == 0, $"Care code writing notifications outside CareNotices: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Holds the patient's admission lock (as a Died discharge does), starts <paramref name="request"/>, waits until it is blocked on an
    /// advisory lock, marks the patient Deceased under the lock (as the discharge does), commits and returns the answer.
    /// </summary>
    private async Task<HttpResponseMessage> UnderHeldDeathAsync(Guid tenantId, Guid patientId, Func<Task<HttpResponseMessage>> request)
    {
        var key = AdmissionService.PatientLockKey(tenantId, patientId);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", Ct);
        var pending = request();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var blocked = false;
        while (!pending.IsCompleted && DateTime.UtcNow < deadline && !blocked)
        {
            blocked = await api.WithDbAsync(d => d.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND wait_event = 'advisory' AND query ILIKE '%pg_advisory_xact_lock%'").FirstAsync(Ct)) > 0;
            if (!blocked) await Task.Delay(50, Ct);
        }
        await db.Database.ExecuteSqlAsync($"UPDATE patients SET status = 'Deceased' WHERE id = {patientId}", Ct);
        await tx.CommitAsync(Ct);
        var response = await pending;
        Assert.True(blocked, $"The request didn't wait on the patient lock: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return response;
    }

    /// <summary>
    /// M3: booking and rescheduling re-check the patient under the patient lock, so a booking racing a Died discharge gets 409 "This
    /// patient is recorded as deceased." and no "booked" or "changed" notice reaches the family after the death commits.
    /// </summary>
    [Fact]
    public async Task Booking_racing_a_death_is_refused_without_a_notice()
    {
        await using var host = PortalHost();
        var clinic = await kit.ClinicAsync("Death Race Clinic");
        var (doctorMain, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (deskMain, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (owner, desk) = (On(host, clinic.Owner), On(host, deskMain));
        _ = doctorMain;
        await Json(await owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = EveryDay() }, Ct));
        var slots = await SlotsAsync(desk, doctorId, Today.AddDays(2));
        var portalUser = await PortalUserAsync("Race Family");

        async Task AssertDeceasedAsync(HttpResponseMessage r)
        {
            var text = await r.Content.ReadAsStringAsync(Ct);
            Assert.True(r.StatusCode == HttpStatusCode.Conflict, $"{(int)r.StatusCode} {text}");
            Assert.Equal(AppointmentService.PatientDeceased, JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json).GetProperty("detail").GetString());
        }

        // A booking that passed its first check waits for the discharge, then sees the death.
        var booker = Id(await kit.PatientAsync(clinic, "Book", "Racer"));
        Links[booker] = [portalUser];
        await AssertDeceasedAsync(await UnderHeldDeathAsync(clinic.TenantId, booker, () => desk.PostAsJsonAsync("/api/care/appointments",
            new { patientId = booker, doctorUserId = doctorId, start = slots[0], reason = "Review" }, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().CountAsync(a => a.PatientId == booker, Ct)));

        // A move of an existing booking does the same (the discharge itself would cancel it; here only the race is held).
        var mover = Id(await kit.PatientAsync(clinic, "Move", "Racer", "1970-02-02"));
        var appointment = Id(await Json(await desk.PostAsJsonAsync("/api/care/appointments",
            new { patientId = mover, doctorUserId = doctorId, start = slots[2], reason = "Review" }, Ct)));
        Links[mover] = [portalUser]; // linked after the booking, so the family has heard nothing yet
        await AssertDeceasedAsync(await UnderHeldDeathAsync(clinic.TenantId, mover, () =>
            desk.PutAsJsonAsync($"/api/care/appointments/{appointment}", new { start = slots[4] }, Ct)));
        Assert.Equal(slots[2], await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.Id == appointment).Select(a => a.Start).FirstAsync(Ct)));

        Assert.Empty((await InAppAsync(clinic.TenantId)).Where(n => n.UserId == portalUser));
        Assert.Empty((await EmailsAsync(clinic.TenantId)).Where(n => n.UserId == portalUser));
    }
}
