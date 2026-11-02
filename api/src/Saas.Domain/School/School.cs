using Saas.Domain.Common;

namespace Saas.Domain.School;

public enum Stage { Creche, Nursery, Primary, Jss, Sss }

/// <summary>How a level is assessed: numeric scores, or a developmental card.</summary>
public enum AssessmentMode { Numeric, Developmental }

/// <summary>The school behind the tenant: name, branding and the fee reminder schedule. One per tenant.</summary>
// Not audited: the audit log would otherwise copy the encrypted Paystack key.
public class SchoolProfile : TenantEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? LogoKey { get; set; }
    public string? Address { get; set; }
    public string? Motto { get; set; }
    /// <summary>Private image key of the principal's signature, printed on report cards.</summary>
    public string? SignatureKey { get; set; }
    /// <summary>When true, a parent cannot open a child's results while fees are unpaid.</summary>
    public bool ResultsHoldForUnpaidFees { get; set; }
    /// <summary>Days before a due date to send the first fee reminder.</summary>
    public int ReminderDaysBefore { get; set; } = 3;
    /// <summary>Days between reminders once a fee is overdue.</summary>
    public int ReminderEveryDays { get; set; } = 7;
    /// <summary>The school's own Paystack secret key, encrypted with <c>ISecretProtector</c>.</summary>
    public string? PaystackSecretKeyProtected { get; set; }
    /// <summary>The key replaced or removed last (encrypted), still accepted for 7 days after <see cref="PreviousKeyRetiredAt"/> so checkouts started under it confirm.</summary>
    public string? PreviousPaystackSecretKeyProtected { get; set; }
    public DateTimeOffset? PreviousKeyRetiredAt { get; set; }
    /// <summary>The school day (Africa/Lagos) the daily fee reminder job last ran for this school, so a restart doesn't run it twice.</summary>
    public DateOnly? LastRemindersOn { get; set; }
    /// <summary>The next fee invoice number to hand out (atomic counter); the formatted number is INV-{start year}-{00001}.</summary>
    public long NextInvoiceNumber { get; set; } = 1;
    /// <summary>The next fee receipt number to hand out (atomic counter); the formatted number is RCT-{year paid}-{00001}.</summary>
    public long NextReceiptNumber { get; set; } = 1;
    /// <summary>When true, a submitted lesson plan waits for the principal. When false, submit accepts it at once. Existing schools stay on.</summary>
    public bool LessonPlansNeedReview { get; set; } = true;
    /// <summary>When false, every student Play route is hidden. New and existing schools start on.</summary>
    public bool PlayEnabled { get; set; } = true;
}

/// <summary>A member of the school's team as the school sees them: job title, staff number and phone. One per membership.</summary>
[Audited]
public class StaffProfile : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public string? StaffNo { get; set; }
    public required string JobTitle { get; set; }
    public string? Phone { get; set; }
}

