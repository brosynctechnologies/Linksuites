using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 13: the care jobs. Bed-day charges (one per Lagos day at the ward occupied at 00:00, admission day included, discharge
/// day excluded, a same-day stay once, idempotent, and charged at discharge before the bill is final), missed doses (Due doses on admitted
/// admissions only, one notice to the ward's nurses), no-shows, appointment reminders (once, the day before from 08:00 Lagos), the
/// low-deposit notice (once per crossing), revenue by bed charge day, the FreshBillAsync guard and the active-clinics-only rule.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareJobTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    /// <summary>The UTC instant of <paramref name="hour"/>:<paramref name="minute"/> Lagos time on <paramref name="day"/>.</summary>
    private static DateTimeOffset Lagos(DateOnly day, int hour, int minute = 0) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), CareCalendar.LagosOffset).ToUniversalTime();

    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);

    private Task RunAsync(Guid tenantId, DateTimeOffset now) => api.Services.GetRequiredService<CareJobs>().RunForClinicAsync(tenantId, now, Ct);

    /// <summary>A clinic with a doctor, a nurse on ward A, the front desk and the cashier; wards A (₦10,000 a day) and B (₦25,000 a day).</summary>
    private sealed record World(
        CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Nurse, Guid NurseId, HttpClient Desk, Guid DeskId, HttpClient Cashier,
        Guid CashierId, Guid WardA, Guid BedA, Guid WardB, Guid BedB, Guid PatientId, Guid AdmissionId);

    private async Task<World> WorldAsync(string name, bool bed = true, long deposit = 0)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, deskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, cashierId) = await kit.StaffAsync(clinic, "Cashier");
        var wardA = await kit.WardAsync(clinic, $"{name} A", nurseUserIds: [nurseId], bedPriceMinor: 1_000_000);
        var bedA = await kit.BedAsync(clinic, wardA, "A1");
        var wardB = await kit.WardAsync(clinic, $"{name} B", bedPriceMinor: 2_500_000);
        var bedB = await kit.BedAsync(clinic, wardB, "B1");
        var patientId = Id(await kit.PatientAsync(clinic, "Jobby", "Patient"));
        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        if (bed) await Json(await kit.AssignBedAsync(desk, admissionId, bedA, deposit));
        return new World(clinic, doctor, doctorId, nurse, nurseId, desk, deskId, cashier, cashierId, wardA, bedA, wardB, bedB, patientId, admissionId);
    }

    /// <summary>Moves the admission and its first bed assignment back to <paramref name="at"/>.</summary>
    private Task BackdateStayAsync(Guid admissionId, DateTimeOffset at) => api.WithDbAsync(async db =>
    {
        await db.Admissions.IgnoreQueryFilters().Where(a => a.Id == admissionId).ExecuteUpdateAsync(s => s.SetProperty(a => a.AdmittedAt, at), Ct);
        return await db.BedMoves.IgnoreQueryFilters().Where(m => m.AdmissionId == admissionId && m.FromBedId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.At, at), Ct);
    });

    private Task<Guid> BillIdAsync(Guid admissionId) => api.WithDbAsync(db =>
        db.PatientBills.IgnoreQueryFilters().Where(b => b.AdmissionId == admissionId).Select(b => b.Id).FirstAsync(Ct));

    private Task<List<BillLine>> BedLinesAsync(Guid admissionId) => api.WithDbAsync(async db =>
    {
        var billId = await db.PatientBills.IgnoreQueryFilters().Where(b => b.AdmissionId == admissionId).Select(b => b.Id).FirstAsync(Ct);
        return await db.BillLines.IgnoreQueryFilters().Where(l => l.BillId == billId && l.ChargeDay != null).OrderBy(l => l.ChargeDay).ToListAsync(Ct);
    });

    private Task<Guid> BedItemAsync(Guid wardId) => api.WithDbAsync(db =>
        db.Wards.IgnoreQueryFilters().Where(w => w.Id == wardId).Select(w => w.BedPriceItemId).FirstAsync(Ct));

    private Task<List<UserNotification>> NoticesAsync(Guid tenantId, string kind) => api.WithDbAsync(db =>
        db.UserNotifications.IgnoreQueryFilters().Where(n => n.TenantId == tenantId && n.Kind == kind).ToListAsync(Ct));

    private static object Item(string drug) => new { drug, dose = "1 tablet", frequency = "QDS", durationDays = 2, route = "Oral" };

    private async Task<List<MedicationDose>> PrescribeAsync(World w, string drug)
    {
        await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false, items = new[] { Item(drug) } }, Ct));
        return await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == w.AdmissionId).OrderBy(d => d.DueAt).ToListAsync(Ct));
    }

    private Task SetDueAsync(Guid doseId, DateTimeOffset dueAt, DoseStatus? status = null) => api.WithDbAsync(db =>
        db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == doseId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DueAt, dueAt).SetProperty(d => d.Status, d => status ?? d.Status), Ct));

    private Task<DoseStatus> DoseStatusAsync(Guid doseId) => api.WithDbAsync(db =>
        db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == doseId).Select(d => d.Status).FirstAsync(Ct));

    // ---------- The brief's tests ----------

    [Fact]
    public async Task Bed_charges_one_per_day_across_transfer_at_2330()
    {
        var w = await WorldAsync("Transfer Charge Clinic");
        var day1 = Today.AddDays(-3);
        await BackdateStayAsync(w.AdmissionId, Lagos(day1, 10));
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/transfer", new { bedId = w.BedB }, Ct));
        // The move to ward B happened at 23:30 Lagos on day 2: day 2 still started in ward A.
        await api.WithDbAsync(db => db.BedMoves.IgnoreQueryFilters().Where(m => m.AdmissionId == w.AdmissionId && m.FromBedId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.At, Lagos(day1.AddDays(1), 23, 30)), Ct));

        await RunAsync(w.Clinic.TenantId, Lagos(Today, 9)); // day 4

        var lines = await BedLinesAsync(w.AdmissionId);
        var itemA = await BedItemAsync(w.WardA);
        var itemB = await BedItemAsync(w.WardB);
        Assert.Equal([day1, day1.AddDays(1), day1.AddDays(2)], lines.Select(l => l.ChargeDay!.Value).ToArray());
        Assert.Equal([itemA, itemA, itemB], lines.Select(l => l.PriceItemId!.Value).ToArray());
        Assert.Equal([1_000_000L, 1_000_000L, 2_500_000L], lines.Select(l => l.AmountMinor).ToArray());
        Assert.All(lines, l => Assert.Equal(BillLineSource.Auto, l.Source));
        var bill = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().FirstAsync(b => b.AdmissionId == w.AdmissionId, Ct));
        Assert.Equal(4_500_000, bill.TotalMinor);
        Assert.Equal(4_500_000, bill.BalanceMinor);
        Assert.Equal(BillStatus.Unpaid, bill.Status);
    }

    [Fact]
    public async Task Same_day_stay_charged_once()
    {
        var w = await WorldAsync("Same Day Clinic");
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/discharge", new { type = "AgainstAdvice", amaReason = "Wanted to go home" }, Ct));

        var line = Assert.Single(await BedLinesAsync(w.AdmissionId));
        Assert.Equal(Today, line.ChargeDay);
        Assert.Equal(1_000_000, line.AmountMinor);
        // The nightly job never charges a discharged admission, today or tomorrow.
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        await RunAsync(w.Clinic.TenantId, Lagos(Today.AddDays(1), 9));
        Assert.Single(await BedLinesAsync(w.AdmissionId));
    }

    [Fact]
    public async Task Job_twice_adds_nothing()
    {
        var w = await WorldAsync("Twice Clinic");
        await BackdateStayAsync(w.AdmissionId, Lagos(Today.AddDays(-2), 15));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        Assert.Equal(2, (await BedLinesAsync(w.AdmissionId)).Count);

        // Even with the clinic's once-a-day marker cleared, every day already charged is skipped.
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(p => p.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastBedChargeDay, (DateOnly?)null), Ct));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        var lines = await BedLinesAsync(w.AdmissionId);
        Assert.Equal([Today.AddDays(-2), Today.AddDays(-1)], lines.Select(l => l.ChargeDay!.Value).ToArray());
        Assert.Equal(2_000_000, await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters()
            .Where(b => b.AdmissionId == w.AdmissionId).Select(b => b.TotalMinor).FirstAsync(Ct)));
    }

    [Fact]
    public async Task Missed_dose_flagged_once()
    {
        var w = await WorldAsync("Missed Job Clinic");
        var doses = await PrescribeAsync(w, "Secretmycin");
        var now = DateTimeOffset.UtcNow;
        await SetDueAsync(doses[0].Id, now.AddMinutes(-61));
        await SetDueAsync(doses[1].Id, now.AddMinutes(-59));

        await RunAsync(w.Clinic.TenantId, now);
        await RunAsync(w.Clinic.TenantId, now.AddSeconds(30));

        Assert.Equal(DoseStatus.Missed, await DoseStatusAsync(doses[0].Id));
        Assert.Equal(DoseStatus.Due, await DoseStatusAsync(doses[1].Id));
        var notice = Assert.Single(await NoticesAsync(w.Clinic.TenantId, "care.missed_dose"));
        Assert.Equal(w.NurseId, notice.UserId);
        var patientNumber = await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == w.PatientId).Select(p => p.Number).FirstAsync(Ct));
        Assert.Contains(patientNumber, notice.Body);
        Assert.Contains("Missed Job Clinic A", notice.Body);
        foreach (var text in new[] { notice.Title, notice.Body, notice.Link })
            foreach (var word in new[] { "Secretmycin", "tablet", "Oral", "malaria", "Jobby" })
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);

        // A Missed dose may still be recorded late.
        var late = await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[0].Id}", new { status = "Given" }, Ct));
        Assert.Equal("Given", late.GetProperty("status").GetString());
    }

    [Fact]
    public async Task No_shows_marked()
    {
        var w = await WorldAsync("No Show Clinic", bed: false);
        var yesterday = Today.AddDays(-1);
        Appointment Appt(DateTimeOffset start, AppointmentStatus status) => new()
        {
            TenantId = w.Clinic.TenantId, PatientId = w.PatientId, DoctorUserId = w.DoctorId, Start = start, End = start.AddMinutes(15), Status = status,
        };
        var missed = Appt(Lagos(yesterday, 16), AppointmentStatus.Booked);
        var lateLastNight = Appt(Lagos(yesterday, 23, 40), AppointmentStatus.Booked); // ends 23:55 yesterday
        var earlierToday = Appt(Lagos(Today, 0, 5), AppointmentStatus.Booked); // today's: not before today
        var seen = Appt(Lagos(yesterday, 10), AppointmentStatus.CheckedIn);
        var cancelled = Appt(Lagos(yesterday, 11), AppointmentStatus.Cancelled);
        await api.WithDbAsync(db =>
        {
            db.Appointments.AddRange(missed, lateLastNight, earlierToday, seen, cancelled);
            return db.SaveChangesAsync(Ct);
        });

        await RunAsync(w.Clinic.TenantId, Lagos(Today, 12));

        var after = await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.PatientId == w.PatientId).ToDictionaryAsync(a => a.Id, a => a.Status, Ct));
        Assert.Equal(AppointmentStatus.NoShow, after[missed.Id]);
        Assert.Equal(AppointmentStatus.NoShow, after[lateLastNight.Id]);
        Assert.Equal(AppointmentStatus.Booked, after[earlierToday.Id]);
        Assert.Equal(AppointmentStatus.CheckedIn, after[seen.Id]);
        Assert.Equal(AppointmentStatus.Cancelled, after[cancelled.Id]);
    }

    [Fact]
    public async Task Low_deposit_notified_once()
    {
        // ₦12,000 deposit, ₦10,000 a day, threshold ₦5,000: after one bed day ₦2,000 is left.
        var w = await WorldAsync("Low Deposit Clinic", deposit: 1_200_000);
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(p => p.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LowDepositThresholdMinor, 500_000L), Ct));
        await BackdateStayAsync(w.AdmissionId, Lagos(Today.AddDays(-1), 10));

        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow.AddMinutes(1));

        var notices = await NoticesAsync(w.Clinic.TenantId, "care.low_deposit");
        Assert.Equal(new[] { w.CashierId, w.DeskId }.Order(), notices.Select(n => n.UserId).Order());
        Assert.All(notices, n => Assert.DoesNotContain("malaria", n.Body, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull((await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == w.AdmissionId, Ct))).LowDepositNotifiedAt);
    }

    // ---------- Carries ----------

    /// <summary>Carry (Task 10): discharge bills the outstanding bed days (admission day in, discharge day out) before the bill is final.</summary>
    [Fact]
    public async Task Discharge_charges_outstanding_bed_days_before_the_bill_is_final()
    {
        var w = await WorldAsync("Discharge Charge Clinic", deposit: 500_000);
        await BackdateStayAsync(w.AdmissionId, Lagos(Today.AddDays(-2), 21));
        var discharged = await Json(await w.Doctor.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/discharge", new { type = "AgainstAdvice", amaReason = "Left" }, Ct));

        var lines = await BedLinesAsync(w.AdmissionId);
        Assert.Equal([Today.AddDays(-2), Today.AddDays(-1)], lines.Select(l => l.ChargeDay!.Value).ToArray());
        // ₦20,000 of bed days less the ₦5,000 deposit is owed, and the answer shows it.
        Assert.Equal(1_500_000, discharged.GetProperty("balanceMinor").GetInt64());
        var bill = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().FirstAsync(b => b.AdmissionId == w.AdmissionId, Ct));
        Assert.NotNull(bill.FinalisedAt);
        Assert.Equal(2_000_000, bill.TotalMinor);
        Assert.Equal(BillStatus.PartPaid, bill.Status);
        // The job never adds to it afterwards.
        await RunAsync(w.Clinic.TenantId, Lagos(Today.AddDays(1), 9));
        Assert.Equal(2, (await BedLinesAsync(w.AdmissionId)).Count);
    }

    /// <summary>Ruling (Task 8 carry): the stay is charged from the Lagos day of the first bed assignment, not the admission time.</summary>
    [Fact]
    public async Task Bed_days_start_when_the_first_bed_is_assigned()
    {
        var w = await WorldAsync("Late Bed Clinic", bed: false);
        await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.Id == w.AdmissionId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AdmittedAt, Lagos(Today.AddDays(-3), 22)), Ct));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        Assert.Empty(await BedLinesAsync(w.AdmissionId)); // no bed yet: nothing to charge

        await Json(await kit.AssignBedAsync(w.Desk, w.AdmissionId, w.BedA));
        await api.WithDbAsync(db => db.BedMoves.IgnoreQueryFilters().Where(m => m.AdmissionId == w.AdmissionId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.At, Lagos(Today.AddDays(-2), 1)), Ct));
        await RunAsync(w.Clinic.TenantId, Lagos(Today.AddDays(1), 0, 30));
        var lines = await BedLinesAsync(w.AdmissionId);
        Assert.Equal([Today.AddDays(-2), Today.AddDays(-1), Today], lines.Select(l => l.ChargeDay!.Value).ToArray());
    }

    /// <summary>Carry (Task 9): only Due doses on admitted admissions are flagged; a discharged admission's dose is left alone.</summary>
    [Fact]
    public async Task Missed_dose_job_flags_admitted_admissions_only()
    {
        var w = await WorldAsync("Discharged Dose Clinic");
        var doses = await PrescribeAsync(w, "Quietcillin");
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/discharge", new { type = "AgainstAdvice", amaReason = "Left" }, Ct));
        // A dose left Due on the discharged admission (as by a racing write), long overdue; and one already given.
        await SetDueAsync(doses[^1].Id, DateTimeOffset.UtcNow.AddHours(-3), DoseStatus.Due);
        await SetDueAsync(doses[^2].Id, DateTimeOffset.UtcNow.AddHours(-3), DoseStatus.Given);

        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);

        Assert.Equal(DoseStatus.Due, await DoseStatusAsync(doses[^1].Id));
        Assert.Equal(DoseStatus.Given, await DoseStatusAsync(doses[^2].Id));
        Assert.Empty(await NoticesAsync(w.Clinic.TenantId, "care.missed_dose"));
    }

    private sealed class FixedRecipients(params Guid[] users) : ICarePortalRecipients
    {
        public Task<IReadOnlyList<Guid>> ForPatientAsync(Guid patientId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(users);
    }

    private Task<Guid> PortalUserAsync(string name) => api.WithDbAsync(async db =>
    {
        var email = $"portal-{Guid.NewGuid():N}@example.com";
        var account = new AppUser { FullName = name, Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
        db.Users.Add(account);
        await db.SaveChangesAsync(Ct);
        return account.Id;
    });

    private async Task<int> RemindAsync(Guid tenantId, Guid portalUser, DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        ICarePortalRecipients recipients = new FixedRecipients(portalUser);
        var notices = ActivatorUtilities.CreateInstance<CareNotices>(sp, recipients);
        return await ActivatorUtilities.CreateInstance<CareJobSteps>(sp, notices).SendRemindersAsync(now, Ct);
    }

    /// <summary>Reminders go out once, from 08:00 Lagos the day before, to the patient's portal users, never echoing the booking reason.</summary>
    [Fact]
    public async Task Reminder_sent_once_the_day_before_from_0800()
    {
        var w = await WorldAsync("Reminder Clinic", bed: false);
        var portalUser = await PortalUserAsync("Reminder Parent");
        var tomorrow = Today.AddDays(1);
        Appointment Appt(DateTimeOffset start, AppointmentStatus status = AppointmentStatus.Booked) => new()
        {
            TenantId = w.Clinic.TenantId, PatientId = w.PatientId, DoctorUserId = w.DoctorId, Start = start, End = start.AddMinutes(15), Status = status,
            Reason = "Haemorrhoid review",
        };
        var due = Appt(Lagos(tomorrow, 10, 30));
        var later = Appt(Lagos(tomorrow.AddDays(1), 10));
        var cancelled = Appt(Lagos(tomorrow, 11), AppointmentStatus.Cancelled);
        await api.WithDbAsync(db =>
        {
            db.Appointments.AddRange(due, later, cancelled);
            return db.SaveChangesAsync(Ct);
        });

        Assert.Equal(0, await RemindAsync(w.Clinic.TenantId, portalUser, Lagos(Today, 7, 59)));
        Assert.Equal(1, await RemindAsync(w.Clinic.TenantId, portalUser, Lagos(Today, 8)));
        Assert.Equal(0, await RemindAsync(w.Clinic.TenantId, portalUser, Lagos(Today, 8, 1)));
        Assert.Equal(0, await RemindAsync(w.Clinic.TenantId, portalUser, Lagos(Today, 15)));

        var notice = Assert.Single(await NoticesAsync(w.Clinic.TenantId, "care.appointment_reminder"));
        Assert.Equal(portalUser, notice.UserId);
        Assert.Contains("10:30", notice.Body);
        foreach (var text in new[] { notice.Title, notice.Body, notice.Link })
            Assert.DoesNotContain("Haemorrhoid", text, StringComparison.OrdinalIgnoreCase);
        var marked = await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.PatientId == w.PatientId)
            .ToDictionaryAsync(a => a.Id, a => a.ReminderSentAt, Ct));
        Assert.NotNull(marked[due.Id]);
        Assert.Null(marked[later.Id]);
        Assert.Null(marked[cancelled.Id]);

        // The full job marks the appointment too and never sends it again.
        await RunAsync(w.Clinic.TenantId, Lagos(Today, 9));
        Assert.Single(await NoticesAsync(w.Clinic.TenantId, "care.appointment_reminder"));
    }

    /// <summary>The low-deposit marker resets when a top-up lifts the deposit back to the threshold, so the next crossing notifies again.</summary>
    [Fact]
    public async Task Low_deposit_resets_after_top_up()
    {
        var w = await WorldAsync("Top Up Clinic", deposit: 1_200_000);
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(p => p.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LowDepositThresholdMinor, 500_000L), Ct));
        await BackdateStayAsync(w.AdmissionId, Lagos(Today.AddDays(-1), 10));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow);
        Assert.Equal(2, (await NoticesAsync(w.Clinic.TenantId, "care.low_deposit")).Count);

        // A ₦10,000 top-up: ₦12,000 left, above the threshold. The marker clears and nobody is told.
        var billId = await BillIdAsync(w.AdmissionId);
        await Json(await w.Cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 1_000_000, method = "Cash" }, Ct));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Null((await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == w.AdmissionId, Ct))).LowDepositNotifiedAt);
        Assert.Equal(2, (await NoticesAsync(w.Clinic.TenantId, "care.low_deposit")).Count);

        // A ₦10,000 procedure: ₦2,000 left again, a new crossing, a new notice each.
        var procedure = await kit.PriceItemAsync(w.Clinic, "Top up procedure", "Procedure", 1_000_000);
        await Json(await w.Clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/lines", new { priceItemId = procedure, quantity = 1, reason = "Dressing" }, Ct));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow.AddMinutes(2));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow.AddMinutes(3));
        Assert.Equal(4, (await NoticesAsync(w.Clinic.TenantId, "care.low_deposit")).Count);
    }

    /// <summary>Carry (Task 12): revenue counts a bed line on its charge day, not the day the job wrote it.</summary>
    [Fact]
    public async Task Revenue_groups_bed_lines_by_charge_day()
    {
        var w = await WorldAsync("Bed Revenue Clinic");
        await BackdateStayAsync(w.AdmissionId, Lagos(Today.AddDays(-1), 10));
        await RunAsync(w.Clinic.TenantId, DateTimeOffset.UtcNow); // writes yesterday's line today

        long Bed(JsonElement report) => report.GetProperty("byCategory").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == "Bed").GetProperty("amountMinor").GetInt64();
        var yesterday = Today.AddDays(-1);
        var onYesterday = await Json(await w.Cashier.GetAsync($"/api/care/reports/revenue?from={yesterday:yyyy-MM-dd}&to={yesterday:yyyy-MM-dd}", Ct));
        var onToday = await Json(await w.Cashier.GetAsync($"/api/care/reports/revenue?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct));
        Assert.Equal(1_000_000, Bed(onYesterday));
        Assert.Equal(0, Bed(onToday));
    }

    /// <summary>Carry (Task 11): recomputing a bill whose tracked entity has unsaved changes throws instead of silently dropping them.</summary>
    [Fact]
    public async Task Fresh_bill_refuses_unsaved_changes()
    {
        var w = await WorldAsync("Fresh Bill Clinic", bed: false);
        var billId = await BillIdAsync(w.AdmissionId);
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(w.Clinic.TenantId);
        var db = sp.GetRequiredService<IAppDbContext>();
        var billing = sp.GetRequiredService<BillingService>();
        var bill = await db.PatientBills.FirstAsync(b => b.Id == billId, Ct);
        bill.NeedsReview = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.RecalculateAsync(billId, Ct));
        Assert.True(bill.NeedsReview);

        // Saved first, it recomputes as before.
        await db.SaveChangesAsync(Ct);
        await billing.RecalculateAsync(billId, Ct);
    }

    /// <summary>Carry (Task 11): the job retires the previous Paystack key once its window is over.</summary>
    [Fact]
    public async Task Job_retires_the_previous_paystack_key()
    {
        var w = await WorldAsync("Old Key Clinic", bed: false);
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(p => p.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaystackPreviousKeyProtected, "old-key").SetProperty(p => p.PreviousKeyUntil, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        await api.Services.GetRequiredService<CareJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);
        var profile = await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().FirstAsync(p => p.TenantId == w.Clinic.TenantId, Ct));
        Assert.Null(profile.PaystackPreviousKeyProtected);
        Assert.Null(profile.PreviousKeyUntil);
    }

    /// <summary>The full run covers active clinics only: a suspended clinic's appointment is left alone.</summary>
    [Fact]
    public async Task Run_skips_suspended_clinics()
    {
        var active = await WorldAsync("Active Jobs Clinic", bed: false);
        var suspended = await WorldAsync("Suspended Jobs Clinic", bed: false);
        var start = Lagos(Today.AddDays(-1), 9);
        Appointment Appt(World w) => new()
        {
            TenantId = w.Clinic.TenantId, PatientId = w.PatientId, DoctorUserId = w.DoctorId, Start = start, End = start.AddMinutes(15),
        };
        var a = Appt(active);
        var s = Appt(suspended);
        await api.WithDbAsync(async db =>
        {
            db.Appointments.AddRange(a, s);
            await db.SaveChangesAsync(Ct);
            return await db.Tenants.Where(t => t.Id == suspended.Clinic.TenantId).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, TenantStatus.Suspended), Ct);
        });

        await api.Services.GetRequiredService<CareJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);

        var after = await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(x => x.Id == a.Id || x.Id == s.Id).ToDictionaryAsync(x => x.Id, x => x.Status, Ct));
        Assert.Equal(AppointmentStatus.NoShow, after[a.Id]);
        Assert.Equal(AppointmentStatus.Booked, after[s.Id]);
    }

    // ---------- Fix round 1 ----------

    /// <summary>Sets the time of the admission's bed move to <paramref name="toBedId"/>.</summary>
    private Task MoveAtAsync(Guid admissionId, Guid toBedId, DateTimeOffset at) => api.WithDbAsync(db =>
        db.BedMoves.IgnoreQueryFilters().Where(m => m.AdmissionId == admissionId && m.ToBedId == toBedId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.At, at), Ct));

    /// <summary>
    /// I1: the day boundary is 00:00 Lagos (23:00 UTC the day before), not UTC midnight. A first bed at 00:30 Lagos on day D (23:30 UTC on
    /// D−1) is charged from D. A transfer to ward B at 00:30 Lagos on D+1 (23:30 UTC on D) came after 00:00 Lagos, so D+1 is still ward A.
    /// A move back to ward A at exactly 00:00 Lagos on D+2 counts for D+2 (in the new bed at the start of the day).
    /// </summary>
    [Fact]
    public async Task Bed_charges_follow_lagos_midnight_not_utc()
    {
        var w = await WorldAsync("Lagos Midnight Clinic");
        var bedA2 = await kit.BedAsync(w.Clinic, w.WardA, "A2");
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/transfer", new { bedId = w.BedB }, Ct));
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/transfer", new { bedId = bedA2 }, Ct));
        var d = Today.AddDays(-3);
        await BackdateStayAsync(w.AdmissionId, Lagos(d, 0, 30));
        await MoveAtAsync(w.AdmissionId, w.BedB, Lagos(d.AddDays(1), 0, 30));
        await MoveAtAsync(w.AdmissionId, bedA2, Lagos(d.AddDays(2), 0, 0));
        Assert.Equal(DateOnly.FromDateTime(Lagos(d, 0, 30).UtcDateTime), d.AddDays(-1)); // the first bed is the day before in UTC

        await RunAsync(w.Clinic.TenantId, Lagos(Today, 9));

        var lines = await BedLinesAsync(w.AdmissionId);
        var itemA = await BedItemAsync(w.WardA);
        Assert.Equal([d, d.AddDays(1), d.AddDays(2)], lines.Select(l => l.ChargeDay!.Value).ToArray());
        Assert.Equal([itemA, itemA, itemA], lines.Select(l => l.PriceItemId!.Value).ToArray());
    }

    /// <summary>M1 (ruling): a transfer on the first day doesn't change that day: it is charged at the first-assigned ward.</summary>
    [Fact]
    public async Task Transfer_on_the_admission_day_charges_the_first_ward_that_day()
    {
        var w = await WorldAsync("First Day Transfer Clinic");
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/transfer", new { bedId = w.BedB }, Ct));
        var day1 = Today.AddDays(-2);
        await BackdateStayAsync(w.AdmissionId, Lagos(day1, 10));
        await MoveAtAsync(w.AdmissionId, w.BedB, Lagos(day1, 15));

        await RunAsync(w.Clinic.TenantId, Lagos(Today, 9));

        var lines = await BedLinesAsync(w.AdmissionId);
        Assert.Equal([day1, day1.AddDays(1)], lines.Select(l => l.ChargeDay!.Value).ToArray());
        Assert.Equal([await BedItemAsync(w.WardA), await BedItemAsync(w.WardB)], lines.Select(l => l.PriceItemId!.Value).ToArray());
    }

    /// <summary>
    /// M5 (ruling): the stranded-checkout recheck runs after every clinic's care steps, and a Pending checkout under 24 hours old is checked
    /// with Paystack at most every 10 minutes.
    /// </summary>
    [Fact]
    public async Task Pending_checkout_is_rechecked_at_most_every_ten_minutes()
    {
        var w = await WorldAsync("Recheck Throttle Clinic", bed: false);
        var billId = await BillIdAsync(w.AdmissionId);
        var procedure = await kit.PriceItemAsync(w.Clinic, "Throttle procedure", "Procedure", 500_000);
        await Json(await w.Clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/lines", new { priceItemId = procedure, quantity = 1, reason = "Dressing" }, Ct));
        await Json(await w.Clinic.Owner.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = "sk_test_integration_care_key" }, Ct));
        var owner = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == w.Clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        var reference = (await api.AsTenantAsync<OnlinePaymentService, CareCheckoutResult>(w.Clinic.TenantId, s => s.StartCheckoutAsync(billId, owner, null, Ct))).Reference;
        var now = DateTimeOffset.UtcNow;
        await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, now.AddHours(-2)), Ct));
        var jobs = api.Services.GetRequiredService<CareJobs>();

        await jobs.RunOnceAsync(now, Ct);
        await jobs.RunOnceAsync(now.AddMinutes(1), Ct);
        Assert.Equal(1, api.Merchant.LookupsFor.GetValueOrDefault(reference));
        await jobs.RunOnceAsync(now.AddMinutes(11), Ct);
        Assert.Equal(2, api.Merchant.LookupsFor.GetValueOrDefault(reference));
        Assert.Equal(CarePaymentStatus.Pending, await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters()
            .Where(p => p.ProviderRef == reference).Select(p => p.Status).FirstAsync(Ct)));
    }

    /// <summary>M6 (ruling): a dose more than 6 hours overdue (after an outage) is flipped to Missed without a notice.</summary>
    [Fact]
    public async Task Long_overdue_dose_flips_without_a_notice()
    {
        var w = await WorldAsync("Stale Dose Clinic");
        var doses = await PrescribeAsync(w, "Stalemycin");
        var now = DateTimeOffset.UtcNow;
        await SetDueAsync(doses[0].Id, now.AddHours(-6).AddMinutes(-1));
        await SetDueAsync(doses[1].Id, now.AddHours(-5));

        await RunAsync(w.Clinic.TenantId, now);

        Assert.Equal(DoseStatus.Missed, await DoseStatusAsync(doses[0].Id));
        Assert.Equal(DoseStatus.Missed, await DoseStatusAsync(doses[1].Id));
        Assert.Single(await NoticesAsync(w.Clinic.TenantId, "care.missed_dose")); // the 5-hour one only
    }
}
