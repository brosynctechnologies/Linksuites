using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>A diagnosis as written in a consultation: free text, an optional code from <see cref="DiagnosisList"/>, and the primary flag.</summary>
public sealed record DiagnosisInput(string? Text, string? Code, bool Primary);

/// <summary>
/// The whole draft (a full replace; autosave sends it all). Blank text fields are stored blank. <see cref="Diagnoses"/> null keeps the
/// consultation's diagnoses; an empty list clears them.
/// </summary>
public sealed record SaveConsultationRequest(
    string? Complaint, string? History, string? Examination, string? Plan, IReadOnlyList<DiagnosisInput>? Diagnoses);

public sealed record AddAddendumRequest(string? Text);

/// <summary>A diagnosis, for clinical staff only.</summary>
public sealed record DiagnosisDto(Guid Id, Guid PatientId, Guid? ConsultationId, Guid? AdmissionId, string Text, string? Code, bool Primary, DateTimeOffset CreatedAt);

/// <summary>An addendum: who added it, when, and its text. For clinical staff only.</summary>
public sealed record AddendumDto(Guid Id, NoteKind NoteKind, Guid NoteId, Guid AuthorUserId, string AuthorName, string Text, DateTimeOffset At);

/// <summary>A consultation note with its diagnoses and addenda (oldest first). For clinical staff only.</summary>
public sealed record ConsultationDto(
    Guid Id, Guid VisitId, Guid PatientId, Guid DoctorUserId, string DoctorName, string? Complaint, string? History, string? Examination,
    string? Plan, NoteStatus Status, DateTimeOffset? SignedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    IReadOnlyList<DiagnosisDto> Diagnoses, IReadOnlyList<AddendumDto> Addenda);

/// <summary>
/// Consultations: the visit's doctor opens one per visit (the visit moves to WithDoctor), writes and saves the draft, and signs it.
/// A signed note is final: corrections are addenda, which any clinical staff member with access to the patient adds. All the text
/// (complaint, history, examination, plan, diagnoses, addenda) is stored through <see cref="ClinicalText"/>.
/// </summary>
public sealed class ConsultationService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, ICurrentUser user, TimeProvider clock)
{
    public const string SignedNote = "This note is signed. Add an addendum instead.";
    public const string AssignedToAnother = "This visit is assigned to another doctor.";
    public const string NoNoteYet = "No note has been written for this visit yet.";
    public const string EmptyNote = "Write the complaint or plan before signing.";
    public const string NoDoctor = "This visit has no doctor yet. Choose one at triage.";
    public const string DoctorsOnly = "Only doctors can write consultation notes.";
    public const string AuthorOnly = "Only the doctor who wrote this note can change it.";
    public const string AddendaAfterSigning = "Addenda are for signed notes. Edit the draft instead.";
    public const int MaxTextLength = 10_000;
    public const int MaxDiagnosisLength = 200;
    public const int MaxDiagnoses = 20;
    public const int MaxAddendumLength = 4_000;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>409 "This note is signed. Add an addendum instead." unless the note is a draft. Ward rounds and discharge summaries use it too.</summary>
    public static void EnsureDraft(NoteStatus status)
    {
        if (status != NoteStatus.Draft) throw new ConflictException(SignedNote, "note_signed");
    }

    /// <summary>
    /// Serialises opening one visit's consultation and re-triage of that visit, so two opens can't create two notes and the visit's
    /// doctor can't change between the doctor check and the draft. Mixed with the clinic, like the other care lock keys.
    /// </summary>
    public static long VisitLockKey(Guid tenantId, Guid visitId) =>
        BitConverter.ToInt64(visitId.ToByteArray(), 8) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x0C0A5017;

    /// <summary>Serialises changes to one note (save, sign, a change of author), so a save can't land after the signature.</summary>
    public static long NoteLockKey(Guid tenantId, Guid noteId) =>
        BitConverter.ToInt64(noteId.ToByteArray(), 8) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x0C0A5106;

    // ---------- Open ----------

