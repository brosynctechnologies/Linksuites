using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

public sealed record NursingNoteRequest(string? Note);

public sealed record FluidRequest(FluidDirection? Direction, FluidKind? Kind, int? Ml);

public sealed record WardRoundRequest(string? Note);

public sealed record RecordDoseRequest(DoseStatus? Status, string? Note);

/// <summary>A nursing note, for clinical staff only.</summary>
public sealed record NursingNoteDto(Guid Id, Guid AdmissionId, Guid AuthorUserId, string AuthorName, string Note, DateTimeOffset At);

/// <summary>One fluid-chart line, for clinical staff only.</summary>
public sealed record FluidEntryDto(Guid Id, Guid AdmissionId, FluidDirection Direction, FluidKind Kind, int Ml, DateTimeOffset At, Guid RecordedByUserId, string RecordedByName);

/// <summary>One Lagos day of the fluid chart: its entries (oldest first) and the day's in and out totals in millilitres.</summary>
public sealed record FluidChartDto(Guid AdmissionId, DateOnly Date, IReadOnlyList<FluidEntryDto> Entries, int InMl, int OutMl, int BalanceMl);

/// <summary>A ward-round note with its addenda (oldest first), for clinical staff only.</summary>
public sealed record WardRoundDto(
    Guid Id, Guid AdmissionId, Guid PatientId, Guid DoctorUserId, string DoctorName, string? Note, NoteStatus Status, DateTimeOffset? SignedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, IReadOnlyList<AddendumDto> Addenda);

/// <summary>A scheduled dose with its drug (decrypted), for clinical staff only.</summary>
public sealed record MedicationDoseDto(
    Guid Id, Guid PrescriptionId, Guid PrescriptionItemId, string Drug, string? Strength, string Dose, DoseFrequency Frequency, string? Route,
    string? Instructions, DateTimeOffset DueAt, DoseStatus Status, Guid? RecordedByUserId, string? RecordedByName, DateTimeOffset? RecordedAt, string? Note);

/// <summary>One Lagos day of the medication chart: the doses due that day (by due time) and the admission's as-needed (PRN) drugs.</summary>
public sealed record MedicationChartDto(Guid AdmissionId, DateOnly Date, IReadOnlyList<MedicationDoseDto> Doses, IReadOnlyList<PrescriptionItemDto> AsNeeded);

/// <summary>
/// One admission drug's medication chart in counts (the record timeline, Task 10): doses due, given, held, refused, missed and stopped,
/// and the last time it was given. For clinical staff only.
/// </summary>
public sealed record MedicationSummaryDto(
    Guid AdmissionId, Guid PrescriptionId, Guid PrescriptionItemId, string Drug, string Dose, DoseFrequency Frequency, int Due, int Given, int Held,
    int Refused, int Missed, int Stopped, DateTimeOffset? LastGivenAt);

/// <summary>A recorded dose: the caller's own entry echoed back (a write, not a record open).</summary>
public sealed record DoseRecordedDto(Guid Id, DoseStatus Status, Guid RecordedByUserId, DateTimeOffset RecordedAt, string? Note);

/// <summary>
/// Ward care on an admission: nursing notes, the fluid chart, ward rounds and the medication chart.
/// <list type="bullet">
/// <item><b>Writes</b> (nursing notes, fluids and doses: nurses, doctors and the director; ward rounds: doctors and the director) need
/// clinical access, then an admitted patient (409 "This patient has been discharged." on every write but an addendum), then a live care
/// relationship with this admission (the ward's nurses, the admitting doctor), else 403 "This patient isn't on your ward."; the director
/// is exempt. Writes take the per-patient admission lock (<see cref="AdmissionService.PatientLockKey"/>), so a discharge can't land
/// between the check and the write. They are not record opens: the answer echoes what the caller wrote.</item>
/// <item><b>Reads</b> are clinical opens through <see cref="RecordGuard.OpenAsync"/> (emergency access with the X-Care-Reason header).</item>
/// <item>Ward rounds sign as consultations do (Task 6): the draft belongs to its author, a blank note can't be signed, signing is final
/// and corrections are addenda (<c>/notes/ward-round/{id}/addenda</c>).</item>
/// <item>Free text (nursing notes, ward-round notes, dose notes) is stored through <see cref="ClinicalText"/>.</item>
/// </list>
/// </summary>
public sealed class WardCareService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, ICurrentUser user, TimeProvider clock)
{
    public const string NotOnWard = "This patient isn't on your ward.";
    public const string DoseRecorded = "This dose is already recorded.";
    public const string DoseStopped = "This dose was stopped.";
    public const string DoctorsOnly = "Only doctors can write ward rounds.";
    public const string EmptyRound = "Write the ward-round note before signing.";
    public const int MaxNursingNoteLength = 4_000;
    public const int MaxRoundLength = 10_000;
    public const int MaxDoseNoteLength = 500;
    public const int MaxMl = 10_000;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Nursing notes ----------

