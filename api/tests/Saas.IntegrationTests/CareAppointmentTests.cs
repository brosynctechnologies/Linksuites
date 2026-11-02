using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 4: doctor schedules and appointments. Schedules are Lagos wall-clock times, appointments are UTC instants, slots
/// are computed for a Lagos date, and one doctor's slot can only ever be booked once.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class CareAppointmentTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string SlotTaken = "That time was just booked. Choose another.";
    private const string Deceased = "This patient is recorded as deceased.";

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

    /// <summary>A Lagos date well in the future whose weekday is <paramref name="weekday"/>, so every slot on it is still open.</summary>
    private static DateOnly FutureDate(DayOfWeek weekday)
    {
        var d = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(14);
        while (d.DayOfWeek != weekday) d = d.AddDays(1);
        return d;
    }

    /// <summary>The UTC instant of a Lagos wall-clock time.</summary>
    private static DateTimeOffset Lagos(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), CareCalendar.LagosOffset).ToUniversalTime();

    private static object[] Block(DayOfWeek day, string start, string end, int slot = 15) =>
        [new { weekday = day.ToString(), start, end, slotMinutes = slot }];

    private async Task<List<DateTimeOffset>> SlotsAsync(HttpClient client, Guid doctorId, DateOnly date) =>
        (await Json(await client.GetAsync($"/api/care/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid patientId, Guid doctorId, DateTimeOffset start, string? reason = null) =>
        client.PostAsJsonAsync("/api/care/appointments", new { patientId, doctorUserId = doctorId, start, reason }, Ct);

    private async Task<int> LiveAppointmentsAsync(Guid doctorId, DateTimeOffset start) =>
        await api.WithDbAsync(db => db.Appointments.IgnoreQueryFilters()
            .CountAsync(a => a.DoctorUserId == doctorId && a.Start == start && a.Status != AppointmentStatus.Cancelled, Ct));

    [Fact]
    public async Task Slots_follow_schedule_minus_bookings()
    {
        var clinic = await kit.ClinicAsync("Slots Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (otherDoctor, otherDoctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var date = FutureDate(DayOfWeek.Monday);
        var ada = (await kit.PatientAsync(clinic, "Ada", "Slots")).GetProperty("id").GetGuid();
        var bola = (await kit.PatientAsync(clinic, "Bola", "Slots")).GetProperty("id").GetGuid();

        // No schedule: no slots. Only leadership or that doctor writes the schedule; front desk and another doctor can't read or write it.
        Assert.Empty(await SlotsAsync(desk, doctorId, date));
        var blocks = new[]
        {
            new { weekday = "Monday", start = "09:00", end = "10:00", slotMinutes = 15 },
            new { weekday = "Monday", start = "11:00", end = "11:30", slotMinutes = 15 },
        };
        foreach (var client in new[] { desk, otherDoctor, nurse })
        {
            await AssertProblemAsync(await client.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks }, Ct), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await client.GetAsync($"/api/care/doctors/{doctorId}/schedule", Ct), HttpStatusCode.Forbidden);
        }
        var saved = await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks }, Ct));
        Assert.Equal(2, saved.GetProperty("blocks").GetArrayLength());
        Assert.Equal(2, (await Json(await clinic.Owner.GetAsync($"/api/care/doctors/{doctorId}/schedule", Ct))).GetProperty("blocks").GetArrayLength());
        Assert.Equal(2, (await Json(await doctor.GetAsync($"/api/care/doctors/{doctorId}/schedule", Ct))).GetProperty("blocks").GetArrayLength());

        // 09:00-10:00 and 11:00-11:30 Lagos in 15-minute slots: 6 slots, stored and returned as UTC (Lagos is UTC+1).
        var slots = await SlotsAsync(desk, doctorId, date);
        Assert.Equal(6, slots.Count);
        Assert.Equal(Lagos(date, 9, 0), slots[0]);
        Assert.Equal(new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero), slots[0]);
        Assert.Equal(Lagos(date, 11, 15), slots[^1]);
        Assert.All(slots, s => Assert.Equal(TimeSpan.Zero, s.Offset));
        Assert.Empty(await SlotsAsync(desk, doctorId, date.AddDays(1))); // Tuesday
        Assert.Equal(6, (await SlotsAsync(desk, doctorId, date.AddDays(7))).Count); // next Monday
        Assert.Empty(await SlotsAsync(desk, otherDoctorId, date));

        // A booking takes its slot out of the list; the booking carries Lagos clinic time as UTC.
        var booked = await Json(await BookAsync(desk, ada, doctorId, Lagos(date, 9, 15), "Follow-up visit"));
        Assert.Equal("Booked", booked.GetProperty("status").GetString());
        Assert.Equal("Desk", booked.GetProperty("source").GetString());
        Assert.Equal(Lagos(date, 9, 15), booked.GetProperty("start").GetDateTimeOffset());
        Assert.Equal(Lagos(date, 9, 30), booked.GetProperty("end").GetDateTimeOffset());
        Assert.Equal("Follow-up visit", booked.GetProperty("reason").GetString());
        var apptId = booked.GetProperty("id").GetGuid();
        slots = await SlotsAsync(desk, doctorId, date);
        Assert.Equal(5, slots.Count);
        Assert.DoesNotContain(Lagos(date, 9, 15), slots);

        // A slot is booked as a whole: a time that isn't one of the doctor's slots is a field error.
        await AssertProblemAsync(await BookAsync(desk, bola, doctorId, Lagos(date, 9, 20)), HttpStatusCode.BadRequest, field: "start");
        await AssertProblemAsync(await BookAsync(desk, bola, doctorId, Lagos(date, 12, 0)), HttpStatusCode.BadRequest, field: "start");
        await AssertProblemAsync(await BookAsync(desk, bola, doctorId, Lagos(date.AddDays(1), 9, 0)), HttpStatusCode.BadRequest, field: "start");
        // The same time, even as a non-UTC offset, is the same slot.
        var asLocal = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 15)), CareCalendar.LagosOffset);
        await AssertProblemAsync(await BookAsync(desk, bola, doctorId, asLocal), HttpStatusCode.Conflict, SlotTaken);

        // Cancelling gives the slot back; a cancelled appointment can't be cancelled or moved again.
        var cancelled = await Json(await desk.PostAsync($"/api/care/appointments/{apptId}/cancel", null, Ct));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(6, (await SlotsAsync(desk, doctorId, date)).Count);
        await AssertProblemAsync(await desk.PostAsync($"/api/care/appointments/{apptId}/cancel", null, Ct), HttpStatusCode.Conflict);
        await AssertProblemAsync(await desk.PutAsJsonAsync($"/api/care/appointments/{apptId}", new { start = Lagos(date, 9, 45) }, Ct), HttpStatusCode.Conflict);

        // Reschedule: the old slot opens, the new one closes, the reason is kept, a taken slot is refused.
        var again = await Json(await BookAsync(desk, ada, doctorId, Lagos(date, 9, 0), "Review"));
        var second = await Json(await BookAsync(desk, bola, doctorId, Lagos(date, 9, 30)));
        var moved = await Json(await desk.PutAsJsonAsync($"/api/care/appointments/{again.GetProperty("id").GetGuid()}", new { start = Lagos(date, 9, 45) }, Ct));
        Assert.Equal(Lagos(date, 9, 45), moved.GetProperty("start").GetDateTimeOffset());
        Assert.Equal("Review", moved.GetProperty("reason").GetString());
        slots = await SlotsAsync(desk, doctorId, date);
        Assert.Contains(Lagos(date, 9, 0), slots);
        Assert.DoesNotContain(Lagos(date, 9, 45), slots);
        await AssertProblemAsync(await desk.PutAsJsonAsync($"/api/care/appointments/{again.GetProperty("id").GetGuid()}", new { start = Lagos(date, 9, 30) }, Ct),
            HttpStatusCode.Conflict, SlotTaken);
        // Moving to the same slot it already holds is fine (it doesn't collide with itself), and it can change doctor.
        await Json(await desk.PutAsJsonAsync($"/api/care/appointments/{again.GetProperty("id").GetGuid()}", new { start = Lagos(date, 9, 45) }, Ct));
        await Json(await otherDoctor.PutAsJsonAsync($"/api/care/doctors/{otherDoctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "14:00", "15:00", 30) }, Ct));
        var toOther = await Json(await desk.PutAsJsonAsync($"/api/care/appointments/{second.GetProperty("id").GetGuid()}",
            new { doctorUserId = otherDoctorId, start = Lagos(date, 14, 30) }, Ct));
        Assert.Equal(otherDoctorId, toOther.GetProperty("doctorUserId").GetGuid());
        Assert.Equal(Lagos(date, 14, 30), toOther.GetProperty("end").GetDateTimeOffset() - TimeSpan.FromMinutes(30));
        Assert.Contains(Lagos(date, 9, 30), await SlotsAsync(desk, doctorId, date));

        // The list shows the day's appointments for a doctor, with patient names and the booking reason.
        var listed = (await Json(await desk.GetAsync($"/api/care/appointments?date={date:yyyy-MM-dd}&doctorUserId={doctorId}&status=Booked", Ct))).EnumerateArray().ToList();
        var only = Assert.Single(listed);
        Assert.Equal("Ada Slots", only.GetProperty("patientName").GetString());
        Assert.Equal("Review", only.GetProperty("reason").GetString());
        var byPatient = (await Json(await desk.GetAsync($"/api/care/appointments?patientId={bola}", Ct))).EnumerateArray().ToList();
        Assert.Single(byPatient);

        // The booking list of doctors lets the desk choose one.
        var doctors = (await Json(await desk.GetAsync("/api/care/doctors", Ct))).EnumerateArray().Select(d => d.GetProperty("userId").GetGuid()).ToList();
        Assert.Contains(doctorId, doctors);
        Assert.Contains(otherDoctorId, doctors);
        Assert.DoesNotContain(nurseId, doctors);
    }

    [Fact]
    public async Task Schedule_and_booking_input_is_validated()
    {
        var clinic = await kit.ClinicAsync("Validation Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (_, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var date = FutureDate(DayOfWeek.Wednesday);
        var patientId = (await kit.PatientAsync(clinic, "Val", "Idate")).GetProperty("id").GetGuid();
        string Path(Guid id) => $"/api/care/doctors/{id}/schedule";

        // End before start, an overlap on one weekday, a slot length outside 5-120 and missing fields are field errors.
        await AssertProblemAsync(await doctor.PutAsJsonAsync(Path(doctorId), new { blocks = Block(DayOfWeek.Wednesday, "10:00", "09:00") }, Ct), HttpStatusCode.BadRequest, field: "blocks");
        await AssertProblemAsync(await doctor.PutAsJsonAsync(Path(doctorId), new { blocks = Block(DayOfWeek.Wednesday, "09:00", "10:00", 3) }, Ct), HttpStatusCode.BadRequest, field: "blocks");
        await AssertProblemAsync(await doctor.PutAsJsonAsync(Path(doctorId), new { blocks = Block(DayOfWeek.Wednesday, "09:00", "10:00", 200) }, Ct), HttpStatusCode.BadRequest, field: "blocks");
        await AssertProblemAsync(await doctor.PutAsJsonAsync(Path(doctorId), new
        {
            blocks = new[]
            {
                new { weekday = "Wednesday", start = "09:00", end = "11:00", slotMinutes = 15 },
                new { weekday = "Wednesday", start = "10:30", end = "12:00", slotMinutes = 15 },
            },
        }, Ct), HttpStatusCode.BadRequest, field: "blocks");
        await AssertProblemAsync(await doctor.PutAsJsonAsync(Path(doctorId), new { blocks = new[] { new { weekday = "Wednesday" } } }, Ct), HttpStatusCode.BadRequest, field: "blocks");
        Assert.Equal(0, (await Json(await doctor.GetAsync(Path(doctorId), Ct))).GetProperty("blocks").GetArrayLength()); // nothing half-saved

        // A nurse isn't a doctor: no schedule, no slots, no booking. An unknown user is the same 404.
        await AssertProblemAsync(await clinic.Owner.PutAsJsonAsync(Path(nurseId), new { blocks = Block(DayOfWeek.Wednesday, "09:00", "10:00") }, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await desk.GetAsync($"/api/care/doctors/{Guid.NewGuid()}/slots?date={date:yyyy-MM-dd}", Ct), HttpStatusCode.NotFound);

        // A full replace: saving again swaps the whole week, and an empty list clears it.
        await Json(await doctor.PutAsJsonAsync(Path(doctorId), new { blocks = Block(DayOfWeek.Wednesday, "09:00", "10:00") }, Ct));
        await Json(await doctor.PutAsJsonAsync(Path(doctorId), new { blocks = Block(DayOfWeek.Wednesday, "13:00", "14:00", 30) }, Ct));
        Assert.Equal([Lagos(date, 13, 0), Lagos(date, 13, 30)], await SlotsAsync(desk, doctorId, date));

        // Slots need a date; a date that isn't one is a field error.
        await AssertProblemAsync(await desk.GetAsync($"/api/care/doctors/{doctorId}/slots", Ct), HttpStatusCode.BadRequest, field: "date");
        await AssertProblemAsync(await desk.GetAsync($"/api/care/doctors/{doctorId}/slots?date=soon", Ct), HttpStatusCode.BadRequest);

        // Booking input: a nurse isn't bookable, a patient from another clinic is 404, the reason is short and plain.
        await AssertProblemAsync(await BookAsync(desk, patientId, nurseId, Lagos(date, 13, 0)), HttpStatusCode.BadRequest, field: "doctorUserId");
        var other = await kit.ClinicAsync("Validation Other Clinic");
        var foreign = (await kit.PatientAsync(other, "Far", "Away")).GetProperty("id").GetGuid();
        await AssertProblemAsync(await BookAsync(desk, foreign, doctorId, Lagos(date, 13, 0)), HttpStatusCode.NotFound);
        await AssertProblemAsync(await BookAsync(desk, patientId, doctorId, Lagos(date, 13, 0), new string('x', 121)), HttpStatusCode.BadRequest, field: "reason");
        var empty = await desk.PostAsJsonAsync("/api/care/appointments", new { }, Ct);
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest);
        // A time that has already passed can't be booked (a past Wednesday the doctor works).
        var past = date.AddDays(-28);
        await AssertProblemAsync(await BookAsync(desk, patientId, doctorId, Lagos(past, 13, 0)), HttpStatusCode.BadRequest, field: "start");
        Assert.Empty(await SlotsAsync(desk, doctorId, past));

        // An inactive patient isn't booked until made active again.
        (await desk.PostAsync($"/api/care/patients/{patientId}/inactive", null, Ct)).EnsureSuccessStatusCode();
        await AssertProblemAsync(await BookAsync(desk, patientId, doctorId, Lagos(date, 13, 0)), HttpStatusCode.Conflict, "This patient is inactive.");
        (await desk.PostAsync($"/api/care/patients/{patientId}/active", null, Ct)).EnsureSuccessStatusCode();
        await Json(await BookAsync(desk, patientId, doctorId, Lagos(date, 13, 0)));
    }

    [Fact]
    public async Task Two_bookings_cannot_take_one_slot()
    {
        var clinic = await kit.ClinicAsync("Race Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var date = FutureDate(DayOfWeek.Friday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Friday, "08:00", "12:00") }, Ct));
        var patients = new List<Guid>();
        for (var i = 0; i < 6; i++) patients.Add((await kit.PatientAsync(clinic, $"Racer{i}", "Slot")).GetProperty("id").GetGuid());

        // Two at once, over and over on fresh slots: exactly one wins and the other is told the time was just taken.
        for (var round = 0; round < 4; round++)
        {
            var start = Lagos(date, 8 + round, 0);
            var results = await Task.WhenAll(BookAsync(desk, patients[0], doctorId, start), BookAsync(clinic.Owner, patients[1], doctorId, start));
            Assert.Single(results, r => r.IsSuccessStatusCode);
            var loser = Assert.Single(results, r => !r.IsSuccessStatusCode);
            await AssertProblemAsync(loser, HttpStatusCode.Conflict, SlotTaken);
            Assert.Equal(1, await LiveAppointmentsAsync(doctorId, start));
        }

        // Six at once for one slot: still exactly one.
        var crowded = Lagos(date, 8, 15);
        var many = await Task.WhenAll(patients.Select(p => BookAsync(desk, p, doctorId, crowded)));
        Assert.Single(many, r => r.IsSuccessStatusCode);
        Assert.Equal(5, many.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await LiveAppointmentsAsync(doctorId, crowded));

        // Two patients moving into one free slot at once: one moves, the other stays put.
        var a = await Json(await BookAsync(desk, patients[2], doctorId, Lagos(date, 10, 15)));
        var b = await Json(await BookAsync(desk, patients[3], doctorId, Lagos(date, 10, 30)));
        var target = Lagos(date, 10, 45);
        var moves = await Task.WhenAll(
            desk.PutAsJsonAsync($"/api/care/appointments/{a.GetProperty("id").GetGuid()}", new { start = target }, Ct),
            desk.PutAsJsonAsync($"/api/care/appointments/{b.GetProperty("id").GetGuid()}", new { start = target }, Ct));
        Assert.Single(moves, r => r.IsSuccessStatusCode);
        await AssertProblemAsync(Assert.Single(moves, r => !r.IsSuccessStatusCode), HttpStatusCode.Conflict, SlotTaken);
        Assert.Equal(1, await LiveAppointmentsAsync(doctorId, target));

        // The portal path through AppointmentService.BookAsync takes the same lock and the same slot rules, and records its source.
        var portalSlot = Lagos(date, 11, 15);
        var viaPortal = await api.AsTenantAsync<AppointmentService, AppointmentDto>(clinic.TenantId,
            s => s.BookAsync(new BookRequest(patients[4], doctorId, portalSlot, "Check-up"), AppointmentSource.Portal, Ct));
        Assert.Equal(AppointmentSource.Portal, viaPortal.Source);
        Assert.Equal(portalSlot, viaPortal.Start);
        var clash = await Assert.ThrowsAsync<Saas.Application.Common.ConflictException>(() => api.AsTenantAsync<AppointmentService, AppointmentDto>(clinic.TenantId,
            s => s.BookAsync(new BookRequest(patients[5], doctorId, portalSlot, null), AppointmentSource.Portal, Ct)));
        Assert.Equal(SlotTaken, clash.Message);
    }

    [Fact]
    public async Task Cashier_cannot_book()
    {
        var clinic = await kit.ClinicAsync("Cashier Booking Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var date = FutureDate(DayOfWeek.Tuesday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Tuesday, "09:00", "10:00") }, Ct));
        var patientId = (await kit.PatientAsync(clinic, "Cash", "Ier")).GetProperty("id").GetGuid();

        await AssertProblemAsync(await BookAsync(cashier, patientId, doctorId, Lagos(date, 9, 0)), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await cashier.GetAsync("/api/care/appointments", Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await cashier.GetAsync("/api/care/doctors", Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await cashier.GetAsync($"/api/care/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct), HttpStatusCode.Forbidden);
        Assert.Equal(0, await LiveAppointmentsAsync(doctorId, Lagos(date, 9, 0)));

        // Front desk, a nurse, an admin and the director do book.
        var slots = new[] { (nurse, 9, 0), (admin, 9, 15), (clinic.Owner, 9, 30) };
        foreach (var (client, h, m) in slots) await Json(await BookAsync(client, patientId, doctorId, Lagos(date, h, m)));

        // A cashier can't move or cancel one either.
        var existing = (await Json(await clinic.Owner.GetAsync($"/api/care/appointments?doctorUserId={doctorId}", Ct))).EnumerateArray().First().GetProperty("id").GetGuid();
        await AssertProblemAsync(await cashier.PutAsJsonAsync($"/api/care/appointments/{existing}", new { start = Lagos(date, 9, 45) }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await cashier.PostAsync($"/api/care/appointments/{existing}/cancel", null, Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Booking_for_a_deceased_patient_is_refused()
    {
        var clinic = await kit.ClinicAsync("Deceased Booking Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var date = FutureDate(DayOfWeek.Thursday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Thursday, "09:00", "10:00") }, Ct));
        var patientId = (await kit.PatientAsync(clinic, "Late", "Patient")).GetProperty("id").GetGuid();
        var booked = await Json(await BookAsync(desk, patientId, doctorId, Lagos(date, 9, 0)));

        // Nothing in this task marks a patient deceased (a later task does), so the status is set directly.
        await api.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE patients SET status = 'Deceased' WHERE id = {patientId}", Ct));

        await AssertProblemAsync(await BookAsync(desk, patientId, doctorId, Lagos(date, 9, 15)), HttpStatusCode.Conflict, Deceased);
        Assert.Equal(0, await LiveAppointmentsAsync(doctorId, Lagos(date, 9, 15)));
        // Moving an existing appointment of a deceased patient is refused too; cancelling it is still allowed.
        await AssertProblemAsync(await desk.PutAsJsonAsync($"/api/care/appointments/{booked.GetProperty("id").GetGuid()}", new { start = Lagos(date, 9, 30) }, Ct),
            HttpStatusCode.Conflict, Deceased);
        var cancelled = await Json(await desk.PostAsync($"/api/care/appointments/{booked.GetProperty("id").GetGuid()}/cancel", null, Ct));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        // The portal path refuses it as well.
        var ex = await Assert.ThrowsAsync<Saas.Application.Common.ConflictException>(() => api.AsTenantAsync<AppointmentService, AppointmentDto>(clinic.TenantId,
            s => s.BookAsync(new BookRequest(patientId, doctorId, Lagos(date, 9, 45), null), AppointmentSource.Portal, Ct)));
        Assert.Equal(Deceased, ex.Message);
    }

    [Fact]
    public async Task Appointments_stay_in_their_own_clinic_and_hold_no_clinical_text()
    {
        var clinic = await kit.ClinicAsync("Own Clinic Appointments");
        var other = await kit.ClinicAsync("Neighbour Clinic Appointments");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (otherDoctor, otherDoctorId) = await kit.StaffAsync(other, "Doctor");
        var (otherDesk, _) = await kit.StaffAsync(other, "FrontDesk");
        var date = FutureDate(DayOfWeek.Monday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "09:00", "10:00") }, Ct));
        await Json(await otherDoctor.PutAsJsonAsync($"/api/care/doctors/{otherDoctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "09:00", "10:00") }, Ct));
        var patientId = (await kit.PatientAsync(clinic, "Own", "Patient")).GetProperty("id").GetGuid();
        var booked = await Json(await BookAsync(clinic.Owner, patientId, doctorId, Lagos(date, 9, 0), "Routine check"));
        var apptId = booked.GetProperty("id").GetGuid();

        // Another clinic's doctor, schedule, slots and appointment are 404; their own list never shows ours.
        await AssertProblemAsync(await otherDesk.GetAsync($"/api/care/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await otherDesk.PostAsync($"/api/care/appointments/{apptId}/cancel", null, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await otherDesk.PutAsJsonAsync($"/api/care/appointments/{apptId}", new { start = Lagos(date, 9, 15) }, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await BookAsync(otherDesk, patientId, otherDoctorId, Lagos(date, 9, 0)), HttpStatusCode.NotFound);
        Assert.Empty((await Json(await otherDesk.GetAsync("/api/care/appointments", Ct))).EnumerateArray());

        // The booking reason is not copied into the audit log (a desk clerk may type more than they should).
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == clinic.TenantId && a.EntityType == nameof(Appointment)).ToListAsync(Ct));
        Assert.NotEmpty(audits);
        Assert.All(audits, a => Assert.DoesNotContain("Routine check", (a.AfterJson ?? "") + (a.BeforeJson ?? "")));
    }
}
