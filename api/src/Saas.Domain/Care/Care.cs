using Saas.Domain.Common;

namespace Saas.Domain.Care;

/// <summary>
/// A clinic member's clinical role, on top of their platform <c>MemberRole</c>. Doctor and Nurse have clinical access;
/// FrontDesk and None do not (the Owner always has it).
/// </summary>
public enum ClinicalRole { None, Doctor, Nurse, FrontDesk }

/// <summary>The clinic behind the tenant: name, contact details, numbering and billing defaults. One per tenant.</summary>
// Not audited: the audit log would otherwise copy the encrypted Paystack key.
public class ClinicProfile : TenantEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? LogoKey { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    /// <summary>Patient numbers are {prefix}-{000000}.</summary>
    public string PatientPrefix { get; set; } = "CL";
    /// <summary>Atomic counters (<c>NextDocumentNumberAsync</c>): patient, bill and receipt numbers, per clinic.</summary>
    public long NextPatientNumber { get; set; } = 1;
    public long NextBillNumber { get; set; } = 1;
    public long NextReceiptNumber { get; set; } = 1;
    /// <summary>The clinic's own Paystack secret key, encrypted with <c>ISecretProtector</c>.</summary>
    public string? PaystackSecretKeyProtected { get; set; }
    /// <summary>The key replaced last (encrypted), still accepted until <see cref="PreviousKeyUntil"/> so checkouts started under it confirm.</summary>
    public string? PaystackPreviousKeyProtected { get; set; }
    public DateTimeOffset? PreviousKeyUntil { get; set; }
    /// <summary>When true, prescribed drugs with a price item go on the bill.</summary>
    public bool DispensesDrugs { get; set; } = true;
    /// <summary>Default minimum admission deposit in kobo (a ward can set its own).</summary>
    public long MinDepositMinor { get; set; }
    /// <summary>An admission's deposit balance below this (kobo) raises a low-deposit notice.</summary>
    public long LowDepositThresholdMinor { get; set; }
    /// <summary>Price item charged on a patient's first visit.</summary>
    public Guid? RegistrationPriceItemId { get; set; }
    /// <summary>Price item charged at check-in for a consultation.</summary>
    public Guid? ConsultationPriceItemId { get; set; }
    /// <summary>The Lagos day the care job last ran the nightly bed charges (Task 13): the step runs once a day.</summary>
    public DateOnly? LastBedChargeDay { get; set; }
}

/// <summary>A clinic member as the clinic sees them: clinical role, title, licence and signature. One per membership.</summary>
[Audited]
public class CareStaffProfile : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public ClinicalRole ClinicalRole { get; set; }
    /// <summary>Display title such as "Consultant physician".</summary>
    public string? Title { get; set; }
    public string? LicenceNumber { get; set; }
    /// <summary>Private image key of the signature printed on signed documents.</summary>
    public string? SignatureKey { get; set; }
}

/// <summary>What a price-list item is for; reports group revenue by it.</summary>
public enum PriceCategory { Consultation, Procedure, Test, Drug, Bed, Registration, Other }

/// <summary>Something the clinic charges for. Bills copy the price at charge time, so editing it never changes an existing bill.</summary>
[Audited]
public class PriceItem : TenantEntity
{
    public required string Name { get; set; }
    public PriceCategory Category { get; set; }
    /// <summary>Price in kobo.</summary>
    public long PriceMinor { get; set; }
    /// <summary>Inactive items stay on old bills but can't be charged again.</summary>
    public bool Active { get; set; } = true;
}

public enum PatientSex { Female, Male }

/// <summary>Inactive replaces deleting a patient (records are never deleted); Deceased blocks new visits and appointments.</summary>
public enum PatientStatus { Active, Inactive, Deceased }