    /// <summary>Adds a nursing note. 400 <c>note</c> (blank or over 4,000 characters); 404; 409 discharged; 403 not on the ward.</summary>
    public async Task<NursingNoteDto> AddNursingNoteAsync(Guid admissionId, NursingNoteRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var body = CareOrders.Required("note", r.Note, MaxNursingNoteLength, "Write the note.");
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForWriteAsync(caller, admissionId, now, ct);
        var note = new NursingNote { TenantId = TenantId, AdmissionId = admissionId, PatientId = patientId, AuthorUserId = caller.UserId, Text = text.Protect(body), At = now };
        db.NursingNotes.Add(note);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        var names = await ConsultationService.NamesAsync(db, [caller.UserId], ct);
        return new NursingNoteDto(note.Id, admissionId, caller.UserId, names.GetValueOrDefault(caller.UserId, ""), body, now);
    }

    /// <summary>The admission's nursing notes, newest first (at most 500). A clinical open (area nursing).</summary>
    public async Task<IReadOnlyList<NursingNoteDto>> NursingNotesAsync(Guid admissionId, string? reason, CancellationToken ct)
    {
        await OpenAsync(admissionId, RecordGuard.Areas.NursingNotes, reason, ct);
        return await ReadNursingNotesAsync(db, text, db.NursingNotes.AsNoTracking().Where(n => n.AdmissionId == admissionId), ct);
    }