    /// <summary>
    /// Opens the visit's consultation. Doctors and the medical director only (a nurse gets 403). Checked under the per-visit lock, which
    /// re-triage takes too:
    /// <list type="bullet">
    /// <item>The visit's doctor (or the director on a visit with no doctor, who then becomes its doctor) gets or keeps a VisitDoctor care
    /// relationship (never on a Done visit), the open goes through <see cref="RecordGuard.OpenAsync"/> (area consultation), a waiting
    /// visit moves to WithDoctor, and the draft is created on the first open with the caller as author. A draft left with another author
    /// is handed to them (audited). A Done visit only reads an existing note (409 "This visit is closed." when there is none).</item>
    /// <item>The director on a visit that has another doctor gets a read view: no relationship, no stage change, no draft (409 "No note has
    /// been written for this visit yet." when there is none).</item>
    /// <item>Any other doctor: 403 "This visit is assigned to another doctor.", or 409 when the visit has no doctor yet.</item>
    /// </list>
    /// </summary>
    public async Task<ConsultationDto> OpenAsync(Guid visitId, string? reason, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var isDirector = caller.MemberRole == MemberRole.Owner;
        if (!isDirector && caller.ClinicalRole != ClinicalRole.Doctor) throw new ForbiddenException(DoctorsOnly);
        if (!await db.Visits.AnyAsync(v => v.Id == visitId, ct)) throw new NotFoundException("Visit");

        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(VisitLockKey(TenantId, visitId), ct);
        // Read under the lock, so a re-triage can't change the doctor between this check and the draft.
        var visit = await db.Visits.AsNoTracking().Where(v => v.Id == visitId)
            .Select(v => new { v.PatientId, v.DoctorUserId, v.Stage }).FirstAsync(ct);
        var existing = await db.Consultations.AsNoTracking().Where(c => c.VisitId == visitId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var writes = visit.DoctorUserId == caller.UserId || (isDirector && visit.DoctorUserId is null);
        if (!writes)
        {
            if (!isDirector)
            {
                if (visit.DoctorUserId is null) throw new ConflictException(NoDoctor, "visit_no_doctor");
                throw new ForbiddenException(AssignedToAnother);
            }
            // The director reads another doctor's visit: no relationship, no stage change, no draft.
            if (existing is not { } readId) throw new ConflictException(NoNoteYet, "no_note_yet");
            await guard.OpenAsync(visit.PatientId, RecordGuard.Areas.Consultation, reason, ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return await ReadAsync(readId, ct);
        }

        if (existing is null && visit.Stage == VisitStage.Done) throw new ConflictException(VisitService.VisitClosed, "visit_closed");
        // The visit's doctor seeing the patient is a care relationship; write it before the open so the guard sees it.
        // A Done visit's relationships have their follow-up window already; opening its note doesn't extend it.
        if (visit.Stage != VisitStage.Done && !await db.CareAssignments.AnyAsync(a => a.VisitId == visitId && a.StaffUserId == caller.UserId
                && a.Kind == CareAssignmentKind.VisitDoctor && (a.EndedAt == null || a.EndedAt > now), ct))
        {
            db.CareAssignments.Add(new CareAssignment
            {
                TenantId = TenantId, PatientId = visit.PatientId, StaffUserId = caller.UserId, Kind = CareAssignmentKind.VisitDoctor, VisitId = visitId,
            });
            await db.SaveChangesAsync(ct);
        }
        await guard.OpenAsync(visit.PatientId, RecordGuard.Areas.Consultation, reason, ct);

        var id = existing ?? Guid.Empty;
        if (visit.Stage != VisitStage.Done)
        {
            var tracked = await db.Visits.FirstAsync(v => v.Id == visitId, ct);
            if (tracked.Stage is VisitStage.WaitingVitals or VisitStage.WaitingDoctor) tracked.Stage = VisitStage.WithDoctor;
            tracked.DoctorUserId ??= caller.UserId;
            if (existing is null)
            {
                var consultation = new Consultation
                {
                    TenantId = TenantId, VisitId = visitId, PatientId = visit.PatientId, DoctorUserId = caller.UserId, Status = NoteStatus.Draft,
                };
                db.Consultations.Add(consultation);
                id = consultation.Id;
            }
            else await HandDraftToAsync(db, TenantId, visitId, caller.UserId, caller.UserId, user.IpAddress, now, ct);
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(id, ct);
    }

    /// <summary>
    /// The draft always belongs to the visit's current doctor: when that changes (re-triage, or a draft left with another author), an
    /// unsigned draft's author moves to <paramref name="doctorId"/> under the note lock, with an audit row (ids only, no clinical text).
    /// A signed note keeps its author. The caller holds the visit lock (taken first) and saves.
    /// </summary>
    internal static async Task HandDraftToAsync(
        IAppDbContext db, Guid tenantId, Guid visitId, Guid doctorId, Guid? actorId, string? ip, DateTimeOffset now, CancellationToken ct)
    {
        var noteId = await db.Consultations.AsNoTracking()
            .Where(c => c.VisitId == visitId && c.Status == NoteStatus.Draft && c.DoctorUserId != doctorId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (noteId is not { } id) return;
        await db.LockAsync(NoteLockKey(tenantId, id), ct);
        var note = await db.Consultations.FirstAsync(c => c.Id == id, ct); // read after the lock: a save or sign may have just committed
        if (note.Status != NoteStatus.Draft || note.DoctorUserId == doctorId) return;
        var from = note.DoctorUserId;
        note.DoctorUserId = doctorId;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId, ActorId = actorId, Action = "care.note_author_changed", EntityType = nameof(Consultation), EntityId = id.ToString(),
            BeforeJson = JsonSerializer.Serialize(new { doctorUserId = from }),
            AfterJson = JsonSerializer.Serialize(new { doctorUserId = doctorId, visitId, noteKind = nameof(NoteKind.Consultation) }),
            Ip = ip, CreatedAt = now,
        });
    }

    // ---------- Draft ----------

    /// <summary>
    /// Saves the draft (a full replace of the text, and of the diagnoses when given). Doctors and the director only, checked before the
    /// note is looked up (403 "Only doctors can write consultation notes.", Task 16); a signed note is 409
    /// "This note is signed. Add an addendum instead."; anyone but the author gets 403. Answers with the caller's own note (a write, not
    /// logged as an open, like saving clinical facts).
    /// </summary>
    public async Task<ConsultationDto> SaveAsync(Guid id, SaveConsultationRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly); // Task 16: the role before the record (a nurse gets 403 for any id)
        var complaint = Field("complaint", r.Complaint);
        var history = Field("history", r.History);
        var examination = Field("examination", r.Examination);
        var plan = Field("plan", r.Plan);
        var diagnoses = r.Diagnoses is null ? null : ValidateDiagnoses(r.Diagnoses);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(NoteLockKey(TenantId, id), ct);
        var consultation = await db.Consultations.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Consultation");
        EnsureDraft(consultation.Status);
        await EnsureAuthorAsync(consultation, caller, ct);

        consultation.Complaint = text.Protect(complaint);
        consultation.History = text.Protect(history);
        consultation.Examination = text.Protect(examination);
        consultation.Plan = text.Protect(plan);
        consultation.UpdatedAt = clock.GetUtcNow(); // a draft whose text didn't change still counts as saved
        if (diagnoses is not null)
        {
            db.Diagnoses.RemoveRange(await db.Diagnoses.Where(d => d.ConsultationId == id).ToListAsync(ct));
            foreach (var d in diagnoses)
                db.Diagnoses.Add(new Diagnosis
                {
                    TenantId = TenantId, PatientId = consultation.PatientId, ConsultationId = id, Text = text.Protect(d.Text), Code = d.Code, Primary = d.Primary,
                });
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(id, ct);
    }

    /// <summary>
    /// Signs the draft: it is final from now on (409 on any later change; corrections are addenda). The author only. Writes an audit row
    /// with no clinical text.
    /// </summary>
    public async Task<ConsultationDto> SignAsync(Guid id, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly); // Task 16: the role before the record (a nurse gets 403 for any id)
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(NoteLockKey(TenantId, id), ct);
        var consultation = await db.Consultations.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Consultation");
        EnsureDraft(consultation.Status);
        await EnsureAuthorAsync(consultation, caller, ct);
        if (string.IsNullOrEmpty(consultation.Complaint) && string.IsNullOrEmpty(consultation.Plan))
            throw new ConflictException(EmptyNote, "note_empty");

        var now = clock.GetUtcNow();
        consultation.Status = NoteStatus.Signed;
        consultation.SignedAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.note_signed", EntityType = nameof(Consultation), EntityId = id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { noteKind = nameof(NoteKind.Consultation), visitId = consultation.VisitId, patientId = consultation.PatientId }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(id, ct);
    }

    /// <summary>
    /// 403 "Only the doctor who wrote this note can change it." unless the caller is both the draft's author and the visit's current
    /// doctor. Runs under the note lock, which a change of visit doctor also takes, so the two always agree.
    /// </summary>
    private async Task EnsureAuthorAsync(Consultation consultation, CareCaller caller, CancellationToken ct)
    {
        var visitDoctor = await db.Visits.AsNoTracking().Where(v => v.Id == consultation.VisitId).Select(v => v.DoctorUserId).FirstOrDefaultAsync(ct);
        if (consultation.DoctorUserId != caller.UserId || visitDoctor != caller.UserId) throw new ForbiddenException(AuthorOnly);
    }

    // ---------- Addenda ----------

    /// <summary>The route's note kind: consultation, ward-round or discharge (case-insensitive; the enum names work too). Null otherwise.</summary>
    public static NoteKind? ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "consultation" => NoteKind.Consultation,
        "ward-round" or "wardround" => NoteKind.WardRound,
        "discharge" => NoteKind.Discharge,
        _ => null,
    };

