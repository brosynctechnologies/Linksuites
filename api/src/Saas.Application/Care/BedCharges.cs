using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

/// <summary>
/// The daily bed charge (Task 13): one Auto bill line per Lagos day of a stay, keyed by <see cref="BillLine.ChargeDay"/>.
/// <list type="bullet">
/// <item>The stay starts on the Lagos day of the first bed assignment (ruling): days before a bed was assigned are not charged.</item>
/// <item>A day is charged at the ward occupied at 00:00 Lagos that day; on the first day, the first-assigned ward. A transfer at 23:30
/// therefore changes the charge from the next day.</item>
/// <item>The price is the ward's bed price item's current price, frozen on the line (charged even if the item has since been made
/// inactive: the patient was in the bed).</item>
/// <item>A day that already has a bed line on the bill is skipped, so charging again adds nothing; the unique index on (bill, price item,
/// charge day) is the backstop.</item>
/// </list>
/// The nightly job charges admitted admissions up to yesterday; the discharge charges up to the day before discharge, or the one day of a
/// stay that starts and ends on the same day.
/// </summary>
public static class BedCharges
{
    /// <summary>
    /// Adds the missing bed lines for the stay's days up to <paramref name="throughDay"/> (inclusive) and recomputes the bill. With
    /// <paramref name="includeSameDayStay"/>, a stay that started after <paramref name="throughDay"/> (it starts and ends on the discharge
    /// day) is charged its one day. Nothing for an admission with no bed or no bill, or a bill that is already final. The caller holds the
    /// patient's admission lock (<see cref="AdmissionService.PatientLockKey"/>) in an open transaction; this takes the bill lock next
    /// (lock order: patient, bed, bill), saves the lines, then recalculates. Returns how many days it charged.
    /// </summary>
    public static async Task<int> ChargeOutstandingAsync(IAppDbContext db, Guid admissionId, DateOnly throughDay, bool includeSameDayStay, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Bed charges run inside the caller's transaction, under the patient's admission lock.");
        var moves = await (from m in db.BedMoves.AsNoTracking()
                           join b in db.Beds.AsNoTracking() on m.ToBedId equals b.Id
                           where m.AdmissionId == admissionId
                           orderby m.At, m.CreatedAt
                           select new { m.TenantId, m.At, b.WardId }).ToListAsync(ct);
        if (moves.Count == 0) return 0;
        var first = CareCalendar.Today(moves[0].At);
        var last = includeSameDayStay && throughDay < first ? first : throughDay;
        if (last < first) return 0;

        var tenantId = moves[0].TenantId;
        await db.LockAsync(BillingService.LockKey(tenantId), ct);
        var bill = await db.PatientBills.AsNoTracking().Where(b => b.AdmissionId == admissionId).Select(b => new { b.Id, b.FinalisedAt }).FirstOrDefaultAsync(ct);
        if (bill is null || bill.FinalisedAt is not null) return 0;
        var charged = (await db.BillLines.AsNoTracking().Where(l => l.BillId == bill.Id && l.ChargeDay != null).Select(l => l.ChargeDay!.Value).ToListAsync(ct)).ToHashSet();

        var wardIds = moves.Select(m => m.WardId).Distinct().ToList();
        var items = await (from w in db.Wards.AsNoTracking()
                           join i in db.PriceItems.AsNoTracking() on w.BedPriceItemId equals i.Id
                           where wardIds.Contains(w.Id)
                           select new { WardId = w.Id, i.Id, i.Name, i.PriceMinor }).ToDictionaryAsync(x => x.WardId, ct);

        var added = 0;
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (charged.Contains(day)) continue;
            var midnight = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset);
            var wardId = moves.LastOrDefault(m => m.At <= midnight)?.WardId ?? moves[0].WardId;
            if (!items.TryGetValue(wardId, out var item)) continue;
            var description = $"{item.Name} ({day.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})";
            db.BillLines.Add(new BillLine
            {
                TenantId = tenantId, BillId = bill.Id, PriceItemId = item.Id, Description = description.Length > 200 ? description[..200] : description,
                Quantity = 1, UnitPriceMinor = item.PriceMinor, AmountMinor = item.PriceMinor, Source = BillLineSource.Auto, ChargeDay = day,
            });
            added++;
        }
        if (added == 0) return 0;
        await db.SaveChangesAsync(ct);
        await BillingService.RecalculateBillAsync(db, bill.Id, ct);
        return added;
    }
}
