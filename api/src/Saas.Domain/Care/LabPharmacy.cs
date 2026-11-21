using Saas.Domain.Common;

namespace Saas.Domain.Care;

// ---------- Lab (wave 4, module 4c item 1) ----------

/// <summary>
/// A test on the clinic's lab catalogue: its name, the sample it needs, the turnaround target and the price-list item charged when a doctor
/// orders it. Its analytes (one or more) carry the units and reference ranges. Catalogue data, the same for every patient: not clinical.
/// </summary>
[Audited]
public class LabTest : TenantEntity
{
    public required string Name { get; set; }
    /// <summary>"Blood (EDTA)", "Urine", "Stool"...</summary>
    public required string SampleType { get; set; }
    public Guid? PriceItemId { get; set; }
    /// <summary>The target from order to result, in hours.</summary>
    public int TurnaroundHours { get; set; } = 24;
    public bool Active { get; set; } = true;
    public List<LabAnalyte> Analytes { get; set; } = [];
}

/// <summary>
/// One value a lab test reports (haemoglobin, white cells...), in catalogue order. <see cref="Ranges"/> are its reference ranges by sex and
/// age band; an analyte with no numeric range takes a text answer ("Not seen"). Not [Audited]: the test's own audit row covers it.
/// </summary>
public class LabAnalyte : TenantEntity
{
    public Guid LabTestId { get; set; }
    public int Position { get; set; }
    public required string Name { get; set; }
    public string? Unit { get; set; }
    public List<LabRange> Ranges { get; set; } = [];
}

/// <summary>
/// A reference range: for <see cref="Sex"/> (null: both) and ages from <see cref="MinAgeYears"/> (inclusive, null: birth) to
/// <see cref="MaxAgeYears"/> (exclusive, null: no limit). A value below <see cref="Low"/> or above <see cref="High"/> is abnormal; at or
/// beyond a critical limit it is critical.
/// </summary>
public class LabRange
{
    public PatientSex? Sex { get; set; }
    public int? MinAgeYears { get; set; }
    public int? MaxAgeYears { get; set; }
    public decimal? Low { get; set; }
    public decimal? High { get; set; }
    public decimal? CriticalLow { get; set; }
    public decimal? CriticalHigh { get; set; }
}

/// <summary>How a value compares with its range: no numeric range (None), normal, low, high, or critically low or high.</summary>
public enum LabFlag { None, Normal, Low, High, CriticalLow, CriticalHigh }

/// <summary>
/// One analyte's value on a lab order. Name, unit and the range used are copied from the catalogue at entry, so editing the catalogue never
/// changes a result. A numeric value is a plain number (like vital signs); a text value is clinical text stored through <c>ClinicalText</c>.
/// A corrected result keeps the earlier values with <see cref="SupersededAt"/> set. Not [Audited] (clinical data).
/// </summary>
public class LabResultValue : TenantEntity
{
    public Guid TestRequestId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? AnalyteId { get; set; }
    public int Position { get; set; }
    public required string Name { get; set; }
    public string? Unit { get; set; }
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public decimal? Low { get; set; }
    public decimal? High { get; set; }
    public decimal? CriticalLow { get; set; }
    public decimal? CriticalHigh { get; set; }
    public LabFlag Flag { get; set; }
    public Guid EnteredBy { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
}

// ---------- Pharmacy stock (wave 4, module 4c item 2) ----------

/// <summary>
/// A drug the pharmacy stocks, with the price-list item it is sold under (a prescription item with that price item is dispensed from it).
/// Stock lives in its batches. <see cref="LowStockNotifiedAt"/> is set when pharmacy staff were told it ran low and cleared when stock
/// rises above <see cref="ReorderLevel"/> again, so each crossing is told once.
/// </summary>
[Audited]
public class DrugItem : TenantEntity
{
    public required string Name { get; set; }
    /// <summary>Tablet, capsule, syrup, injection...</summary>
    public string? Form { get; set; }
    /// <summary>What one unit of stock is: "tablet", "bottle", "vial".</summary>
    public string Unit { get; set; } = "unit";
    public Guid? PriceItemId { get; set; }
    public int ReorderLevel { get; set; }
    public bool Active { get; set; } = true;
    [AuditIgnore]
    public DateTimeOffset? LowStockNotifiedAt { get; set; }
}

/// <summary>
/// A batch of a drug: its batch number, expiry date (the last day it may be used) and how much is left. <see cref="QuantityOnHand"/> never
/// goes below zero (a check constraint backs the per-item lock). Changes only through stock movements. Not [Audited]: the movements are the log.
/// </summary>
public class DrugBatch : TenantEntity
{
    public Guid DrugItemId { get; set; }
    public required string BatchNumber { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public int QuantityReceived { get; set; }
    public int QuantityOnHand { get; set; }
    /// <summary>Cost of one unit in kobo (0 when not given).</summary>
    public long UnitCostMinor { get; set; }
    public Guid? ReceiptId { get; set; }
    /// <summary>When pharmacy staff were told this batch is near expiry (told once).</summary>
    public DateTimeOffset? ExpiryNotifiedAt { get; set; }
}

/// <summary>A purchase receipt: stock that arrived from a supplier. Its lines are the batches it created.</summary>
public class StockReceipt : TenantEntity
{
    public string? Supplier { get; set; }
    public string? Reference { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public Guid ReceivedBy { get; set; }
    public long TotalCostMinor { get; set; }
}

public enum StockMovementKind { Receipt, Dispense, Adjustment }

/// <summary>
/// One change to one batch: a receipt (+), a dispense (−) or a stock-count adjustment (±, with a reason). <see cref="BalanceAfter"/> is the
/// batch's quantity right after. A dispense names the dispensing and the prescription item it filled (never shown in stock reports).
/// Never edited or deleted.
/// </summary>
public class StockMovement : TenantEntity
{
    public Guid DrugItemId { get; set; }
    public Guid DrugBatchId { get; set; }
    public StockMovementKind Kind { get; set; }
    public int Quantity { get; set; }
    public int BalanceAfter { get; set; }
    public string? Reason { get; set; }
    public Guid ByUserId { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? ReceiptId { get; set; }
    public Guid? DispensingId { get; set; }
    public Guid? PrescriptionItemId { get; set; }
}

/// <summary>One dispensing of (part of) a prescription by pharmacy staff; its stock movements say which batches were used.</summary>
public class Dispensing : TenantEntity
{
    public Guid PrescriptionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DispensedBy { get; set; }
    public DateTimeOffset At { get; set; }
}