    /// <summary>Nursing notes as DTOs, newest first, at most 500, decrypted. No access check: the caller has opened the record (the timeline uses it too).</summary>
    internal static async Task<List<NursingNoteDto>> ReadNursingNotesAsync(IAppDbContext db, ClinicalText text, IQueryable<NursingNote> query, CancellationToken ct)
    {
        var rows = await query.OrderByDescending(n => n.At).ThenByDescending(n => n.Id).Take(500).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(n => n.AuthorUserId), ct);
        return rows.Select(n => new NursingNoteDto(n.Id, n.AdmissionId, n.AuthorUserId, names.GetValueOrDefault(n.AuthorUserId, ""), text.Read(n.Text) ?? "", n.At))
            .ToList();
    }

    /// <summary>
    /// The medication chart summarised per admission drug (Task 10, for the record timeline): how many doses are due, given, held, refused,
    /// missed and stopped, and when it was last given. Drug and dose decrypted. No access check: the caller has opened the record.
    /// </summary>
    internal static async Task<List<MedicationSummaryDto>> MedicationSummaryAsync(IAppDbContext db, ClinicalText text, Guid patientId, CancellationToken ct)
    {
        var counts = await db.MedicationDoses.AsNoTracking().Where(d => d.PatientId == patientId)
            .GroupBy(d => new { d.AdmissionId, d.PrescriptionItemId, d.Status })
            .Select(g => new { g.Key.AdmissionId, g.Key.PrescriptionItemId, g.Key.Status, Count = g.Count(), LastAt = g.Max(d => d.RecordedAt) })
            .ToListAsync(ct);
        if (counts.Count == 0) return [];
        var itemIds = counts.Select(c => c.PrescriptionItemId).Distinct().ToList();
        var items = await (from i in db.PrescriptionItems.AsNoTracking()
                           join p in db.Prescriptions.AsNoTracking() on i.PrescriptionId equals p.Id
                           where itemIds.Contains(i.Id)
                           select new { Item = i, p.CreatedAt }).ToDictionaryAsync(x => x.Item.Id, ct);
        return counts.GroupBy(c => new { c.AdmissionId, c.PrescriptionItemId })
            .Select(g =>
            {
                var item = items[g.Key.PrescriptionItemId].Item;
                int N(DoseStatus s) => g.Where(c => c.Status == s).Sum(c => c.Count);
                return new
                {
                    items[g.Key.PrescriptionItemId].CreatedAt,
                    Dto = new MedicationSummaryDto(g.Key.AdmissionId, item.PrescriptionId, item.Id, text.Read(item.Drug) ?? "", text.Read(item.Dose) ?? "",
                        item.Frequency, N(DoseStatus.Due), N(DoseStatus.Given), N(DoseStatus.Held), N(DoseStatus.Refused), N(DoseStatus.Missed),
                        N(DoseStatus.Stopped), g.Where(c => c.Status == DoseStatus.Given).Max(c => c.LastAt)),
                    item.Position,
                };
            })
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Position).Select(x => x.Dto).ToList();
    }

    // ---------- Fluid chart ----------

    /// <summary>Adds a fluid entry now. 400 <c>direction</c>, <c>kind</c>, <c>ml</c> (1 to 10,000); 404; 409 discharged; 403 not on the ward.</summary>
    public async Task<FluidEntryDto> AddFluidAsync(Guid admissionId, FluidRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        if (r.Direction is not { } direction || !Enum.IsDefined(direction)) throw BadRequestException.Field("direction", "Choose in or out.");
        if (r.Kind is not { } kind || !Enum.IsDefined(kind)) throw BadRequestException.Field("kind", "Choose the kind of fluid.");
        if (r.Ml is not { } ml || ml < 1 || ml > MaxMl) throw BadRequestException.Field("ml", $"Enter millilitres from 1 to {MaxMl:N0}.");
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForWriteAsync(caller, admissionId, now, ct);
        var entry = new FluidEntry
        {
            TenantId = TenantId, AdmissionId = admissionId, PatientId = patientId, Direction = direction, Kind = kind, Ml = ml, RecordedByUserId = caller.UserId, At = now,
        };
        db.FluidEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        var names = await ConsultationService.NamesAsync(db, [caller.UserId], ct);
        return ToDto(entry, names);
    }

    /// <summary>
    /// One Lagos day of the fluid chart (<paramref name="date"/>, default today): its entries, oldest first, and the day's in and out
    /// totals and balance. A clinical open (area fluids).
    /// </summary>
    public async Task<FluidChartDto> FluidsAsync(Guid admissionId, DateOnly? date, string? reason, CancellationToken ct)
    {
        await OpenAsync(admissionId, RecordGuard.Areas.Fluids, reason, ct);
        var day = date ?? CareCalendar.Today(clock.GetUtcNow());
        var (dayStart, dayEnd) = DayRange(day);
        var rows = await db.FluidEntries.AsNoTracking().Where(f => f.AdmissionId == admissionId && f.At >= dayStart && f.At < dayEnd)
            .OrderBy(f => f.At).ThenBy(f => f.Id).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(f => f.RecordedByUserId), ct);
        var inMl = rows.Where(f => f.Direction == FluidDirection.In).Sum(f => f.Ml);
        var outMl = rows.Where(f => f.Direction == FluidDirection.Out).Sum(f => f.Ml);
        return new FluidChartDto(admissionId, day, rows.Select(f => ToDto(f, names)).ToList(), inMl, outMl, inMl - outMl);
    }

    private static FluidEntryDto ToDto(FluidEntry f, IReadOnlyDictionary<Guid, string> names) =>
        new(f.Id, f.AdmissionId, f.Direction, f.Kind, f.Ml, f.At, f.RecordedByUserId, names.GetValueOrDefault(f.RecordedByUserId, ""));

    // ---------- Ward rounds ----------

    /// <summary>
    /// Starts a ward-round draft (the note may be blank until signing): doctors and the director (403 "Only doctors can write ward
    /// rounds." for a nurse). 400 <c>note</c> over 10,000 characters; 404; 409 discharged; 403 not on the ward.
    /// </summary>
    public async Task<WardRoundDto> StartRoundAsync(Guid admissionId, WardRoundRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly);
        var note = CareOrders.Optional("note", r.Note, MaxRoundLength);
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForWriteAsync(caller, admissionId, now, ct);
        var round = new WardRound
        {
            TenantId = TenantId, AdmissionId = admissionId, PatientId = patientId, DoctorUserId = caller.UserId, Note = text.Protect(note), Status = NoteStatus.Draft,
        };
        db.WardRounds.Add(round);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadRoundAsync(round.Id, ct);
    }

    /// <summary>
    /// Saves the draft (a full replace of the note). In order: 403 "Only doctors can write ward rounds." for anyone but a doctor or the
    /// director (before any lookup, Task 16); 404; 409 discharged; 409 "This note is signed. Add an addendum instead.";
    /// 403 "Only the doctor who wrote this note can change it." for anyone but the author (the director included). The author keeps the
    /// draft even if their care relationship has ended, as with consultations.
    /// </summary>
    public async Task<WardRoundDto> SaveRoundAsync(Guid id, WardRoundRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly); // Task 16: the role before the record (a nurse gets 403 for any id)
        var note = CareOrders.Optional("note", r.Note, MaxRoundLength);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var round = await LockRoundAsync(id, caller, ct);
        round.Note = text.Protect(note);
        round.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadRoundAsync(id, ct);
    }

    /// <summary>
    /// Signs the draft: final from now on. The same checks as <see cref="SaveRoundAsync"/>, then 409 "Write the ward-round note before
    /// signing." for a blank note. Writes an audit row with no clinical text.
    /// </summary>
    public async Task<WardRoundDto> SignRoundAsync(Guid id, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly); // Task 16: the role before the record
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var round = await LockRoundAsync(id, caller, ct);
        if (string.IsNullOrEmpty(round.Note)) throw new ConflictException(EmptyRound, "note_empty");
        var now = Now();
        round.Status = NoteStatus.Signed;
        round.SignedAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.note_signed", EntityType = nameof(WardRound), EntityId = id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { noteKind = nameof(NoteKind.WardRound), admissionId = round.AdmissionId, patientId = round.PatientId }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadRoundAsync(id, ct);
    }

    /// <summary>
    /// Discards an unsigned draft (Task 10: "sign or discard" before discharge). The same checks as <see cref="SaveRoundAsync"/>: 404; 409
    /// discharged; 409 signed (a signed note is never removed); 403 for anyone but the author. Audited with ids only.
    /// </summary>
    public async Task DiscardRoundAsync(Guid id, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly); // Task 16: the role before the record
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var round = await LockRoundAsync(id, caller, ct);
        db.WardRounds.Remove(round);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.note_discarded", EntityType = nameof(WardRound), EntityId = id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { noteKind = nameof(NoteKind.WardRound), admissionId = round.AdmissionId, patientId = round.PatientId }),
            Ip = user.IpAddress, CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>The admission's ward rounds, newest first, drafts included with their status, each with its addenda. A clinical open (area ward-round).</summary>
    public async Task<IReadOnlyList<WardRoundDto>> WardRoundsAsync(Guid admissionId, string? reason, CancellationToken ct)
    {
        await OpenAsync(admissionId, RecordGuard.Areas.WardRounds, reason, ct);
        return await ReadRoundsAsync(db, text, db.WardRounds.AsNoTracking().Where(w => w.AdmissionId == admissionId), ct);
    }

    /// <summary>The round, tracked, under the patient's admission lock: 404, 409 discharged, 409 signed, 403 not the author.</summary>
    private async Task<WardRound> LockRoundAsync(Guid id, CareCaller caller, CancellationToken ct)
    {
        var patientId = await db.WardRounds.AsNoTracking().Where(w => w.Id == id).Select(w => (Guid?)w.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Ward round");
        await db.LockAsync(AdmissionService.PatientLockKey(TenantId, patientId), ct);
        var round = await db.WardRounds.FirstAsync(w => w.Id == id, ct);
        await EnsureAdmittedAsync(round.AdmissionId, ct);
        ConsultationService.EnsureDraft(round.Status);
        if (round.DoctorUserId != caller.UserId) throw new ForbiddenException(ConsultationService.AuthorOnly);
        return round;
    }

    private async Task<WardRoundDto> ReadRoundAsync(Guid id, CancellationToken ct) =>
        (await ReadRoundsAsync(db, text, db.WardRounds.AsNoTracking().Where(w => w.Id == id), ct)).Single();

    /// <summary>Ward rounds as DTOs, newest first, each with its addenda (oldest first), decrypted. No access check: the caller has opened the record.</summary>
    internal static async Task<List<WardRoundDto>> ReadRoundsAsync(IAppDbContext db, ClinicalText text, IQueryable<WardRound> query, CancellationToken ct)
    {
        var rounds = await query.OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id).ToListAsync(ct);
        if (rounds.Count == 0) return [];
        var ids = rounds.Select(w => w.Id).ToList();
        var addenda = await db.Addenda.AsNoTracking().Where(a => a.NoteKind == NoteKind.WardRound && ids.Contains(a.NoteId))
            .OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rounds.Select(w => w.DoctorUserId).Concat(addenda.Select(a => a.AuthorUserId)), ct);
        return rounds.Select(w => new WardRoundDto(w.Id, w.AdmissionId, w.PatientId, w.DoctorUserId, names.GetValueOrDefault(w.DoctorUserId, ""), text.Read(w.Note),
                w.Status, w.SignedAt, w.CreatedAt, w.UpdatedAt, addenda.Where(a => a.NoteId == w.Id).Select(a => ConsultationService.ToDto(a, names, text)).ToList()))
            .ToList();
    }

    // ---------- Medication chart ----------

    /// <summary>
    /// One Lagos day of the medication chart (<paramref name="date"/>, default today): the doses due that day by due time, with the drug
    /// decrypted and who recorded each, plus the admission's as-needed (PRN) drugs, which are never scheduled. A clinical open (area medication).
    /// </summary>
    public async Task<MedicationChartDto> MedicationChartAsync(Guid admissionId, DateOnly? date, string? reason, CancellationToken ct)
    {
        await OpenAsync(admissionId, RecordGuard.Areas.MedicationChart, reason, ct);
        var day = date ?? CareCalendar.Today(clock.GetUtcNow());
        var (dayStart, dayEnd) = DayRange(day);
        var rows = await (from d in db.MedicationDoses.AsNoTracking()
                          where d.AdmissionId == admissionId && d.DueAt >= dayStart && d.DueAt < dayEnd
                          join i in db.PrescriptionItems.AsNoTracking() on d.PrescriptionItemId equals i.Id
                          orderby d.DueAt, i.Position, d.Id
                          select new { Dose = d, Item = i }).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Where(r => r.Dose.RecordedByUserId is not null).Select(r => r.Dose.RecordedByUserId!.Value), ct);
        var doses = rows.Select(r => new MedicationDoseDto(r.Dose.Id, r.Dose.PrescriptionId, r.Item.Id, text.Read(r.Item.Drug) ?? "", text.Read(r.Item.Strength),
            text.Read(r.Item.Dose) ?? "", r.Item.Frequency, text.Read(r.Item.Route), text.Read(r.Item.Instructions), r.Dose.DueAt, r.Dose.Status,
            r.Dose.RecordedByUserId, r.Dose.RecordedByUserId is { } by ? names.GetValueOrDefault(by) : null, r.Dose.RecordedAt, text.Read(r.Dose.Note))).ToList();

        var asNeeded = await (from i in db.PrescriptionItems.AsNoTracking()
                              join p in db.Prescriptions.AsNoTracking() on i.PrescriptionId equals p.Id
                              where p.AdmissionId == admissionId && i.Frequency == DoseFrequency.PRN
                              orderby p.CreatedAt, i.Position
                              select i).ToListAsync(ct);
        return new MedicationChartDto(admissionId, day, doses, asNeeded.Select(i => new PrescriptionItemDto(i.Id, text.Read(i.Drug) ?? "", text.Read(i.Strength),
            text.Read(i.Dose) ?? "", i.Frequency, i.DurationDays, i.Quantity, text.Read(i.Route), text.Read(i.Instructions), i.PriceItemId)).ToList());
    }

    /// <summary>
    /// Records a dose as Given, Held or Refused (400 <c>status</c> otherwise; 400 <c>note</c> over 500 characters): nurses, doctors and
    /// the director. 404; 409 discharged; 403 not on the ward; 409 "This dose was stopped." for a Stopped dose; 409 "This dose is already
    /// recorded." unless it is still Due (or Missed: a late dose). The record is one conditional update, so of two racing records exactly one wins.
    /// </summary>
    public async Task<DoseRecordedDto> RecordDoseAsync(Guid doseId, RecordDoseRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        if (r.Status is not ({ } status and (DoseStatus.Given or DoseStatus.Held or DoseStatus.Refused)))
            throw BadRequestException.Field("status", "Choose given, held or refused.");
        var note = CareOrders.Optional("note", r.Note, MaxDoseNoteLength);
        var admissionId = await db.MedicationDoses.AsNoTracking().Where(d => d.Id == doseId).Select(d => (Guid?)d.AdmissionId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Dose");
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockForWriteAsync(caller, admissionId, now, ct);
        var stored = note is null ? null : text.Protect(note);
        var recorded = await db.MedicationDoses
            .Where(d => d.Id == doseId && (d.Status == DoseStatus.Due || d.Status == DoseStatus.Missed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.RecordedByUserId, (Guid?)caller.UserId)
                .SetProperty(d => d.RecordedAt, (DateTimeOffset?)now)
                .SetProperty(d => d.Note, stored)
                .SetProperty(d => d.UpdatedAt, (DateTimeOffset?)now), ct);
        if (recorded == 0)
        {
            // Nothing matched: the dose was stopped (end of course or discharge) or already recorded. Read under the same lock.
            var current = await db.MedicationDoses.AsNoTracking().Where(d => d.Id == doseId).Select(d => d.Status).FirstAsync(ct);
            if (current == DoseStatus.Stopped) throw new ConflictException(DoseStopped, "dose_stopped");
            throw new ConflictException(DoseRecorded, "dose_recorded");
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return new DoseRecordedDto(doseId, status, caller.UserId, now, note);
    }

    // ---------- Internals ----------

    /// <summary>A clinical read of the admission: 403 without clinical access, 404, then <see cref="RecordGuard.OpenAsync"/>.</summary>
    private async Task OpenAsync(Guid admissionId, string area, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Admission");
        await guard.OpenAsync(patientId, area, reason, ct);
    }

    /// <summary>
    /// The write checks, in a transaction: 404 for an unknown admission; the patient's admission lock; 409 discharged; 403 "This patient
    /// isn't on your ward." without a live care assignment for this admission (the director is exempt). Returns the patient.
    /// </summary>
    private async Task<Guid> LockForWriteAsync(CareCaller caller, Guid admissionId, DateTimeOffset now, CancellationToken ct)
    {
        var patientId = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Admission");
        await db.LockAsync(AdmissionService.PatientLockKey(TenantId, patientId), ct);
        await EnsureAdmittedAsync(admissionId, ct);
        if (caller.MemberRole != MemberRole.Owner && !await db.CareAssignments.AnyAsync(a => a.AdmissionId == admissionId && a.StaffUserId == caller.UserId
                && (a.EndedAt == null || a.EndedAt > now), ct))
            throw new ForbiddenException(NotOnWard);
        return patientId;
    }

    /// <summary>409 "This patient has been discharged." unless the admission is still Admitted (read under the caller's lock).</summary>
    private async Task EnsureAdmittedAsync(Guid admissionId, CancellationToken ct)
    {
        var status = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => a.Status).FirstAsync(ct);
        if (status != AdmissionStatus.Admitted) throw new ConflictException(AdmissionService.Discharged, "admission_discharged");
    }

    /// <summary>A Lagos day as a UTC range [from, to).</summary>
    private static (DateTimeOffset From, DateTimeOffset To) DayRange(DateOnly day)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        return (from, from.AddDays(1));
    }

    /// <summary>Now, truncated to microseconds (Postgres precision), so an answer's time is the time stored.</summary>
    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }
}
