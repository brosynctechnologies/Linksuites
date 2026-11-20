using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

// ---------- Stock ----------

public sealed record SaveStockItemRequest(string? Name, string? Form, string? Unit, Guid? PriceItemId, int? ReorderLevel, bool? Active);

/// <summary>
/// A stock item with what is on hand: <see cref="OnHand"/> counts batches that haven't expired, <see cref="ExpiredOnHand"/> those that
/// have (to be removed by a count); <see cref="Low"/> when the usable stock is at or below the reorder level. Stock, not a patient's data.
/// </summary>
public sealed record StockItemDto(
    Guid Id, string Name, string? Form, string Unit, Guid? PriceItemId, string? PriceItemName, int ReorderLevel, bool Active, int OnHand,
    int ExpiredOnHand, DateOnly? NextExpiry, bool Low, long ValueMinor);

public sealed record StockBatchDto(
    Guid Id, Guid ItemId, string ItemName, string Unit, string BatchNumber, DateOnly ExpiresOn, int Received, int OnHand, long UnitCostMinor,
    bool Expired, int DaysToExpiry, DateTimeOffset ReceivedAt);

public sealed record ReceiptLineInput(Guid? ItemId, string? BatchNumber, DateOnly? ExpiresOn, int? Quantity, long? UnitCostMinor);
public sealed record ReceiveStockRequest(string? Supplier, string? Reference, DateOnly? ReceivedOn, IReadOnlyList<ReceiptLineInput>? Lines);
public sealed record StockReceiptDto(
    Guid Id, string? Supplier, string? Reference, DateOnly ReceivedOn, string ReceivedByName, long TotalCostMinor, IReadOnlyList<StockBatchDto> Batches);

public sealed record CountLineInput(Guid? BatchId, int? Counted);
public sealed record StockCountRequest(string? Reason, IReadOnlyList<CountLineInput>? Lines);

/// <summary>A stock movement for the movements report: item, batch, kind, quantity and balance, who and when. Never a patient.</summary>
public sealed record StockMovementDto(
    Guid Id, Guid ItemId, string ItemName, Guid BatchId, string BatchNumber, StockMovementKind Kind, int Quantity, int BalanceAfter, string? Reason,
    string ByName, DateTimeOffset At, Guid? ReceiptId, Guid? DispensingId);

public sealed record StockCountResultDto(int Adjusted, IReadOnlyList<StockMovementDto> Movements);

public sealed record StockAlertsDto(int Days, IReadOnlyList<StockItemDto> LowStock, IReadOnlyList<StockBatchDto> NearExpiry, IReadOnlyList<StockBatchDto> Expired);

// ---------- Dispensing ----------

/// <summary>A prescription still to dispense (dispensers only): patient, prescriber, when, and how many of its drugs are done. No drug names.</summary>
public sealed record DispenseQueueRowDto(
    Guid PrescriptionId, Guid PatientId, string PatientNumber, string PatientName, string DoctorName, DateTimeOffset WrittenAt, int Items, int ItemsDone,
    bool Inpatient);

public sealed record DispenseItemDto(
    Guid PrescriptionItemId, string Drug, string? Strength, string Dose, DoseFrequency Frequency, int? DurationDays, int? Quantity, int Dispensed,
    int? Remaining, Guid? StockItemId, string? StockItemName, int? StockOnHand);
public sealed record DispensedLineDto(Guid PrescriptionItemId, Guid ItemId, string ItemName, Guid BatchId, string BatchNumber, DateOnly ExpiresOn, int Quantity);
public sealed record DispensingDto(Guid Id, string DispensedByName, DateTimeOffset At, IReadOnlyList<DispensedLineDto> Lines);
/// <summary>One prescription opened to dispense it (a logged department read, area dispensing).</summary>
public sealed record DispensePrescriptionDto(
    Guid PrescriptionId, Guid PatientId, string PatientNumber, string PatientName, string DoctorName, DateTimeOffset WrittenAt,
    IReadOnlyList<DispenseItemDto> Items, IReadOnlyList<DispensingDto> Dispensings);

public sealed record DispenseLineInput(Guid? PrescriptionItemId, Guid? ItemId, int? Quantity);
public sealed record DispenseRequest(IReadOnlyList<DispenseLineInput>? Lines);