/// <summary>An academic year such as "2026/2027". One session per school is current.</summary>
[Audited]
public class AcademicSession : TenantEntity
{
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>One of the (usually three) terms of a session. One term per school is current.</summary>
[Audited]
public class Term : TenantEntity
{
    public Guid SessionId { get; set; }
    public int Number { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly? NextTermStarts { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>A year group such as "JSS 1". Classes (arms) hang off a level.</summary>
[Audited]
public class Level : TenantEntity
{
    public required string Name { get; set; }
    public Stage Stage { get; set; }
    public int Order { get; set; }
    /// <summary>Students in this level sign in themselves (true for Primary, JSS and SSS on the Nigerian template).</summary>
    public bool HasStudentLogins { get; set; }
    /// <summary>Numeric scores, or a developmental card. Existing rows stay numeric.</summary>
    public AssessmentMode AssessmentMode { get; set; } = AssessmentMode.Numeric;
}

/// <summary>A class (arm) of a level in one session, such as "JSS 1A". The name is the level name followed by the arm.</summary>
[Audited]
public class SchoolClass : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid LevelId { get; set; }
    public required string Arm { get; set; }
    public required string Name { get; set; }
    /// <summary>The staff profile of the class teacher. Cleared when that staff member leaves.</summary>
    public Guid? ClassTeacherStaffId { get; set; }
}

/// <summary>A subject the school teaches, such as "Mathematics" (MTH).</summary>
[Audited]
public class Subject : TenantEntity
{
    public required string Name { get; set; }
    public required string Code { get; set; }
}

/// <summary>A subject taught in a class, with the teacher who scores it.</summary>
[Audited]
public class ClassSubject : TenantEntity
{
    public Guid ClassId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? TeacherStaffId { get; set; }
}

/// <summary>One column of the score sheet. <c>LevelId</c> null is the school default; a level can override the whole scheme.</summary>
[Audited]
public class AssessmentComponent : TenantEntity
{
    public Guid? LevelId { get; set; }
    public required string Name { get; set; }
    public decimal MaxScore { get; set; }
    public int Order { get; set; }
}

/// <summary>A grade band such as A1 for 75 to 100. <c>LevelId</c> null is the school default; a level can override the whole scale.</summary>
[Audited]
public class GradingBand : TenantEntity
{
    public Guid? LevelId { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public required string Grade { get; set; }
    public required string Remark { get; set; }
}

public enum Gender { Female, Male }

public enum StudentStatus { Active, Graduated, Withdrawn, Transferred }

/// <summary>A pupil. The admission number is unique in the school. Records are kept when a student leaves; only the status changes.</summary>
[Audited]
public class Student : TenantEntity
{
    public required string AdmissionNo { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>Private storage key of the resized photo. Never served from a public URL.</summary>
    public string? PhotoKey { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;
    /// <summary>When the student left (or came back), as entered with the last status change.</summary>
    public DateOnly? StatusDate { get; set; }
    public string? StatusReason { get; set; }
    public DateOnly AdmittedAt { get; set; }
    /// <summary>The student's own login, for levels that have one.</summary>
    public Guid? UserId { get; set; }
}

/// <summary>A student in a class for one session. A student has exactly one enrolment per session.</summary>
[Audited]
public class Enrolment : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ClassId { get; set; }
}

/// <summary>A parent or guardian. Shared by siblings; matched by phone, then email. <c>UserId</c> is set once they accept an invite.</summary>
[Audited]
public class Guardian : TenantEntity
{
    public required string FullName { get; set; }
    /// <summary>Normalised: Nigerian numbers as +234XXXXXXXXXX.</summary>
    public string? Phone { get; set; }
    /// <summary>Lower case.</summary>
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
}

[Audited]
public class StudentGuardian : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid GuardianId { get; set; }
    public required string Relationship { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// A link that lets a parent account claim a guardian record. Only the SHA-256 hash of the token is stored. Single use, 14 days;
/// a new invite for the same guardian expires the earlier unused ones.
/// </summary>
public class GuardianInvite : TenantEntity
{
    public Guid GuardianId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    /// <summary>The staff member who sent it.</summary>
    public Guid? InvitedBy { get; set; }
}

public enum AttendanceStatus { Present, Absent, Late, Excused }

/// <summary>The register of one class for one date. Unique per class and date; a later save replaces the marks. <c>ClientId</c> is the device's id for the save, so a resend is recognised.</summary>
public class AttendanceRegister : TenantEntity
{
    public Guid ClassId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? TakenByStaffId { get; set; }
    public string? ClientId { get; set; }
}

/// <summary>One student's mark on a register. Late is its own count and is not added to days present.</summary>
public class AttendanceMark : TenantEntity
{
    public Guid RegisterId { get; set; }
    public Guid EnrolmentId { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
}

/// <summary>Records that the guardian was told about this student's absence on this date, so the alert goes out once per student per day.</summary>
public class AbsenceAlert : TenantEntity
{
    public Guid StudentId { get; set; }
    public DateOnly Date { get; set; }
}

/// <summary>Where a held import is: waiting for its confirm, or confirmed (kept with its result until it expires).</summary>
public enum ImportBatchStatus { Held, Confirmed }

/// <summary>
/// An import held between the preview and the confirm. The payload is the uploaded rows as JSON. A confirmed batch is kept, with its result,
/// until it expires, so a repeat confirm of the same token returns that result instead of importing again; expired batches are cleared.
/// </summary>
public class ImportBatch : TenantEntity
{
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Held;
    /// <summary>The confirm's result (the ImportApplied JSON), set when the batch is confirmed.</summary>
    public string? Result { get; set; }
    /// <summary>Random, unguessable, unique across schools.</summary>
    public required string Token { get; set; }
    /// <summary>The importer's kind, for example <c>students</c> or <c>scores</c>.</summary>
    public string Kind { get; set; } = "students";
    /// <summary>The query-string scope of the preview (class, term, date...) as a JSON object; null when the kind takes none.</summary>
    public string? Scope { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// One student's score for one component of one class subject in one term. Unique per cell; clearing a score deletes the row.
/// Changes are audited, so edits after a lock and unlock can be traced.
/// </summary>
[Audited]
public class Score : TenantEntity
{
    public Guid EnrolmentId { get; set; }
    public Guid ClassSubjectId { get; set; }
    public Guid TermId { get; set; }
    public Guid ComponentId { get; set; }
    /// <summary>0 up to the component's maximum, one decimal place.</summary>
    public decimal Value { get; set; }
    public Guid? EnteredByStaffId { get; set; }
}

/// <summary>Present when a class's term is locked: its scores no longer change. Unique per class and term; unlocking deletes the row (audited).</summary>
public class TermResultLock : TenantEntity
{
    public Guid ClassId { get; set; }
    public Guid TermId { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    /// <summary>The user who locked it.</summary>
    public Guid? LockedBy { get; set; }
}

/// <summary>
/// One student's total for one subject in one term, rebuilt from the scores whenever the class's term is computed.
/// <c>Incomplete</c> is true while a component is missing and the term is not locked. Unique per enrolment, class subject and term.
/// <see cref="Remark"/> is the teacher's own comment and stays null until someone writes one.
/// </summary>
public class SubjectResult : TenantEntity
{
    public Guid ClassId { get; set; }
    public Guid EnrolmentId { get; set; }
    public Guid ClassSubjectId { get; set; }
    public Guid TermId { get; set; }
    public decimal Total { get; set; }
    public required string Grade { get; set; }
    /// <summary>The subject teacher's comment. Null when nobody has written one. Compute does not copy the grade band's remark.</summary>
    public string? Remark { get; set; }
    public decimal ClassAverage { get; set; }
    public int PositionInSubject { get; set; }
    public bool Incomplete { get; set; }
}

/// <summary>Where a comment-bank line is offered: a subject, an overall remark, or a developmental area.</summary>
public enum CommentScope { Subject, TeacherOverall, PrincipalOverall, Developmental }

/// <summary>
/// One line a teacher can pick. <see cref="MatchKey"/> is the grade (such as A1) the line belongs to.
/// The school can replace the whole bank. A school with none yet receives the template lines.
/// </summary>
[Audited]
public class CommentBankEntry : TenantEntity
{
    public CommentScope Scope { get; set; }
    public required string MatchKey { get; set; }
    public required string Text { get; set; }
    public int Order { get; set; }
}

/// <summary>Psychomotor skills or affective behaviour. A rating of either kind is never an exam mark.</summary>
public enum TraitKind { Psychomotor, Affective }

/// <summary>
/// One item the school rates, such as Handwriting or Punctuality. <c>LevelId</c> null is the school default;
/// a level can override the whole list for one kind. The first read of the school's traits stores the template.
/// </summary>
[Audited]
public class TraitCriterion : TenantEntity
{
    public Guid? LevelId { get; set; }
    public TraitKind Kind { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// A class teacher's rating of one item for one student in one term, from 1 to 5. Unique per enrolment, term and criterion.
/// It is not a score and is not added into the exam total. It stays editable after results are published.
/// </summary>
[Audited]
public class TraitRating : TenantEntity
{
    public Guid EnrolmentId { get; set; }
    public Guid TermId { get; set; }
    public Guid CriterionId { get; set; }
    /// <summary>1 to 5.</summary>
    public int Value { get; set; }
}

/// <summary>
/// One developmental area, such as Language or Self-care. <c>LevelId</c> null is the school default;
/// a level can override the whole list. The first read stores the template. Not an exam subject.
/// </summary>
[Audited]
public class DevelopmentalArea : TenantEntity
{
    public Guid? LevelId { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// One point on the developmental scale, such as Excellent. <c>LevelId</c> null is the school default.
/// A rating picks one point. It is not a mark.
/// </summary>
[Audited]
public class DevelopmentalScale : TenantEntity
{
    public Guid? LevelId { get; set; }
    public required string Code { get; set; }
    public required string Label { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// A class teacher's rating of one developmental area for one student in one term. Unique per enrolment, term and area.
/// It is not a score and is not added into an exam total.
/// </summary>
[Audited]
public class DevelopmentalRating : TenantEntity
{
    public Guid EnrolmentId { get; set; }
    public Guid TermId { get; set; }
    public Guid AreaId { get; set; }
    public Guid ScaleId { get; set; }
    /// <summary>Optional note. Null when the teacher wrote none.</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// One student's report for one term: totals, position, attendance and remarks. Rebuilt on demand; <c>Stale</c> is set whenever the
/// scores or the lock change, and a stale row is recomputed before it is viewed or published. Remarks survive recomputation.
/// Unique per enrolment, class and term. Once published the rows are frozen until the class is unpublished. Not audited (it is derived data); publishing and unpublishing write their own audit entries.
/// </summary>
public class TermResult : TenantEntity
{
    /// <summary>The class these figures belong to. A student who changes class keeps the old class's row; results are always per class and term.</summary>
    public Guid ClassId { get; set; }
    public Guid EnrolmentId { get; set; }
    public Guid TermId { get; set; }
    public decimal Total { get; set; }
    public decimal Average { get; set; }
    public int Position { get; set; }
    public int ClassSize { get; set; }
    /// <summary>Days marked present. Late is not included (except on rows published before late, absent and excused were counted).</summary>
    public int DaysPresent { get; set; }
    /// <summary>Null on rows published before these were counted; the card then prints only present of open.</summary>
    public int? DaysAbsent { get; set; }
    public int? DaysLate { get; set; }
    public int? DaysExcused { get; set; }
    public int DaysOpen { get; set; }
    /// <summary>The mode the row was computed in. Null on rows computed before modes existed, which were numeric.</summary>
    public AssessmentMode? AssessmentMode { get; set; }
    public string? TeacherRemark { get; set; }
    public string? PrincipalRemark { get; set; }
    public bool Incomplete { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public bool Stale { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public enum ReportCardBundleStatus { Queued, Ready, Failed }

/// <summary>
/// A request for every report card of a class's term as one PDF. The request is queued; a job builds the PDF into private storage
/// (<c>FileKey</c>) and marks it Ready. Bundles older than 7 days may be deleted by the job (file and row).
/// </summary>
public class ReportCardBundle : TenantEntity
{
    public Guid ClassId { get; set; }
    public Guid TermId { get; set; }
    public ReportCardBundleStatus Status { get; set; } = ReportCardBundleStatus.Queued;
    public string? FileKey { get; set; }
    /// <summary>The user who asked for it. Only they and the school's leadership can read it.</summary>
    public Guid RequestedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Records that the guardians were emailed that this student's results for this term are ready, so publishing again does not email again.</summary>
public class ResultsNotice : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid TermId { get; set; }
}

/// <summary>Something the school charges for, such as "Tuition". Optional items are added per student. Amounts live in the fee structure.</summary>
[Audited]
public class FeeItem : TenantEntity
{
    public required string Name { get; set; }
    public bool IsOptional { get; set; }
}

/// <summary>What one level pays for one fee item in one term, in kobo. Unique per term, level and item.</summary>
[Audited]
public class FeeStructureLine : TenantEntity
{
    public Guid TermId { get; set; }
    public Guid LevelId { get; set; }
    public Guid FeeItemId { get; set; }
    public long AmountMinor { get; set; }
}

public enum InvoiceStatus { Unpaid, PartlyPaid, Paid, Cancelled }

/// <summary>One student's fees for one term. Unique per enrolment and term. Totals are kept by <c>FeeService.RecalculateAsync</c>; money is in kobo.</summary>
[Audited]
public class StudentInvoice : TenantEntity
{
    public Guid EnrolmentId { get; set; }
    public Guid TermId { get; set; }
    public required string Number { get; set; }
    public long Total { get; set; }
    public long Paid { get; set; }
    public long Balance { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateOnly DueDate { get; set; }
    /// <summary>Set when an online payment arrived for more than the balance at that moment (the balance changed after the parent started paying). The money is kept; the bursar decides.</summary>
    public bool NeedsReview { get; set; }
}

public enum FeePaymentMethod { Online, Cash, Transfer, Pos }

/// <summary>Pending: an online checkout was started. Succeeded: the money counts. Mismatch: Paystack reported another amount or currency, so nothing was applied. Abandoned: Paystack never confirmed it within a day, so the school job stopped checking it (the school was alerted).</summary>
public enum FeePaymentStatus { Pending, Succeeded, Mismatch, Abandoned }

/// <summary>
/// Money paid towards a student's invoice, into the school's own account. Online payments start as Pending with a unique
/// <c>ProviderRef</c> (sfee_…) and are confirmed once (a conditional claim); manual ones are recorded as Succeeded. Only Succeeded,
/// non-reversed payments count towards the invoice. Payments are never edited: a mistake is reversed with a reason.
/// </summary>
[Audited]
public class FeePayment : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public long AmountMinor { get; set; }
    public required string Currency { get; set; }
    public FeePaymentMethod Method { get; set; }
    public FeePaymentStatus Status { get; set; }
    /// <summary>Online only: the Paystack reference, unique across schools.</summary>
    public string? ProviderRef { get; set; }
    /// <summary>Manual only: the teller, transfer or POS reference the bursar typed.</summary>
    public string? Reference { get; set; }
    /// <summary>The school day (Africa/Lagos) the money was received.</summary>
    public DateOnly PaidOn { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>The parent account that started an online payment.</summary>
    public Guid? PayerUserId { get; set; }
    /// <summary>The staff member who recorded a manual payment.</summary>
    public Guid? RecordedBy { get; set; }
    /// <summary>Set once the payment succeeds: RCT-{year}-{00001}.</summary>
    public string? ReceiptNumber { get; set; }
    /// <summary>Mismatch only: what Paystack reported.</summary>
    public long? ChargedAmountMinor { get; set; }
    public string? ChargedCurrency { get; set; }
    /// <summary>When the school was told that a charge for this checkout couldn't be checked with any of its keys (sent once).</summary>
    public DateTimeOffset? UnverifiedAlertedAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public Guid? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
}

/// <summary>A charge on an invoice. A null <c>FeeItemId</c> is a discount, with a negative amount.</summary>
[Audited]
public class InvoiceLine : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public Guid? FeeItemId { get; set; }
    public required string Description { get; set; }
    public long AmountMinor { get; set; }
    /// <summary>Discount lines only: how the discount was given, so a refresh can work it out again.</summary>
    public DiscountKind? DiscountKind { get; set; }
    /// <summary>Discount lines only: the percentage (Percent) or the amount in kobo (Amount).</summary>
    public decimal? DiscountValue { get; set; }
}

public enum DiscountKind { Percent, Amount }

public enum FeeReminderKind { BeforeDue, Overdue }

/// <summary>
/// Records that a fee reminder email was queued for an invoice. <c>SentOn</c> is the school day it was sent for (Africa/Lagos), which is
/// what the schedule is measured from; unique per invoice and day, so a rerun of the daily job can't send twice. At most 6 per invoice.
/// </summary>
public class FeeReminder : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateOnly SentOn { get; set; }
    public FeeReminderKind Kind { get; set; }
}

/// <summary>
/// Money the school spent. The category is free text (trimmed, at most 60 characters). The receipt photo, when there is one, is private:
/// <c>ReceiptKey</c> is its storage key and it is shown only through a signed link.
/// </summary>
[Audited]
public class SchoolExpense : TenantEntity
{
    public required string Category { get; set; }
    public long AmountMinor { get; set; }
    /// <summary>The school day (Africa/Lagos) the money was spent. Never in the future.</summary>
    public DateOnly Date { get; set; }
    public string? Note { get; set; }
    public string? ReceiptKey { get; set; }
    public Guid? RecordedBy { get; set; }
}

/// <summary>One due date of a payment plan. The instalments of an invoice add up to its total.</summary>
[Audited]
public class Instalment : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public DateOnly DueDate { get; set; }
    public long AmountMinor { get; set; }
}

/// <summary>
/// School: every family and the staff. Levels: the students of every class of <c>LevelIds</c>. Classes: the students of <c>ClassIds</c>.
/// Staff: the school's members only, never families.
/// </summary>
public enum AnnouncementAudience { School, Levels, Classes, Staff }

/// <summary>
/// A notice from the school to its families. The audience is read against the students' current classes, so a portal shows what
/// matches the child today. The attachment is a private file (<c>AttachmentKey</c>), shown only through a signed link.
/// </summary>
[Audited]
public class Announcement : TenantEntity
{
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? AttachmentKey { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentType { get; set; }
    public AnnouncementAudience Audience { get; set; }
    public List<Guid> LevelIds { get; set; } = [];
    public List<Guid> ClassIds { get; set; } = [];
    /// <summary>When true, every matching guardian with an email gets one, sent in batches by the broadcast job.</summary>
    public bool EmailGuardians { get; set; }
    /// <summary>The user who posted it.</summary>
    public Guid PostedBy { get; set; }
    public DateTimeOffset PostedAt { get; set; }
}

public enum AnnouncementDeliveryStatus { Pending, Sent }

/// <summary>
/// One guardian's email of an announcement, queued when it is posted and sent by the broadcast job in batches. Unique per announcement
/// and guardian, so a guardian of two children in the audience gets one email.
/// </summary>
public class AnnouncementDelivery : TenantEntity
{
    public Guid AnnouncementId { get; set; }
    public Guid GuardianId { get; set; }
    public required string Email { get; set; }
    public AnnouncementDeliveryStatus Status { get; set; } = AnnouncementDeliveryStatus.Pending;
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// A conversation between a guardian and the school about one student: either with a staff member (the class teacher when it was
/// started) or with the office (Owner and Admin). One thread per guardian, student and staff member, and one per guardian and student for the office.
/// </summary>
public class MessageThread : TenantEntity
{
    public Guid GuardianId { get; set; }
    public Guid StudentId { get; set; }
    public Guid? StaffId { get; set; }
    public bool IsOffice { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
}

/// <summary>One message of a thread: text and at most one private image. <c>ReadAt</c> is set when the other side opens the thread.</summary>
public class Message : TenantEntity
{
    public Guid ThreadId { get; set; }
    public Guid SenderUserId { get; set; }
    /// <summary>True when the guardian sent it; false for the school's side.</summary>
    public bool FromParent { get; set; }
    public required string Body { get; set; }
    public string? AttachmentKey { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>A school day of the timetable. Saturday is allowed for schools that teach then.</summary>
public enum SchoolDay { Mon, Tue, Wed, Thu, Fri, Sat }

/// <summary>
/// One lesson of a class's weekly timetable: a class subject at a day and period, from <c>Start</c> to <c>End</c>. Unique per class, day
/// and period. A teacher in two classes at the same day and period is only warned about, never refused.
/// </summary>
[Audited]
public class TimetableSlot : TenantEntity
{
    public Guid ClassId { get; set; }
    public SchoolDay Day { get; set; }
    /// <summary>1 to 12.</summary>
    public int Period { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public Guid ClassSubjectId { get; set; }
}

/// <summary>Whether the school has the student's work. <see cref="AssignmentSubmissionStatus.None"/> means it has not come in.</summary>
public enum AssignmentSubmissionStatus { None, Received, Returned }

/// <summary>
/// Homework for one class subject in one term. A null <see cref="AssessmentComponentId"/> is practice and does not write a score.
/// When it is set, a saved mark upserts that component's <see cref="Score"/>.
/// </summary>
[Audited]
public class Assignment : TenantEntity
{
    public Guid ClassSubjectId { get; set; }
    public Guid TermId { get; set; }
    public required string Title { get; set; }
    public required string Instructions { get; set; }
    public DateTimeOffset DueAt { get; set; }
    /// <summary>What the mark is out of. Null when the mark is already on the component's scale.</summary>
    public decimal? MaxMarks { get; set; }
    /// <summary>Null for practice. Otherwise the score component a counted mark writes.</summary>
    public Guid? AssessmentComponentId { get; set; }
    /// <summary>The user who set it.</summary>
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// One student's work for one assignment. Unique per assignment and enrolment. The file is private (<see cref="FileKey"/>)
/// and is shown only through a signed link. <see cref="Reopened"/> lets the student replace the file after a mark.
/// </summary>
[Audited]
public class AssignmentSubmission : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public Guid EnrolmentId { get; set; }
    public string? Body { get; set; }
    public string? FileKey { get; set; }
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public bool Late { get; set; }
    public decimal? Mark { get; set; }
    public string? Feedback { get; set; }
    public Guid? MarkedByStaffId { get; set; }
    public AssignmentSubmissionStatus Status { get; set; }
    /// <summary>True after the teacher reopens a marked submission, until the student sends it again.</summary>
    public bool Reopened { get; set; }
}

/// <summary>Where a lesson plan is in review. A draft is visible only to the person who wrote it.</summary>
public enum LessonPlanStatus { Draft, Submitted, Accepted, Returned }

/// <summary>
/// One week's lesson for a class subject. It does not create a timetable slot and it is not printed on the report card.
/// An optional <see cref="AssignmentId"/> must be homework for the same class subject and term.
/// </summary>
[Audited]
public class LessonPlan : TenantEntity
{
    public Guid ClassSubjectId { get; set; }
    public Guid TermId { get; set; }
    public int Week { get; set; }
    public DateOnly Date { get; set; }
    public required string Topic { get; set; }
    public required string Objectives { get; set; }
    public required string Materials { get; set; }
    public required string Methods { get; set; }
    public required string Activities { get; set; }
    public required string PreviousKnowledge { get; set; }
    public required string Introduction { get; set; }
    public required string Presentation { get; set; }
    public required string Conclusion { get; set; }
    public required string Evaluation { get; set; }
    public Guid? AssignmentId { get; set; }
    public string? TeacherRemarks { get; set; }
    public LessonPlanStatus Status { get; set; } = LessonPlanStatus.Draft;
    public string? ReviewerNote { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    /// <summary>The user who accepted or returned it. Empty when submit accepted the plan because review is off.</summary>
    public Guid? ReviewedBy { get; set; }
    /// <summary>The user who wrote it.</summary>
    public Guid CreatedBy { get; set; }
}

/// <summary>A test week or a full examination window. Unpublished periods are visible only to leadership.</summary>
public enum ExamPeriodKind { Test, Examination }

/// <summary>
/// A named exam window in one term. Leadership writes it and publishes it. Later sittings update this same period.
/// Teachers, students and parents see it only after <see cref="PublishedAt"/> is set.
/// </summary>
[Audited]
public class ExamPeriod : TenantEntity
{
    public Guid TermId { get; set; }
    public required string Name { get; set; }
    public ExamPeriodKind Kind { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// One class subject sitting an exam on a calendar date, from <see cref="Start"/> to <see cref="End"/> (half-open).
/// Two subjects of one class cannot overlap, and a venue cannot be booked twice. An invigilator overlap is kept and warned about.
/// </summary>
[Audited]
public class ExamSitting : TenantEntity
{
    public Guid PeriodId { get; set; }
    public Guid ClassSubjectId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int DurationMinutes { get; set; }
    public required string Venue { get; set; }
    public required string Instructions { get; set; }
    public Guid? InvigilatorStaffId { get; set; }
}

/// <summary>
/// One objective item in a subject's question bank. <see cref="LevelId"/> null can be drawn for any class of the subject.
/// A level tag limits new draws to that level. <see cref="Archived"/> hides the item from new draws. Old papers keep their picks.
/// </summary>
[Audited]
public class BankQuestion : TenantEntity
{
    public Guid SubjectId { get; set; }
    public Guid? LevelId { get; set; }
    public required string Stem { get; set; }
    public List<string> Options { get; set; } = [];
    public int CorrectIndex { get; set; }
    public decimal Marks { get; set; }
    public bool Archived { get; set; }
    /// <summary>The user who wrote it.</summary>
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// A computer-based test for one class subject and term. Picked questions always appear. <see cref="DrawCount"/> is how many
/// more to take from the bank when a student starts, excluding the picks. Null <see cref="AssessmentComponentId"/> is practice.
/// The window is the linked sitting, or the times entered when there is no sitting.
/// </summary>
[Audited]
public class CbtPaper : TenantEntity
{
    public Guid ClassSubjectId { get; set; }
    public Guid TermId { get; set; }
    public required string Title { get; set; }
    public int DurationMinutes { get; set; }
    public Guid? ExamSittingId { get; set; }
    public DateTimeOffset OpenAt { get; set; }
    public DateTimeOffset CloseAt { get; set; }
    /// <summary>Null for practice. Otherwise the score component a counted total writes.</summary>
    public Guid? AssessmentComponentId { get; set; }
    /// <summary>Extra questions drawn from the bank, on top of the picks. Zero when the paper is pick-only.</summary>
    public int DrawCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>When the teacher released the answer key. The window end reveals the key as well.</summary>
    public DateTimeOffset? ReleasedKeyAt { get; set; }
    /// <summary>The user who wrote it.</summary>
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// One student sitting one paper. An open attempt (not submitted, and not reopened) is unique per paper and enrolment.
/// The deadline is the start plus the paper's duration. A practice paper does not write a score.
/// </summary>
[Audited]
public class CbtAttempt : TenantEntity
{
    public Guid PaperId { get; set; }
    public Guid EnrolmentId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal TotalMarks { get; set; }
    public bool AutoMarked { get; set; }
    /// <summary>Copied from the paper when the key is released. The window end reveals the key without this.</summary>
    public DateTimeOffset? ReleasedKeyAt { get; set; }
    /// <summary>Set when a teacher reopens the sitting so the student can start again.</summary>
    public DateTimeOffset? ReopenedAt { get; set; }
}

/// <summary>
/// One question as this student saw it. The stem, the options and which one is correct are copied at the start, so a later
/// archive does not change the sitting. <see cref="OptionOrder"/> is the permutation of the bank option indexes.
/// <see cref="CorrectShownIndex"/> is the correct option in that shown order, and it is never sent to the student.
/// </summary>
[Audited]
public class CbtAttemptItem : TenantEntity
{
    public Guid AttemptId { get; set; }
    public Guid BankQuestionId { get; set; }
    /// <summary>The order the question was shown, after the shuffle.</summary>
    public int Order { get; set; }
    public required string Stem { get; set; }
    /// <summary>The option texts in the order shown.</summary>
    public List<string> Options { get; set; } = [];
    /// <summary>Bank option indexes, in the order shown.</summary>
    public List<int> OptionOrder { get; set; } = [];
    public int CorrectShownIndex { get; set; }
    public decimal Marks { get; set; }
    public int? ChosenIndex { get; set; }
    public decimal? MarksAwarded { get; set; }
}

/// <summary>A question chosen for a paper, in the order the teacher picked it. A draw-only paper has none of these. A draw is stored on the attempt, not here.</summary>
[Audited]
public class CbtPaperPick : TenantEntity
{
    public Guid PaperId { get; set; }
    public Guid BankQuestionId { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Words for the student word search. A list is for one class subject, for one level, or (both null) for the whole school.
/// The words are stored upper case, A–Z only. The newest list for a scope is the one used.
/// </summary>
[Audited]
public class WordList : TenantEntity
{
    public Guid? ClassSubjectId { get; set; }
    public Guid? LevelId { get; set; }
    public required string Name { get; set; }
    public List<string> Words { get; set; } = [];
    /// <summary>The user who wrote it.</summary>
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// A riddle for the student Play area. <see cref="LevelId"/> null is shown to every level. <see cref="Answer"/> is for staff only and
/// is never sent to a student. <see cref="Archived"/> hides the teaser from students.
/// </summary>
[Audited]
public class BrainTeaser : TenantEntity
{
    public Guid? LevelId { get; set; }
    public required string Prompt { get; set; }
    public required string Answer { get; set; }
    public bool Archived { get; set; }
    /// <summary>The user who wrote it.</summary>
    public Guid CreatedBy { get; set; }
}
