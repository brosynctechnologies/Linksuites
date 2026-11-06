using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// One entry of a patient's access log, for leadership. A portal read (final review C1) has <see cref="Portal"/> set, the account's short
/// name ("Nkem W.") as <see cref="StaffName"/>, its relationship ("parent") as <see cref="Relationship"/> and <see cref="Role"/>, and
/// <see cref="Who"/> "Nkem W. (parent), patient portal". A staff entry's <see cref="Who"/> is "{name}, {role}".
/// </summary>
public sealed record RecordAccessDto(
    Guid Id, Guid StaffUserId, string StaffName, string Role, string Area, string? Reason, bool Emergency, DateTimeOffset At, string AreaLabel,
    bool Portal, string? Relationship, string Who);

/// <summary>
/// The clinical record guard. Every staff route that opens clinical data calls <see cref="OpenAsync"/> first: it requires clinical access,
/// requires a reason when the caller has no care relationship with the patient (emergency access), writes the <see cref="RecordAccess"/>
/// row and, on emergency access, tells the medical director in-app and writes an audit row. Nothing clinical is ever stored here.
/// </summary>
public sealed class RecordGuard(
    IAppDbContext db, CareAccess access, CareNotices notices, ClinicalText text, ITenantContext tenant, ICurrentUser user, TimeProvider clock)
{
    public const string ReasonRequired = "Give a reason to open this patient's record.";
    public const string EmergencyNotice = CareNotices.EmergencyAccessText;
    public const int ReasonMaxLength = 300;

    /// <summary>
    /// The request header that carries the emergency-access reason (never the query string, so it stays out of URLs and request logs).
    /// The value is UTF-8, percent-encoded (encodeURIComponent); <see cref="OpenAsync"/> decodes it.
    /// </summary>
    public const string ReasonHeader = "X-Care-Reason";

    /// <summary>What a clinical read opened, as stored in <see cref="RecordAccess.Area"/>.</summary>
    public static class Areas
    {
        public const string Record = "record";
        public const string Consultation = "consultation";
        public const string Admission = "admission";
        public const string Prescription = "prescription";
        public const string Result = "result";
        public const string Discharge = "discharge";
        public const string Pdf = "pdf";
        public const string File = "file";
        /// <summary>Not an open: triage gave the caller a new care relationship with the patient (written by <c>VisitService</c>).</summary>
        public const string Triage = "triage";
        /// <summary>Task 9: an admission's nursing notes.</summary>
        public const string NursingNotes = "nursing";
        /// <summary>Task 9: an admission's fluid chart.</summary>
        public const string Fluids = "fluids";
        /// <summary>Task 9: an admission's ward rounds (an addendum to one logs <see cref="Admission"/>, as ruled in Task 6).</summary>
        public const string WardRounds = "ward-round";
        /// <summary>Task 9: an admission's medication chart.</summary>
        public const string MedicationChart = "medication";
        /// <summary>Final review C1: a portal account read the patient's bills (their lines name drugs and tests). Written only by the portal.</summary>
        public const string Bills = "bills";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
            { Record, Consultation, Admission, Prescription, Result, Discharge, Pdf, File, Triage, NursingNotes, Fluids, WardRounds, MedicationChart, Bills };

        /// <summary>
        /// What each area reads as on the director's access log (Task 16). Every area has one; the portal has its own patient-facing
        /// wording (<see cref="PortalService.AreaLabels"/>).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Record] = "Patient record",
            [Consultation] = "Consultation",
            [Admission] = "Admission",
            [Prescription] = "Prescriptions",
            [Result] = "Test results",
            [Discharge] = "Discharge summary",
            [Pdf] = "Printed document",
            [File] = "Result file",
            [Triage] = "Triage (joined the patient's care)",
            [NursingNotes] = "Nursing notes",
            [Fluids] = "Fluid chart",
            [WardRounds] = "Ward rounds",
            [MedicationChart] = "Medication chart",
            [Bills] = "Bills",
        };

        /// <summary>The access-log label for <paramref name="area"/>; an unknown area (none is written) shows as the patient record.</summary>
        public static string Label(string area) => Labels.GetValueOrDefault(area, Labels[Record]);
    }

    /// <summary>
    /// Opens <paramref name="patientId"/>'s clinical record for the signed-in staff member and logs it. Throws 403 "Only doctors and nurses
    /// can see clinical notes." without clinical access, 404 for a patient that isn't this clinic's, 409 <c>emergency_reason_required</c>
    /// when there is no care relationship and no reason, and 400 <c>reason</c> for an emergency reason over 300 characters.
    /// <paramref name="reason"/> is the raw <see cref="ReasonHeader"/> value (percent-encoded UTF-8; decoded here) and is ignored when the
    /// caller has a care relationship. Saves immediately, so the open is logged even if the read that follows fails. Returns the caller.
    /// </summary>
    public Task<CareCaller> OpenAsync(Guid patientId, string area, string? reason, CancellationToken ct) =>
        OpenAsync(patientId, area, reason, emergencyUnlessDirector: false, ct);

    /// <summary>
    /// As <see cref="OpenAsync(Guid, string, string?, CancellationToken)"/>. With <paramref name="emergencyUnlessDirector"/>, any caller but
    /// the medical director goes through the emergency path (reason required, Emergency=true, the director's notice) even with a care
    /// relationship: used where the action itself would create a relationship (admitting someone else's patient).
    /// </summary>
    public async Task<CareCaller> OpenAsync(Guid patientId, string area, string? reason, bool emergencyUnlessDirector, CancellationToken ct)
    {
        if (!Areas.All.Contains(area)) throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown record area.");
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient");

        // The medical director oversees every patient (and is the one told about emergency access); everyone else needs an assignment
        // that hasn't ended. A visit's assignments end 30 days after the visit is Done (EndedAt is then set in the future).
        var now = clock.GetUtcNow();
        var related = caller.MemberRole == MemberRole.Owner
            || (!emergencyUnlessDirector && await HasRelationshipAsync(db, patientId, caller.UserId, now, ct));
        if (related)
        {
            // A reason sent with a related open is ignored (and so not checked).
            db.RecordAccesses.Add(new RecordAccess
            {
                StaffUserId = caller.UserId, Role = RoleLabel(caller), PatientId = patientId, Area = area, Emergency = false, At = now,
            });
            await db.SaveChangesAsync(ct);
            return caller;
        }

        reason = DecodeReason(reason);
        if (reason is null) throw new ConflictException(ReasonRequired, "emergency_reason_required");
        if (reason.Length > ReasonMaxLength)
            throw BadRequestException.Field("reason", $"The reason can have at most {ReasonMaxLength} characters.");

        // Emergency: every open is logged and audited; the director is told at most once per staff member, patient and Lagos day.
        // The advisory lock serialises concurrent first opens so only one of them sees "no notice yet today".
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(NoticeLockKey(caller.UserId, patientId), ct);
        var dayStart = new DateTimeOffset(CareCalendar.Today(now).ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);
        var noticedToday = await db.RecordAccesses.AnyAsync(a => a.StaffUserId == caller.UserId && a.PatientId == patientId && a.Emergency
            && a.At >= dayStart && a.At < dayEnd, ct);

        var entry = new RecordAccess
        {
            StaffUserId = caller.UserId, Role = RoleLabel(caller), PatientId = patientId, Area = area,
            Reason = text.Protect(reason), Emergency = true, At = now,
        };
        db.RecordAccesses.Add(entry);
        // In-app and by email (Task 14): the fixed sentence and the access-log link, never the reason or the area.
        if (!noticedToday) await notices.EmergencyAccessAsync(patientId, ct);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.TenantId, ActorId = caller.UserId, Action = "care.emergency_access", EntityType = nameof(Patient),
            EntityId = patientId.ToString(), AfterJson = JsonSerializer.Serialize(new { area, recordAccessId = entry.Id, role = entry.Role }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return caller;
    }

    /// <summary>True when the staff member has a care assignment for the patient that hasn't ended at <paramref name="now"/>.</summary>
    public static Task<bool> HasRelationshipAsync(IAppDbContext db, Guid patientId, Guid staffUserId, DateTimeOffset now, CancellationToken ct) =>
        db.CareAssignments.AnyAsync(a => a.PatientId == patientId && a.StaffUserId == staffUserId && (a.EndedAt == null || a.EndedAt > now), ct);

    /// <summary>The <see cref="ReasonHeader"/> value, percent-decoded and trimmed; null when blank.</summary>
    public static string? DecodeReason(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var decoded = Uri.UnescapeDataString(header).Trim();
        return decoded.Length == 0 ? null : decoded;
    }

    private static long NoticeLockKey(Guid staffUserId, Guid patientId) =>
        BitConverter.ToInt64(staffUserId.ToByteArray(), 8) ^ BitConverter.ToInt64(patientId.ToByteArray(), 8) ^ 0x0CA4E3E6;

    /// <summary>
    /// The patient's access log, newest first, with emergency reasons decrypted. Only the medical director (Owner) reads it, and never
    /// through support's view-as session. At most 500 entries.
    /// </summary>
    public async Task<IReadOnlyList<RecordAccessDto>> LogAsync(Guid patientId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (caller.MemberRole != MemberRole.Owner || caller.IsSupportView) throw new ForbiddenException();
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient");
        var rows = await db.RecordAccesses.AsNoTracking().Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(500)
            .Select(a => new
            {
                a.Id, a.StaffUserId, StaffName = db.Users.Where(u => u.Id == a.StaffUserId).Select(u => u.FullName).FirstOrDefault(),
                a.Role, a.Area, a.Reason, a.Emergency, a.At, a.Portal,
            })
            .ToListAsync(ct);
        return rows.Select(a =>
            {
                var name = a.Portal ? PortalName(a.StaffName) : a.StaffName ?? "";
                return new RecordAccessDto(a.Id, a.StaffUserId, name, a.Role, a.Area, text.Read(a.Reason), a.Emergency, a.At, Areas.Label(a.Area),
                    a.Portal, a.Portal ? a.Role : null, Who(name, a.Role, a.Portal));
            })
            .ToList();
    }

    /// <summary>
    /// Final review C1: logs a portal account's read of the patient's clinical data (the record, or the bills with their item names): the
    /// account as the reader, its relationship to the patient as the role, marked as a portal read. Never emergency, never a reason. The
    /// caller has passed <see cref="PatientPortalAccess.RequirePatientAsync"/>, so the tenant is the patient's clinic. Saves.
    /// </summary>
    public static async Task LogPortalReadAsync(IAppDbContext db, PortalPatientRef patient, Guid accountId, string area, DateTimeOffset at, CancellationToken ct)
    {
        if (!Areas.All.Contains(area)) throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown record area.");
        db.RecordAccesses.Add(new RecordAccess
        {
            TenantId = patient.ClinicId, StaffUserId = accountId, Role = patient.Relationship.ToString().ToLowerInvariant(), PatientId = patient.Id,
            Area = area, Emergency = false, Portal = true, At = at,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A portal account's name as both logs show it: the first name and the last name's initial ("Nkem Williams" → "Nkem W.").</summary>
    public static string PortalName(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "A portal account",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    /// <summary>"Nkem W. (parent), patient portal" for a portal read; "Ada Obi, Doctor" for staff.</summary>
    public static string Who(string name, string role, bool portal) => portal ? $"{name} ({role}), patient portal" : $"{name}, {role}";

    /// <summary>The clinic role label stored on the log: "Medical director", "Doctor" or "Nurse".</summary>
    public static string RoleLabel(CareCaller c) =>
        c.MemberRole == MemberRole.Owner ? "Medical director" : c.ClinicalRole.ToString();
}