/// <summary>A clinic's patient. Demographics are for every staff member; blood group, genotype, allergies and conditions are clinical.</summary>
// Not [Audited]: the automatic audit copies values, and clinical facts must never reach the audit log in plain text.
// PatientService writes explicit audit rows instead (action and changed demographic field names only).
public class Patient : TenantEntity
{
    /// <summary>{prefix}-{000000}, from the clinic's patient counter.</summary>
    public required string Number { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public PatientSex Sex { get; set; }
    public DateOnly DateOfBirth { get; set; }
    /// <summary>Normalised (+234…), so the same number typed two ways matches.</summary>
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? NextOfKinName { get; set; }
    public string? NextOfKinPhone { get; set; }
    public string? NextOfKinRelationship { get; set; }
    public string? BloodGroup { get; set; }
    public string? Genotype { get; set; }
    /// <summary>Each entry is clinical free text, stored protected with <c>ISecretProtector</c>.</summary>
    public List<string> Allergies { get; set; } = [];
    /// <summary>Key conditions; each entry stored protected with <c>ISecretProtector</c>.</summary>
    public List<string> Conditions { get; set; } = [];
    /// <summary>
    /// Final review I1: bumped by every clinical-facts save. A save names the version it loaded and is refused (409) when it is stale, so
    /// two clinicians editing the allergy list at once never silently drop an entry. 0 until the first save.
    /// </summary>
    public int FactsVersion { get; set; }
    public PatientStatus Status { get; set; } = PatientStatus.Active;
}

/// <summary>
/// Unpaid, part paid, paid, waived (discount and waiver adjustments cleared what was owed), or in credit: the clinic holds more than the
/// bill's net amount (an admission deposit before the charges arrive, or an online overpayment). In credit the balance is 0 and
/// <see cref="PatientBill.CreditMinor"/> shows the money held (Task 11 ruling).
/// </summary>
public enum BillStatus { Unpaid, PartPaid, Paid, Waived, InCredit }

/// <summary>One bill per outpatient visit and one per admission, created on first use. Money in kobo.</summary>
[Audited]
public class PatientBill : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid? VisitId { get; set; }
    public Guid? AdmissionId { get; set; }
    /// <summary>BILL-{yyyy}-{00000}.</summary>
    public required string Number { get; set; }
    public BillStatus Status { get; set; } = BillStatus.Unpaid;
    /// <summary>Sum of the lines that aren't removed.</summary>
    public long TotalMinor { get; set; }
    public long PaidMinor { get; set; }
    public long BalanceMinor { get; set; }
    /// <summary>
    /// Set when the bill is final (an admission bill at discharge, Task 10): no new lines can be added from then on. Payments are still
    /// taken, so a balance left at discharge stays open as a debt.
    /// </summary>
    public DateTimeOffset? FinalisedAt { get; set; }
    /// <summary>Sum of the bill's discount and waiver adjustments (Task 11). Net amount = max(0, Total − Adjusted).</summary>
    public long AdjustedMinor { get; set; }
    /// <summary>Money held beyond the net amount (Paid − net, at least 0). Shown as credit; the balance is then 0.</summary>
    public long CreditMinor { get; set; }
    /// <summary>Set when an online payment landed for more than the balance at that moment: the cashier checks it (refund, or keep as credit).</summary>
    public bool NeedsReview { get; set; }
}

/// <summary>A discount (part of the bill) or a waiver (forgiving what is owed).</summary>
public enum AdjustmentKind { Discount, Waiver }

