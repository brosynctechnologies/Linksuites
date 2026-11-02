using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>A ward with its bed counts. <see cref="NurseUserIds"/> empty means every active nurse in the clinic.</summary>
public sealed record WardDto(
    Guid Id, string Name, WardKind Kind, Guid BedPriceItemId, string? BedPriceItemName, long? MinDepositMinor, IReadOnlyList<Guid> NurseUserIds,
    int ActiveBeds, int FreeBeds, int OccupiedBeds);

public sealed record SaveWardRequest(string? Name, WardKind? Kind, Guid? BedPriceItemId, long? MinDepositMinor, IReadOnlyList<Guid>? NurseUserIds);

public sealed record BedDto(Guid Id, Guid WardId, string Label, BedStatus Status, bool Active);

public sealed record SaveBedRequest(string? Label, bool? Active);

/// <summary>One bed on the bed board, with its patient (demographics only) when occupied.</summary>
public sealed record BedBoardBedDto(Guid Id, string Label, BedStatus Status, AdmissionSummaryDto? Patient);

public sealed record BedBoardWardDto(Guid Id, string Name, WardKind Kind, IReadOnlyList<BedBoardBedDto> Beds);

/// <summary>Every ward's active beds and who is in them, plus the admissions still waiting for a bed. Demographics and bed status only.</summary>
public sealed record BedBoardDto(IReadOnlyList<BedBoardWardDto> Wards, IReadOnlyList<AdmissionSummaryDto> AwaitingBed);

/// <summary>
/// Wards and beds: leadership sets them up (active beds count against the plan's <c>beds</c> limit, 402 above it); every staff member
/// reads the wards, the bed board and a ward's patients, which show demographics and bed status only, never the admitting reason or
/// diagnosis. A bed a patient left is Cleaning until front desk or clinical staff mark it ready.
/// </summary>
public sealed class WardService(IAppDbContext db, ITenantContext tenant, CareAccess access, PlanService plans, IDbErrors dbErrors, TimeProvider clock)
{
    public const string BedOccupied = "That bed is occupied.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>Serialises bed additions and activations of one clinic, so two at once can't pass the plan limit together.</summary>
    private static long BedLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0CA5BED5;

    // ---------- Wards ----------

    public async Task<IReadOnlyList<WardDto>> ListAsync(CancellationToken ct)
    {
        await access.CallerAsync(ct);
        return await ReadAsync(db.Wards.AsNoTracking(), ct);
    }

    public async Task<WardDto> CreateAsync(SaveWardRequest r, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        var ward = new Ward { TenantId = TenantId, Name = "" };
        await ApplyAsync(ward, r, ct);
        db.Wards.Add(ward);
        await SaveUniqueAsync(ct);
        return (await ReadAsync(db.Wards.AsNoTracking().Where(w => w.Id == ward.Id), ct)).Single();
    }

