using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

public sealed record ScheduleBlockDto(DayOfWeek Weekday, TimeOnly Start, TimeOnly End, int SlotMinutes);

public sealed record DoctorScheduleDto(Guid DoctorUserId, string DoctorName, IReadOnlyList<ScheduleBlockDto> Blocks);

public sealed record SaveScheduleBlock(DayOfWeek? Weekday, TimeOnly? Start, TimeOnly? End, int? SlotMinutes);

public sealed record SaveScheduleRequest(List<SaveScheduleBlock>? Blocks);

/// <summary>An open slot, as UTC instants.</summary>
public sealed record SlotDto(DateTimeOffset Start, DateTimeOffset End);

public sealed record DoctorDto(Guid UserId, string Name);

/// <summary>Book the doctor's slot starting at <see cref="Start"/> (any offset; it is stored as UTC). <see cref="Reason"/> is a short, non-clinical booking reason.</summary>
public sealed record BookRequest(Guid? PatientId, Guid? DoctorUserId, DateTimeOffset? Start, string? Reason);

/// <summary>Move a booked appointment to another slot (and optionally another doctor). A null reason keeps the current one.</summary>
public sealed record RescheduleRequest(Guid? DoctorUserId, DateTimeOffset? Start, string? Reason);

public sealed record AppointmentDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, Guid DoctorUserId, string DoctorName,
    DateTimeOffset Start, DateTimeOffset End, string? Reason, AppointmentStatus Status, AppointmentSource Source);

/// <summary>
/// Doctor schedules, slots and appointments. Schedules are Africa/Lagos wall-clock <see cref="TimeOnly"/> blocks; slots are computed for a
/// Lagos date and handed out as UTC instants; appointments are stored as UTC. Every change that can take a slot (book, move, cancel) runs
/// under the doctor's advisory lock, so two simultaneous bookings for one slot give exactly one success.
/// <see cref="BookAsync"/> does no role check: the desk route checks the caller, and the portal (Task 15) checks its own session.
/// </summary>
public sealed class AppointmentService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, ICurrentUser user, TimeProvider clock, IDbErrors dbErrors, CareNotices notices)
{
    public const string SlotTaken = "That time was just booked. Choose another.";
    public const string PatientDeceased = "This patient is recorded as deceased.";
    public const string PatientInactive = "This patient is inactive.";
    public const int ReasonMax = 120;
    /// <summary>Task 15: the portal cancels only this long before the start; later, the patient calls the clinic.</summary>
    public static readonly TimeSpan PortalCancelNotice = TimeSpan.FromHours(2);
    public const string LateCancel = "This appointment starts soon. Call the clinic to change it.";
    /// <summary>How the booking reason is labelled wherever it is entered or shown, the portal included (Task 4 ruling).</summary>
    public const string ReasonLabel = "Reason for visit (no medical details)";
    public const int DefaultSlotMinutes = 15;
    private const int MaxBlocks = 42;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>The per-doctor booking lock (a transaction-scoped advisory lock).</summary>
    public static long LockKey(Guid tenantId, Guid doctorUserId) =>
        BitConverter.ToInt64(doctorUserId.ToByteArray(), 0) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0A9901D;

    private sealed record Slot(DateTimeOffset Start, DateTimeOffset End);

