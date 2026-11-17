using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1, CareLink items 4 and 6 (portal side): rescheduling from the portal under the booking rules, the unread notice count, reading
/// the patient's contact details, and a results-only list.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CarePortalWave1Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);
    private static string P(Guid patientId) => $"/api/care-portal/patients/{patientId}";

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

    private static object[] EveryDay() => Enum.GetValues<DayOfWeek>()
        .Select(d => (object)new { weekday = d.ToString(), start = "07:00", end = "21:00", slotMinutes = 15 }).ToArray();

    private async Task<(HttpClient Client, Guid Id)> DoctorWithHoursAsync(CareKit.Clinic clinic, object[]? blocks = null)
    {
        var (client, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = blocks ?? EveryDay() }, Ct));
        return (client, doctorId);
    }

    private static async Task<List<DateTimeOffset>> SlotsAsync(HttpClient portal, Guid patientId, Guid doctorId, DateOnly date) =>
        (await Json(await portal.GetAsync($"{P(patientId)}/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();

    [Fact]
    public async Task The_portal_reschedules_under_the_booking_rules()
    {
        var clinic = await kit.ClinicAsync("Portal Move Clinic");
        var (_, doctorId) = await DoctorWithHoursAsync(clinic);
        // A second doctor who only works Mondays 09:00 to 10:00.
        var (_, mondayDoctor) = await DoctorWithHoursAsync(clinic, [new { weekday = "Monday", start = "09:00", end = "10:00", slotMinutes = 30 }]);
        var patientId = (await kit.PatientAsync(clinic, "Mover", "Patient")).GetProperty("id").GetGuid();
        var sibling = (await kit.PatientAsync(clinic, "Sibling", "Patient", "2001-01-01")).GetProperty("id").GetGuid();
        var (portal, _) = await kit.PortalAccountAsync(clinic, patientId, "Self");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");

        var slots = await SlotsAsync(portal, patientId, doctorId, Today.AddDays(3));
        var booked = await Json(await portal.PostAsJsonAsync($"{P(patientId)}/appointments", new { doctorUserId = doctorId, start = slots[0], reason = "Check-up" }, Ct));
        var id = booked.GetProperty("id").GetGuid();
        var path = $"{P(patientId)}/appointments/{id}/reschedule";
        var changedBefore = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.TenantId == clinic.TenantId && n.Kind == "care.appointment_changed", Ct));

        // Move to another open slot: the time changes, the reason stays, the patient hears the new time.
        var moved = await Json(await portal.PostAsJsonAsync(path, new { start = slots[2] }, Ct));
        Assert.Equal(slots[2], moved.GetProperty("start").GetDateTimeOffset());
        Assert.Equal("Check-up", moved.GetProperty("reason").GetString());
        Assert.Equal("Booked", moved.GetProperty("status").GetString());
        Assert.Equal(changedBefore + 1, await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == clinic.TenantId && n.Kind == "care.appointment_changed", Ct)));

        // A time taken by someone else is refused; so is a time outside the doctor's hours, and a past one.
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = sibling, doctorUserId = doctorId, start = slots[4] }, Ct));
        await AssertProblemAsync(await portal.PostAsJsonAsync(path, new { start = slots[4] }, Ct), HttpStatusCode.Conflict, AppointmentService.SlotTaken);
        await AssertProblemAsync(await portal.PostAsJsonAsync(path, new { start = slots[0].AddMinutes(7) }, Ct), HttpStatusCode.BadRequest, field: "start");
        await AssertProblemAsync(await portal.PostAsJsonAsync(path, new { start = DateTimeOffset.UtcNow.AddDays(-1) }, Ct), HttpStatusCode.BadRequest, field: "start");
        await AssertProblemAsync(await portal.PostAsJsonAsync(path, new { }, Ct), HttpStatusCode.BadRequest, field: "start");
        // Another doctor's hours apply when moving to them: a Tuesday at 09:00 isn't one of the Monday doctor's slots.
        var tuesday = Enumerable.Range(1, 7).Select(Today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Tuesday);
        var tuesdayNine = new DateTimeOffset(tuesday.ToDateTime(new TimeOnly(9, 0)), CareCalendar.LagosOffset);
        await AssertProblemAsync(await portal.PostAsJsonAsync(path, new { doctorUserId = mondayDoctor, start = tuesdayNine }, Ct), HttpStatusCode.BadRequest, field: "start");
        var monday = Enumerable.Range(1, 7).Select(Today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Monday);
        var withMonday = await Json(await portal.PostAsJsonAsync(path,
            new { doctorUserId = mondayDoctor, start = new DateTimeOffset(monday.ToDateTime(new TimeOnly(9, 30)), CareCalendar.LagosOffset) }, Ct));
        Assert.Equal(mondayDoctor, withMonday.GetProperty("doctorUserId").GetGuid());

        // Another patient's appointment is 404 through this patient.
        var theirs = await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = sibling, doctorUserId = doctorId, start = slots[6] }, Ct));
        await AssertProblemAsync(await portal.PostAsJsonAsync($"{P(patientId)}/appointments/{theirs.GetProperty("id").GetGuid()}/reschedule",
            new { start = slots[7] }, Ct), HttpStatusCode.NotFound);

        // Within two hours of the start the patient calls the clinic instead; a cancelled appointment can't be moved.
        var soonId = Guid.NewGuid();
        var soon = DateTimeOffset.UtcNow.AddMinutes(90);
        await api.WithDbAsync(db =>
        {
            db.Appointments.Add(new Appointment
            {
                Id = soonId, TenantId = clinic.TenantId, PatientId = patientId, DoctorUserId = doctorId, Start = soon, End = soon.AddMinutes(15),
                Status = AppointmentStatus.Booked, Source = AppointmentSource.Portal,
            });
            return db.SaveChangesAsync(Ct);
        });
        await AssertProblemAsync(await portal.PostAsJsonAsync($"{P(patientId)}/appointments/{soonId}/reschedule", new { start = slots[8] }, Ct),
            HttpStatusCode.Conflict, AppointmentService.LateCancel);
        await Json(await portal.PostAsync($"{P(patientId)}/appointments/{id}/cancel", null, Ct));
        await AssertProblemAsync(await portal.PostAsJsonAsync(path, new { start = slots[9] }, Ct), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_portal_reads_its_unread_count_contact_details_and_results()
    {
        var clinic = await kit.ClinicAsync("Portal Reads Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = (await kit.PatientAsync(clinic, "Reader", "Patient", phone: "08031234567")).GetProperty("id").GetGuid();
        var (portal, userId) = await kit.PortalAccountAsync(clinic, patientId, "Self");
        var other = (await kit.PatientAsync(clinic, "Unlinked", "Patient", "1980-02-02")).GetProperty("id").GetGuid();

        // Contact details as stored (the same shape the PUT answers).
        var contact = await Json(await portal.GetAsync($"{P(patientId)}/contact", Ct));
        Assert.Equal("+2348031234567", contact.GetProperty("phone").GetString());
        Assert.Equal(JsonValueKind.Null, contact.GetProperty("email").ValueKind);
        await Json(await portal.PutAsJsonAsync($"{P(patientId)}/contact", new { phone = "08031234567", email = "reader@example.com", address = "2 Marina" }, Ct));
        Assert.Equal("2 Marina", (await Json(await portal.GetAsync($"{P(patientId)}/contact", Ct))).GetProperty("address").GetString());
        await AssertProblemAsync(await portal.GetAsync($"{P(other)}/contact", Ct), HttpStatusCode.NotFound);

        // Results only: reviewed results, no notes or bills; a portal read like the record.
        var admissionId = await kit.AdmitAsync(doctor, patientId);
        var testId = (await Json(await doctor.PostAsJsonAsync("/api/care/tests", new { patientId, admissionId, kind = "Lab", description = "Malaria smear" }, Ct)))
            .GetProperty("id").GetGuid();
        await Json(await doctor.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Negative" }, Ct));
        Assert.Empty((await Json(await portal.GetAsync($"{P(patientId)}/results", Ct))).EnumerateArray());
        await Json(await doctor.PostAsync($"/api/care/tests/{testId}/review", null, Ct));
        var reads = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId && a.Portal, Ct));
        var result = Assert.Single((await Json(await portal.GetAsync($"{P(patientId)}/results", Ct))).EnumerateArray());
        Assert.Equal("Malaria smear", result.GetProperty("description").GetString());
        Assert.Equal("Negative", result.GetProperty("resultText").GetString());
        Assert.False(result.GetProperty("corrected").GetBoolean());
        Assert.Equal(reads + 1, await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId && a.Portal, Ct)));
        await AssertProblemAsync(await portal.GetAsync($"{P(other)}/results", Ct), HttpStatusCode.NotFound);

        // Unread notices for this patient at this clinic (the review sent one).
        var unread = await Json(await portal.GetAsync($"{P(patientId)}/notifications/unread-count", Ct));
        var expected = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .CountAsync(n => n.UserId == userId && n.TenantId == clinic.TenantId && n.ReadAt == null && n.Link.EndsWith($"?patient={patientId}"), Ct));
        Assert.True(expected >= 1);
        Assert.Equal(expected, unread.GetProperty("count").GetInt32());
        await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), Ct));
        Assert.Equal(0, (await Json(await portal.GetAsync($"{P(patientId)}/notifications/unread-count", Ct))).GetProperty("count").GetInt32());
        await AssertProblemAsync(await portal.GetAsync($"{P(other)}/notifications/unread-count", Ct), HttpStatusCode.NotFound);
    }
}