/// <summary>
/// A discount or waiver on a bill by the medical director or an admin, with a reason. A fixed amount of at most the balance when granted,
/// so it covers only what was on the bill then: a later charge is owed. Not [Audited]: an explicit audit row (ids and amounts) is written.
/// </summary>
public class BillAdjustment : TenantEntity
{
    public Guid BillId { get; set; }
    public Guid PatientId { get; set; }
    public AdjustmentKind Kind { get; set; }
    public long AmountMinor { get; set; }
    public required string Reason { get; set; }
    public Guid ByUserId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Auto lines come from clinical work (check-in, tests, drugs, bed days); manual lines are added by the cashier or leadership with a reason.</summary>
public enum BillLineSource { Auto, Manual }

/// <summary>
/// A charge on a bill. The unit price is frozen at charge time. Lines are never deleted: a removal keeps the line with its reason and takes
/// it out of the total. <see cref="ClinicalRef"/> (for example a prescription item id) is only ever shown to clinical staff.
/// </summary>
[Audited]
public class BillLine : TenantEntity
{
    public Guid BillId { get; set; }
    public Guid? PriceItemId { get; set; }
    /// <summary>The price item's name at charge time.</summary>
    public required string Description { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceMinor { get; set; }
    public long AmountMinor { get; set; }
    public BillLineSource Source { get; set; }
    [AuditIgnore]
    public string? ClinicalRef { get; set; }
    /// <summary>Why a manual line was added.</summary>
    public string? Reason { get; set; }
    public Guid? AddedByUserId { get; set; }
    public string? RemovedReason { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    /// <summary>
    /// The Lagos day a bed line charges for (Task 13); null for every other line. Unique per (bill, price item, day), so a bed day is
    /// charged at most once. Revenue counts a bed line on this day.
    /// </summary>
    public DateOnly? ChargeDay { get; set; }
}

/// <summary>
/// Why a staff member has a care relationship with a patient (Task 5 writes visit and triage rows, Task 8 admission and ward rows, Task 16
/// covering doctors the admitting doctor or the director adds to an admission's care team).
/// </summary>
public enum CareAssignmentKind { VisitDoctor, TriageNurse, AdmittingDoctor, WardNurse, CoveringDoctor }

/// <summary>
/// A care relationship: this staff member may open this patient's clinical record without giving a reason. Active until
/// <see cref="EndedAt"/> (null: no end; a visit's assignments end 30 days after it is Done). Anyone else with clinical access must give a reason (emergency access), which is logged and reported.
/// </summary>
public class CareAssignment : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid StaffUserId { get; set; }
    public CareAssignmentKind Kind { get; set; }
    public Guid? VisitId { get; set; }
    public Guid? AdmissionId { get; set; }
    /// <summary>For ward nurses: the ward the patient was admitted to.</summary>
    public Guid? WardId { get; set; }
    /// <summary>Set when the relationship ends (for example the nurse leaves the ward); may be in the future (a visit's follow-up window); from then on the row no longer opens the record.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>
/// One opening of a patient's clinical record by a staff member (NDPA access log). Holds who, which role, which area and why; never any
/// clinical value. Not [Audited]: every clinical read writes one, and emergency access writes its own audit row.
/// </summary>
public class RecordAccess : TenantEntity
{
    /// <summary>Who read: the staff member, or (when <see cref="Portal"/>) the portal account.</summary>
    public Guid StaffUserId { get; set; }
    /// <summary>
    /// The clinic role label at the time: "Medical director", "Doctor" or "Nurse"; for a portal read, the account's relationship to the
    /// patient ("self", "parent" or "guardian").
    /// </summary>
    public required string Role { get; set; }
    /// <summary>Final review C1: a read through the patient portal by a linked account, not a staff open.</summary>
    public bool Portal { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>What was opened: record, consultation, admission, prescription, result, discharge, pdf or file.</summary>
    public required string Area { get; set; }
    /// <summary>The reason given for emergency access (no care relationship).</summary>
    public string? Reason { get; set; }
    public bool Emergency { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// One block of a doctor's weekly clinic hours, in Africa/Lagos wall-clock time (<see cref="TimeOnly"/>, no date and no zone).
/// A doctor may have several blocks on one weekday (a morning and an afternoon) as long as they don't overlap.
/// </summary>
[Audited]
public class DoctorSchedule : TenantEntity
{
    public Guid DoctorUserId { get; set; }
    public DayOfWeek Weekday { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int SlotMinutes { get; set; } = 15;
}

public enum AppointmentStatus { Booked, CheckedIn, Done, Cancelled, NoShow }

/// <summary>Who made the booking: the front desk, or the patient through the portal.</summary>
public enum AppointmentSource { Desk, Portal }

/// <summary>
/// A booked slot with a doctor. <see cref="Start"/> and <see cref="End"/> are UTC instants (a Lagos slot at 09:00 is 08:00Z).
/// Holds no clinical text: <see cref="Reason"/> is a short booking reason in plain text that every non-cashier staff screen shows.
/// </summary>
[Audited]
public class Appointment : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid DoctorUserId { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    /// <summary>A short, non-clinical booking reason (at most 120 characters). Not copied into the audit log.</summary>
    [AuditIgnore]
    public string? Reason { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;
    public AppointmentSource Source { get; set; } = AppointmentSource.Desk;
    public Guid? BookedByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    /// <summary>When the day-before reminder went out (Task 13); cleared when the appointment moves to another time.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }
}

/// <summary>Where an outpatient visit is in the clinic's flow, in order.</summary>
public enum VisitStage { WaitingVitals, WaitingDoctor, WithDoctor, WaitingPayment, Done }

/// <summary>How soon the patient must be seen, set at triage. The queue puts Emergency first.</summary>
public enum Urgency { Routine, Urgent, Emergency }

/// <summary>
/// One outpatient visit: the patient arrived (checked in), had vitals taken, saw a doctor, paid. Holds no clinical value: stage, urgency
/// and times are what the queue shows every staff member. Vital signs live in <see cref="VitalSigns"/>, read by clinical staff only.
/// </summary>
[Audited]
public class Visit : TenantEntity
{
    public Guid PatientId { get; set; }
    /// <summary>Check-in time (UTC). Minutes waited count from here.</summary>
    public DateTimeOffset ArrivedAt { get; set; }
    public VisitStage Stage { get; set; } = VisitStage.WaitingVitals;
    public Urgency Urgency { get; set; } = Urgency.Routine;
    /// <summary>The doctor seeing the patient: the appointment's doctor at check-in, or the one chosen at triage.</summary>
    public Guid? DoctorUserId { get; set; }
    /// <summary>The appointment this visit checked in (set to CheckedIn at check-in).</summary>
    public Guid? AppointmentId { get; set; }
    public Guid? CheckedInByUserId { get; set; }
    /// <summary>When the visit reached <see cref="VisitStage.Done"/>; the wait stops counting here.</summary>
    public DateTimeOffset? DoneAt { get; set; }
}

/// <summary>
/// One set of vital signs, taken at triage (a visit) or on the ward (an admission, Task 9). Clinical: only doctors and nurses read it,
/// through RecordGuard. Not [Audited]: the automatic audit would copy the values. <see cref="Flags"/> come from <c>Vitals.Flag</c>.
/// </summary>
public class VitalSigns : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid? VisitId { get; set; }
    public Guid? AdmissionId { get; set; }
    /// <summary>The staff member who took them.</summary>
    public Guid TakenBy { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public decimal? TemperatureC { get; set; }
    public int? Systolic { get; set; }
    public int? Diastolic { get; set; }
    public int? Pulse { get; set; }
    public int? RespRate { get; set; }
    public int? SpO2 { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    /// <summary>Computed from weight and height when both are given.</summary>
    public decimal? Bmi { get; set; }
    /// <summary>Out-of-range readings, such as "High temperature" or "Critical SpO2".</summary>
    public List<string> Flags { get; set; } = [];
}

/// <summary>A clinical note's state. A signed note is final: corrections are addenda.</summary>
public enum NoteStatus { Draft, Signed }

/// <summary>The kind of note an addendum is attached to.</summary>
public enum NoteKind { Consultation, WardRound, Discharge }

/// <summary>
/// The doctor's note for one outpatient visit (one per visit). Complaint, history, examination and plan are clinical free text, stored
/// through <c>ClinicalText</c> ("" when blank). A signed note never changes; corrections are <see cref="Addendum"/> rows.
/// Not [Audited]: the automatic audit would copy clinical text (signing writes its own audit row).
/// </summary>
public class Consultation : TenantEntity
{
    public Guid VisitId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The author: the doctor who opened the consultation. Only the author edits or signs the draft.</summary>
    public Guid DoctorUserId { get; set; }
    public string Complaint { get; set; } = "";
    public string History { get; set; } = "";
    public string Examination { get; set; } = "";
    public string Plan { get; set; } = "";
    public NoteStatus Status { get; set; } = NoteStatus.Draft;
    public DateTimeOffset? SignedAt { get; set; }
}

/// <summary>
/// A diagnosis made in a consultation (or, from Task 8, an admission). <see cref="Text"/> is clinical free text stored through
/// <c>ClinicalText</c>; <see cref="Code"/> is an optional code from the built-in list (<c>DiagnosisList</c>). Not [Audited].
/// </summary>
public class Diagnosis : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid? ConsultationId { get; set; }
    public Guid? AdmissionId { get; set; }
    public string Text { get; set; } = "";
    public string? Code { get; set; }
    public bool Primary { get; set; }
}

/// <summary>
/// A correction or addition to a signed note, with its author and time; the original note stays as it was. <see cref="Text"/> is
/// clinical free text stored through <c>ClinicalText</c>. Addenda are never edited or deleted. Not [Audited].
/// </summary>
public class Addendum : TenantEntity
{
    public NoteKind NoteKind { get; set; }
    /// <summary>The consultation, ward round or discharge summary id (by <see cref="NoteKind"/>; no foreign key).</summary>
    public Guid NoteId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// How often a prescribed dose is given: once daily, twice, three and four times daily, every 4, 6 or 8 hours, at night, once now (Stat)
/// or as needed (PRN). <see cref="FrequencyDosesPerDay"/> turns it into scheduled doses (Task 9's medication chart).
/// </summary>
public enum DoseFrequency { OD, BD, TDS, QDS, Q4H, Q6H, Q8H, Nocte, Stat, PRN }

/// <summary>
/// Scheduled doses per day for each <see cref="DoseFrequency"/>: OD 1, BD 2, TDS 3, QDS 4, Q4H 6, Q6H 4, Q8H 3, Nocte 1, Stat 1 (once
/// only, not daily) and PRN 0 (given as needed, never scheduled). Task 9 expands an admission prescription into doses with it.
/// </summary>
public static class FrequencyDosesPerDay
{
    public static int For(DoseFrequency frequency) => frequency switch
    {
        DoseFrequency.OD => 1,
        DoseFrequency.BD => 2,
        DoseFrequency.TDS => 3,
        DoseFrequency.QDS => 4,
        DoseFrequency.Q4H => 6,
        DoseFrequency.Q6H => 4,
        DoseFrequency.Q8H => 3,
        DoseFrequency.Nocte => 1,
        DoseFrequency.Stat => 1,
        DoseFrequency.PRN => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null),
    };

    /// <summary>Stat: one dose, given once (not one a day).</summary>
    public static bool IsOnce(DoseFrequency frequency) => frequency == DoseFrequency.Stat;

    /// <summary>PRN: given as needed, so nothing is scheduled.</summary>
    public static bool IsAsNeeded(DoseFrequency frequency) => frequency == DoseFrequency.PRN;
}

/// <summary>
/// A doctor's prescription for a patient, from a consultation (outpatient) or an admission (Task 8). The items hold the drugs.
/// <see cref="AllergyAcknowledged"/> records that the doctor prescribed despite a recorded allergy match. Not [Audited]: the automatic
/// audit would copy clinical text (an allergy override writes its own audit row, with ids only).
/// </summary>
public class Prescription : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid? ConsultationId { get; set; }
    /// <summary>The admission it was written on. Task 8 adds the admission entity; Task 9 expands these into medication doses.</summary>
    public Guid? AdmissionId { get; set; }
    public Guid DoctorUserId { get; set; }
    public bool AllergyAcknowledged { get; set; }
    /// <summary>The recorded allergies the doctor prescribed over (as recorded then), each stored through <c>ClinicalText</c>. Empty without an override.</summary>
    public List<string> OverriddenAllergens { get; set; } = [];
    public List<PrescriptionItem> Items { get; set; } = [];
}

/// <summary>
/// One drug on a prescription. <see cref="Drug"/>, <see cref="Strength"/>, <see cref="Dose"/>, <see cref="Route"/> and
/// <see cref="Instructions"/> are clinical text stored through <c>ClinicalText</c> ("" when blank), so no raw column names a drug.
/// <see cref="Frequency"/>, <see cref="DurationDays"/> and <see cref="Quantity"/> are plain numbers. With a <see cref="PriceItemId"/>
/// and a clinic that dispenses drugs, the item is charged on the bill (the bill line shows the price item's name). Not [Audited].
/// </summary>
public class PrescriptionItem : TenantEntity
{
    public Guid PrescriptionId { get; set; }
    /// <summary>Order on the prescription (0-based).</summary>
    public int Position { get; set; }
    public string Drug { get; set; } = "";
    public string? Strength { get; set; }
    public string Dose { get; set; } = "";
    public DoseFrequency Frequency { get; set; }
    public int? DurationDays { get; set; }
    public int? Quantity { get; set; }
    public string? Route { get; set; }
    public string? Instructions { get; set; }
    public Guid? PriceItemId { get; set; }
}

public enum TestKind { Lab, Imaging, Other }

public enum TestStatus { Requested, Resulted }

/// <summary>
/// A test a doctor requested (lab, imaging or other) and, once done, its result: text (<see cref="ResultText"/>, stored through
/// <c>ClinicalText</c>) and/or a private file (<see cref="ResultFileKey"/>, served by short-lived signed links). <see cref="Description"/>
/// names the test and is clinical text too. Not [Audited].
/// </summary>
public class TestRequest : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid? ConsultationId { get; set; }
    public Guid? AdmissionId { get; set; }
    public Guid RequestedBy { get; set; }
    public TestKind Kind { get; set; }
    public string Description { get; set; } = "";
    public Guid? PriceItemId { get; set; }
    public TestStatus Status { get; set; } = TestStatus.Requested;
    public string? ResultText { get; set; }
    public string? ResultFileKey { get; set; }
    /// <summary>The stored file's content type: application/pdf or image/webp (images are re-encoded).</summary>
    public string? ResultFileType { get; set; }
    public Guid? ResultedBy { get; set; }
    public DateTimeOffset? ResultedAt { get; set; }
    /// <summary>
    /// Post-build (user decision): a result shows in the patient's portal, and the patient is told, only once a doctor or the medical director
    /// has reviewed it. Results recorded before this change were marked reviewed at their recorded time (no reviewer).
    /// </summary>
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>What kind of ward it is (for the bed board and reports).</summary>
public enum WardKind { General, Male, Female, Maternity, Paediatric, Surgical, Intensive, Private, Other }

/// <summary>
/// A ward: its beds, the daily bed charge (a Bed price-list item, charged by Task 13's job) and its own minimum admission deposit (null:
/// the clinic default). <see cref="NurseUserIds"/> are the nurses on this ward: they get a ward-nurse care assignment for every patient
/// in it. An empty list means every active nurse in the clinic (a small clinic with one nursing team).
/// </summary>
[Audited]
public class Ward : TenantEntity
{
    public required string Name { get; set; }
    public WardKind Kind { get; set; }
    public Guid BedPriceItemId { get; set; }
    /// <summary>Minimum deposit in kobo; null uses the clinic's <see cref="ClinicProfile.MinDepositMinor"/>.</summary>
    public long? MinDepositMinor { get; set; }
    public List<Guid> NurseUserIds { get; set; } = [];
}

/// <summary>Free beds can be claimed; an occupied bed holds one admission; a bed a patient left goes to Cleaning until staff mark it ready.</summary>
public enum BedStatus { Free, Occupied, Cleaning }

/// <summary>A bed in a ward. Active beds count against the plan's <c>beds</c> limit; an inactive bed stays for history but can't be used.</summary>
[Audited]
public class Bed : TenantEntity
{
    public Guid WardId { get; set; }
    public required string Label { get; set; }
    public BedStatus Status { get; set; } = BedStatus.Free;
    public bool Active { get; set; } = true;
}

public enum AdmissionStatus { Admitted, Discharged }

/// <summary>How the patient left (Task 10): home, against medical advice, transferred out, or died.</summary>
public enum DischargeType { Home, AgainstAdvice, TransferredOut, Died }

/// <summary>
/// An inpatient stay. The doctor admits (reason and admitting diagnosis, both clinical text stored through <c>ClinicalText</c>); the front
/// desk then assigns a bed (<see cref="BedId"/> is null until then) and records the deposit. Discharge fields are Task 10's.
/// Not [Audited]: the automatic audit would copy clinical text (a deposit override writes its own audit row).
/// </summary>
public class Admission : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid AdmittingDoctorUserId { get; set; }
    /// <summary>The consultation the doctor admitted from, when there was one.</summary>
    public Guid? ConsultationId { get; set; }
    public string Reason { get; set; } = "";
    public string AdmittingDiagnosis { get; set; } = "";
    public Guid? BedId { get; set; }
    public DateTimeOffset AdmittedAt { get; set; }
    /// <summary>The medical director who accepted a deposit below the ward minimum.</summary>
    public Guid? DepositOverrideBy { get; set; }
    public AdmissionStatus Status { get; set; } = AdmissionStatus.Admitted;
    public DischargeType? DischargeType { get; set; }
    public DateTimeOffset? DischargedAt { get; set; }
    public DateTimeOffset? DeathTime { get; set; }
    /// <summary>Clinical text (ClinicalText), Task 10.</summary>
    public string? DeathCause { get; set; }
    /// <summary>Clinical text (ClinicalText), Task 10.</summary>
    public string? AmaReason { get; set; }
    /// <summary>The doctor (or medical director) who discharged the patient.</summary>
    public Guid? DischargedByUserId { get; set; }
    /// <summary>How many ward-round drafts were still unsigned when the doctor discharged and acknowledged them (0 when none).</summary>
    public int UnsignedRoundsAtDischarge { get; set; }
    /// <summary>
    /// When the cashier and front desk were told the deposit ran low (Task 13); cleared when a top-up lifts it back to the threshold, so
    /// each crossing is told once.
    /// </summary>
    public DateTimeOffset? LowDepositNotifiedAt { get; set; }
}

/// <summary>
/// The discharge summary of one admission (one per admission). The diagnoses, treatment given, condition at discharge and take-home
/// instructions are clinical free text stored through <c>ClinicalText</c>. Signs like a consultation: the draft belongs to its author,
/// signing is final, corrections are addenda (<see cref="NoteKind.Discharge"/>). Home and transferred-out discharges need it signed.
/// Not [Audited]: the automatic audit would copy clinical text (signing writes its own audit row).
/// </summary>
public class DischargeSummary : TenantEntity
{
    public Guid AdmissionId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The author.</summary>
    public Guid DoctorUserId { get; set; }
    public string Diagnoses { get; set; } = "";
    public string Treatment { get; set; } = "";
    public string Condition { get; set; } = "";
    public string TakeHome { get; set; } = "";
    /// <summary>The follow-up appointment date (Lagos calendar day), if one is planned.</summary>
    public DateOnly? FollowUp { get; set; }
    public NoteStatus Status { get; set; } = NoteStatus.Draft;
    public DateTimeOffset? SignedAt { get; set; }
}

/// <summary>A bed assignment or move: <see cref="FromBedId"/> is null for the first bed. The bed charge follows the ward (Task 13).</summary>
public class BedMove : TenantEntity
{
    public Guid AdmissionId { get; set; }
    public Guid? FromBedId { get; set; }
    public Guid ToBedId { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid ByUserId { get; set; }
}

/// <summary>How money was paid: at the desk (cash, POS, transfer) or online through the clinic's Paystack key (Task 11).</summary>
public enum PayMethod { Cash, Pos, Transfer, Online }

/// <summary>
/// Succeeded (counted on the bill); Pending (an online checkout waiting for Paystack); Mismatch (Paystack reported another amount or
/// currency: not applied, needs review); Reversed (taken back out, with a reason); Abandoned (a checkout nobody finished: a soft state, a
/// later confirmation is still applied).
/// </summary>
public enum CarePaymentStatus { Succeeded, Pending, Mismatch, Reversed, Abandoned }

/// <summary>
/// Money paid towards a patient bill (a deposit on an admission bill is a payment too). A succeeded payment has a receipt number
/// RCP-{yyyy}-{00000} and is applied under the clinic's bill lock. Never edited except through the status changes above.
/// </summary>
[Audited]
public class CarePayment : TenantEntity
{
    public Guid BillId { get; set; }
    public Guid PatientId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "NGN";
    public PayMethod Method { get; set; }
    public CarePaymentStatus Status { get; set; } = CarePaymentStatus.Succeeded;
    /// <summary>The POS, transfer or teller reference the desk typed.</summary>
    public string? Reference { get; set; }
    /// <summary>Set when the payment succeeds (null while an online checkout is pending, and after a mismatch).</summary>
    public string? ReceiptNumber { get; set; }
    /// <summary>True when recorded as an admission deposit.</summary>
    public bool IsDeposit { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    /// <summary>A desk payment's idempotency key (unique per clinic): the same key returns the same payment and records nothing new.</summary>
    public string? IdempotencyKey { get; set; }
    /// <summary>Online: the Paystack reference care_{billId}_{random} (unique per clinic).</summary>
    public string? ProviderRef { get; set; }
    /// <summary>Online: the portal user who started the checkout.</summary>
    public Guid? PayerUserId { get; set; }
    /// <summary>Mismatch: what Paystack reported.</summary>
    public long? ChargedAmountMinor { get; set; }
    public string? ChargedCurrency { get; set; }
    /// <summary>Set once the clinic was told that this checkout couldn't be checked with any of its keys (told once).</summary>
    public DateTimeOffset? UnverifiedAlertedAt { get; set; }
    /// <summary>When the care job last checked this Pending checkout with Paystack (Task 13): every 10 minutes for 24 hours, then hourly.</summary>
    public DateTimeOffset? RecheckedAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public Guid? ReversedByUserId { get; set; }
    /// <summary>Free text the cashier typed: kept out of the audit rows (ids and amounts only).</summary>
    [AuditIgnore]
    public string? ReversalReason { get; set; }
}

/// <summary>
/// A nurse's (or doctor's) note on an inpatient: free text stored through <c>ClinicalText</c>, with its author and time. Never edited;
/// written only while the patient is admitted. Not [Audited]: the automatic audit would copy clinical text.
/// </summary>
public class NursingNote : TenantEntity
{
    public Guid AdmissionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>Fluid going in or coming out.</summary>
public enum FluidDirection { In, Out }

/// <summary>What the fluid was: oral, IV, blood or NG feed going in; urine, vomit, drain, stool or NG aspirate coming out; or other.</summary>
public enum FluidKind { Oral, IV, Blood, NG, Urine, Vomit, Drain, Stool, Other }

/// <summary>One line on the fluid chart: in or out, its kind and millilitres, when and by whom. Not [Audited] (clinical data).</summary>
public class FluidEntry : TenantEntity
{
    public Guid AdmissionId { get; set; }
    public Guid PatientId { get; set; }
    public FluidDirection Direction { get; set; }
    public FluidKind Kind { get; set; }
    public int Ml { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// A doctor's ward-round note on an inpatient. Signs like a consultation (Task 6): the draft belongs to its author, signing is final,
/// corrections are addenda (<see cref="NoteKind.WardRound"/>). <see cref="Note"/> is clinical text stored through <c>ClinicalText</c>.
/// Not [Audited].
/// </summary>
public class WardRound : TenantEntity
{
    public Guid AdmissionId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The author.</summary>
    public Guid DoctorUserId { get; set; }
    public string Note { get; set; } = "";
    public NoteStatus Status { get; set; } = NoteStatus.Draft;
    public DateTimeOffset? SignedAt { get; set; }
}

/// <summary>
/// A scheduled dose's state: Due until a nurse records it as Given, Held or Refused. Task 13's job marks a Due dose Missed 60 minutes
/// after <see cref="MedicationDose.DueAt"/>; a Missed dose can still be recorded (a late dose). Stopped: the course ended before the dose
/// was due (discharge, Task 10, through <c>MedicationChart.StopFutureDosesAsync</c>); never recorded and never Missed.
/// </summary>
public enum DoseStatus { Due, Given, Held, Refused, Missed, Stopped }

/// <summary>
/// One scheduled dose of an admission prescription item (the medication chart). Written when the admission prescription is created
/// (<c>MedicationChart.Schedule</c>); recorded once by ward staff. <see cref="Note"/> is clinical text stored through <c>ClinicalText</c>.
/// Not [Audited].
/// </summary>
public class MedicationDose : TenantEntity
{
    public Guid AdmissionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid PrescriptionId { get; set; }
    public Guid PrescriptionItemId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DoseStatus Status { get; set; } = DoseStatus.Due;
    public Guid? RecordedByUserId { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>How a portal account stands to the patient it is linked to (Task 15): the patient themself, a parent or a guardian.</summary>
public enum PatientLinkRelationship { Self, Parent, Guardian }

/// <summary>
/// A portal account (a customer account, no membership) linked to one of the clinic's patients, the way SchoolLink links parents to
/// guardian records. One account can be linked to several patients in several clinics. Created when an invite is accepted; the clinic can
/// revoke it (<see cref="Active"/> false), after which the account no longer reaches the patient and gets no notices. Unique per patient
/// and account. Not [Audited]: explicit audit rows (ids only) are written.
/// </summary>
public class PatientLink : TenantEntity
{
    public Guid PatientId { get; set; }
    /// <summary>The portal (customer) account.</summary>
    public Guid UserId { get; set; }
    public PatientLinkRelationship Relationship { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>The invite it was accepted from (the latest one, when an account is linked again).</summary>
    public Guid? InviteId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}

/// <summary>
/// A single-use portal invite for a patient: a random token, stored only as its hash, valid for 14 days. Front desk or leadership sends it
/// (emailed when an address is given, and always offered as a WhatsApp link staff send themselves). Accepting it signs the account in to
/// the patient (a <see cref="PatientLink"/>). Not [Audited]: the email and phone are contact details, and the token hash is a secret.
/// </summary>
public class PatientInvite : TenantEntity
{
    public Guid PatientId { get; set; }
    public required string TokenHash { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    /// <summary>The staff member who sent it.</summary>
    public Guid? InvitedBy { get; set; }
}