    /// <summary>
    /// Adds an addendum to a signed note: any clinical staff member, through <see cref="RecordGuard.OpenAsync"/> (an unrelated caller
    /// needs the <c>X-Care-Reason</c> header). 400 <c>text</c>; 404 for an unknown kind or note; 409 for a draft. Ward rounds (Task 9) and
    /// discharge summaries (Task 10) plug into <see cref="NoteAsync"/>.
    /// </summary>
    public async Task<AddendumDto> AddAddendumAsync(string kind, Guid noteId, AddAddendumRequest r, string? reason, CancellationToken ct)
    {
        var noteKind = ParseKind(kind) ?? throw new NotFoundException("Note");
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var body = r.Text?.Trim();
        if (string.IsNullOrEmpty(body)) throw BadRequestException.Field("text", "Write the addendum.");
        if (body.Length > MaxAddendumLength) throw BadRequestException.Field("text", $"An addendum can have at most {MaxAddendumLength:N0} characters.");

        var note = await NoteAsync(noteKind, noteId, ct) ?? throw new NotFoundException("Note");
        await guard.OpenAsync(note.PatientId, note.Area, reason, ct);
        if (note.Status != NoteStatus.Signed) throw new ConflictException(AddendaAfterSigning, "note_draft");

        // Postgres keeps microseconds: truncate, so the time in this answer is the time the record shows later.
        var at = clock.GetUtcNow();
        at = at.AddTicks(-(at.Ticks % 10));
        var addendum = new Addendum
        {
            TenantId = TenantId, NoteKind = noteKind, NoteId = noteId, AuthorUserId = caller.UserId, Text = text.Protect(body), At = at,
        };
        db.Addenda.Add(addendum);
        await db.SaveChangesAsync(ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == caller.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return new AddendumDto(addendum.Id, noteKind, noteId, caller.UserId, name ?? "", body, addendum.At);
    }

    /// <summary>The note an addendum attaches to: its patient, status and the access-log area. Null when there is no such note (yet).</summary>
    private async Task<(Guid PatientId, NoteStatus Status, string Area)?> NoteAsync(NoteKind kind, Guid noteId, CancellationToken ct)
    {
        switch (kind)
        {
            case NoteKind.Consultation:
                var c = await db.Consultations.AsNoTracking().Where(x => x.Id == noteId).Select(x => new { x.PatientId, x.Status }).FirstOrDefaultAsync(ct);
                return c is null ? null : (c.PatientId, c.Status, RecordGuard.Areas.Consultation);
            case NoteKind.WardRound:
                // An addendum is the one write a discharged admission still takes (ruling), so the admission's status isn't checked here.
                var w = await db.WardRounds.AsNoTracking().Where(x => x.Id == noteId).Select(x => new { x.PatientId, x.Status }).FirstOrDefaultAsync(ct);
                return w is null ? null : (w.PatientId, w.Status, RecordGuard.Areas.Admission);
            case NoteKind.Discharge:
                // Task 10: a signed discharge summary takes addenda after discharge too (area discharge).
                var s = await db.DischargeSummaries.AsNoTracking().Where(x => x.Id == noteId).Select(x => new { x.PatientId, x.Status }).FirstOrDefaultAsync(ct);
                return s is null ? null : (s.PatientId, s.Status, RecordGuard.Areas.Discharge);
            default:
                return null;
        }
    }

    // ---------- Codes ----------

    /// <summary>The built-in diagnosis list, searched by name or code. Clinical staff only (a clinical tool; no patient data, not logged).</summary>
    public async Task<IReadOnlyList<DiagnosisCode>> CodesAsync(string? q, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        return DiagnosisList.Search(q);
    }

    // ---------- Reads ----------

    /// <summary>One consultation with its diagnoses and addenda, decrypted. No access check: callers open the record first.</summary>
    private async Task<ConsultationDto> ReadAsync(Guid id, CancellationToken ct)
    {
        var list = await ReadManyAsync(db, text, db.Consultations.AsNoTracking().Where(c => c.Id == id), ct);
        return list.FirstOrDefault() ?? throw new NotFoundException("Consultation");
    }

    /// <summary>
    /// Consultations as DTOs, newest first, each with its diagnoses (primary first) and addenda (oldest first), decrypted. No access check:
    /// the caller has opened the record. Used by the record timeline too.
    /// </summary>
    internal static async Task<List<ConsultationDto>> ReadManyAsync(IAppDbContext db, ClinicalText text, IQueryable<Consultation> query, CancellationToken ct)
    {
        var consultations = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync(ct);
        if (consultations.Count == 0) return [];
        var ids = consultations.Select(c => c.Id).ToList();
        var diagnoses = await db.Diagnoses.AsNoTracking().Where(d => d.ConsultationId != null && ids.Contains(d.ConsultationId.Value)).ToListAsync(ct);
        var addenda = await db.Addenda.AsNoTracking().Where(a => a.NoteKind == NoteKind.Consultation && ids.Contains(a.NoteId))
            .OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        var names = await NamesAsync(db, consultations.Select(c => c.DoctorUserId).Concat(addenda.Select(a => a.AuthorUserId)), ct);
        return consultations.Select(c => new ConsultationDto(
                c.Id, c.VisitId, c.PatientId, c.DoctorUserId, names.GetValueOrDefault(c.DoctorUserId, ""), text.Read(c.Complaint), text.Read(c.History),
                text.Read(c.Examination), text.Read(c.Plan), c.Status, c.SignedAt, c.CreatedAt, c.UpdatedAt,
                diagnoses.Where(d => d.ConsultationId == c.Id).OrderByDescending(d => d.Primary).ThenBy(d => d.CreatedAt).ThenBy(d => d.Id)
                    .Select(d => ToDto(d, text)).ToList(),
                addenda.Where(a => a.NoteId == c.Id).Select(a => ToDto(a, names, text)).ToList()))
            .ToList();
    }

    internal static DiagnosisDto ToDto(Diagnosis d, ClinicalText text) =>
        new(d.Id, d.PatientId, d.ConsultationId, d.AdmissionId, text.Read(d.Text) ?? "", d.Code, d.Primary, d.CreatedAt);

    internal static AddendumDto ToDto(Addendum a, IReadOnlyDictionary<Guid, string> names, ClinicalText text) =>
        new(a.Id, a.NoteKind, a.NoteId, a.AuthorUserId, names.GetValueOrDefault(a.AuthorUserId, ""), text.Read(a.Text) ?? "", a.At);

    internal static async Task<Dictionary<Guid, string>> NamesAsync(IAppDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    // ---------- Validation ----------

    private static string? Field(string field, string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (v is { Length: > MaxTextLength }) throw BadRequestException.Field(field, $"This can have at most {MaxTextLength:N0} characters.");
        return v;
    }

    /// <summary>
    /// Each diagnosis needs text or a code from the list (a code alone takes the list's name); at most one is primary, and when none is
    /// marked the first one is. Fields <c>diagnoses</c>, <c>diagnoses[i].text</c> and <c>diagnoses[i].code</c>.
    /// </summary>
    private static List<(string Text, string? Code, bool Primary)> ValidateDiagnoses(IReadOnlyList<DiagnosisInput> input)
    {
        if (input.Count > MaxDiagnoses) throw BadRequestException.Field("diagnoses", $"A note can have at most {MaxDiagnoses} diagnoses.");
        if (input.Count(d => d is { Primary: true }) > 1) throw BadRequestException.Field("diagnoses", "Mark only one primary diagnosis.");
        var result = new List<(string Text, string? Code, bool Primary)>();
        for (var i = 0; i < input.Count; i++)
        {
            var d = input[i] ?? throw BadRequestException.Field($"diagnoses[{i}].text", "Write the diagnosis or choose a code.");
            DiagnosisCode? coded = null;
            if (!string.IsNullOrWhiteSpace(d.Code))
                coded = DiagnosisList.Find(d.Code) ?? throw BadRequestException.Field($"diagnoses[{i}].code", "Choose a code from the list.");
            var t = string.IsNullOrWhiteSpace(d.Text) ? coded?.Name : d.Text.Trim();
            if (t is null) throw BadRequestException.Field($"diagnoses[{i}].text", "Write the diagnosis or choose a code.");
            if (t.Length > MaxDiagnosisLength) throw BadRequestException.Field($"diagnoses[{i}].text", $"A diagnosis can have at most {MaxDiagnosisLength} characters.");
            result.Add((t, coded?.Code, d.Primary));
        }
        if (result.Count > 0 && !result.Any(d => d.Primary)) result[0] = result[0] with { Primary = true };
        return result;
    }
}
