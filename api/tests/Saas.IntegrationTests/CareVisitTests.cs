using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;
using Products = Saas.Application.Billing.Products;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 5: visits, check-in, the queue and triage. Check-in charges registration once and the consultation every visit and
/// checks the appointment in; the queue shows demographics, stage, urgency and minutes waited only; triage records vital signs
/// (clinical staff only), flags them against the range table and tells the doctor about a critical value without any value.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareVisitTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";
    private const string CriticalNotice = "A patient you are seeing has a critical vital sign.";
    private const string TriageNotWaiting = "Triage is only for patients waiting to be seen.";
    private const string BillNotPaid = "This bill isn't paid yet.";

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> TriageAsync(HttpClient client, Guid visitId, object vitals, Guid doctorUserId, string urgency = "Routine") =>
        client.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new { vitals, urgency, doctorUserId }, Ct);

    private static Task<HttpResponseMessage> StageAsync(HttpClient client, Guid visitId, string stage) =>
        client.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage }, Ct);

    private static readonly object NormalVitals = new { temperatureC = 36.8, systolic = 118, diastolic = 76, pulse = 72, respRate = 14, spO2 = 98 };

    /// <summary>The clinic's registration and consultation items, set on the clinic profile.</summary>
    private async Task<(Guid Registration, Guid Consultation)> PricesAsync(CareKit.Clinic clinic, string name)
    {
        var registration = await kit.PriceItemAsync(clinic, "Registration", "Registration", 200_000);
        var consultation = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        (await clinic.Owner.PutAsJsonAsync("/api/care/settings/profile",
            new { name, registrationPriceItemId = registration, consultationPriceItemId = consultation }, Ct)).EnsureSuccessStatusCode();
        return (registration, consultation);
    }

    private static async Task<List<(Guid? Item, long Amount)>> LinesAsync(HttpClient client, Guid billId) =>
        (await Json(await client.GetAsync($"/api/care/bills/{billId}", Ct))).GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("priceItemId").ValueKind == JsonValueKind.Null ? (Guid?)null : l.GetProperty("priceItemId").GetGuid(),
                l.GetProperty("amountMinor").GetInt64()))
            .ToList();

    [Fact]
    public async Task Check_in_adds_registration_once_and_consultation_charge()
    {
        var clinic = await kit.ClinicAsync("Check In Clinic");
        var (registration, consultation) = await PricesAsync(clinic, "Check In Clinic");
        var (desk, deskId) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = Id(await kit.PatientAsync(clinic, "Ngozi", "Eze"));

        // First visit: registration and consultation on the visit's bill.
        var first = await kit.CheckInAsync(desk, patientId);
        Assert.Equal("WaitingVitals", first.GetProperty("stage").GetString());
        Assert.Equal("Routine", first.GetProperty("urgency").GetString());
        Assert.Equal(patientId, first.GetProperty("patientId").GetGuid());
        var firstBill = first.GetProperty("billId").GetGuid();
        var lines = await LinesAsync(desk, firstBill);
        Assert.Equal(2, lines.Count);
        Assert.Contains((registration, 200_000L), lines);
        Assert.Contains((consultation, 500_000L), lines);
        var bill = await Json(await desk.GetAsync($"/api/care/bills/{firstBill}", Ct));
        Assert.Equal(700_000, bill.GetProperty("totalMinor").GetInt64());
        Assert.Equal(Id(first), bill.GetProperty("visitId").GetGuid());
        var stored = await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().FirstAsync(v => v.Id == Id(first), Ct));
        Assert.Equal(deskId, stored.CheckedInByUserId);
        Assert.Equal(clinic.TenantId, stored.TenantId);

        // The patient is already in today's queue: a second check-in is refused and charges nothing.
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/visits", new { patientId }, Ct), HttpStatusCode.Conflict, "This patient is already checked in.");
        Assert.Equal(1, await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().CountAsync(b => b.PatientId == patientId, Ct)));

        // The next visit (after the first is done) is charged the consultation only.
        await Json(await StageAsync(desk, Id(first), "Done"));
        var second = await kit.CheckInAsync(desk, patientId);
        var secondLines = await LinesAsync(desk, second.GetProperty("billId").GetGuid());
        Assert.Equal([(consultation, 500_000L)], secondLines);
        Assert.NotEqual(firstBill, second.GetProperty("billId").GetGuid());
        await Json(await StageAsync(desk, Id(second), "Done"));

        // With an appointment: the booked appointment is checked in and the visit takes its doctor.
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var day = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(14);
        (await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule",
            new { blocks = new[] { new { weekday = day.DayOfWeek.ToString(), start = "09:00", end = "10:00", slotMinutes = 15 } } }, Ct)).EnsureSuccessStatusCode();
        var slot = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), CareCalendar.LagosOffset);
        var appointment = await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId, doctorUserId = doctorId, start = slot }, Ct));
        var otherPatientId = Id(await kit.PatientAsync(clinic, "Other", "Eze"));

        // Someone else's appointment is a field error, and checks nothing in.
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/visits", new { patientId = otherPatientId, appointmentId = Id(appointment) }, Ct),
            HttpStatusCode.BadRequest, field: "appointmentId");
        var third = await kit.CheckInAsync(desk, patientId, Id(appointment));
        Assert.Equal(doctorId, third.GetProperty("doctorUserId").GetGuid());
        Assert.Equal(Id(appointment), third.GetProperty("appointmentId").GetGuid());
        var status = await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.Id == Id(appointment)).Select(a => a.Status).FirstAsync(Ct));
        Assert.Equal(AppointmentStatus.CheckedIn, status);
        Assert.Equal([(consultation, 500_000L)], await LinesAsync(desk, third.GetProperty("billId").GetGuid()));

        // A checked-in appointment can't be checked in again; closing the visit completes the appointment.
        await Json(await StageAsync(desk, Id(third), "Done"));
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/visits", new { patientId, appointmentId = Id(appointment) }, Ct),
            HttpStatusCode.Conflict, "Only a booked appointment can be checked in.");
        status = await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters().Where(a => a.Id == Id(appointment)).Select(a => a.Status).FirstAsync(Ct));
        Assert.Equal(AppointmentStatus.Done, status);

        // The cashier doesn't check patients in; another clinic's patient is 404; a deceased patient can't be checked in.
        await AssertProblemAsync(await cashier.PostAsJsonAsync("/api/care/visits", new { patientId = otherPatientId }, Ct), HttpStatusCode.Forbidden);
        var foreign = Id(await kit.PatientAsync(await kit.ClinicAsync("Check In Other Clinic"), "Far", "Away"));
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/visits", new { patientId = foreign }, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/visits", new { }, Ct), HttpStatusCode.BadRequest, field: "patientId");
        await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == otherPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PatientStatus.Deceased), Ct));
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/visits", new { patientId = otherPatientId }, Ct),
            HttpStatusCode.Conflict, "This patient is recorded as deceased.");
        Assert.Equal(0, await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().CountAsync(v => v.PatientId == otherPatientId, Ct)));

        // Two simultaneous check-ins of a new patient: one visit, one registration charge.
        var racer = Id(await kit.PatientAsync(clinic, "Racer", "Eze"));
        var results = await Task.WhenAll(
            desk.PostAsJsonAsync("/api/care/visits", new { patientId = racer }, Ct),
            desk.PostAsJsonAsync("/api/care/visits", new { patientId = racer }, Ct));
        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().CountAsync(v => v.PatientId == racer, Ct)));
        Assert.Equal(1, await api.WithDbAsync(db => (from l in db.BillLines.IgnoreQueryFilters()
                                                     join b in db.PatientBills.IgnoreQueryFilters() on l.BillId equals b.Id
                                                     where b.PatientId == racer && l.PriceItemId == registration
                                                     select l).CountAsync(Ct)));
    }

    [Fact]
    public async Task Triage_flags_and_notifies_on_critical_values()
    {
        var clinic = await kit.ClinicAsync("Triage Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Kola", "Ayo"));
        var visit = await kit.CheckInAsync(desk, patientId);
        var visitId = Id(visit);

        var triaged = await Json(await TriageAsync(nurse, visitId,
            new { temperatureC = 38.2, systolic = 120, diastolic = 80, pulse = 88, respRate = 16, spO2 = 85, weightKg = 70, heightCm = 175 }, doctorId, "Urgent"));
        var vitals = triaged.GetProperty("vitals");
        var flags = vitals.GetProperty("flags").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Contains("Critical SpO2", flags);
        Assert.Contains("High temperature", flags);
        Assert.Equal(2, flags.Count);
        Assert.True(vitals.GetProperty("critical").GetBoolean());
        Assert.Equal(22.9m, vitals.GetProperty("bmi").GetDecimal());
        Assert.Equal(nurseId, vitals.GetProperty("takenBy").GetGuid());
        Assert.Equal("WaitingDoctor", triaged.GetProperty("visit").GetProperty("stage").GetString());
        Assert.Equal("Urgent", triaged.GetProperty("visit").GetProperty("urgency").GetString());
        Assert.Equal(doctorId, triaged.GetProperty("visit").GetProperty("doctorUserId").GetGuid());

        // The doctor is told in-app, with no value, no patient and no number anywhere in the notice.
        var notice = Assert.Single(await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == doctorId && n.Kind == "care.critical_vital").ToListAsync(Ct)));
        Assert.Equal(CriticalNotice, notice.Body);
        Assert.Equal(Products.Care, notice.Product);
        Assert.Equal(clinic.TenantId, notice.TenantId);
        foreach (var text in new[] { notice.Title, notice.Body, notice.Link })
        {
            Assert.DoesNotMatch(new Regex(@"\d"), text);
            Assert.DoesNotContain("SpO2", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Kola", text);
        }
        Assert.Equal(0, await api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.UserId == nurseId && n.Kind == "care.critical_vital", Ct)));

        // Triage writes the care relationships: the nurse who took the vitals and the doctor who will see the patient.
        var assignments = await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.VisitId == visitId && a.EndedAt == null).ToListAsync(Ct));
        Assert.Contains(assignments, a => a.StaffUserId == nurseId && a.Kind == CareAssignmentKind.TriageNurse && a.PatientId == patientId);
        Assert.Contains(assignments, a => a.StaffUserId == doctorId && a.Kind == CareAssignmentKind.VisitDoctor && a.PatientId == patientId);
        Assert.Equal(2, assignments.Count);

        // Stored values; the doctor reads them through the record guard (logged, no reason needed).
        var row = Assert.Single(await api.WithDbAsync(db => db.VitalSigns.IgnoreQueryFilters().Where(v => v.VisitId == visitId).ToListAsync(Ct)));
        Assert.Equal(85, row.SpO2);
        Assert.Equal(38.2m, row.TemperatureC);
        var read = (await Json(await doctor.GetAsync($"/api/care/visits/{visitId}/vitals", Ct))).EnumerateArray().ToList();
        Assert.Equal(85, Assert.Single(read).GetProperty("spO2").GetInt32());
        var log = await AccessLogAsync(patientId);
        Assert.Equal(2, log.Count);
        Assert.Equal(doctorId, log[1].StaffUserId);
        Assert.Equal("record", log[1].Area);
        Assert.False(log[1].Emergency);

        // The nurse's first triage of this patient gave her a care relationship, and that is in the access log (area triage).
        var triageRow = log[0];
        Assert.Equal(nurseId, triageRow.StaffUserId);
        Assert.Equal("triage", triageRow.Area);
        Assert.Equal("Nurse", triageRow.Role);
        Assert.False(triageRow.Emergency);
        Assert.Null(triageRow.Reason);

        // A repeat triage by the same nurse (she already has the relationship) adds readings but no log row.
        await Json(await TriageAsync(nurse, visitId, NormalVitals, doctorId));
        Assert.Single(await AccessLogAsync(patientId), a => a.Area == "triage");
        Assert.Equal(2, await api.WithDbAsync(db => db.VitalSigns.IgnoreQueryFilters().CountAsync(v => v.VisitId == visitId, Ct)));

        // Triage is only for patients waiting to be seen: not with the doctor, waiting to pay, or done.
        foreach (var stage in new[] { "WithDoctor", "WaitingPayment", "Done" })
        {
            await Json(await StageAsync(desk, visitId, stage));
            await AssertProblemAsync(await TriageAsync(nurse, visitId, NormalVitals, doctorId), HttpStatusCode.Conflict, TriageNotWaiting);
        }
        Assert.Equal(2, await api.WithDbAsync(db => db.VitalSigns.IgnoreQueryFilters().CountAsync(v => v.VisitId == visitId, Ct)));

        // The doctor who takes a critical reading themself already knows: no notice to them.
        var own = await kit.CheckInAsync(desk, Id(await kit.PatientAsync(clinic, "Own", "Ayo")));
        var ownVitals = (await Json(await TriageAsync(doctor, Id(own), new { spO2 = 82 }, doctorId))).GetProperty("vitals");
        Assert.True(ownVitals.GetProperty("critical").GetBoolean());
        Assert.Equal(1, await api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.UserId == doctorId && n.Kind == "care.critical_vital", Ct)));

        // Normal readings on another patient: no flag, no notice.
        var calm = await kit.CheckInAsync(desk, Id(await kit.PatientAsync(clinic, "Calm", "Ayo")));
        var calmVitals = (await Json(await TriageAsync(nurse, Id(calm), NormalVitals, doctorId))).GetProperty("vitals");
        Assert.Empty(calmVitals.GetProperty("flags").EnumerateArray());
        Assert.False(calmVitals.GetProperty("critical").GetBoolean());
        Assert.Equal(1, await api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.UserId == doctorId && n.Kind == "care.critical_vital", Ct)));

        // Field errors: no readings at all, an impossible reading, a doctor who isn't one of the clinic's doctors.
        var third = await kit.CheckInAsync(desk, Id(await kit.PatientAsync(clinic, "Third", "Ayo")));
        await AssertProblemAsync(await TriageAsync(nurse, Id(third), new { }, doctorId), HttpStatusCode.BadRequest, field: "vitals");
        await AssertProblemAsync(await TriageAsync(nurse, Id(third), new { spO2 = 150 }, doctorId), HttpStatusCode.BadRequest, field: "vitals.spO2");
        await AssertProblemAsync(await TriageAsync(nurse, Id(third), NormalVitals, nurseId), HttpStatusCode.BadRequest, field: "doctorUserId");
        await AssertProblemAsync(await TriageAsync(nurse, Guid.NewGuid(), NormalVitals, doctorId), HttpStatusCode.NotFound);
    }

    [Fact]
    public void Vital_ranges_follow_the_table()
    {
        static List<string> Flags(decimal? t = null, int? sys = null, int? dia = null, int? pulse = null, int? rr = null, int? spo2 = null) =>
            Vitals.Flag(new VitalSigns { TemperatureC = t, Systolic = sys, Diastolic = dia, Pulse = pulse, RespRate = rr, SpO2 = spo2 });

        // Temperature: normal 36.1–37.8 °C, critical below 35 or at or above 40.
        Assert.Empty(Flags(t: 36.1m));
        Assert.Empty(Flags(t: 37.8m));
        Assert.Equal(["Low temperature"], Flags(t: 36.0m));
        Assert.Equal(["High temperature"], Flags(t: 37.9m));
        Assert.Equal(["High temperature"], Flags(t: 39.9m));
        Assert.Equal(["Critical temperature"], Flags(t: 40.0m));
        Assert.Equal(["Low temperature"], Flags(t: 35.0m));
        Assert.Equal(["Critical temperature"], Flags(t: 34.9m));

        // Systolic: normal 90–139, critical below 80 or at or above 180.
        Assert.Empty(Flags(sys: 90));
        Assert.Empty(Flags(sys: 139));
        Assert.Equal(["Low systolic pressure"], Flags(sys: 80));
        Assert.Equal(["Critical systolic pressure"], Flags(sys: 79));
        Assert.Equal(["High systolic pressure"], Flags(sys: 140));
        Assert.Equal(["High systolic pressure"], Flags(sys: 179));
        Assert.Equal(["Critical systolic pressure"], Flags(sys: 180));

        // Diastolic: normal 60–89, critical at or above 120.
        Assert.Empty(Flags(dia: 60));
        Assert.Empty(Flags(dia: 89));
        Assert.Equal(["Low diastolic pressure"], Flags(dia: 40));
        Assert.Equal(["High diastolic pressure"], Flags(dia: 90));
        Assert.Equal(["High diastolic pressure"], Flags(dia: 119));
        Assert.Equal(["Critical diastolic pressure"], Flags(dia: 120));

        // Pulse: normal 60–100, critical below 40 or above 140.
        Assert.Empty(Flags(pulse: 60));
        Assert.Empty(Flags(pulse: 100));
        Assert.Equal(["Low pulse"], Flags(pulse: 40));
        Assert.Equal(["Critical pulse"], Flags(pulse: 39));
        Assert.Equal(["High pulse"], Flags(pulse: 140));
        Assert.Equal(["Critical pulse"], Flags(pulse: 141));

        // Respiratory rate: normal 12–20, critical below 8 or above 30.
        Assert.Empty(Flags(rr: 12));
        Assert.Empty(Flags(rr: 20));
        Assert.Equal(["Low respiratory rate"], Flags(rr: 8));
        Assert.Equal(["Critical respiratory rate"], Flags(rr: 7));
        Assert.Equal(["High respiratory rate"], Flags(rr: 30));
        Assert.Equal(["Critical respiratory rate"], Flags(rr: 31));

        // SpO2: normal 95 or above, critical below 90.
        Assert.Empty(Flags(spo2: 95));
        Assert.Empty(Flags(spo2: 100));
        Assert.Equal(["Low SpO2"], Flags(spo2: 94));
        Assert.Equal(["Low SpO2"], Flags(spo2: 90));
        Assert.Equal(["Critical SpO2"], Flags(spo2: 89));

        // Nothing measured, nothing flagged; several readings flag in table order.
        Assert.Empty(Flags());
        Assert.Equal(["High temperature", "Critical SpO2"], Flags(t: 38.2m, spo2: 85));
        Assert.True(Vitals.IsCritical(["High temperature", "Critical SpO2"]));
        Assert.False(Vitals.IsCritical(["High temperature", "Low SpO2"]));
    }

    [Fact]
    public async Task Queue_shows_stages_and_wait()
    {
        var clinic = await kit.ClinicAsync("Queue Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (doctorA, doctorAId) = await kit.StaffAsync(clinic, "Doctor");
        var (_, doctorBId) = await kit.StaffAsync(clinic, "Doctor");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var p1 = Id(await kit.PatientAsync(clinic, "Waiting", "One"));
        var p2 = Id(await kit.PatientAsync(clinic, "Seen", "Two"));
        var p3 = Id(await kit.PatientAsync(clinic, "Other", "Three"));
        var p4 = Id(await kit.PatientAsync(clinic, "Later", "Four"));
        var v1 = Id(await kit.CheckInAsync(desk, p1));
        var v2 = Id(await kit.CheckInAsync(desk, p2));
        var v3 = Id(await kit.CheckInAsync(desk, p3));
        var v4 = Id(await kit.CheckInAsync(desk, p4));

        // The queue is read for the Lagos day the patients arrived on, and the backdating never crosses that day's midnight, so the
        // test doesn't depend on the time of day it runs (near Lagos midnight the waits are simply shorter).
        var arrived = await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().Where(v => v.Id == v1).Select(v => v.ArrivedAt).FirstAsync(Ct));
        var day = CareCalendar.Today(arrived);
        var sinceMidnight = (int)(arrived - new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset)).TotalMinutes;
        var wait1 = Math.Min(25, sinceMidnight);
        var wait3 = Math.Min(5, sinceMidnight);
        string Queue(string extra = "") => $"/api/care/queue?date={day:yyyy-MM-dd}{extra}";

        // p1 has waited about 25 minutes; p3 (routine) arrived before p2 (emergency) but p2 is seen first.
        await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().Where(v => v.Id == v1)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ArrivedAt, arrived.AddMinutes(-wait1)), Ct));
        await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().Where(v => v.Id == v3)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ArrivedAt, arrived.AddMinutes(-wait3)), Ct));
        await Json(await TriageAsync(nurse, v2, new { temperatureC = 37.0, spO2 = 85 }, doctorAId, "Emergency"));
        await Json(await TriageAsync(nurse, v3, NormalVitals, doctorBId));
        await Json(await TriageAsync(nurse, v4, NormalVitals, doctorAId));
        await Json(await StageAsync(doctorA, v4, "WithDoctor"));

        var response = await desk.GetAsync(Queue(), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var queue = await Json(response);
        Assert.Equal(day, DateOnly.Parse(queue.GetProperty("date").GetString()!));
        Assert.False(queue.GetProperty("mine").GetBoolean());
        var stages = queue.GetProperty("stages").EnumerateArray().ToList();
        Assert.Equal(["WaitingVitals", "WaitingDoctor", "WithDoctor", "WaitingPayment", "Done"], stages.Select(s => s.GetProperty("stage").GetString()));
        List<JsonElement> In(string stage) => stages.Single(s => s.GetProperty("stage").GetString() == stage).GetProperty("visits").EnumerateArray().ToList();

        var waiting = Assert.Single(In("WaitingVitals"));
        Assert.Equal(v1, Id(waiting));
        Assert.InRange(waiting.GetProperty("minutesWaited").GetInt32(), wait1, wait1 + 1);
        Assert.Equal("Waiting One", waiting.GetProperty("patientName").GetString());
        Assert.Equal("Female", waiting.GetProperty("sex").GetString());
        var today = CareCalendar.Today(DateTimeOffset.UtcNow);
        var age = waiting.GetProperty("ageYears").GetInt32();
        Assert.Equal(today.Year - 1990 - (today < new DateOnly(today.Year, 4, 12) ? 1 : 0), age);
        Assert.Equal([v2, v3], In("WaitingDoctor").Select(Id));
        Assert.Equal("Emergency", In("WaitingDoctor")[0].GetProperty("urgency").GetString());
        Assert.Equal("Doctor Staff", In("WaitingDoctor")[0].GetProperty("doctorName").GetString());
        Assert.InRange(In("WaitingDoctor")[1].GetProperty("minutesWaited").GetInt32(), wait3, wait3 + 1);
        Assert.Equal([v4], In("WithDoctor").Select(Id));
        Assert.Empty(In("WaitingPayment"));

        // Demographics, stage, urgency and wait only: nothing from triage reaches the queue.
        foreach (var leak in new[] { "spO2", "temperatureC", "flags", "critical", "vitals", "systolic", "bmi" })
            Assert.DoesNotContain($"\"{leak}\":", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Critical SpO2", body);

        // A doctor sees their own patients by default; mine=false shows everyone.
        var mine = await Json(await doctorA.GetAsync(Queue(), Ct));
        Assert.True(mine.GetProperty("mine").GetBoolean());
        var mineIds = mine.GetProperty("stages").EnumerateArray().SelectMany(s => s.GetProperty("visits").EnumerateArray()).Select(Id).ToList();
        Assert.Equal(new[] { v2, v4 }.Order(), mineIds.Order());
        var everyone = await Json(await doctorA.GetAsync(Queue("&mine=false"), Ct));
        Assert.Equal(4, everyone.GetProperty("stages").EnumerateArray().Sum(s => s.GetProperty("visits").GetArrayLength()));

        // Moving on: payment, then done (the wait stops counting); the cashier sees the queue too.
        await Json(await StageAsync(doctorA, v4, "WaitingPayment"));
        var paying = await Json(await cashier.GetAsync(Queue(), Ct));
        Assert.Equal(v4, Id(Assert.Single(paying.GetProperty("stages")[3].GetProperty("visits").EnumerateArray())));
        await Json(await StageAsync(cashier, v4, "Done"));
        await AssertProblemAsync(await StageAsync(desk, v4, "WithDoctor"), HttpStatusCode.Conflict, "This visit is closed.");
        await AssertProblemAsync(await StageAsync(cashier, v1, "Done"), HttpStatusCode.Forbidden);
        var one = await Json(await desk.GetAsync($"/api/care/visits/{v4}", Ct));
        Assert.Equal("Done", one.GetProperty("stage").GetString());

        // Another day's queue is empty; a visit of another clinic is 404.
        var yesterday = day.AddDays(-1);
        var old = await Json(await desk.GetAsync($"/api/care/queue?date={yesterday:yyyy-MM-dd}", Ct));
        Assert.All(old.GetProperty("stages").EnumerateArray(), s => Assert.Equal(0, s.GetProperty("visits").GetArrayLength()));
        var other = await kit.ClinicAsync("Queue Other Clinic");
        await AssertProblemAsync(await other.Owner.GetAsync($"/api/care/visits/{v1}", Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Front_desk_cannot_record_vitals()
    {
        var clinic = await kit.ClinicAsync("Desk Vitals Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var visitId = Id(await kit.CheckInAsync(desk, Id(await kit.PatientAsync(clinic, "Desk", "Vitals"))));

        foreach (var client in new[] { desk, cashier, admin })
        {
            await AssertProblemAsync(await TriageAsync(client, visitId, NormalVitals, doctorId), HttpStatusCode.Forbidden, ClinicalRefused);
            await AssertProblemAsync(await client.GetAsync($"/api/care/visits/{visitId}/vitals", Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        }
        Assert.Equal(0, await api.WithDbAsync(db => db.VitalSigns.IgnoreQueryFilters().CountAsync(v => v.VisitId == visitId, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().CountAsync(a => a.VisitId == visitId, Ct)));
        var visit = await Json(await desk.GetAsync($"/api/care/visits/{visitId}", Ct));
        Assert.Equal("WaitingVitals", visit.GetProperty("stage").GetString());

        // The medical director records them (clinical access always).
        await Json(await TriageAsync(clinic.Owner, visitId, NormalVitals, doctorId));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaffUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public MemberRole? Role => MemberRole.Staff;
        public string? IpAddress => null;
    }

    /// <summary>Opens the patient's record as <paramref name="userId"/> through <c>RecordGuard</c> with the clock at <paramref name="at"/>.</summary>
    private async Task OpenAtAsync(CareKit.Clinic clinic, Guid userId, Guid patientId, DateTimeOffset at, string? reason = null)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(clinic.TenantId);
        ICurrentUser user = new StaffUser(userId);
        var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
        TimeProvider clock = new FixedClock(at);
        var guard = ActivatorUtilities.CreateInstance<RecordGuard>(sp, access, user, clock);
        await guard.OpenAsync(patientId, RecordGuard.Areas.Record, reason, Ct);
    }

    [Fact]
    public async Task Visit_relationships_end_thirty_days_after_done()
    {
        var clinic = await kit.ClinicAsync("Follow Up Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Follow", "Up"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await TriageAsync(nurse, visitId, NormalVitals, doctorId));

        // While the visit is open the relationships have no end.
        var open = await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.VisitId == visitId).ToListAsync(Ct));
        Assert.Equal(2, open.Count);
        Assert.All(open, a => Assert.Null(a.EndedAt));

        // Done: both end 30 days after the done time.
        await Json(await StageAsync(desk, visitId, "Done"));
        var doneAt = (await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().FirstAsync(v => v.Id == visitId, Ct))).DoneAt!.Value;
        var ended = await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.VisitId == visitId).ToListAsync(Ct));
        Assert.Equal(2, ended.Count);
        Assert.All(ended, a => Assert.Equal(doneAt.AddDays(30), a.EndedAt));

        // Day 29 after Done: the nurse and the doctor still open the record freely (logged, not emergencies).
        foreach (var staff in new[] { nurseId, doctorId })
            await OpenAtAsync(clinic, staff, patientId, doneAt.AddDays(29));
        var log = await AccessLogAsync(patientId);
        Assert.Equal(2, log.Count(a => a.Area == "record" && !a.Emergency));

        // Day 31: the relationship has ended, so an open needs a reason (409 without one), and with one it is an emergency.
        foreach (var staff in new[] { nurseId, doctorId })
        {
            var refused = await Assert.ThrowsAsync<ConflictException>(() => OpenAtAsync(clinic, staff, patientId, doneAt.AddDays(31)));
            Assert.Equal("Give a reason to open this patient's record.", refused.Message);
        }
        await OpenAtAsync(clinic, nurseId, patientId, doneAt.AddDays(31), "Patient phoned about the results");
        Assert.True((await AccessLogAsync(patientId)).Last().Emergency);

        // Over HTTP today (inside the 30 days) the nurse still opens the record without a reason.
        await Json(await nurse.GetAsync($"/api/care/patients/{patientId}", Ct));
    }

    [Fact]
    public async Task Cashier_closes_a_visit_only_when_its_bill_is_settled()
    {
        var clinic = await kit.ClinicAsync("Settle Clinic");
        await PricesAsync(clinic, "Settle Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = Id(await kit.PatientAsync(clinic, "Owes", "Money"));
        var visit = await kit.CheckInAsync(desk, patientId);
        var visitId = Id(visit);
        var billId = visit.GetProperty("billId").GetGuid();
        await Json(await StageAsync(desk, visitId, "WaitingPayment"));

        // 700,000 kobo owed: the cashier can't close it, and can't move it anywhere else either.
        await AssertProblemAsync(await StageAsync(cashier, visitId, "Done"), HttpStatusCode.Conflict, BillNotPaid);
        await AssertProblemAsync(await StageAsync(cashier, visitId, "WaitingDoctor"), HttpStatusCode.Forbidden);
        Assert.Equal("WaitingPayment", (await Json(await desk.GetAsync($"/api/care/visits/{visitId}", Ct))).GetProperty("stage").GetString());

        // Once nothing is owed (here both lines removed with a reason), the cashier closes it.
        var lines = (await Json(await cashier.GetAsync($"/api/care/bills/{billId}", Ct))).GetProperty("lines").EnumerateArray().Select(Id).ToList();
        foreach (var line in lines)
            await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines/{line}/remove", new { reason = "Charged in error" }, Ct));
        Assert.Equal("Done", (await Json(await StageAsync(cashier, visitId, "Done"))).GetProperty("stage").GetString());

        // A waived bill counts as settled.
        var second = await kit.CheckInAsync(desk, patientId);
        await Json(await StageAsync(desk, Id(second), "WaitingPayment"));
        await AssertProblemAsync(await StageAsync(cashier, Id(second), "Done"), HttpStatusCode.Conflict, BillNotPaid);
        await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(b => b.Id == second.GetProperty("billId").GetGuid())
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BillStatus.Waived), Ct));
        Assert.Equal("Done", (await Json(await StageAsync(cashier, Id(second), "Done"))).GetProperty("stage").GetString());
    }
}
