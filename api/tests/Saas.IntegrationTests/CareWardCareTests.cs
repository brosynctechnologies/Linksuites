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
/// CareLink Task 9: ward care. Nursing notes, the fluid chart (per Lagos day, with in and out totals), ward rounds (signed like
/// consultations), the medication chart (admission prescriptions expanded into scheduled doses, each recorded once), the ward rule
/// (writes need a care relationship with the admission, or the director) and the discharged admission being final except for addenda.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareWardCareTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";
    private const string NotOnWard = "This patient isn't on your ward.";
    private const string Discharged = "This patient has been discharged.";
    private const string DoseRecorded = "This dose is already recorded.";
    private const string SignedNote = "This note is signed. Add an addendum instead.";
    private const string AuthorOnly = "Only the doctor who wrote this note can change it.";
    private const string AddendaAfterSigning = "Addenda are for signed notes. Edit the draft instead.";
    private const string DoctorsOnly = "Only doctors can write ward rounds.";
    private const string EmptyRound = "Write the ward-round note before signing.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null, string? code = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        if (detail is null && field is null && code is null) return;
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
        if (code is not null) Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private ClinicalText Text => api.Services.GetRequiredService<ClinicalText>();

    private static DateTimeOffset LagosMidnight(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();

    private static Task<HttpResponseMessage> GetWithReasonAsync(HttpClient client, string path, string reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(RecordGuard.ReasonHeader, Uri.EscapeDataString(reason));
        return client.SendAsync(request, Ct);
    }

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    /// <summary>A clinic with a doctor who admitted the patient, the ward's own nurse (bed assigned), and the front desk.</summary>
    private sealed record WardWorld(
        CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Nurse, Guid NurseId, HttpClient Desk, Guid PatientId, Guid AdmissionId, Guid WardId);

    private async Task<WardWorld> WorldAsync(string name)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var wardId = await kit.WardAsync(clinic, $"{name} ward", nurseUserIds: [nurseId]);
        var bedId = await kit.BedAsync(clinic, wardId, "W1");
        var patientId = Id(await kit.PatientAsync(clinic, "Ward", "Patient"));
        // The doctor already cares for the patient (an outpatient visit), so admitting needs no reason and logs nothing (Task 8 ruling).
        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        return new WardWorld(clinic, doctor, doctorId, nurse, nurseId, desk, patientId, admissionId, wardId);
    }

    private static object Item(string drug, string frequency, int? durationDays = null) =>
        new { drug, dose = "1 tablet", frequency, durationDays, route = "Oral" };

    private async Task<JsonElement> PrescribeAsync(WardWorld w, params object[] items) =>
        await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false, items }, Ct));

    private Task<List<MedicationDose>> DosesAsync(Guid admissionId) =>
        api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == admissionId).OrderBy(d => d.DueAt).ThenBy(d => d.Id).ToListAsync(Ct));

    // ---------- Medication chart ----------

    [Fact]
    public async Task Tds_for_3_days_schedules_9_doses()
    {
        // The schedule: doses spaced evenly across 24 hours from the start rounded up to the hour, for the duration (3 days when none).
        var start = new DateTimeOffset(2026, 10, 29, 9, 20, 30, TimeSpan.Zero);
        var tds = MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.TDS, DurationDays = 3 }, start);
        Assert.Equal(9, tds.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero), tds[0]);
        for (var i = 1; i < tds.Count; i++) Assert.Equal(TimeSpan.FromHours(8), tds[i] - tds[i - 1]);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero), tds[^1]); // 10:00, 18:00, 02:00 for three days
        Assert.Equal(9, MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.TDS }, start).Count); // no duration: 3 days
        Assert.Equal(6, MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.BD }, start).Count);
        var onTheHour = new DateTimeOffset(2026, 10, 29, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal(onTheHour, MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.OD, DurationDays = 1 }, onTheHour).Single());
        var q4h = MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.Q4H, DurationDays = 1 }, start);
        Assert.Equal(6, q4h.Count);
        Assert.Equal(TimeSpan.FromHours(4), q4h[1] - q4h[0]);
        // Stat: one dose at the start; PRN: none.
        Assert.Equal(start, MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.Stat, DurationDays = 5 }, start).Single());
        Assert.Empty(MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.PRN, DurationDays = 5 }, start));
        // Nocte: one a night at 22:00 Lagos (21:00 UTC).
        var nocte = MedicationChart.Schedule(new PrescriptionItem { Frequency = DoseFrequency.Nocte, DurationDays = 2 }, start);
        Assert.Equal([new DateTimeOffset(2026, 10, 29, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 21, 0, 0, TimeSpan.Zero)], nocte);

        // An admission prescription writes its doses in the same transaction: TDS × 3 days = 9 Due doses, Stat 1, PRN none.
        var w = await WorldAsync("Tds Clinic");
        var before = DateTimeOffset.UtcNow;
        var rx = await PrescribeAsync(w, Item("Tdsamol", "TDS", 3), Item("Statamine", "Stat"), Item("Prnofen", "PRN", 5));
        var items = rx.GetProperty("items").EnumerateArray().Select(Id).ToList();
        var doses = await DosesAsync(w.AdmissionId);
        Assert.Equal(10, doses.Count);
        Assert.All(doses, d => Assert.Equal(DoseStatus.Due, d.Status));
        Assert.All(doses, d => Assert.Equal(w.PatientId, d.PatientId));
        Assert.All(doses, d => Assert.Equal(Id(rx), d.PrescriptionId));
        var tdsDoses = doses.Where(d => d.PrescriptionItemId == items[0]).ToList();
        Assert.Equal(9, tdsDoses.Count);
        Assert.Equal(0, tdsDoses[0].DueAt.Minute + tdsDoses[0].DueAt.Second);
        Assert.InRange(tdsDoses[0].DueAt, before.AddMinutes(-1), before.AddHours(1).AddMinutes(1));
        for (var i = 1; i < 9; i++) Assert.Equal(TimeSpan.FromHours(8), tdsDoses[i].DueAt - tdsDoses[i - 1].DueAt);
        Assert.Single(doses, d => d.PrescriptionItemId == items[1]);
        Assert.DoesNotContain(doses, d => d.PrescriptionItemId == items[2]);

        // An outpatient prescription (from a consultation) writes no doses.
        var visitId = Id(await kit.CheckInAsync(w.Desk, w.PatientId));
        var consultationId = Id(await Json(await w.Clinic.Owner.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        await Json(await w.Clinic.Owner.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, consultationId, allergyAcknowledged = false, items = new[] { Item("Outpatientol", "TDS", 5) } }, Ct));
        Assert.Equal(10, await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().CountAsync(d => d.PatientId == w.PatientId, Ct)));

        // The chart for a day lists that Lagos day's doses (drug decrypted) and the as-needed drugs; it is a logged clinical read.
        var day = CareCalendar.Today(tdsDoses[0].DueAt);
        var chart = await Json(await w.Nurse.GetAsync($"/api/care/admissions/{w.AdmissionId}/medication-chart?date={day:yyyy-MM-dd}", Ct));
        var expected = doses.Count(d => d.DueAt >= LagosMidnight(day) && d.DueAt < LagosMidnight(day.AddDays(1)));
        var listed = chart.GetProperty("doses").EnumerateArray().ToList();
        Assert.Equal(expected, listed.Count);
        Assert.Contains(listed, d => d.GetProperty("drug").GetString() == "Tdsamol" && d.GetProperty("status").GetString() == "Due");
        Assert.Equal("Prnofen", chart.GetProperty("asNeeded").EnumerateArray().Single().GetProperty("drug").GetString());
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.StaffUserId == w.NurseId && a.Area == "medication" && !a.Emergency);
        await AssertProblemAsync(await w.Desk.GetAsync($"/api/care/admissions/{w.AdmissionId}/medication-chart", Ct), HttpStatusCode.Forbidden, ClinicalRefused);
    }

    [Fact]
    public async Task Dose_recorded_once()
    {
        var w = await WorldAsync("Dose Clinic");
        await PrescribeAsync(w, Item("Dosamycin", "QDS", 2));
        var doses = await DosesAsync(w.AdmissionId);
        Assert.Equal(8, doses.Count);
        var first = doses[0].Id;
        var (strangerNurse, _) = await kit.StaffAsync(w.Clinic, "Nurse");
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        var logBefore = (await AccessLogAsync(w.PatientId)).Count;

        // Non-clinical staff: 403; a nurse who isn't on this ward: 403; a status that isn't Given, Held or Refused: 400; unknown dose: 404.
        await AssertProblemAsync(await w.Desk.PostAsJsonAsync($"/api/care/doses/{first}", new { status = "Given" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await cashier.PostAsJsonAsync($"/api/care/doses/{first}", new { status = "Given" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await strangerNurse.PostAsJsonAsync($"/api/care/doses/{first}", new { status = "Given" }, Ct), HttpStatusCode.Forbidden, NotOnWard);
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{first}", new { status = "Due" }, Ct), HttpStatusCode.BadRequest, field: "status");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{first}", new { status = "Missed" }, Ct), HttpStatusCode.BadRequest, field: "status");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{Guid.NewGuid()}", new { status = "Given" }, Ct), HttpStatusCode.NotFound);
        Assert.Equal(DoseStatus.Due, (await DosesAsync(w.AdmissionId))[0].Status);

        // The ward's nurse records it: Given, by them, now, with the note stored encrypted. Not a record open.
        var recorded = await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{first}", new { status = "Given", note = "Taken with water, dosewalk" }, Ct));
        Assert.Equal("Given", recorded.GetProperty("status").GetString());
        Assert.Equal("Taken with water, dosewalk", recorded.GetProperty("note").GetString());
        var row = (await DosesAsync(w.AdmissionId)).Single(d => d.Id == first);
        Assert.Equal(DoseStatus.Given, row.Status);
        Assert.Equal(w.NurseId, row.RecordedByUserId);
        Assert.NotNull(row.RecordedAt);
        Assert.DoesNotContain("dosewalk", row.Note);
        Assert.Equal("Taken with water, dosewalk", Text.Read(row.Note));
        Assert.Equal(logBefore, (await AccessLogAsync(w.PatientId)).Count);

        // Once recorded, never again: the same nurse, the doctor and the director all get 409, and the row is unchanged.
        foreach (var (client, status) in new[] { (w.Nurse, "Held"), (w.Doctor, "Refused"), (w.Clinic.Owner, "Given") })
            await AssertProblemAsync(await client.PostAsJsonAsync($"/api/care/doses/{first}", new { status }, Ct), HttpStatusCode.Conflict, DoseRecorded, code: "dose_recorded");
        var unchanged = (await DosesAsync(w.AdmissionId)).Single(d => d.Id == first);
        Assert.Equal(DoseStatus.Given, unchanged.Status);
        Assert.Equal(w.NurseId, unchanged.RecordedByUserId);
        Assert.Equal(row.RecordedAt, unchanged.RecordedAt);

        // The admitting doctor and the director may record doses too; a dose the missed-dose job flagged can still be recorded (late).
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/doses/{doses[1].Id}", new { status = "Held", note = "Nil by mouth" }, Ct));
        await Json(await w.Clinic.Owner.PostAsJsonAsync($"/api/care/doses/{doses[2].Id}", new { status = "Refused" }, Ct));
        await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == doses[3].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DoseStatus.Missed), Ct));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[3].Id}", new { status = "Given", note = "Given late" }, Ct));

        // Two records racing for one dose: exactly one wins.
        var race = await Task.WhenAll(
            w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[4].Id}", new { status = "Given" }, Ct),
            w.Doctor.PostAsJsonAsync($"/api/care/doses/{doses[4].Id}", new { status = "Held" }, Ct));
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.Conflict);

        // The chart shows who recorded what (note decrypted).
        var chart = await Json(await w.Nurse.GetAsync($"/api/care/admissions/{w.AdmissionId}/medication-chart?date={CareCalendar.Today(doses[0].DueAt):yyyy-MM-dd}", Ct));
        var listed = chart.GetProperty("doses").EnumerateArray().Single(d => Id(d) == first);
        Assert.Equal("Given", listed.GetProperty("status").GetString());
        Assert.Equal(w.NurseId, listed.GetProperty("recordedByUserId").GetGuid());
        Assert.Equal("Nurse Staff", listed.GetProperty("recordedByName").GetString());
        Assert.Equal("Taken with water, dosewalk", listed.GetProperty("note").GetString());
        Assert.Equal("Dosamycin", listed.GetProperty("drug").GetString());
    }

    // ---------- Fluid chart ----------

    [Fact]
    public async Task Fluid_totals_per_day()
    {
        var w = await WorldAsync("Fluid Clinic");
        var (strangerDoctor, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        var path = $"/api/care/admissions/{w.AdmissionId}/fluids";

        // Field errors.
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { direction = "In", kind = "Oral", ml = 0 }, Ct), HttpStatusCode.BadRequest, field: "ml");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { direction = "In", kind = "Oral", ml = 10_001 }, Ct), HttpStatusCode.BadRequest, field: "ml");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { kind = "Oral", ml = 100 }, Ct), HttpStatusCode.BadRequest, field: "direction");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { direction = "Out", ml = 100 }, Ct), HttpStatusCode.BadRequest, field: "kind");
        // Who may chart: not the front desk, not a doctor who isn't caring for this admission.
        await AssertProblemAsync(await w.Desk.PostAsJsonAsync(path, new { direction = "In", kind = "Oral", ml = 100 }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await strangerDoctor.PostAsJsonAsync(path, new { direction = "In", kind = "Oral", ml = 100 }, Ct), HttpStatusCode.Forbidden, NotOnWard);

        // Today: 500 + 1000 in, 700 out (the nurse, the doctor and the director all chart).
        var entry = await Json(await w.Nurse.PostAsJsonAsync(path, new { direction = "In", kind = "Oral", ml = 500 }, Ct));
        Assert.Equal(500, entry.GetProperty("ml").GetInt32());
        Assert.Equal(w.NurseId, entry.GetProperty("recordedByUserId").GetGuid());
        await Json(await w.Doctor.PostAsJsonAsync(path, new { direction = "In", kind = "IV", ml = 1000 }, Ct));
        await Json(await w.Clinic.Owner.PostAsJsonAsync(path, new { direction = "Out", kind = "Urine", ml = 700 }, Ct));
        Assert.Empty(await AccessLogAsync(w.PatientId)); // writes are not record opens
        // Yesterday (23:00 Lagos): 300 in, on another day's chart.
        var today = CareCalendar.Today(DateTimeOffset.UtcNow);
        await api.WithDbAsync(db =>
        {
            db.FluidEntries.Add(new FluidEntry
            {
                TenantId = w.Clinic.TenantId, AdmissionId = w.AdmissionId, PatientId = w.PatientId, Direction = FluidDirection.In, Kind = FluidKind.Oral,
                Ml = 300, RecordedByUserId = w.NurseId, At = LagosMidnight(today).AddHours(-1),
            });
            return db.SaveChangesAsync(Ct);
        });

        var chart = await Json(await w.Nurse.GetAsync(path, Ct));
        Assert.Equal(today.ToString("yyyy-MM-dd"), chart.GetProperty("date").GetString());
        Assert.Equal(3, chart.GetProperty("entries").GetArrayLength());
        Assert.Equal(1500, chart.GetProperty("inMl").GetInt32());
        Assert.Equal(700, chart.GetProperty("outMl").GetInt32());
        Assert.Equal(800, chart.GetProperty("balanceMl").GetInt32());
        var kinds = chart.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["Oral", "IV", "Urine"], kinds); // oldest first

        var yesterday = await Json(await w.Nurse.GetAsync($"{path}?date={today.AddDays(-1):yyyy-MM-dd}", Ct));
        Assert.Equal(1, yesterday.GetProperty("entries").GetArrayLength());
        Assert.Equal(300, yesterday.GetProperty("inMl").GetInt32());
        Assert.Equal(0, yesterday.GetProperty("outMl").GetInt32());

        // Reading is a logged clinical open: the front desk gets 403; a doctor with no relationship needs a reason (emergency access).
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.StaffUserId == w.NurseId && a.Area == "fluids");
        await AssertProblemAsync(await w.Desk.GetAsync(path, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await strangerDoctor.GetAsync(path, Ct), HttpStatusCode.Conflict, code: "emergency_reason_required");
        await Json(await GetWithReasonAsync(strangerDoctor, path, "Covering the ward tonight"));
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.Area == "fluids" && a.Emergency);
        await AssertProblemAsync(await w.Nurse.GetAsync($"/api/care/admissions/{Guid.NewGuid()}/fluids", Ct), HttpStatusCode.NotFound);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaffUser(Guid userId, MemberRole role) : ICurrentUser
    {
        public Guid? UserId => userId;
        public MemberRole? Role => role;
        public string? IpAddress => null;
    }

    /// <summary>The ward-care service as <paramref name="userId"/> with the clock fixed at <paramref name="at"/> (RecordGuard on the same clock).</summary>
    private async Task<T> AsStaffAtAsync<T>(Guid tenantId, Guid userId, DateTimeOffset at, Func<WardCareService, Task<T>> action)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        ICurrentUser user = new StaffUser(userId, MemberRole.Staff);
        var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
        TimeProvider clock = new FixedClock(at);
        var guard = ActivatorUtilities.CreateInstance<RecordGuard>(sp, access, user, clock);
        return await action(ActivatorUtilities.CreateInstance<WardCareService>(sp, access, guard, user, clock));
    }

    /// <summary>
    /// I1: "today" on the fluid chart and the medication chart is the Lagos day, not the UTC day. With the clock fixed at noon Lagos on
    /// day D, entries exactly at Lagos midnight (23:00 UTC the day before) and 30 minutes after it (23:30 UTC the day before) are on D's
    /// chart; one exactly at the next Lagos midnight is on D+1's, not D's.
    /// </summary>
    [Fact]
    public async Task Lagos_day_bounds_fluid_totals_and_the_medication_chart()
    {
        var w = await WorldAsync("Lagos Day Ward Clinic");
        var rx = await PrescribeAsync(w, Item("Boundarycillin", "OD", 1));
        var itemId = Id(rx.GetProperty("items").EnumerateArray().Single());
        var day = new DateOnly(2026, 10, 20);
        var midnight = LagosMidnight(day);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 23, 0, 0, TimeSpan.Zero), midnight);
        var noonLagos = midnight.AddHours(12);
        DateTimeOffset[] inside = [midnight, midnight.AddMinutes(30)];
        var nextMidnight = LagosMidnight(day.AddDays(1));

        await api.WithDbAsync(db =>
        {
            foreach (var (at, ml) in new[] { (inside[0], 100), (inside[1], 200), (nextMidnight, 400) })
                db.FluidEntries.Add(new FluidEntry
                {
                    TenantId = w.Clinic.TenantId, AdmissionId = w.AdmissionId, PatientId = w.PatientId, Direction = FluidDirection.In, Kind = FluidKind.Oral,
                    Ml = ml, RecordedByUserId = w.NurseId, At = at,
                });
            foreach (var at in inside.Append(nextMidnight))
                db.MedicationDoses.Add(new MedicationDose
                {
                    TenantId = w.Clinic.TenantId, AdmissionId = w.AdmissionId, PatientId = w.PatientId, PrescriptionId = Id(rx), PrescriptionItemId = itemId, DueAt = at,
                });
            return db.SaveChangesAsync(Ct);
        });

        // Default date: today by the fixed clock, in Lagos.
        var fluids = await AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, noonLagos, s => s.FluidsAsync(w.AdmissionId, null, null, Ct));
        Assert.Equal(day, fluids.Date);
        Assert.Equal(inside, fluids.Entries.Select(e => e.At));
        Assert.Equal(300, fluids.InMl);
        var chart = await AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, noonLagos, s => s.MedicationChartAsync(w.AdmissionId, null, null, Ct));
        Assert.Equal(day, chart.Date);
        Assert.Equal(inside, chart.Doses.Select(d => d.DueAt));

        // The next Lagos day holds the entry at its midnight, and nothing from day D.
        var next = await AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, noonLagos, s => s.FluidsAsync(w.AdmissionId, day.AddDays(1), null, Ct));
        Assert.Equal([nextMidnight], next.Entries.Select(e => e.At));
        Assert.Equal(400, next.InMl);
        var nextChart = await AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, noonLagos, s => s.MedicationChartAsync(w.AdmissionId, day.AddDays(1), null, Ct));
        Assert.Equal([nextMidnight], nextChart.Doses.Select(d => d.DueAt));
    }

    /// <summary>
    /// I2 ruling: <see cref="MedicationChart.StopFutureDosesAsync"/> (Task 10's discharge calls it) moves every Due dose due after the
    /// given time to Stopped and leaves earlier and recorded doses alone; recording a stopped dose is 409 "This dose was stopped."
    /// </summary>
    [Fact]
    public async Task Stopped_doses_are_never_recorded()
    {
        var w = await WorldAsync("Stopped Dose Clinic");
        await PrescribeAsync(w, Item("Stopamycin", "QDS", 2));
        var doses = await DosesAsync(w.AdmissionId);
        Assert.Equal(8, doses.Count);
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[0].Id}", new { status = "Given" }, Ct));
        // A Missed dose after the stop time is not Due, so it stays as the job left it.
        await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == doses[7].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DoseStatus.Missed), Ct));

        var stopAt = doses[2].DueAt; // doses due exactly at the stop time are kept
        var stopped = await api.AsTenantAsync<IAppDbContext, int>(w.Clinic.TenantId, db => MedicationChart.StopFutureDosesAsync(db, w.AdmissionId, stopAt, Ct));
        Assert.Equal(4, stopped);
        var after = await DosesAsync(w.AdmissionId);
        Assert.Equal([DoseStatus.Given, DoseStatus.Due, DoseStatus.Due, DoseStatus.Stopped, DoseStatus.Stopped, DoseStatus.Stopped, DoseStatus.Stopped, DoseStatus.Missed],
            after.Select(d => d.Status));
        Assert.Equal(0, await api.AsTenantAsync<IAppDbContext, int>(w.Clinic.TenantId, db => MedicationChart.StopFutureDosesAsync(db, w.AdmissionId, stopAt, Ct)));

        // Recording a stopped dose: 409, the row unchanged. Doses before the stop are still recorded as usual.
        foreach (var client in new[] { w.Nurse, w.Clinic.Owner })
            await AssertProblemAsync(await client.PostAsJsonAsync($"/api/care/doses/{doses[4].Id}", new { status = "Given" }, Ct),
                HttpStatusCode.Conflict, "This dose was stopped.", code: "dose_stopped");
        var row = (await DosesAsync(w.AdmissionId)).Single(d => d.Id == doses[4].Id);
        Assert.Equal(DoseStatus.Stopped, row.Status);
        Assert.Null(row.RecordedByUserId);
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[1].Id}", new { status = "Held" }, Ct));
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[0].Id}", new { status = "Given" }, Ct), HttpStatusCode.Conflict, DoseRecorded);

        // The chart shows them as stopped.
        var chart = await Json(await w.Nurse.GetAsync($"/api/care/admissions/{w.AdmissionId}/medication-chart?date={CareCalendar.Today(doses[4].DueAt):yyyy-MM-dd}", Ct));
        Assert.Equal("Stopped", chart.GetProperty("doses").EnumerateArray().Single(d => Id(d) == doses[4].Id).GetProperty("status").GetString());
    }

    // ---------- Nursing notes ----------

    [Fact]
    public async Task Nursing_notes_are_ward_care_and_encrypted()
    {
        var w = await WorldAsync("Nursing Clinic");
        var (strangerNurse, strangerNurseId) = await kit.StaffAsync(w.Clinic, "Nurse");
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        var path = $"/api/care/admissions/{w.AdmissionId}/nursing-notes";

        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { note = "  " }, Ct), HttpStatusCode.BadRequest, field: "note");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { note = new string('x', 4_001) }, Ct), HttpStatusCode.BadRequest, field: "note");
        await AssertProblemAsync(await w.Desk.PostAsJsonAsync(path, new { note = "Desk note" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await cashier.PostAsJsonAsync(path, new { note = "Cashier note" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await strangerNurse.PostAsJsonAsync(path, new { note = "Not my ward" }, Ct), HttpStatusCode.Forbidden, NotOnWard);
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/admissions/{Guid.NewGuid()}/nursing-notes", new { note = "Nobody" }, Ct), HttpStatusCode.NotFound);

        var note = await Json(await w.Nurse.PostAsJsonAsync(path, new { note = "Restless overnight, nursewalk" }, Ct));
        Assert.Equal("Restless overnight, nursewalk", note.GetProperty("note").GetString());
        Assert.Equal(w.NurseId, note.GetProperty("authorUserId").GetGuid());
        await Json(await w.Doctor.PostAsJsonAsync(path, new { note = "Doctor's nursing note" }, Ct));
        await Json(await w.Clinic.Owner.PostAsJsonAsync(path, new { note = "Director's nursing note" }, Ct));
        var rows = await api.WithDbAsync(db => db.NursingNotes.IgnoreQueryFilters().Where(n => n.AdmissionId == w.AdmissionId).ToListAsync(Ct));
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, r => r.Text.Contains("nursewalk") || r.Text.Contains("nursing note"));
        Assert.Contains(rows, r => Text.Read(r.Text) == "Restless overnight, nursewalk");
        Assert.Empty(await AccessLogAsync(w.PatientId)); // writes are not record opens

        // Reading: newest first; a logged open. An unrelated nurse needs a reason.
        var notes = (await Json(await w.Nurse.GetAsync(path, Ct))).EnumerateArray().ToList();
        Assert.Equal(3, notes.Count);
        Assert.Equal("Director's nursing note", notes[0].GetProperty("note").GetString());
        Assert.Equal("Restless overnight, nursewalk", notes[^1].GetProperty("note").GetString());
        Assert.Equal("nursing", Assert.Single(await AccessLogAsync(w.PatientId)).Area);
        await AssertProblemAsync(await strangerNurse.GetAsync(path, Ct), HttpStatusCode.Conflict, code: "emergency_reason_required");
        await Json(await GetWithReasonAsync(strangerNurse, path, "Helping on the ward"));
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.StaffUserId == strangerNurseId && a.Emergency);
        // Emergency reading doesn't make it their ward.
        await AssertProblemAsync(await strangerNurse.PostAsJsonAsync(path, new { note = "Still not my ward" }, Ct), HttpStatusCode.Forbidden, NotOnWard);

        // A transfer to another ward moves the ward rule with the patient: the old ward's nurse can't chart, the new ward's can.
        var otherWard = await kit.WardAsync(w.Clinic, "Nursing Clinic other ward", nurseUserIds: [strangerNurseId]);
        var otherBed = await kit.BedAsync(w.Clinic, otherWard, "O1");
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/transfer", new { bedId = otherBed }, Ct));
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { note = "Left the ward" }, Ct), HttpStatusCode.Forbidden, NotOnWard);
        await Json(await strangerNurse.PostAsJsonAsync(path, new { note = "Now my ward" }, Ct));
    }

    // ---------- Ward rounds ----------

    [Fact]
    public async Task Ward_round_signing_matches_consultation_rules()
    {
        var w = await WorldAsync("Round Clinic");
        var (otherDoctor, otherDoctorId) = await kit.StaffAsync(w.Clinic, "Doctor");
        // A care relationship with the patient from elsewhere (an outpatient visit) isn't one with this admission.
        await kit.AssignAsync(w.Clinic, w.PatientId, otherDoctorId);
        var path = $"/api/care/admissions/{w.AdmissionId}/ward-rounds";

        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync(path, new { note = "Nurse round" }, Ct), HttpStatusCode.Forbidden, DoctorsOnly);
        await AssertProblemAsync(await w.Desk.PostAsJsonAsync(path, new { note = "Desk round" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await otherDoctor.PostAsJsonAsync(path, new { note = "Other round" }, Ct), HttpStatusCode.Forbidden, NotOnWard);
        Assert.Equal(0, await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().CountAsync(r => r.AdmissionId == w.AdmissionId, Ct)));

        // The admitting doctor starts a blank draft; a blank note can't be signed.
        var round = await Json(await w.Doctor.PostAsJsonAsync(path, new { note = (string?)null }, Ct));
        var id = Id(round);
        Assert.Equal("Draft", round.GetProperty("status").GetString());
        Assert.Equal(w.DoctorId, round.GetProperty("doctorUserId").GetGuid());
        await AssertProblemAsync(await w.Doctor.PostAsync($"/api/care/ward-rounds/{id}/sign", null, Ct), HttpStatusCode.Conflict, EmptyRound, code: "note_empty");

        // The draft belongs to its author: the director and other doctors can't change or sign it; addenda wait for the signature.
        await AssertProblemAsync(await w.Clinic.Owner.PutAsJsonAsync($"/api/care/ward-rounds/{id}", new { note = "Director edit" }, Ct), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await w.Clinic.Owner.PostAsync($"/api/care/ward-rounds/{id}/sign", null, Ct), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await otherDoctor.PutAsJsonAsync($"/api/care/ward-rounds/{id}", new { note = "Other edit" }, Ct), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await w.Desk.PutAsJsonAsync($"/api/care/ward-rounds/{id}", new { note = "Desk edit" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/notes/ward-round/{id}/addenda", new { text = "Too early" }, Ct),
            HttpStatusCode.Conflict, AddendaAfterSigning);
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync($"/api/care/ward-rounds/{Guid.NewGuid()}", new { note = "Nothing" }, Ct), HttpStatusCode.NotFound);

        // The author writes and signs it; the text is encrypted; the signature is audited without text.
        var saved = await Json(await w.Doctor.PutAsJsonAsync($"/api/care/ward-rounds/{id}", new { note = "Roundwalk: improving, continue IV fluids" }, Ct));
        Assert.Equal("Roundwalk: improving, continue IV fluids", saved.GetProperty("note").GetString());
        var signed = await Json(await w.Doctor.PostAsync($"/api/care/ward-rounds/{id}/sign", null, Ct));
        Assert.Equal("Signed", signed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, signed.GetProperty("signedAt").ValueKind);
        var row = await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().FirstAsync(r => r.Id == id, Ct));
        Assert.DoesNotContain("Roundwalk", row.Note);
        Assert.Equal(NoteStatus.Signed, row.Status);
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == "care.note_signed" && a.EntityId == id.ToString()).ToListAsync(Ct)));
        Assert.Contains("WardRound", audit.AfterJson);
        Assert.DoesNotContain("Roundwalk", audit.AfterJson);

        // Signing is final: any change is 409 with the signed-note sentence, even from the director.
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync($"/api/care/ward-rounds/{id}", new { note = "Changed" }, Ct), HttpStatusCode.Conflict, SignedNote, code: "note_signed");
        await AssertProblemAsync(await w.Doctor.PostAsync($"/api/care/ward-rounds/{id}/sign", null, Ct), HttpStatusCode.Conflict, SignedNote);
        await AssertProblemAsync(await w.Clinic.Owner.PutAsJsonAsync($"/api/care/ward-rounds/{id}", new { note = "Changed" }, Ct), HttpStatusCode.Conflict, SignedNote);
        Assert.Equal("Roundwalk: improving, continue IV fluids", Text.Read((await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().FirstAsync(r => r.Id == id, Ct))).Note));

        // Corrections are addenda: the ward nurse adds one (a logged open, area admission).
        var addendum = await Json(await w.Nurse.PostAsJsonAsync($"/api/care/notes/ward-round/{id}/addenda", new { text = "Fluids running at 125 ml/h" }, Ct));
        Assert.Equal("WardRound", addendum.GetProperty("noteKind").GetString());
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.StaffUserId == w.NurseId && a.Area == "admission");

        // The rounds read (newest first, drafts with their status, addenda oldest first) is a logged clinical open.
        var second = Id(await Json(await w.Clinic.Owner.PostAsJsonAsync(path, new { note = "Director round draft" }, Ct)));
        var rounds = (await Json(await w.Nurse.GetAsync(path, Ct))).EnumerateArray().ToList();
        Assert.Equal([second, id], rounds.Select(Id));
        Assert.Equal("Draft", rounds[0].GetProperty("status").GetString());
        Assert.Equal("Roundwalk: improving, continue IV fluids", rounds[1].GetProperty("note").GetString());
        Assert.Equal("Fluids running at 125 ml/h", rounds[1].GetProperty("addenda").EnumerateArray().Single().GetProperty("text").GetString());
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.StaffUserId == w.NurseId && a.Area == "ward-round");
        await AssertProblemAsync(await w.Desk.GetAsync(path, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
    }

    // ---------- Discharge ----------

    [Fact]
    public async Task Discharged_admission_is_final()
    {
        var w = await WorldAsync("Final Clinic");
        await PrescribeAsync(w, Item("Finalcillin", "BD", 2));
        var dose = (await DosesAsync(w.AdmissionId))[0].Id;
        var roundsPath = $"/api/care/admissions/{w.AdmissionId}/ward-rounds";
        var signedRound = Id(await Json(await w.Doctor.PostAsJsonAsync(roundsPath, new { note = "Ready for home" }, Ct)));
        await Json(await w.Doctor.PostAsync($"/api/care/ward-rounds/{signedRound}/sign", null, Ct));
        var draftRound = Id(await Json(await w.Doctor.PostAsJsonAsync(roundsPath, new { note = "Unfinished draft" }, Ct)));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/nursing-notes", new { note = "Before discharge" }, Ct));

        // Task 10 discharges; set directly here.
        await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.Id == w.AdmissionId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AdmissionStatus.Discharged).SetProperty(a => a.DischargedAt, DateTimeOffset.UtcNow), Ct));

        // Every write but an addendum is 409, for everyone (the director too).
        foreach (var client in new[] { w.Nurse, w.Doctor, w.Clinic.Owner })
        {
            await AssertProblemAsync(await client.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/nursing-notes", new { note = "After discharge" }, Ct),
                HttpStatusCode.Conflict, Discharged, code: "admission_discharged");
            await AssertProblemAsync(await client.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/fluids", new { direction = "In", kind = "Oral", ml = 100 }, Ct),
                HttpStatusCode.Conflict, Discharged);
            await AssertProblemAsync(await client.PostAsJsonAsync($"/api/care/doses/{dose}", new { status = "Given" }, Ct), HttpStatusCode.Conflict, Discharged);
        }
        foreach (var client in new[] { w.Doctor, w.Clinic.Owner })
            await AssertProblemAsync(await client.PostAsJsonAsync(roundsPath, new { note = "After discharge" }, Ct), HttpStatusCode.Conflict, Discharged);
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync($"/api/care/ward-rounds/{draftRound}", new { note = "Finish it" }, Ct), HttpStatusCode.Conflict, Discharged);
        await AssertProblemAsync(await w.Doctor.PostAsync($"/api/care/ward-rounds/{draftRound}/sign", null, Ct), HttpStatusCode.Conflict, Discharged);
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false, items = new[] { Item("Latecillin", "OD") } }, Ct),
            HttpStatusCode.Conflict, Discharged);

        Assert.Equal(1, await api.WithDbAsync(db => db.NursingNotes.IgnoreQueryFilters().CountAsync(n => n.AdmissionId == w.AdmissionId, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.FluidEntries.IgnoreQueryFilters().CountAsync(n => n.AdmissionId == w.AdmissionId, Ct)));
        Assert.Equal(2, await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().CountAsync(n => n.AdmissionId == w.AdmissionId, Ct)));
        Assert.Equal(NoteStatus.Draft, (await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().FirstAsync(r => r.Id == draftRound, Ct))).Status);
        Assert.All(await DosesAsync(w.AdmissionId), d => Assert.Equal(DoseStatus.Due, d.Status));

        // Addenda are the exception, and the record stays readable.
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/notes/ward-round/{signedRound}/addenda", new { text = "Follow up in clinic in a week" }, Ct));
        Assert.Equal(2, (await Json(await w.Clinic.Owner.GetAsync(roundsPath, Ct))).GetArrayLength());
        Assert.Equal(1, (await Json(await w.Clinic.Owner.GetAsync($"/api/care/admissions/{w.AdmissionId}/nursing-notes", Ct))).GetArrayLength());
        await Json(await w.Clinic.Owner.GetAsync($"/api/care/admissions/{w.AdmissionId}/medication-chart", Ct));
        await Json(await w.Clinic.Owner.GetAsync($"/api/care/admissions/{w.AdmissionId}/fluids", Ct));
    }
}
