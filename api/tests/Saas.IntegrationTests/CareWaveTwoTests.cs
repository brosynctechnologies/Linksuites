using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 2 known bugs in CareLink: the allergy check's false match and missing classes, a transfer's deposit, early doses, the receipt's
/// balance at the time, debtors net of credit, the bill-due notice once per balance, a cap on portal bookings, the portal reschedule's
/// doctor check, and the files of corrected results.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareWaveTwoTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);
    private static string P(Guid patientId) => $"/api/care-portal/patients/{patientId}";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    // ---------- 16. Allergy check ----------

    [Theory]
    [InlineData("Nystatin", "Statin", false)]
    [InlineData("Nystatin oral suspension", "Statins", false)]
    [InlineData("Atorvastatin", "Statin", true)]
    [InlineData("Aspirin", "ASA", true)]
    [InlineData("Nasal saline spray", "ASA", false)]
    [InlineData("Amoxicillin", "PCN", true)]
    [InlineData("Ceftriaxone", "Cephalosporins", true)]
    [InlineData("Cefalexin", "Cephalosporin", true)]
    [InlineData("Enoxaparin", "Heparin", true)]
    [InlineData("Clexane", "heparin", true)]
    [InlineData("Paracetamol", "Heparin", false)]
    public void The_allergy_check_knows_more_classes_and_no_longer_matches_inside_another_drug(string drug, string allergy, bool matches)
    {
        Assert.Equal(matches, AllergyMatcher.Matches([drug], [allergy]).Count > 0);
    }

    // ---------- 17. Transfer and the new ward's deposit ----------

    [Fact]
    public async Task A_transfer_to_a_ward_with_a_higher_deposit_needs_the_difference()
    {
        var clinic = await kit.ClinicAsync("Deposit Transfer Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var general = await kit.WardAsync(clinic, "General", minDepositMinor: 1_000_000);
        var privateWard = await kit.WardAsync(clinic, "Private", minDepositMinor: 5_000_000);
        var g1 = await kit.BedAsync(clinic, general, "G1");
        var p1 = await kit.BedAsync(clinic, privateWard, "P1");
        var patientId = Id(await kit.PatientAsync(clinic, "Deposit", "Mover"));
        var admissionId = await kit.AdmitAsync(doctor, patientId);
        await Json(await kit.AssignBedAsync(desk, admissionId, g1, depositMinor: 1_000_000));

        var refused = await ProblemAsync(await desk.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer", new { bedId = p1 }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("deposit_below_minimum", refused.GetProperty("code").GetString());
        Assert.Equal(g1, await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.Id == admissionId).Select(a => a.BedId).SingleAsync(Ct)));

        // The difference paid with the move: accepted, and recorded as a deposit.
        var moved = await Json(await desk.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer",
            new { bedId = p1, depositMinor = 4_000_000L, method = "Cash" }, Ct));
        Assert.Equal(p1, moved.GetProperty("bedId").GetGuid());
        var deposits = await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters()
            .Where(p => p.PatientId == patientId && p.IsDeposit && p.Status == CarePaymentStatus.Succeeded).SumAsync(p => p.AmountMinor, Ct));
        Assert.Equal(5_000_000, deposits);
    }

    // ---------- 18. A dose long before it is due ----------

    [Fact]
    public async Task A_dose_cannot_be_given_long_before_it_is_due()
    {
        var clinic = await kit.ClinicAsync("Early Dose Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var wardId = await kit.WardAsync(clinic, "Early ward", nurseUserIds: [nurseId]);
        var bedId = await kit.BedAsync(clinic, wardId, "E1");
        var patientId = Id(await kit.PatientAsync(clinic, "Early", "Dose"));
        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, admissionId, allergyAcknowledged = false,
            items = new[] { new { drug = "Earlymycin", dose = "1 tablet", frequency = "BD", durationDays = 2, route = "Oral" } },
        }, Ct));
        var doses = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == admissionId).OrderBy(d => d.DueAt).ToListAsync(Ct));

        var early = await ProblemAsync(await nurse.PostAsJsonAsync($"/api/care/doses/{doses[2].Id}", new { status = "Given" }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("dose_not_due", early.GetProperty("code").GetString());
        Assert.Equal(DoseStatus.Due, await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == doses[2].Id).Select(d => d.Status).SingleAsync(Ct)));
        // The next dose (within the hour) is fine, and a later one may still be held ahead of time.
        await Json(await nurse.PostAsJsonAsync($"/api/care/doses/{doses[0].Id}", new { status = "Given" }, Ct));
        await Json(await doctor.PostAsJsonAsync($"/api/care/doses/{doses[3].Id}", new { status = "Held", note = "Nil by mouth tomorrow" }, Ct));
    }

    // ---------- 19, 20. Receipts and debtors ----------

    private sealed record Billed(CareKit.Clinic Clinic, HttpClient Cashier, Guid PatientId, Guid BillId, Guid ItemId);

    private async Task<Billed> BilledAsync(string name, long amountMinor = 500_000)
    {
        var clinic = await kit.ClinicAsync(name);
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = Id(await kit.PatientAsync(clinic, "Billed", "Patient"));
        var itemId = await kit.PriceItemAsync(clinic, "Consultation", "Consultation", amountMinor);
        var billId = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct)).Id);
        await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, itemId, 1, null, Ct)).Id);
        return new Billed(clinic, cashier, patientId, billId, itemId);
    }

    [Fact]
    public async Task A_receipt_shows_the_balance_right_after_its_payment()
    {
        var b = await BilledAsync("Receipt Balance Clinic");
        var first = Id(await Json(await b.Cashier.PostAsJsonAsync($"/api/care/bills/{b.BillId}/payments", new { amountMinor = 200_000, method = "Cash" }, Ct)));
        var second = Id(await Json(await b.Cashier.PostAsJsonAsync($"/api/care/bills/{b.BillId}/payments", new { amountMinor = 100_000, method = "Cash" }, Ct)));

        var one = await api.AsTenantAsync<PaymentService, CareReceiptData>(b.Clinic.TenantId, s => s.ReceiptDataAsync(first, Ct));
        var two = await api.AsTenantAsync<PaymentService, CareReceiptData>(b.Clinic.TenantId, s => s.ReceiptDataAsync(second, Ct));
        Assert.Equal(Saas.Application.Common.Money.Format(300_000, "NGN"), one.BillBalance);
        Assert.Equal(Saas.Application.Common.Money.Format(200_000, "NGN"), two.BillBalance);
    }

    [Fact]
    public async Task Debtors_net_a_patients_credit_on_other_bills()
    {
        var b = await BilledAsync("Net Debtors Clinic");
        var other = await api.AsTenantAsync<BillingService, Guid>(b.Clinic.TenantId, async s => (await s.OpenBillForVisitAsync(b.PatientId, Guid.NewGuid(), Ct)).Id);
        await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(x => x.Id == other)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditMinor, 200_000L).SetProperty(x => x.BalanceMinor, 0L), Ct));

        var report = await Json(await b.Clinic.Owner.GetAsync("/api/care/reports/debtors", Ct));
        var row = Assert.Single(report.GetProperty("debtors").EnumerateArray());
        Assert.Equal(300_000, row.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(300_000, report.GetProperty("totalMinor").GetInt64());

        // Credit that covers the debt: the patient is no longer a debtor.
        await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(x => x.Id == other)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditMinor, 600_000L), Ct));
        Assert.Equal(0, (await Json(await b.Clinic.Owner.GetAsync("/api/care/reports/debtors", Ct))).GetProperty("count").GetInt32());
    }

    // ---------- 21. Bill-due notice once per balance ----------

    [Fact]
    public async Task Returning_to_waiting_payment_does_not_repeat_the_bill_due_notice()
    {
        var clinic = await kit.ClinicAsync("Bill Due Once Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Due", "Once"));
        var (_, portalUser) = await kit.PortalAccountAsync(clinic, patientId);
        var visit = await kit.CheckInAsync(desk, patientId);
        var visitId = Id(visit);
        var billId = visit.GetProperty("billId").GetGuid();
        var itemId = await kit.PriceItemAsync(clinic, "Card", "Other", 100_000);
        await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, itemId, 1, null, Ct)).Id);

        Task<int> NoticesAsync() => api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == portalUser && n.Kind == CareNotices.BillDue, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingPayment" }, Ct));
        Assert.Equal(1, await NoticesAsync());
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WithDoctor" }, Ct));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingPayment" }, Ct));
        Assert.Equal(1, await NoticesAsync());

        // A new charge changes what is owed: going back to payment tells the patient again.
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WithDoctor" }, Ct));
        await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, itemId, 1, null, Ct)).Id);
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingPayment" }, Ct));
        Assert.Equal(2, await NoticesAsync());
    }

    // ---------- 22, 24. Portal bookings ----------

    private static object[] EveryDay() => Enum.GetValues<DayOfWeek>()
        .Select(d => (object)new { weekday = d.ToString(), start = "07:00", end = "21:00", slotMinutes = 15 }).ToArray();

    private async Task<Guid> DoctorWithHoursAsync(CareKit.Clinic clinic)
    {
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = EveryDay() }, Ct));
        return doctorId;
    }

    private static async Task<List<DateTimeOffset>> SlotsAsync(HttpClient portal, Guid patientId, Guid doctorId, DateOnly date) =>
        (await Json(await portal.GetAsync($"{P(patientId)}/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();

    [Fact]
    public async Task A_portal_account_holds_at_most_five_open_future_bookings()
    {
        var clinic = await kit.ClinicAsync("Booking Cap Clinic");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var patientId = Id(await kit.PatientAsync(clinic, "Many", "Bookings"));
        var (portal, _) = await kit.PortalAccountAsync(clinic, patientId, "Self");
        var slots = await SlotsAsync(portal, patientId, doctorId, Today.AddDays(4));
        for (var i = 0; i < PortalService.MaxOpenPortalBookings; i++)
            await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[i * 2], reason = "Check-up" }, Ct));

        var refused = await ProblemAsync(await portal.PostAsJsonAsync($"{P(patientId)}/appointments",
            new { doctorUserId = doctorId, start = slots[20], reason = "Check-up" }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("too_many_bookings", refused.GetProperty("code").GetString());

        // The desk can still book for the patient; a cancelled portal booking frees a place.
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId, doctorUserId = doctorId, start = slots[22] }, Ct));
        var mine = (await Json(await portal.GetAsync($"{P(patientId)}/appointments", Ct))).GetProperty("appointments").EnumerateArray()
            .First(a => a.GetProperty("source").GetString() == "Portal");
        await Json(await portal.PostAsync($"{P(patientId)}/appointments/{Id(mine)}/cancel", null, Ct));
        await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[24], reason = "Check-up" }, Ct));
    }

    [Fact]
    public async Task A_portal_reschedule_checks_the_doctor_is_still_bookable()
    {
        var clinic = await kit.ClinicAsync("Reschedule Doctor Clinic");
        var doctorId = await DoctorWithHoursAsync(clinic);
        var patientId = Id(await kit.PatientAsync(clinic, "Moving", "Patient"));
        var (portal, _) = await kit.PortalAccountAsync(clinic, patientId, "Self");
        var slots = await SlotsAsync(portal, patientId, doctorId, Today.AddDays(3));
        var booked = Id(await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[0], reason = "Check-up" }, Ct)));

        // The doctor leaves the clinic before the patient moves the booking.
        await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == clinic.TenantId && m.UserId == doctorId).ExecuteDeleteAsync(Ct));
        await ProblemAsync(await portal.PostAsJsonAsync($"{P(patientId)}/appointments/{booked}/reschedule", new { start = slots[2] }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(slots[0], await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.Id == booked).Select(a => a.Start).SingleAsync(Ct)));
    }

    // ---------- 24. Files of corrected results ----------

    private static readonly byte[] FirstPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% first result\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");
    private static readonly byte[] SecondPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% corrected result\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static MultipartFormDataContent PdfForm(byte[] bytes, params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "result.pdf");
        foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        return form;
    }

    [Fact]
    public async Task The_file_of_a_corrected_result_can_still_be_opened_and_the_read_is_logged()
    {
        var clinic = await kit.ClinicAsync("Superseded File Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Corrected", "File"));
        var admissionId = await kit.AdmitAsync(doctor, patientId);
        var testId = Id(await Json(await doctor.PostAsJsonAsync("/api/care/tests", new { patientId, admissionId, kind = "Lab", description = "Full blood count" }, Ct)));
        await Json(await nurse.PostAsync($"/api/care/tests/{testId}/result", PdfForm(FirstPdf), Ct));
        await Json(await nurse.PostAsync($"/api/care/tests/{testId}/amend", PdfForm(SecondPdf, ("reason", "Wrong sample")), Ct));
        var amendmentId = await api.WithDbAsync(db => db.TestResultAmendments.IgnoreQueryFilters().Where(a => a.TestRequestId == testId).Select(a => a.Id).SingleAsync(Ct));

        var path = $"/api/care/tests/{testId}/superseded/{amendmentId}/file";
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.GetAsync(path, Ct)).StatusCode);
        var before = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId && a.StaffUserId == doctorId && a.Area == "file", Ct));
        var link = await Json(await doctor.GetAsync(path, Ct));
        Assert.Equal(before + 1,
            await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId && a.StaffUserId == doctorId && a.Area == "file", Ct)));
        var download = await api.CreateClient().GetAsync(new Uri(link.GetProperty("url").GetString()!).PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(FirstPdf, await download.Content.ReadAsByteArrayAsync(Ct));
        // An amendment of another test is not found through this test.
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.GetAsync($"/api/care/tests/{Guid.NewGuid()}/superseded/{amendmentId}/file", Ct)).StatusCode);
    }
}