/// <summary>
/// Wave 4 (CareLink pharmacy stock, module 4c item 2): drug items linked to price-list items, batches with expiry dates, purchase receipts,
/// stock counts (adjustments with a reason, audited), dispensing a prescription first-expiry-first-out, low-stock and near-expiry alerts,
/// and the stock reports. Every stock change takes the item's lock (<see cref="ItemLockKey"/>, items in id order) inside one transaction,
/// so two dispensings never take the same units and stock never goes below zero (a check constraint backs it). Stock is managed by
/// pharmacists and leadership (<see cref="CareCaller.CanManageStock"/>); prescriptions are dispensed by pharmacists and clinical staff
/// (<see cref="CareCaller.CanDispense"/>), opening one through <see cref="RecordGuard.OpenOrderAsync"/> (area dispensing, logged).
/// </summary>
public sealed class PharmacyService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, CareNotices notices, ICurrentUser user,
    TimeProvider clock)
{
    public const int NearExpiryDays = 90;
    public const int MaxLines = 50;
    public const string NameTaken = "There is already a stock item with that name.";
    public const string PriceItemTaken = "Another stock item already uses that price item.";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();
    private DateOnly Today => CareCalendar.Today(clock.GetUtcNow());

    public static long ItemLockKey(Guid tenantId, Guid itemId) =>
        BitConverter.ToInt64(itemId.ToByteArray(), 8) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x57_0C_C1;

    public static long PrescriptionLockKey(Guid tenantId, Guid prescriptionId) =>
        BitConverter.ToInt64(prescriptionId.ToByteArray(), 8) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x0D_15_9E;

    private async Task<CareCaller> StockCallerAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureStock(caller);
        return caller;
    }

    /// <summary>Takes the item locks in id order (inside the caller's transaction), so two writers never wait on each other in a cycle.</summary>
    private async Task LockItemsAsync(IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        foreach (var id in itemIds.Distinct().Order()) await db.LockAsync(ItemLockKey(TenantId, id), ct);
    }

    // ---------- Items ----------

    /// <summary>Stock on hand: every item with its usable and expired stock, next expiry and value at cost. Pharmacists and leadership.</summary>
    public async Task<IReadOnlyList<StockItemDto>> ItemsAsync(bool? active, CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var query = db.DrugItems.AsNoTracking();
        if (active is { } a) query = query.Where(i => i.Active == a);
        var items = await query.OrderBy(i => i.Name).ToListAsync(ct);
        return await ToItemDtosAsync(items, ct);
    }

    public async Task<StockItemDto> ItemAsync(Guid id, CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var item = await db.DrugItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Stock item");
        return (await ToItemDtosAsync([item], ct)).Single();
    }

    private async Task<List<StockItemDto>> ToItemDtosAsync(IReadOnlyList<DrugItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var today = Today;
        var batches = await db.DrugBatches.AsNoTracking().Where(b => ids.Contains(b.DrugItemId) && b.QuantityOnHand > 0)
            .Select(b => new { b.DrugItemId, b.ExpiresOn, b.QuantityOnHand, b.UnitCostMinor }).ToListAsync(ct);
        var priceIds = items.Select(i => i.PriceItemId).OfType<Guid>().ToList();
        var prices = await db.PriceItems.AsNoTracking().Where(p => priceIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var byItem = batches.ToLookup(b => b.DrugItemId);
        return items.Select(i =>
        {
            var usable = byItem[i.Id].Where(b => b.ExpiresOn >= today).ToList();
            var onHand = usable.Sum(b => b.QuantityOnHand);
            return new StockItemDto(i.Id, i.Name, i.Form, i.Unit, i.PriceItemId, i.PriceItemId is { } p ? prices.GetValueOrDefault(p) : null, i.ReorderLevel,
                i.Active, onHand, byItem[i.Id].Where(b => b.ExpiresOn < today).Sum(b => b.QuantityOnHand),
                usable.Count == 0 ? null : usable.Min(b => b.ExpiresOn), i.Active && onHand <= i.ReorderLevel, usable.Sum(b => b.QuantityOnHand * b.UnitCostMinor));
        }).ToList();
    }

    public Task<StockItemDto> CreateItemAsync(SaveStockItemRequest r, CancellationToken ct) => SaveItemAsync(null, r, ct);

    public Task<StockItemDto> UpdateItemAsync(Guid id, SaveStockItemRequest r, CancellationToken ct) => SaveItemAsync(id, r, ct);

    /// <summary>
    /// Pharmacists and leadership. 400 per field; 400 <c>priceItemId</c> for a price item that isn't an active Drug item; 409
    /// <c>stock_item_exists</c> for a name in use, <c>price_item_taken</c> when another item already sells under that price item.
    /// </summary>
    private async Task<StockItemDto> SaveItemAsync(Guid? id, SaveStockItemRequest r, CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var name = CareOrders.Required("name", r.Name, 120, "Name the stock item.");
        var form = CareOrders.Optional("form", r.Form, 40);
        var unit = CareOrders.Optional("unit", r.Unit, 20) ?? "unit";
        var reorder = r.ReorderLevel ?? 0;
        if (reorder is < 0 or > 1_000_000) throw BadRequestException.Field("reorderLevel", "Enter a reorder level from 0 to 1,000,000.");
        if (r.PriceItemId is { } pid && !await db.PriceItems.AnyAsync(p => p.Id == pid && p.Active && p.Category == PriceCategory.Drug, ct))
            throw BadRequestException.Field("priceItemId", "Choose an active Drug item from the price list.");
        var item = id is { } existing ? await db.DrugItems.FirstOrDefaultAsync(i => i.Id == existing, ct) ?? throw new NotFoundException("Stock item") : null;
        var lower = name.ToLowerInvariant();
        if (await db.DrugItems.AnyAsync(i => i.Id != (id ?? Guid.Empty) && i.Name.ToLower() == lower, ct)) throw new ConflictException(NameTaken, "stock_item_exists");
        if (r.PriceItemId is { } p2 && await db.DrugItems.AnyAsync(i => i.Id != (id ?? Guid.Empty) && i.PriceItemId == p2, ct))
            throw new ConflictException(PriceItemTaken, "price_item_taken");
        if (item is null)
        {
            item = new DrugItem { TenantId = TenantId, Name = name };
            db.DrugItems.Add(item);
        }
        item.Name = name;
        item.Form = form;
        item.Unit = unit;
        item.PriceItemId = r.PriceItemId;
        item.ReorderLevel = reorder;
        if (r.Active is { } active) item.Active = active;
        await db.SaveChangesAsync(ct);
        return (await ToItemDtosAsync([item], ct)).Single();
    }

    /// <summary>An item's batches that still hold stock (or every batch with <paramref name="all"/>), first expiry first.</summary>
    public async Task<IReadOnlyList<StockBatchDto>> BatchesAsync(Guid itemId, bool all, CancellationToken ct)
    {
        await StockCallerAsync(ct);
        if (!await db.DrugItems.AnyAsync(i => i.Id == itemId, ct)) throw new NotFoundException("Stock item");
        var query = db.DrugBatches.AsNoTracking().Where(b => b.DrugItemId == itemId);
        if (!all) query = query.Where(b => b.QuantityOnHand > 0);
        return await BatchDtosAsync(query.OrderBy(b => b.ExpiresOn).ThenBy(b => b.CreatedAt), ct);
    }

    private async Task<List<StockBatchDto>> BatchDtosAsync(IQueryable<DrugBatch> query, CancellationToken ct)
    {
        var today = Today;
        var rows = await query.Join(db.DrugItems, b => b.DrugItemId, i => i.Id, (b, i) => new { b, i.Name, i.Unit }).ToListAsync(ct);
        return rows.Select(r => new StockBatchDto(r.b.Id, r.b.DrugItemId, r.Name, r.Unit, r.b.BatchNumber, r.b.ExpiresOn, r.b.QuantityReceived, r.b.QuantityOnHand,
            r.b.UnitCostMinor, r.b.ExpiresOn < today, r.b.ExpiresOn.DayNumber - today.DayNumber, r.b.CreatedAt)).ToList();
    }

    // ---------- Receipts ----------

    /// <summary>
    /// A purchase receipt: each line becomes a batch (+ a receipt movement). Pharmacists and leadership. 400 per line (an active item, a
    /// batch number, an expiry date not already past, a quantity of 1 to 1,000,000, a unit cost of at least 0). An item lifted above its
    /// reorder level can warn again later.
    /// </summary>
    public async Task<StockReceiptDto> ReceiveAsync(ReceiveStockRequest r, CancellationToken ct)
    {
        var caller = await StockCallerAsync(ct);
        var supplier = CareOrders.Optional("supplier", r.Supplier, 120);
        var reference = CareOrders.Optional("reference", r.Reference, 60);
        var today = Today;
        var receivedOn = r.ReceivedOn ?? today;
        if (receivedOn > today || receivedOn < today.AddDays(-366)) throw BadRequestException.Field("receivedOn", "Enter the day the stock arrived, within the last year.");
        if (r.Lines is null || r.Lines.Count == 0) throw BadRequestException.Field("lines", "Add at least one line.");
        if (r.Lines.Count > MaxLines) throw BadRequestException.Field("lines", $"A receipt can have at most {MaxLines} lines.");
        var itemIds = r.Lines.Select(l => l?.ItemId).OfType<Guid>().Distinct().ToList();
        var items = await db.DrugItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        for (var i = 0; i < r.Lines.Count; i++)
        {
            var line = r.Lines[i];
            if (line?.ItemId is not { } id || !items.TryGetValue(id, out var item) || !item.Active)
                throw BadRequestException.Field($"lines[{i}].itemId", "Choose an active stock item.");
            CareOrders.Required($"lines[{i}].batchNumber", line.BatchNumber, 60, "Enter the batch number.");
            if (line.ExpiresOn is not { } exp) throw BadRequestException.Field($"lines[{i}].expiresOn", "Enter the expiry date.");
            if (exp < today) throw BadRequestException.Field($"lines[{i}].expiresOn", "This batch has already expired.");
            if (line.Quantity is not (>= 1 and <= 1_000_000)) throw BadRequestException.Field($"lines[{i}].quantity", "Enter a quantity from 1 to 1,000,000.");
            if (line.UnitCostMinor is < 0 or > 100_000_000_00) throw BadRequestException.Field($"lines[{i}].unitCostMinor", "Enter a unit cost of 0 or more.");
        }

        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockItemsAsync(items.Keys, ct);
        var receipt = new StockReceipt { TenantId = TenantId, Supplier = supplier, Reference = reference, ReceivedOn = receivedOn, ReceivedBy = caller.UserId };
        db.StockReceipts.Add(receipt);
        var batches = new List<DrugBatch>();
        foreach (var line in r.Lines)
        {
            var batch = new DrugBatch
            {
                TenantId = TenantId, DrugItemId = line.ItemId!.Value, BatchNumber = line.BatchNumber!.Trim(), ExpiresOn = line.ExpiresOn!.Value,
                QuantityReceived = line.Quantity!.Value, QuantityOnHand = line.Quantity.Value, UnitCostMinor = line.UnitCostMinor ?? 0, ReceiptId = receipt.Id,
            };
            batches.Add(batch);
            db.DrugBatches.Add(batch);
            db.StockMovements.Add(new StockMovement
            {
                TenantId = TenantId, DrugItemId = batch.DrugItemId, DrugBatchId = batch.Id, Kind = StockMovementKind.Receipt, Quantity = batch.QuantityOnHand,
                BalanceAfter = batch.QuantityOnHand, ByUserId = caller.UserId, At = now, ReceiptId = receipt.Id,
            });
        }
        receipt.TotalCostMinor = batches.Sum(b => b.QuantityReceived * b.UnitCostMinor);
        await db.SaveChangesAsync(ct);
        await RefreshLowStockAsync(items.Keys, now, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReceiptDtoAsync(receipt.Id, ct);
    }

    public async Task<IReadOnlyList<StockReceiptDto>> ReceiptsAsync(CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var ids = await db.StockReceipts.AsNoTracking().OrderByDescending(r => r.ReceivedOn).ThenByDescending(r => r.CreatedAt).Take(100).Select(r => r.Id).ToListAsync(ct);
        var result = new List<StockReceiptDto>();
        foreach (var id in ids) result.Add(await ReceiptDtoAsync(id, ct));
        return result;
    }

    private async Task<StockReceiptDto> ReceiptDtoAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.StockReceipts.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var names = await ConsultationService.NamesAsync(db, [receipt.ReceivedBy], ct);
        var batches = await BatchDtosAsync(db.DrugBatches.AsNoTracking().Where(b => b.ReceiptId == id).OrderBy(b => b.CreatedAt), ct);
        return new StockReceiptDto(receipt.Id, receipt.Supplier, receipt.Reference, receipt.ReceivedOn, names.GetValueOrDefault(receipt.ReceivedBy, ""),
            receipt.TotalCostMinor, batches);
    }

    // ---------- Counts ----------

    /// <summary>
    /// A stock count: each batch's counted quantity; a difference becomes an adjustment movement with the reason (required). Pharmacists and
    /// leadership. 400 <c>reason</c>, <c>lines</c>, <c>lines[i].…</c> (an unknown or repeated batch, a count below 0). Audited (ids and
    /// quantities). Answers the adjustments made (none when every count matched).
    /// </summary>
    public async Task<StockCountResultDto> CountAsync(StockCountRequest r, CancellationToken ct)
    {
        var caller = await StockCallerAsync(ct);
        var reason = CareOrders.Required("reason", r.Reason, 300, "Say why the stock is being adjusted.");
        if (r.Lines is null || r.Lines.Count == 0) throw BadRequestException.Field("lines", "Count at least one batch.");
        if (r.Lines.Count > 200) throw BadRequestException.Field("lines", "Count at most 200 batches at a time.");
        var seen = new HashSet<Guid>();
        for (var i = 0; i < r.Lines.Count; i++)
        {
            if (r.Lines[i]?.BatchId is not { } bid || !seen.Add(bid)) throw BadRequestException.Field($"lines[{i}].batchId", "Choose each batch once.");
            if (r.Lines[i].Counted is not (>= 0 and <= 10_000_000)) throw BadRequestException.Field($"lines[{i}].counted", "Enter the quantity counted (0 or more).");
        }
        var itemIds = await db.DrugBatches.AsNoTracking().Where(b => seen.Contains(b.Id)).Select(b => new { b.Id, b.DrugItemId }).ToListAsync(ct);
        if (itemIds.Count != seen.Count)
        {
            var known = itemIds.Select(b => b.Id).ToHashSet();
            var index = r.Lines.ToList().FindIndex(l => !known.Contains(l.BatchId!.Value));
            throw BadRequestException.Field($"lines[{index}].batchId", "That batch isn't in this pharmacy's stock.");
        }

        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockItemsAsync(itemIds.Select(b => b.DrugItemId), ct);
        var batches = await db.DrugBatches.Where(b => seen.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var movements = new List<StockMovement>();
        foreach (var line in r.Lines)
        {
            var batch = batches[line.BatchId!.Value];
            var delta = line.Counted!.Value - batch.QuantityOnHand;
            if (delta == 0) continue;
            batch.QuantityOnHand = line.Counted.Value;
            var movement = new StockMovement
            {
                TenantId = TenantId, DrugItemId = batch.DrugItemId, DrugBatchId = batch.Id, Kind = StockMovementKind.Adjustment, Quantity = delta,
                BalanceAfter = batch.QuantityOnHand, Reason = reason, ByUserId = caller.UserId, At = now,
            };
            movements.Add(movement);
            db.StockMovements.Add(movement);
        }
        if (movements.Count > 0)
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = TenantId, ActorId = caller.UserId, Action = "care.stock_counted", EntityType = nameof(DrugBatch), EntityId = movements[0].DrugBatchId.ToString(),
                AfterJson = JsonSerializer.Serialize(new { adjustments = movements.Select(m => new { batchId = m.DrugBatchId, itemId = m.DrugItemId, quantity = m.Quantity, balanceAfter = m.BalanceAfter }) }),
                Ip = user.IpAddress, CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);
        await RefreshLowStockAsync(movements.Select(m => m.DrugItemId), now, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new StockCountResultDto(movements.Count, await MovementDtosAsync(movements.Select(m => m.Id).ToList(), ct));
    }

    // ---------- Reports and alerts ----------

    /// <summary>Movements from <paramref name="from"/> to <paramref name="to"/> (Lagos days; default the last 30), optionally one item; newest first, at most 500.</summary>
    public async Task<IReadOnlyList<StockMovementDto>> MovementsAsync(DateOnly? from, DateOnly? to, Guid? itemId, CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var end = to ?? Today;
        var start = from ?? end.AddDays(-29);
        if (start > end) throw BadRequestException.Field("from", "The start date must be on or before the end date.");
        var startAt = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        var endAt = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        var query = db.StockMovements.AsNoTracking().Where(m => m.At >= startAt && m.At < endAt);
        if (itemId is { } iid) query = query.Where(m => m.DrugItemId == iid);
        var ids = await query.OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Take(500).Select(m => m.Id).ToListAsync(ct);
        return await MovementDtosAsync(ids, ct);
    }

    private async Task<List<StockMovementDto>> MovementDtosAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await db.StockMovements.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Join(db.DrugItems, m => m.DrugItemId, i => i.Id, (m, i) => new { m, i.Name })
            .Join(db.DrugBatches, x => x.m.DrugBatchId, b => b.Id, (x, b) => new { x.m, x.Name, b.BatchNumber })
            .ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(r => r.m.ByUserId), ct);
        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return rows.OrderBy(r => order[r.m.Id]).Select(r => new StockMovementDto(r.m.Id, r.m.DrugItemId, r.Name, r.m.DrugBatchId, r.BatchNumber, r.m.Kind,
            r.m.Quantity, r.m.BalanceAfter, r.m.Reason, names.GetValueOrDefault(r.m.ByUserId, ""), r.m.At, r.m.ReceiptId, r.m.DispensingId)).ToList();
    }

    /// <summary>Batches holding stock that expire within <paramref name="days"/> (default 90, at most 730), expired ones included, first expiry first.</summary>
    public async Task<IReadOnlyList<StockBatchDto>> ExpiringAsync(int? days, CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var window = days ?? NearExpiryDays;
        if (window is < 0 or > 730) throw BadRequestException.Field("days", "Choose from 0 to 730 days.");
        var limit = Today.AddDays(window);
        return await BatchDtosAsync(db.DrugBatches.AsNoTracking().Where(b => b.QuantityOnHand > 0 && b.ExpiresOn <= limit).OrderBy(b => b.ExpiresOn).ThenBy(b => b.CreatedAt), ct);
    }

    /// <summary>The pharmacy's alerts now: active items at or below their reorder level, batches expiring within 90 days, and expired stock.</summary>
    public async Task<StockAlertsDto> AlertsAsync(CancellationToken ct)
    {
        await StockCallerAsync(ct);
        var items = await db.DrugItems.AsNoTracking().Where(i => i.Active).OrderBy(i => i.Name).ToListAsync(ct);
        var low = (await ToItemDtosAsync(items, ct)).Where(i => i.Low).ToList();
        var today = Today;
        var limit = today.AddDays(NearExpiryDays);
        var batches = await BatchDtosAsync(db.DrugBatches.AsNoTracking().Where(b => b.QuantityOnHand > 0 && b.ExpiresOn <= limit).OrderBy(b => b.ExpiresOn), ct);
        return new StockAlertsDto(NearExpiryDays, low, batches.Where(b => !b.Expired).ToList(), batches.Where(b => b.Expired).ToList());
    }

    /// <summary>
    /// After a stock change, inside the writer's transaction (items locked): an active item whose usable stock is at or below its reorder
    /// level tells the pharmacists and the director once (<see cref="DrugItem.LowStockNotifiedAt"/>); one back above it is re-armed. Saves.
    /// </summary>
    private async Task RefreshLowStockAsync(IEnumerable<Guid> itemIds, DateTimeOffset now, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0) return;
        await NotifyLowStockAsync(db, notices, ids, CareCalendar.Today(now), now, ct);
    }

    /// <summary>The low-stock rule, shared with the care job (expiry can drop usable stock with no movement).</summary>
    internal static async Task<int> NotifyLowStockAsync(IAppDbContext db, CareNotices notices, IReadOnlyCollection<Guid>? itemIds, DateOnly today, DateTimeOffset now,
        CancellationToken ct)
    {
        var query = db.DrugItems.Where(i => i.Active);
        if (itemIds is not null) query = query.Where(i => itemIds.Contains(i.Id));
        var items = await query.ToListAsync(ct);
        if (items.Count == 0) return 0;
        var ids = items.Select(i => i.Id).ToList();
        var onHand = await db.DrugBatches.AsNoTracking().Where(b => ids.Contains(b.DrugItemId) && b.QuantityOnHand > 0 && b.ExpiresOn >= today)
            .GroupBy(b => b.DrugItemId).Select(g => new { g.Key, Sum = g.Sum(b => b.QuantityOnHand) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var told = 0;
        foreach (var item in items)
        {
            var left = onHand.GetValueOrDefault(item.Id);
            if (left > item.ReorderLevel) item.LowStockNotifiedAt = null;
            else if (item.LowStockNotifiedAt is null)
            {
                item.LowStockNotifiedAt = now;
                await notices.LowStockAsync(item.Name, left, item.Unit, item.ReorderLevel, item.Id, ct);
                told++;
            }
        }
        await db.SaveChangesAsync(ct);
        return told;
    }

    // ---------- Dispensing ----------

    /// <summary>
    /// Prescriptions of the last 30 days with a drug still to dispense, oldest first (at most 200): dispensers only (403 otherwise). Patient,
    /// prescriber and counts; never a drug, so it opens no record and writes no access-log row.
    /// </summary>
    public async Task<IReadOnlyList<DispenseQueueRowDto>> QueueAsync(CancellationToken ct)
    {
        access.EnsureDispenser(await access.CallerAsync(ct));
        var since = clock.GetUtcNow().AddDays(-30);
        var rows = await db.Prescriptions.AsNoTracking().Where(p => p.CreatedAt >= since).OrderBy(p => p.CreatedAt).Take(1000)
            .Join(db.Patients, p => p.PatientId, pt => pt.Id, (p, pt) => new
            {
                p.Id, p.PatientId, pt.Number, pt.FirstName, pt.LastName, p.DoctorUserId, p.CreatedAt, Inpatient = p.AdmissionId != null,
                Items = p.Items.Select(i => new { i.Id, i.Quantity }).ToList(),
            })
            .ToListAsync(ct);
        var itemIds = rows.SelectMany(r => r.Items.Select(i => i.Id)).ToList();
        var dispensed = await DispensedAsync(itemIds, ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(r => r.DoctorUserId), ct);
        return rows.Select(r =>
            {
                var done = r.Items.Count(i => Done(i.Quantity, dispensed.GetValueOrDefault(i.Id)));
                return new DispenseQueueRowDto(r.Id, r.PatientId, r.Number, $"{r.FirstName} {r.LastName}", names.GetValueOrDefault(r.DoctorUserId, ""), r.CreatedAt,
                    r.Items.Count, done, r.Inpatient);
            })
            .Where(r => r.ItemsDone < r.Items).Take(200).ToList();
    }

    private static bool Done(int? prescribed, int dispensed) => prescribed is { } q ? dispensed >= q : dispensed > 0;

    /// <summary>Units dispensed so far per prescription item.</summary>
    private async Task<Dictionary<Guid, int>> DispensedAsync(IReadOnlyCollection<Guid> prescriptionItemIds, CancellationToken ct) =>
        prescriptionItemIds.Count == 0 ? [] : await db.StockMovements.AsNoTracking()
            .Where(m => m.Kind == StockMovementKind.Dispense && m.PrescriptionItemId != null && prescriptionItemIds.Contains(m.PrescriptionItemId.Value))
            .GroupBy(m => m.PrescriptionItemId!.Value).Select(g => new { g.Key, Sum = -g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    /// <summary>
    /// One prescription opened to dispense it: 403 for non-dispensers before anything is looked up, 404 for an unknown or another clinic's
    /// prescription, then the department open (area dispensing, logged). Each drug with what is dispensed and left, and the stock item that
    /// sells under its price item (with the usable stock).
    /// </summary>
    public async Task<DispensePrescriptionDto> PrescriptionAsync(Guid id, CancellationToken ct)
    {
        access.EnsureDispenser(await access.CallerAsync(ct));
        var patientId = await db.Prescriptions.AsNoTracking().Where(p => p.Id == id).Select(p => (Guid?)p.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Prescription");
        await guard.OpenOrderAsync(patientId, RecordGuard.Areas.Dispensing, ct);

        var prescription = await db.Prescriptions.AsNoTracking().Include(p => p.Items).FirstAsync(p => p.Id == id, ct);
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);
        var itemIds = prescription.Items.Select(i => i.Id).ToList();
        var dispensed = await DispensedAsync(itemIds, ct);
        var priceIds = prescription.Items.Select(i => i.PriceItemId).OfType<Guid>().ToList();
        var stock = await db.DrugItems.AsNoTracking().Where(d => d.PriceItemId != null && priceIds.Contains(d.PriceItemId.Value) && d.Active).ToListAsync(ct);
        var stockDtos = (await ToItemDtosAsync(stock, ct)).ToDictionary(s => s.PriceItemId!.Value);
        var dispensings = await db.Dispensings.AsNoTracking().Where(d => d.PrescriptionId == id).OrderBy(d => d.At).ToListAsync(ct);
        var dispensingIds = dispensings.Select(d => d.Id).ToList();
        var lines = await db.StockMovements.AsNoTracking().Where(m => m.DispensingId != null && dispensingIds.Contains(m.DispensingId.Value))
            .Join(db.DrugItems, m => m.DrugItemId, i => i.Id, (m, i) => new { m, i.Name })
            .Join(db.DrugBatches, x => x.m.DrugBatchId, b => b.Id, (x, b) => new { x.m, x.Name, b.BatchNumber, b.ExpiresOn }).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, dispensings.Select(d => d.DispensedBy).Append(prescription.DoctorUserId), ct);
        return new DispensePrescriptionDto(prescription.Id, patient.Id, patient.Number, $"{patient.FirstName} {patient.LastName}",
            names.GetValueOrDefault(prescription.DoctorUserId, ""), prescription.CreatedAt,
            prescription.Items.OrderBy(i => i.Position).Select(i =>
            {
                var done = dispensed.GetValueOrDefault(i.Id);
                var s = i.PriceItemId is { } pid ? stockDtos.GetValueOrDefault(pid) : null;
                return new DispenseItemDto(i.Id, text.Read(i.Drug) ?? "", text.Read(i.Strength), text.Read(i.Dose) ?? "", i.Frequency, i.DurationDays, i.Quantity,
                    done, i.Quantity is { } q ? Math.Max(0, q - done) : null, s?.Id, s?.Name, s?.OnHand);
            }).ToList(),
            dispensings.Select(d => new DispensingDto(d.Id, names.GetValueOrDefault(d.DispensedBy, ""), d.At,
                lines.Where(l => l.m.DispensingId == d.Id).Select(l => new DispensedLineDto(l.m.PrescriptionItemId!.Value, l.m.DrugItemId, l.Name, l.m.DrugBatchId,
                    l.BatchNumber, l.ExpiresOn, -l.m.Quantity)).ToList())).ToList());
    }

    public const string OverDispense = "That's more than was prescribed.";

    /// <summary>
    /// Dispenses (part of) a prescription: dispensers only. Each line names a prescription item, the stock item (default: the one selling
    /// under the drug's price item) and the quantity (default: what is left of the prescribed quantity). Stock is taken first-expiry-first-out
    /// from batches that haven't expired, under the prescription's lock and then each item's lock (in id order), in one transaction: two
    /// dispensers never take the same units and stock never goes below zero. In order: 403; 400 <c>lines</c> / <c>lines[i].…</c>; 404 for
    /// an unknown or another clinic's prescription; 409 <c>over_dispense</c> beyond the prescribed quantity, <c>insufficient_stock</c> when
    /// the usable stock is short (nothing is taken then). A stock item that falls to its reorder level tells the pharmacy. Audited (ids only).
    /// </summary>
    public async Task<DispensingDto> DispenseAsync(Guid prescriptionId, DispenseRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureDispenser(caller);
        if (r.Lines is null || r.Lines.Count == 0) throw BadRequestException.Field("lines", "Choose what to dispense.");
        if (r.Lines.Count > PrescriptionService.MaxItems) throw BadRequestException.Field("lines", $"Dispense at most {PrescriptionService.MaxItems} drugs at a time.");
        var seen = new HashSet<Guid>();
        for (var i = 0; i < r.Lines.Count; i++)
        {
            if (r.Lines[i]?.PrescriptionItemId is not { } pid || !seen.Add(pid)) throw BadRequestException.Field($"lines[{i}].prescriptionItemId", "Choose each drug once.");
            if (r.Lines[i].Quantity is < 1 or > 100_000) throw BadRequestException.Field($"lines[{i}].quantity", "Enter a quantity from 1 to 100,000.");
        }

        var now = clock.GetUtcNow();
        var today = CareCalendar.Today(now);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(PrescriptionLockKey(TenantId, prescriptionId), ct);
        var prescription = await db.Prescriptions.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == prescriptionId, ct)
            ?? throw new NotFoundException("Prescription");

        // Resolve each line: the prescription item, its stock item and the quantity (validated before any lock on stock).
        var priceIds = prescription.Items.Select(i => i.PriceItemId).OfType<Guid>().ToList();
        var requestedItems = r.Lines.Select(l => l.ItemId).OfType<Guid>().ToList();
        var stockItems = await db.DrugItems.AsNoTracking()
            .Where(d => d.Active && (requestedItems.Contains(d.Id) || (d.PriceItemId != null && priceIds.Contains(d.PriceItemId.Value)))).ToListAsync(ct);
        var dispensed = await DispensedAsync(prescription.Items.Select(i => i.Id).ToList(), ct);
        var plan = new List<(PrescriptionItem Item, DrugItem Stock, int Quantity)>();
        for (var i = 0; i < r.Lines.Count; i++)
        {
            var line = r.Lines[i];
            var item = prescription.Items.FirstOrDefault(x => x.Id == line.PrescriptionItemId)
                ?? throw BadRequestException.Field($"lines[{i}].prescriptionItemId", "That drug isn't on this prescription.");
            var stock = line.ItemId is { } sid
                ? stockItems.FirstOrDefault(s => s.Id == sid) ?? throw BadRequestException.Field($"lines[{i}].itemId", "Choose an active stock item.")
                : stockItems.FirstOrDefault(s => s.PriceItemId is { } p && p == item.PriceItemId)
                  ?? throw BadRequestException.Field($"lines[{i}].itemId", "Choose the stock item to dispense this drug from.");
            var done = dispensed.GetValueOrDefault(item.Id);
            var left = item.Quantity is { } q ? q - done : (int?)null;
            var quantity = line.Quantity ?? left ?? throw BadRequestException.Field($"lines[{i}].quantity", "Enter how many to dispense.");
            if (left is { } l && quantity > l)
                throw new ConflictException(l <= 0 ? "This drug has already been dispensed in full." : $"{OverDispense} {l} left to dispense.", "over_dispense");
            plan.Add((item, stock, quantity));
        }

        await LockItemsAsync(plan.Select(p => p.Stock.Id), ct);
        var dispensing = new Dispensing { TenantId = TenantId, PrescriptionId = prescription.Id, PatientId = prescription.PatientId, DispensedBy = caller.UserId, At = now };
        db.Dispensings.Add(dispensing);
        var stockIds = plan.Select(p => p.Stock.Id).Distinct().ToList();
        var batches = await db.DrugBatches.Where(b => stockIds.Contains(b.DrugItemId) && b.QuantityOnHand > 0 && b.ExpiresOn >= today)
            .OrderBy(b => b.ExpiresOn).ThenBy(b => b.CreatedAt).ThenBy(b => b.Id).ToListAsync(ct);
        foreach (var (item, stock, quantity) in plan)
        {
            var available = batches.Where(b => b.DrugItemId == stock.Id).ToList();
            var total = available.Sum(b => b.QuantityOnHand);
            if (total < quantity)
                throw new ConflictException($"Not enough {stock.Name} in stock: {total} {stock.Unit} left that hasn't expired.", "insufficient_stock");
            var need = quantity;
            foreach (var batch in available)
            {
                if (need == 0) break;
                var take = Math.Min(need, batch.QuantityOnHand);
                if (take == 0) continue;
                batch.QuantityOnHand -= take;
                need -= take;
                db.StockMovements.Add(new StockMovement
                {
                    TenantId = TenantId, DrugItemId = stock.Id, DrugBatchId = batch.Id, Kind = StockMovementKind.Dispense, Quantity = -take,
                    BalanceAfter = batch.QuantityOnHand, ByUserId = caller.UserId, At = now, DispensingId = dispensing.Id, PrescriptionItemId = item.Id,
                });
            }
        }
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.dispensed", EntityType = nameof(Prescription), EntityId = prescription.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { prescriptionId = prescription.Id, patientId = prescription.PatientId, dispensingId = dispensing.Id, lines = plan.Count }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await RefreshLowStockAsync(stockIds, now, ct);
        if (tx is not null) await tx.CommitAsync(ct);

        var name = (await ConsultationService.NamesAsync(db, [caller.UserId], ct)).GetValueOrDefault(caller.UserId, "");
        var taken = db.ChangeTracker.Entries<StockMovement>().Select(e => e.Entity).Where(m => m.DispensingId == dispensing.Id).ToList();
        return new DispensingDto(dispensing.Id, name, now, taken.Select(m =>
        {
            var batch = batches.First(b => b.Id == m.DrugBatchId);
            return new DispensedLineDto(m.PrescriptionItemId!.Value, m.DrugItemId, plan.First(p => p.Stock.Id == m.DrugItemId).Stock.Name, batch.Id, batch.BatchNumber,
                batch.ExpiresOn, -m.Quantity);
        }).ToList());
    }

    // ---------- Job ----------

    /// <summary>
    /// The care job's daily pharmacy check (Task 13 pattern, idempotent): batches holding stock that expire within 90 days and haven't been
    /// told are marked and told once in one notice; then the low-stock rule runs for every active item (expiry can drop usable stock).
    /// Returns how many batches were newly flagged.
    /// </summary>
    public async Task<int> CheckStockAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = CareCalendar.Today(now);
        var limit = today.AddDays(NearExpiryDays);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var marked = await db.DrugBatches.Where(b => b.QuantityOnHand > 0 && b.ExpiresOn <= limit && b.ExpiryNotifiedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ExpiryNotifiedAt, (DateTimeOffset?)now), ct);
        if (marked > 0)
        {
            await notices.NearExpiryAsync(marked, NearExpiryDays, ct);
            await db.SaveChangesAsync(ct);
        }
        await NotifyLowStockAsync(db, notices, null, today, now, ct);
        await tx.CommitAsync(ct);
        return marked;
    }
}