    public async Task<WardDto> UpdateAsync(Guid id, SaveWardRequest r, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var ward = await db.Wards.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Ward");
        var nursesBefore = ward.NurseUserIds.ToList();
        await ApplyAsync(ward, r, ct);
        await SaveUniqueAsync(ct);
        // A changed nurse list moves the ward-nurse assignments of the patients in this ward now.
        if (!nursesBefore.SequenceEqual(ward.NurseUserIds))
        {
            var now = clock.GetUtcNow();
            var admissions = await (from a in db.Admissions.AsNoTracking()
                                    join b in db.Beds on a.BedId equals b.Id
                                    where b.WardId == ward.Id && a.Status == AdmissionStatus.Admitted
                                    orderby a.PatientId
                                    select new { a.Id, a.PatientId }).ToListAsync(ct);
            // Task 10 fix (I2): each patient's admission lock, in patient-id order (no deadlock between two edits), then "still admitted
            // and still in this ward" re-read under it, so a discharge or transfer that won the lock is never undone by fresh nurse rows.
            foreach (var row in admissions)
            {
                await db.LockAsync(AdmissionService.PatientLockKey(TenantId, row.PatientId), ct);
                var admission = await db.Admissions.FirstAsync(a => a.Id == row.Id, ct); // first tracked read: fresh, after the lock
                if (admission.Status != AdmissionStatus.Admitted || admission.BedId is not { } bedId) continue;
                if (!await db.Beds.AnyAsync(b => b.Id == bedId && b.WardId == ward.Id, ct)) continue;
                await SyncWardNursesAsync(db, TenantId, admission, ward, now, ct);
            }
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return (await ReadAsync(db.Wards.AsNoTracking().Where(w => w.Id == ward.Id), ct)).Single();
    }

    /// <summary>Deletes a ward that has no beds (a ward with beds keeps its history; make the beds inactive instead).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        var ward = await db.Wards.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Ward");
        if (await db.Beds.AnyAsync(b => b.WardId == id, ct))
            throw new ConflictException("This ward has beds. Remove them, or make them inactive, first.", "ward_has_beds");
        db.Wards.Remove(ward);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Beds ----------

    public async Task<IReadOnlyList<BedDto>> BedsAsync(Guid wardId, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        if (!await db.Wards.AnyAsync(w => w.Id == wardId, ct)) throw new NotFoundException("Ward");
        return await db.Beds.AsNoTracking().Where(b => b.WardId == wardId).OrderBy(b => b.Label)
            .Select(b => new BedDto(b.Id, b.WardId, b.Label, b.Status, b.Active)).ToListAsync(ct);
    }

    public async Task<BedDto> AddBedAsync(Guid wardId, SaveBedRequest r, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        var label = Label(r.Label);
        if (!await db.Wards.AnyAsync(w => w.Id == wardId, ct)) throw new NotFoundException("Ward");
        var active = r.Active ?? true;

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        if (active) await EnsureBedAllowedAsync(ct);
        var bed = new Bed { TenantId = TenantId, WardId = wardId, Label = label, Active = active };
        db.Beds.Add(bed);
        await SaveUniqueAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new BedDto(bed.Id, bed.WardId, bed.Label, bed.Status, bed.Active);
    }

    public async Task<BedDto> UpdateBedAsync(Guid wardId, Guid bedId, SaveBedRequest r, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        var label = Label(r.Label);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var bed = await db.Beds.FirstOrDefaultAsync(b => b.Id == bedId && b.WardId == wardId, ct) ?? throw new NotFoundException("Bed");
        var active = r.Active ?? bed.Active;
        if (active && !bed.Active) await EnsureBedAllowedAsync(ct);
        // Deactivating locks the row only if no patient is in it, so a claim racing this change either commits first (and we refuse) or
        // waits for our commit and then finds an inactive bed.
        if (!active && bed.Active) await LockUnoccupiedAsync(bedId, ct);
        bed.Label = label;
        bed.Active = active;
        await SaveUniqueAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new BedDto(bed.Id, bed.WardId, bed.Label, bed.Status, bed.Active);
    }

    /// <summary>Deletes a bed that was never used and has no patient (a used bed keeps its history; make it inactive instead).</summary>
    public async Task DeleteBedAsync(Guid wardId, Guid bedId, CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var bed = await db.Beds.FirstOrDefaultAsync(b => b.Id == bedId && b.WardId == wardId, ct) ?? throw new NotFoundException("Bed");
        await LockUnoccupiedAsync(bedId, ct);
        if (await db.Admissions.AnyAsync(a => a.BedId == bedId, ct) || await db.BedMoves.AnyAsync(m => m.FromBedId == bedId || m.ToBedId == bedId, ct))
            throw new ConflictException("This bed has been used. Make it inactive instead.", "bed_used");
        db.Beds.Remove(bed);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    public const string BedHasPatient = "This bed has a patient in it.";

    /// <summary>
    /// Takes the bed's row lock, but only when no patient is in it: a no-op conditional update on <c>status &lt;&gt; Occupied</c>. A claim
    /// holding the row makes this wait; it then re-checks the committed row, so a bed claimed meanwhile matches nothing and gets 409
    /// "This bed has a patient in it.". Must run inside a transaction (the lock lasts until commit).
    /// </summary>
    private async Task LockUnoccupiedAsync(Guid bedId, CancellationToken ct)
    {
        var locked = await db.Beds.Where(b => b.Id == bedId && b.Status != BedStatus.Occupied)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Label, b => b.Label), ct);
        if (locked == 0) throw new ConflictException(BedHasPatient, "bed_occupied");
    }