    /// <summary>The UTC instant of a Lagos wall-clock time.</summary>
    private static DateTimeOffset LagosInstant(DateOnly date, TimeSpan time) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue) + time, CareCalendar.LagosOffset).ToUniversalTime();

    // ---------- Doctors and schedules ----------

    /// <summary>The clinic's bookable doctors (active members whose clinical role is Doctor), by name. Front desk and above.</summary>
    public async Task<IReadOnlyList<DoctorDto>> DoctorsAsync(CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        return await BookableDoctorsAsync(ct);
    }

    /// <summary>The clinic's bookable doctors (names and ids only), with no role check: the portal checks its own session first (Task 15).</summary>
    public async Task<IReadOnlyList<DoctorDto>> BookableDoctorsAsync(CancellationToken ct) =>
        await (from p in db.CareStaffProfiles.AsNoTracking()
               join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
               join u in db.Users.AsNoTracking() on p.UserId equals u.Id
               where p.ClinicalRole == ClinicalRole.Doctor && m.Status == MembershipStatus.Active
               orderby u.FullName
               select new DoctorDto(u.Id, u.FullName)).ToListAsync(ct);

    /// <summary>The doctor's weekly hours. Leadership, or the doctor themself.</summary>
    public async Task<DoctorScheduleDto> GetScheduleAsync(Guid doctorUserId, CancellationToken ct)
    {
        EnsureScheduleAccess(await access.CallerAsync(ct), doctorUserId);
        var name = await DoctorNameAsync(doctorUserId, ct);
        return await ScheduleDtoAsync(doctorUserId, name, ct);
    }

    /// <summary>Replaces the doctor's weekly hours (an empty list clears them). Existing appointments are left alone.</summary>
    public async Task<DoctorScheduleDto> SaveScheduleAsync(Guid doctorUserId, SaveScheduleRequest r, CancellationToken ct)
    {
        EnsureScheduleAccess(await access.CallerAsync(ct), doctorUserId);
        var name = await DoctorNameAsync(doctorUserId, ct);
        var blocks = ValidateBlocks(r);

        db.DoctorSchedules.RemoveRange(await db.DoctorSchedules.Where(s => s.DoctorUserId == doctorUserId).ToListAsync(ct));
        foreach (var b in blocks)
            db.DoctorSchedules.Add(new DoctorSchedule
            {
                TenantId = TenantId, DoctorUserId = doctorUserId, Weekday = b.Weekday, Start = b.Start, End = b.End, SlotMinutes = b.SlotMinutes,
            });
        await db.SaveChangesAsync(ct);
        return await ScheduleDtoAsync(doctorUserId, name, ct);
    }

    private static void EnsureScheduleAccess(CareCaller c, Guid doctorUserId)
    {
        if (!c.IsLeadership && c.UserId != doctorUserId) throw new ForbiddenException();
    }

    private static List<ScheduleBlockDto> ValidateBlocks(SaveScheduleRequest r)
    {
        const string field = "blocks";
        if (r.Blocks is not { } given) throw BadRequestException.Field(field, "Send the weekly hours (an empty list clears them).");
        if (given.Count > MaxBlocks) throw BadRequestException.Field(field, $"Use at most {MaxBlocks} blocks.");
        var blocks = new List<ScheduleBlockDto>();
        foreach (var b in given)
        {
            if (b.Weekday is not { } day || b.Start is not { } start || b.End is not { } end)
                throw BadRequestException.Field(field, "Each block needs a weekday, a start time and an end time.");
            var slot = b.SlotMinutes ?? DefaultSlotMinutes;
            if (slot is < 5 or > 120) throw BadRequestException.Field(field, "A slot is between 5 and 120 minutes.");
            if (end <= start) throw BadRequestException.Field(field, "Each block must end after it starts.");
            if (end.ToTimeSpan() - start.ToTimeSpan() < TimeSpan.FromMinutes(slot))
                throw BadRequestException.Field(field, "Each block must be at least one slot long.");
            blocks.Add(new ScheduleBlockDto(day, start, end, slot));
        }
        foreach (var day in blocks.GroupBy(b => b.Weekday))
        {
            var ordered = day.OrderBy(b => b.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].Start < ordered[i - 1].End) throw BadRequestException.Field(field, $"Blocks on {day.Key} overlap.");
        }
        return blocks.OrderBy(b => b.Weekday).ThenBy(b => b.Start).ToList();
    }

    private async Task<DoctorScheduleDto> ScheduleDtoAsync(Guid doctorUserId, string name, CancellationToken ct)
    {
        var rows = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorUserId == doctorUserId).ToListAsync(ct);
        return new DoctorScheduleDto(doctorUserId, name,
            rows.OrderBy(s => s.Weekday).ThenBy(s => s.Start).Select(s => new ScheduleBlockDto(s.Weekday, s.Start, s.End, s.SlotMinutes)).ToList());
    }

    /// <summary>The name of an active doctor of this clinic; 404 when the user isn't one (another clinic's, a nurse, a disabled member).</summary>
    private async Task<string> DoctorNameAsync(Guid doctorUserId, CancellationToken ct) =>
        await (from p in db.CareStaffProfiles.AsNoTracking()
               join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
               join u in db.Users.AsNoTracking() on p.UserId equals u.Id
               where p.UserId == doctorUserId && p.ClinicalRole == ClinicalRole.Doctor && m.Status == MembershipStatus.Active
               select u.FullName).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Doctor");

    // ---------- Slots ----------

    /// <summary>The doctor's open slots on a Lagos date (front desk and above).</summary>
    public async Task<IReadOnlyList<SlotDto>> SlotsForDeskAsync(Guid doctorUserId, DateOnly? date, CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        if (date is not { } day) throw BadRequestException.Field("date", "Choose a date.");
        return await SlotsAsync(doctorUserId, day, ct);
    }

    /// <summary>
    /// The doctor's open slots on a Lagos date: the schedule's slots for that weekday, minus those that overlap a non-cancelled appointment
    /// and those that have already ended. No role check, so the portal can use it. 404 for a user who isn't one of this clinic's doctors.
    /// </summary>
    public async Task<IReadOnlyList<SlotDto>> SlotsAsync(Guid doctorUserId, DateOnly date, CancellationToken ct)
    {
        await DoctorNameAsync(doctorUserId, ct);
        var slots = await ScheduleSlotsAsync(doctorUserId, date, ct);
        if (slots.Count == 0) return [];
        var from = slots[0].Start;
        var to = slots[^1].End;
        var taken = await db.Appointments.AsNoTracking()
            .Where(a => a.DoctorUserId == doctorUserId && a.Status != AppointmentStatus.Cancelled && a.Start < to && a.End > from)
            .Select(a => new Slot(a.Start, a.End)).ToListAsync(ct);
        var now = clock.GetUtcNow();
        return slots.Where(s => s.End > now && !taken.Any(t => t.Start < s.End && t.End > s.Start)).Select(s => new SlotDto(s.Start, s.End)).ToList();
    }

    /// <summary>Every slot the schedule gives on a Lagos date, in order, as UTC instants (bookings not considered).</summary>
    private async Task<List<Slot>> ScheduleSlotsAsync(Guid doctorUserId, DateOnly date, CancellationToken ct)
    {
        var day = date.DayOfWeek;
        var blocks = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorUserId == doctorUserId && s.Weekday == day).ToListAsync(ct);
        var slots = new List<Slot>();
        foreach (var b in blocks.OrderBy(b => b.Start))
        {
            var step = TimeSpan.FromMinutes(b.SlotMinutes);
            for (var at = b.Start.ToTimeSpan(); at + step <= b.End.ToTimeSpan(); at += step)
                slots.Add(new Slot(LagosInstant(date, at), LagosInstant(date, at + step)));
        }
        return slots;
    }

    // ---------- Appointments ----------

    /// <summary>Appointments, newest slot last. Front desk and above. With no date and no patient: today (Lagos) onwards. At most 500.</summary>
    public async Task<IReadOnlyList<AppointmentDto>> ListAsync(DateOnly? date, Guid? doctorUserId, Guid? patientId, AppointmentStatus? status, CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        IQueryable<Appointment> query = db.Appointments.AsNoTracking();
        if (date is { } day)
        {
            var from = LagosInstant(day, TimeSpan.Zero);
            var to = from.AddDays(1);
            query = query.Where(x => x.Start >= from && x.Start < to);
        }
        else if (patientId is null)
        {
            var from = LagosInstant(CareCalendar.Today(clock.GetUtcNow()), TimeSpan.Zero);
            query = query.Where(x => x.Start >= from);
        }
        if (doctorUserId is { } d) query = query.Where(x => x.DoctorUserId == d);
        if (patientId is { } p) query = query.Where(x => x.PatientId == p);
        if (status is { } st) query = query.Where(x => x.Status == st);
        return (await Dtos(query.OrderBy(x => x.Start).Take(500)).ToListAsync(ct)).OrderBy(dto => dto.Start).ToList();
    }

    /// <summary>
    /// One patient's appointments, soonest first: every one from today (Lagos) on and the last 50 before. No role check: the portal checks
    /// the account's link to the patient first (Task 15).
    /// </summary>
    public async Task<IReadOnlyList<AppointmentDto>> ForPatientAsync(Guid patientId, CancellationToken ct)
    {
        var from = LagosInstant(CareCalendar.Today(clock.GetUtcNow()), TimeSpan.Zero);
        var upcoming = await Dtos(db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId && a.Start >= from).OrderBy(a => a.Start).Take(200)).ToListAsync(ct);
        var past = await Dtos(db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId && a.Start < from).OrderByDescending(a => a.Start).Take(50)).ToListAsync(ct);
        return past.Concat(upcoming).OrderBy(a => a.Start).ToList();
    }

    /// <summary>The front desk's booking: refuses the cashier, then books with source Desk.</summary>
    public async Task<AppointmentDto> BookForDeskAsync(BookRequest r, CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        return await BookAsync(r, AppointmentSource.Desk, ct);
    }

    /// <summary>
    /// Books the doctor's slot. Used by the desk route and, with <see cref="AppointmentSource.Portal"/>, by the portal. No role check.
    /// 400 for missing fields, a time that isn't one of the doctor's slots, or a time that has passed; 404 for another clinic's patient;
    /// 409 for a deceased or inactive patient, or a slot someone else just took (the doctor's advisory lock decides the race).
    /// </summary>
    public async Task<AppointmentDto> BookAsync(BookRequest r, AppointmentSource source, CancellationToken ct)
    {
        if (r.PatientId is not { } patientId) throw BadRequestException.Field("patientId", "Choose a patient.");
        if (r.DoctorUserId is not { } doctorId) throw BadRequestException.Field("doctorUserId", "Choose a doctor.");
        if (r.Start is not { } requested) throw BadRequestException.Field("start", "Choose a time.");
        var reason = CleanReason(r.Reason);
        var start = requested.ToUniversalTime();

        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => new { p.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Patient");
        EnsureBookable(patient.Status);
        await DoctorForBookingAsync(doctorId, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId, doctorId), ct);
        await EnsureAliveUnderLockAsync(patientId, ct);
        var slot = await ClaimSlotAsync(doctorId, start, null, ct);
        var appointment = new Appointment
        {
            TenantId = TenantId, PatientId = patientId, DoctorUserId = doctorId, Start = slot.Start, End = slot.End, Reason = reason,
            Status = AppointmentStatus.Booked, Source = source, BookedByUserId = user.UserId,
        };
        db.Appointments.Add(appointment);
        // The patient's portal users hear the clinic, day and time (never the reason), saved with the booking (Task 14).
        await notices.AppointmentBookedAsync(patientId, slot.Start, ct);
        await SaveAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(appointment.Id, ct);
    }

    /// <summary>Moves a booked appointment to another slot, and optionally another doctor. Front desk and above.</summary>
    public async Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest r, CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        if (r.Start is not { } requested) throw BadRequestException.Field("start", "Choose a time.");
        var reason = r.Reason is null ? null : CleanReason(r.Reason);
        var current = await db.Appointments.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.DoctorUserId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Appointment");
        var doctorId = r.DoctorUserId ?? current.DoctorUserId;
        if (doctorId != current.DoctorUserId) await DoctorForBookingAsync(doctorId, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Both doctors' locks, in a fixed order, so a move can't cross a cancel or a booking on either side.
        foreach (var key in new[] { LockKey(TenantId, current.DoctorUserId), LockKey(TenantId, doctorId) }.Distinct().Order())
            await db.LockAsync(key, ct);
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Appointment");
        if (appointment.Status != AppointmentStatus.Booked) throw new ConflictException("Only a booked appointment can be moved.", "appointment_not_booked");
        await EnsureAliveUnderLockAsync(appointment.PatientId, ct);

        var slot = await ClaimSlotAsync(doctorId, requested.ToUniversalTime(), appointment.Id, ct);
        appointment.DoctorUserId = doctorId;
        // A move to another time gets its own day-before reminder (Task 13).
        var moved = appointment.Start != slot.Start;
        if (moved) appointment.ReminderSentAt = null;
        appointment.Start = slot.Start;
        appointment.End = slot.End;
        if (reason is not null) appointment.Reason = reason;
        // A new time is news to the patient; a change of doctor or reason alone is not (Task 14).
        if (moved) await notices.AppointmentChangedAsync(appointment.PatientId, slot.Start, cancelled: false, ct);
        await SaveAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(appointment.Id, ct);
    }

    /// <summary>Cancels a booked appointment and frees its slot. A deceased patient's appointment can still be cancelled. Front desk and above.</summary>
    public async Task<AppointmentDto> CancelAsync(Guid id, CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        return await CancelCoreAsync(id, null, ct);
    }

    /// <summary>
    /// The portal's cancel (Task 15; the portal has already checked the account's link to <paramref name="patientId"/>): the patient's own
    /// booked appointment only (404 otherwise), and at least <see cref="PortalCancelNotice"/> before it starts, otherwise 409 "This
    /// appointment starts soon. Call the clinic to change it." The time is checked under the doctor's lock.
    /// </summary>
    public Task<AppointmentDto> CancelForPortalAsync(Guid patientId, Guid id, CancellationToken ct) => CancelCoreAsync(id, patientId, ct);

    /// <summary>True when the portal may still cancel an appointment starting at <paramref name="start"/>.</summary>
    public bool PortalCanCancel(AppointmentStatus status, DateTimeOffset start) =>
        status == AppointmentStatus.Booked && start - clock.GetUtcNow() >= PortalCancelNotice;

    private async Task<AppointmentDto> CancelCoreAsync(Guid id, Guid? portalPatientId, CancellationToken ct)
    {
        var current = await db.Appointments.AsNoTracking().Where(a => a.Id == id && (portalPatientId == null || a.PatientId == portalPatientId))
            .Select(a => new { a.DoctorUserId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Appointment");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId, current.DoctorUserId), ct);
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Appointment");
        if (appointment.Status != AppointmentStatus.Booked) throw new ConflictException("Only a booked appointment can be cancelled.", "appointment_not_booked");
        if (portalPatientId is not null && appointment.Start - clock.GetUtcNow() < PortalCancelNotice)
            throw new ConflictException(LateCancel, "appointment_starts_soon");
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = clock.GetUtcNow();
        await notices.AppointmentChangedAsync(appointment.PatientId, appointment.Start, cancelled: true, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(appointment.Id, ct);
    }

    // ---------- Internals ----------

    private static string? CleanReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > ReasonMax) throw BadRequestException.Field("reason", $"Keep the reason under {ReasonMax} characters.");
        return trimmed;
    }

    /// <summary>
    /// M3 (Task 14): inside the doctor's lock, takes the patient's admission lock (<see cref="AdmissionService.PatientLockKey"/>; the order
    /// is always doctor then patient, and the discharge holds only the patient lock, so no cycle) and re-reads the patient's status. A booking
    /// or move racing a Died discharge waits for it to commit, then gets 409, so no "booked" or "changed" notice follows a death.
    /// </summary>
    private async Task EnsureAliveUnderLockAsync(Guid patientId, CancellationToken ct)
    {
        await db.LockAsync(AdmissionService.PatientLockKey(TenantId, patientId), ct);
        var status = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => (PatientStatus?)p.Status).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Patient");
        EnsureBookable(status);
    }

    private static void EnsureBookable(PatientStatus status)
    {
        if (status == PatientStatus.Deceased) throw new ConflictException(PatientDeceased, "patient_deceased");
        if (status == PatientStatus.Inactive) throw new ConflictException(PatientInactive, "patient_inactive");
    }

    /// <summary>The doctor must be one of this clinic's active doctors; anything else is a field error for the booking form.</summary>
    private async Task DoctorForBookingAsync(Guid doctorId, CancellationToken ct)
    {
        try { await DoctorNameAsync(doctorId, ct); }
        catch (NotFoundException) { throw BadRequestException.Field("doctorUserId", "Choose a doctor."); }
    }

    /// <summary>
    /// Inside the doctor's lock: the start must be one of the doctor's slots that hasn't ended, and no other non-cancelled appointment
    /// may overlap it. <paramref name="except"/> is the appointment being moved.
    /// </summary>
    private async Task<Slot> ClaimSlotAsync(Guid doctorId, DateTimeOffset start, Guid? except, CancellationToken ct)
    {
        var slot = (await ScheduleSlotsAsync(doctorId, CareCalendar.Today(start), ct)).FirstOrDefault(s => s.Start == start)
            ?? throw BadRequestException.Field("start", "That time isn't one of the doctor's slots. Choose another.");
        if (slot.End <= clock.GetUtcNow()) throw BadRequestException.Field("start", "That time has passed. Choose a later one.");
        var skip = except ?? Guid.Empty;
        var taken = await db.Appointments.AnyAsync(a => a.DoctorUserId == doctorId && a.Status != AppointmentStatus.Cancelled && a.Id != skip
            && a.Start < slot.End && a.End > slot.Start, ct);
        if (taken) throw new ConflictException(SlotTaken, "slot_taken");
        return slot;
    }

    /// <summary>The unique index on a doctor's live start times backs the lock up: a rejected insert is the same answer.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (dbErrors.IsUniqueViolation(ex)) { throw new ConflictException(SlotTaken, "slot_taken"); }
    }

    private async Task<AppointmentDto> ReadAsync(Guid id, CancellationToken ct) =>
        await Dtos(db.Appointments.AsNoTracking().Where(a => a.Id == id)).FirstAsync(ct);

    /// <summary>Joins the appointments to their patient and doctor names. Demographics only: no clinical field is reachable from here.</summary>
    private IQueryable<AppointmentDto> Dtos(IQueryable<Appointment> appointments) =>
        from a in appointments
        join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
        join u in db.Users.AsNoTracking() on a.DoctorUserId equals u.Id
        select new AppointmentDto(a.Id, p.Id, p.Number, p.FirstName + " " + p.LastName, a.DoctorUserId, u.FullName,
            a.Start, a.End, a.Reason, a.Status, a.Source);
}
