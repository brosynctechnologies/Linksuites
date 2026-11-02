using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>CareLink Task 4, fix round 1: the review's gaps (rebooking, the Lagos date, the clock boundary, partial slots, inactive moves).</summary>
public sealed partial class CareAppointmentTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Runs <paramref name="action"/> on an <c>AppointmentService</c> whose clock is fixed at <paramref name="now"/>.</summary>
    private async Task<T> AtAsync<T>(Guid tenantId, DateTimeOffset now, Func<AppointmentService, Task<T>> action)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        TimeProvider clock = new FixedClock(now);
        return await action(ActivatorUtilities.CreateInstance<AppointmentService>(sp, clock));
    }

    [Fact]
    public async Task A_cancelled_slot_can_be_booked_again()
    {
        var clinic = await kit.ClinicAsync("Rebook Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var date = FutureDate(DayOfWeek.Monday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "09:00", "10:00") }, Ct));
        var ada = (await kit.PatientAsync(clinic, "Ada", "Rebook")).GetProperty("id").GetGuid();
        var bola = (await kit.PatientAsync(clinic, "Bola", "Rebook")).GetProperty("id").GetGuid();

        var first = await Json(await BookAsync(desk, ada, doctorId, Lagos(date, 9, 15)));
        (await desk.PostAsync($"/api/care/appointments/{first.GetProperty("id").GetGuid()}/cancel", null, Ct)).EnsureSuccessStatusCode();
        var again = await Json(await BookAsync(desk, bola, doctorId, Lagos(date, 9, 15)));
        Assert.Equal("Booked", again.GetProperty("status").GetString());
        Assert.NotEqual(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal(1, await LiveAppointmentsAsync(doctorId, Lagos(date, 9, 15)));
        await AssertProblemAsync(await BookAsync(desk, ada, doctorId, Lagos(date, 9, 15)), HttpStatusCode.Conflict, SlotTaken);
    }

    [Fact]
    public async Task Slots_booking_and_lists_use_the_Lagos_date_not_the_UTC_date()
    {
        var clinic = await kit.ClinicAsync("Midnight Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var monday = FutureDate(DayOfWeek.Monday);
        var sunday = monday.AddDays(-1);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "00:00", "01:00") }, Ct));
        var patientId = (await kit.PatientAsync(clinic, "Night", "Owl")).GetProperty("id").GetGuid();

        // 00:00 Lagos on Monday is 23:00Z on Sunday: the UTC date of every slot is the day before.
        var slots = await SlotsAsync(desk, doctorId, monday);
        Assert.Equal(4, slots.Count);
        Assert.Equal(new DateTimeOffset(sunday.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), slots[0]);
        Assert.All(slots, s => Assert.Equal(DayOfWeek.Sunday, s.UtcDateTime.DayOfWeek));
        Assert.Empty(await SlotsAsync(desk, doctorId, sunday)); // the UTC date of those slots has no hours

        var booked = await Json(await BookAsync(desk, patientId, doctorId, Lagos(monday, 0, 15)));
        Assert.Equal(new DateTimeOffset(sunday.ToDateTime(new TimeOnly(23, 15)), TimeSpan.Zero), booked.GetProperty("start").GetDateTimeOffset());
        Assert.Equal(3, (await SlotsAsync(desk, doctorId, monday)).Count);

        async Task<int> CountAsync(DateOnly day) =>
            (await Json(await desk.GetAsync($"/api/care/appointments?date={day:yyyy-MM-dd}&doctorUserId={doctorId}", Ct))).GetArrayLength();
        Assert.Equal(1, await CountAsync(monday));
        Assert.Equal(0, await CountAsync(sunday));
        Assert.Equal(0, await CountAsync(monday.AddDays(1)));
    }

    [Fact]
    public async Task The_slot_running_now_is_bookable_and_an_ended_slot_is_not()
    {
        var clinic = await kit.ClinicAsync("Clock Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var date = FutureDate(DayOfWeek.Monday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "09:00", "10:00") }, Ct));
        var patients = new List<Guid>();
        for (var i = 0; i < 3; i++) patients.Add((await kit.PatientAsync(clinic, $"Clock{i}", "Patient")).GetProperty("id").GetGuid());
        Task<AppointmentDto> Book(DateTimeOffset now, Guid patient, DateTimeOffset slot) =>
            AtAsync(clinic.TenantId, now, s => s.BookAsync(new BookRequest(patient, doctorId, slot, null), AppointmentSource.Desk, Ct));

        // 09:20 Lagos: the 09:00 slot ended at 09:15, the 09:15 slot is running.
        var now = Lagos(date, 9, 20);
        var open = await AtAsync(clinic.TenantId, now, s => s.SlotsAsync(doctorId, date, Ct));
        Assert.Equal([Lagos(date, 9, 15), Lagos(date, 9, 30), Lagos(date, 9, 45)], open.Select(s => s.Start).ToList());
        Assert.Equal(Lagos(date, 9, 15), (await Book(now, patients[0], Lagos(date, 9, 15))).Start);
        var ended = await Assert.ThrowsAsync<BadRequestException>(() => Book(now, patients[1], Lagos(date, 9, 0)));
        Assert.True(ended.Errors!.ContainsKey("start"));

        // The boundary: a slot that ends exactly now has ended, and the slot list leaves it out.
        var atEnd = Lagos(date, 9, 30);
        Assert.DoesNotContain(Lagos(date, 9, 15), (await AtAsync(clinic.TenantId, atEnd, s => s.SlotsAsync(doctorId, date, Ct))).Select(s => s.Start));
        var gone = await Assert.ThrowsAsync<BadRequestException>(() => Book(atEnd, patients[2], Lagos(date, 9, 15)));
        Assert.True(gone.Errors!.ContainsKey("start"));
        // One minute before it ends the slot is still running: the answer is the clash with the booking, not "passed".
        await Assert.ThrowsAsync<ConflictException>(() => Book(Lagos(date, 9, 29), patients[2], Lagos(date, 9, 15)));
    }

    [Fact]
    public async Task A_block_that_isnt_a_slot_multiple_drops_its_partial_tail()
    {
        var clinic = await kit.ClinicAsync("Partial Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var date = FutureDate(DayOfWeek.Monday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "09:00", "09:50") }, Ct));
        Assert.Equal([Lagos(date, 9, 0), Lagos(date, 9, 15), Lagos(date, 9, 30)], await SlotsAsync(desk, doctorId, date));
        var patientId = (await kit.PatientAsync(clinic, "Tail", "Patient")).GetProperty("id").GetGuid();
        await AssertProblemAsync(await BookAsync(desk, patientId, doctorId, Lagos(date, 9, 45)), HttpStatusCode.BadRequest, field: "start");
    }

    [Fact]
    public async Task Moving_an_appointment_of_an_inactive_patient_is_refused()
    {
        var clinic = await kit.ClinicAsync("Inactive Move Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var date = FutureDate(DayOfWeek.Monday);
        await Json(await doctor.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new { blocks = Block(DayOfWeek.Monday, "09:00", "10:00") }, Ct));
        var patientId = (await kit.PatientAsync(clinic, "Dormant", "Patient")).GetProperty("id").GetGuid();
        var booked = await Json(await BookAsync(desk, patientId, doctorId, Lagos(date, 9, 0)));
        var path = $"/api/care/appointments/{booked.GetProperty("id").GetGuid()}";

        (await desk.PostAsync($"/api/care/patients/{patientId}/inactive", null, Ct)).EnsureSuccessStatusCode();
        await AssertProblemAsync(await desk.PutAsJsonAsync(path, new { start = Lagos(date, 9, 30) }, Ct), HttpStatusCode.Conflict, "This patient is inactive.");
        Assert.Equal(1, await LiveAppointmentsAsync(doctorId, Lagos(date, 9, 0)));
        // An active patient moves freely again.
        (await desk.PostAsync($"/api/care/patients/{patientId}/active", null, Ct)).EnsureSuccessStatusCode();
        await Json(await desk.PutAsJsonAsync(path, new { start = Lagos(date, 9, 30) }, Ct));
    }
}