    /// <summary>Marks a cleaned bed ready (Cleaning to Free). Front desk and clinical staff and above; the cashier gets 403.</summary>
    public async Task<BedDto> ReadyAsync(Guid bedId, CancellationToken ct)
    {
        PatientService.EnsureDesk(await access.CallerAsync(ct));
        var bed = await db.Beds.FirstOrDefaultAsync(b => b.Id == bedId, ct) ?? throw new NotFoundException("Bed");
        if (bed.Status != BedStatus.Cleaning) throw new ConflictException("Only a bed being cleaned can be marked ready.", "bed_not_cleaning");
        bed.Status = BedStatus.Free;
        await db.SaveChangesAsync(ct);
        return new BedDto(bed.Id, bed.WardId, bed.Label, bed.Status, bed.Active);
    }

    // ---------- Bed board and ward patients (demographics only) ----------

    public async Task<BedBoardDto> BedBoardAsync(CancellationToken ct)
    {
        await access.CallerAsync(ct);
        var wards = await db.Wards.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);
        var beds = await db.Beds.AsNoTracking().Where(b => b.Active).OrderBy(b => b.Label).ToListAsync(ct);
        var admissions = await AdmissionService.SummariesAsync(db, db.Admissions.AsNoTracking().Where(a => a.Status == AdmissionStatus.Admitted), clock, ct);
        var byBed = admissions.Where(a => a.BedId is not null).ToDictionary(a => a.BedId!.Value);
        return new BedBoardDto(
            wards.Select(w => new BedBoardWardDto(w.Id, w.Name, w.Kind, beds.Where(b => b.WardId == w.Id)
                .Select(b => new BedBoardBedDto(b.Id, b.Label, b.Status, byBed.GetValueOrDefault(b.Id))).ToList())).ToList(),
            admissions.Where(a => a.BedId is null).OrderBy(a => a.AdmittedAt).ToList());
    }

    public async Task<IReadOnlyList<AdmissionSummaryDto>> PatientsAsync(Guid wardId, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        if (!await db.Wards.AnyAsync(w => w.Id == wardId, ct)) throw new NotFoundException("Ward");
        var query = from a in db.Admissions.AsNoTracking()
                    join b in db.Beds on a.BedId equals b.Id
                    where b.WardId == wardId && a.Status == AdmissionStatus.Admitted
                    select a;
        return (await AdmissionService.SummariesAsync(db, query, clock, ct)).OrderBy(a => a.BedLabel).ToList();
    }

    // ---------- Ward nurses ----------

    /// <summary>
    /// The nurses of a ward now: its nurse list (only those still active nurses), or every active nurse in the clinic when the list is empty.
    /// </summary>
    internal static async Task<HashSet<Guid>> WardNursesAsync(IAppDbContext db, Ward ward, CancellationToken ct)
    {
        var nurses = await (from p in db.CareStaffProfiles.AsNoTracking()
                            join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
                            where p.ClinicalRole == ClinicalRole.Nurse && m.Status == MembershipStatus.Active
                                  && m.Role != MemberRole.Accountant
                            select p.UserId).ToListAsync(ct);
        return ward.NurseUserIds.Count == 0 ? nurses.ToHashSet() : nurses.Where(ward.NurseUserIds.Contains).ToHashSet();
    }

    /// <summary>
    /// Makes the admission's open ward-nurse assignments match <paramref name="ward"/>'s nurses: a nurse of the new ward keeps (or gets) a
    /// row pointing at it; a nurse who isn't on it any more has their row ended now. Adds to the context only; the caller saves.
    /// </summary>
    internal static async Task SyncWardNursesAsync(IAppDbContext db, Guid tenantId, Admission admission, Ward ward, DateTimeOffset now, CancellationToken ct)
    {
        var nurses = await WardNursesAsync(db, ward, ct);
        var rows = await db.CareAssignments
            .Where(a => a.AdmissionId == admission.Id && a.Kind == CareAssignmentKind.WardNurse && a.EndedAt == null).ToListAsync(ct);
        foreach (var row in rows)
        {
            if (nurses.Contains(row.StaffUserId)) row.WardId = ward.Id;
            else row.EndedAt = now;
        }
        foreach (var nurse in nurses.Where(n => rows.All(r => r.StaffUserId != n)))
            db.CareAssignments.Add(new CareAssignment
            {
                TenantId = tenantId, PatientId = admission.PatientId, StaffUserId = nurse, Kind = CareAssignmentKind.WardNurse,
                AdmissionId = admission.Id, WardId = ward.Id,
            });
    }

    // ---------- Internals ----------

    private async Task EnsureBedAllowedAsync(CancellationToken ct)
    {
        await db.LockAsync(BedLockKey(TenantId), ct);
        var active = await db.Beds.CountAsync(b => b.Active, ct);
        await plans.EnsureCanAddAsync(LimitKeys.Beds, active, "Beds", ct);
    }

    private async Task ApplyAsync(Ward ward, SaveWardRequest r, CancellationToken ct)
    {
        var name = (r.Name ?? "").Trim();
        if (name.Length == 0) throw BadRequestException.Field("name", "Enter the ward's name.");
        if (name.Length > 80) throw BadRequestException.Field("name", "The name can have at most 80 characters.");
        var kind = r.Kind ?? WardKind.General;
        if (!Enum.IsDefined(kind)) throw BadRequestException.Field("kind", "Choose the kind of ward.");
        if (r.BedPriceItemId is not { } itemId
            || (itemId != ward.BedPriceItemId && !await db.PriceItems.AnyAsync(i => i.Id == itemId && i.Category == PriceCategory.Bed && i.Active, ct)))
            throw BadRequestException.Field("bedPriceItemId", "Choose an active Bed item from the price list for the daily bed charge.");
        if (r.MinDepositMinor is < 0) throw BadRequestException.Field("minDepositMinor", "The minimum deposit can't be negative.");
        if (r.MinDepositMinor > PaymentService.MaxAmountMinor)
            throw BadRequestException.Field("minDepositMinor", $"Enter a minimum deposit up to {Money.Format(PaymentService.MaxAmountMinor, "NGN")}.");
        var nurses = (r.NurseUserIds ?? []).Distinct().ToList();
        if (nurses.Count > 200) throw BadRequestException.Field("nurseUserIds", "A ward can have at most 200 nurses.");
        if (nurses.Count > 0)
        {
            var known = await (from p in db.CareStaffProfiles.AsNoTracking()
                               join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
                               where nurses.Contains(p.UserId) && p.ClinicalRole == ClinicalRole.Nurse && m.Status == MembershipStatus.Active
                               select p.UserId).Distinct().CountAsync(ct);
            if (known != nurses.Count) throw BadRequestException.Field("nurseUserIds", "Choose nurses from your staff.");
        }
        ward.Name = name;
        ward.Kind = kind;
        ward.BedPriceItemId = itemId;
        ward.MinDepositMinor = r.MinDepositMinor;
        ward.NurseUserIds = nurses;
    }

    private static string Label(string? raw)
    {
        var label = (raw ?? "").Trim();
        if (label.Length == 0) throw BadRequestException.Field("label", "Enter the bed's label.");
        if (label.Length > 40) throw BadRequestException.Field("label", "The label can have at most 40 characters.");
        return label;
    }

    private async Task SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (dbErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("That name is already used.", "duplicate_name");
        }
    }

    private async Task<List<WardDto>> ReadAsync(IQueryable<Ward> wards, CancellationToken ct)
    {
        var rows = await wards.OrderBy(w => w.Name)
            .Select(w => new
            {
                Ward = w,
                Item = db.PriceItems.Where(i => i.Id == w.BedPriceItemId).Select(i => i.Name).FirstOrDefault(),
                Active = db.Beds.Count(b => b.WardId == w.Id && b.Active),
                Free = db.Beds.Count(b => b.WardId == w.Id && b.Active && b.Status == BedStatus.Free),
                Occupied = db.Beds.Count(b => b.WardId == w.Id && b.Status == BedStatus.Occupied),
            }).ToListAsync(ct);
        return rows.Select(r => new WardDto(r.Ward.Id, r.Ward.Name, r.Ward.Kind, r.Ward.BedPriceItemId, r.Item, r.Ward.MinDepositMinor,
            r.Ward.NurseUserIds, r.Active, r.Free, r.Occupied)).ToList();
    }
}
