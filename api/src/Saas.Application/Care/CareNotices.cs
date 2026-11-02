using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// Every CareLink notice goes through here (Task 14): the in-app feed (<see cref="InAppNotifications"/>) and, where the spec asks for it,
/// an email through the outbox (<see cref="NotificationQueue"/>). Nothing is sent from here: both are rows added to the caller's context,
/// saved by the caller's <c>SaveChangesAsync</c> inside the caller's transaction, and the dispatcher only sends committed outbox rows.
/// <para>
/// No notice carries clinical detail. The text is built here from a closed set of facts: the clinic's name, dates and times, patient
/// numbers, ward names, bill and receipt numbers, amounts and Paystack references. Never a drug, a diagnosis, a test or its result, note
/// text, <see cref="Appointment.Reason"/> or an uploaded file's name. Patient-facing notices say only that something is booked, ready
/// or due, and link to the portal.
/// </para>
/// <para>
/// Patient notices go to the patient's portal users (<see cref="ICarePortalRecipients"/>: <see cref="PatientLinkRecipients"/>, the active
/// portal links, Task 15), name the patient's first name, and link to the portal page for that patient (<see cref="PatientPage"/>). A
/// deceased patient's portal users are never sent anything (ruling, Task 14).
/// </para>
/// </summary>
public sealed class CareNotices(
    IAppDbContext db, ITenantContext tenant, InAppNotifications feed, NotificationQueue outbox, ICarePortalRecipients recipients, AppUrls urls)
{
    // Patient-facing kinds (in-app and email).
    public const string AppointmentBooked = "care.appointment_booked";
    public const string AppointmentChanged = "care.appointment_changed";
    public const string AppointmentReminder = "care.appointment_reminder";
    public const string ResultReady = "care.result_ready";
    public const string SummaryReady = "care.summary_ready";
    public const string BillDue = "care.bill_due";
    public const string PaymentReceived = "care.payment_received";
    /// <summary>The portal invite (Task 15): email only, to the address staff gave.</summary>
    public const string PatientInvite = "care.patient_invite";
    // Staff kinds.
    public const string PatientWaiting = "care.patient_waiting";
    public const string CriticalVital = "care.critical_vital";
    public const string MissedDose = "care.missed_dose";
    public const string LowDeposit = "care.low_deposit";
    public const string EmergencyAccess = "care.emergency_access";
    public const string PaymentMismatch = "care.payment_mismatch";
    public const string PaymentUnverified = "care.payment_unverified";
    public const string ImportFinished = "care.import_finished";
    /// <summary>Task 15 fix round 1: a portal account was linked (or linked again) to a patient; to the medical director, in-app.</summary>
    public const string PortalLinked = "care.portal_linked";

    /// <summary>Every kind this helper writes, for tests and the web app's icons.</summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        AppointmentBooked, AppointmentChanged, AppointmentReminder, ResultReady, SummaryReady, BillDue, PaymentReceived,
        PatientWaiting, CriticalVital, MissedDose, LowDeposit, EmergencyAccess, PaymentMismatch, PaymentUnverified, ImportFinished,
        PatientInvite, PortalLinked,
    ];

    /// <summary>
    /// The kinds that also send an email (ruling, Task 14: every patient notice, emergency access, and the payment alerts; Task 15: the portal
    /// invite, which is an email only).
    /// </summary>
    public static readonly IReadOnlyList<string> EmailKinds =
    [
        AppointmentBooked, AppointmentChanged, AppointmentReminder, ResultReady, SummaryReady, BillDue, PaymentReceived,
        EmergencyAccess, PaymentMismatch, PaymentUnverified, PatientInvite,
    ];

    /// <summary>
    /// The patient result sentence (global constraint "A new result is ready in your CareLink portal."), naming the patient's first name
    /// (ruling, Task 15): "A new result for Ada is ready in your CareLink portal."
    /// </summary>
    public static string ResultReadyFor(string firstName) => $"A new result for {firstName} is ready in your CareLink portal.";
    public static string SummaryReadyFor(string firstName) => $"{firstName}'s discharge summary is ready in your CareLink portal.";
    public const string CriticalVitalText = "A patient you are seeing has a critical vital sign.";
    public const string EmergencyAccessText = "Emergency access was used on a patient record.";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    private static string Time(DateTimeOffset at) => at.ToOffset(CareCalendar.LagosOffset).ToString("HH:mm", CultureInfo.InvariantCulture);
    private static string Day(DateTimeOffset at) => at.ToOffset(CareCalendar.LagosOffset).ToString("dddd d MMMM", CultureInfo.InvariantCulture);
    private static string Naira(long minor) => Money.Format(minor, "NGN");

    private string Portal(string page) => $"{urls.Web(Products.Care)}{page}";

    /// <summary>
    /// A portal page for one patient: the portal selects that patient when the link is opened, and the portal's feed for a patient
    /// (<c>GET /api/care-portal/patients/{id}/notifications</c>) is the account's care notices of that clinic carrying this marker (Task 15).
    /// </summary>
    public static string PatientPage(string page, Guid patientId) => $"{page}?patient={patientId}";

    // ---------- Patients ----------
    // Ruling (Task 15): every patient notice names the patient's first name (never the last name), so a guardian with several linked
    // patients can tell them apart. The first name is not clinical.

    /// <summary>"{First}'s appointment at {clinic} is booked for {day} at {HH:mm}." Never the booking reason.</summary>
    public Task AppointmentBookedAsync(Guid patientId, DateTimeOffset start, CancellationToken ct) =>
        ToPatientAsync(patientId, AppointmentBooked, "Appointment booked",
            (clinic, first) => $"{first}'s appointment at {clinic} is booked for {Day(start)} at {Time(start)}.", "/portal/appointments", ct);

    /// <summary>A moved appointment ("…has moved to {day} at {HH:mm}.") or a cancelled one. Never the booking reason.</summary>
    public Task AppointmentChangedAsync(Guid patientId, DateTimeOffset start, bool cancelled, CancellationToken ct) =>
        ToPatientAsync(patientId, AppointmentChanged, cancelled ? "Appointment cancelled" : "Appointment moved",
            (clinic, first) => cancelled
                ? $"{first}'s appointment at {clinic} on {Day(start)} at {Time(start)} was cancelled."
                : $"{first}'s appointment at {clinic} has moved to {Day(start)} at {Time(start)}.",
            "/portal/appointments", ct);

    /// <summary>The day-before reminder (Task 13 wording, with the first name): "{First} has an appointment at {clinic} tomorrow at {HH:mm}."</summary>
    public Task AppointmentReminderAsync(Guid patientId, DateTimeOffset start, CancellationToken ct) =>
        ToPatientAsync(patientId, AppointmentReminder, "Appointment reminder",
            (clinic, first) => $"{first} has an appointment at {clinic} tomorrow at {Time(start)}.", "/portal/appointments", ct);

    /// <summary>"A new result for {First} is ready in your CareLink portal." Nothing else: not the test, not the result.</summary>
    public Task ResultReadyAsync(Guid patientId, CancellationToken ct) =>
        ToPatientAsync(patientId, ResultReady, "New result", (_, first) => ResultReadyFor(first), "/portal/results", ct);

    /// <summary>"{First}'s discharge summary is ready in your CareLink portal."</summary>
    public Task SummaryReadyAsync(Guid patientId, CancellationToken ct) =>
        ToPatientAsync(patientId, SummaryReady, "Discharge summary ready", (_, first) => SummaryReadyFor(first), "/portal/record", ct);

    /// <summary>"Bill {number} for {First} at {clinic} has {amount} to pay." The bill number and balance only, never its lines.</summary>
    public Task BillDueAsync(Guid patientId, string billNumber, long balanceMinor, CancellationToken ct) =>
        balanceMinor <= 0 ? Task.CompletedTask
            : ToPatientAsync(patientId, BillDue, "Bill to pay",
                (clinic, first) => $"Bill {billNumber} for {first} at {clinic} has {Naira(balanceMinor)} to pay.", "/portal/bills", ct);

    /// <summary>"{clinic} received {amount} for {First} on bill {number}. Receipt {receipt}."</summary>
    public Task PaymentReceivedAsync(Guid patientId, string billNumber, long amountMinor, string receiptNumber, CancellationToken ct) =>
        ToPatientAsync(patientId, PaymentReceived, "Payment received",
            (clinic, first) => $"{clinic} received {Naira(amountMinor)} for {first} on bill {billNumber}. Receipt {receiptNumber}.", "/portal/bills", ct);

    /// <summary>
    /// The portal invite email (Task 15), to the address staff gave: "{clinic} has invited you to the CareLink patient portal for {First}."
    /// and the invite link. Email only (the invitee has no account yet). The clinic, the first name and the link only.
    /// </summary>
    public async Task PatientInviteAsync(string email, string firstName, string link, CancellationToken ct)
    {
        var clinic = await ClinicNameAsync(ct);
        outbox.Email(email, PatientInvite, new Dictionary<string, string>
        {
            ["clinic"] = clinic, ["title"] = "Patient portal invitation", ["message"] = PatientInviteText(clinic, firstName), ["link"] = link,
        }, TenantId);
    }

    /// <summary>The invite sentence, shared by the email and the WhatsApp message staff send.</summary>
    public static string PatientInviteText(string clinic, string firstName) =>
        $"{clinic} has invited you to the CareLink patient portal for {firstName}.";

    private async Task ToPatientAsync(Guid patientId, string kind, string title, Func<string, string, string> body, string page, CancellationToken ct)
    {
        var tenantId = TenantId;
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => new { p.Status, p.FirstName }).FirstOrDefaultAsync(ct);
        if (patient is null || patient.Status == PatientStatus.Deceased) return;
        var users = (await recipients.ForPatientAsync(patientId, ct)).Distinct().ToList();
        if (users.Count == 0) return;
        var clinic = await ClinicNameAsync(ct);
        var text = body(clinic, patient.FirstName);
        var link = PatientPage(page, patientId);
        foreach (var userId in users) feed.Add(userId, Products.Care, kind, title, text, link, tenantId);
        await EmailAsync(users, kind, title, text, Portal(link), [], ct, clinic);
    }

    // ---------- Staff ----------

    /// <summary>"Patient {number} is waiting to see you." to the visit's doctor (in-app only).</summary>
    public void PatientWaitingFor(Guid doctorId, string patientNumber) =>
        feed.Add(doctorId, Products.Care, PatientWaiting, "Patient waiting", $"Patient {patientNumber} is waiting to see you.", "/app/queue", TenantId);

    /// <summary>The fixed critical-vital sentence, with no value (in-app only).</summary>
    public void CriticalVitalFor(Guid doctorId) =>
        feed.Add(doctorId, Products.Care, CriticalVital, "Critical vital sign", CriticalVitalText, "/app/queue", TenantId);

    /// <summary>"A dose for patient {number} on {ward} is overdue." to the ward's nurses (in-app only). Never the drug.</summary>
    public void MissedDoseFor(IEnumerable<Guid> nurses, string patientNumber, string wardName, Guid admissionId)
    {
        foreach (var nurse in nurses.Distinct())
            feed.Add(nurse, Products.Care, MissedDose, "Missed dose", $"A dose for patient {patientNumber} on {wardName} is overdue.",
                $"/app/admissions/{admissionId}", TenantId);
    }

    /// <summary>The deposit left (or the amount owed) on an admission bill, to the cashiers and the front desk (in-app only).</summary>
    public void LowDepositFor(IEnumerable<Guid> staff, string patientNumber, string billNumber, long leftMinor, Guid billId)
    {
        var body = leftMinor >= 0
            ? $"Patient {patientNumber} has {Naira(leftMinor)} of deposit left on bill {billNumber}."
            : $"Patient {patientNumber} owes {Naira(-leftMinor)} on bill {billNumber}.";
        foreach (var userId in staff.Distinct())
            feed.Add(userId, Products.Care, LowDeposit, "Low deposit", body, $"/app/bills/{billId}", TenantId);
    }

    /// <summary>Emergency access, to the medical director: in-app and email, the fixed sentence and a link to the access log.</summary>
    public async Task EmergencyAccessAsync(Guid patientId, CancellationToken ct)
    {
        var tenantId = TenantId;
        var directors = await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active).Select(m => m.UserId).ToListAsync(ct);
        var link = $"/app/patients/{patientId}/access-log";
        foreach (var director in directors) feed.Add(director, Products.Care, EmergencyAccess, "Emergency access", EmergencyAccessText, link, tenantId);
        await EmailAsync(directors, EmergencyAccess, "Emergency access", EmergencyAccessText, $"{urls.Web(Products.Care)}{link}", [], ct);
    }

    /// <summary>
    /// A portal link was created or re-activated (Task 15 fix round 1), to the medical director, in-app: "A portal account was linked to
    /// patient {number} as {relationship}. Invited by {inviter}." The patient number, the relationship and the inviter's name only, so a
    /// link the director didn't expect is noticed.
    /// </summary>
    public async Task PortalLinkedAsync(Guid patientId, string patientNumber, PatientLinkRelationship relationship, string inviterName, bool offFile,
        CancellationToken ct)
    {
        var tenantId = TenantId;
        var directors = await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active).Select(m => m.UserId).ToListAsync(ct);
        // Fix round 2: an invite emailed to an address that isn't the patient's recorded email is pointed out (never the address itself).
        var body = $"A portal account was linked to patient {patientNumber} as {relationship.ToString().ToLowerInvariant()}. Invited by {inviterName}"
            + (offFile ? ", sent to an address that isn't on the patient's file." : ".");
        foreach (var director in directors)
            feed.Add(director, Products.Care, PortalLinked, "Portal account linked", body, $"/app/patients/{patientId}", tenantId);
    }

    /// <summary>
    /// A payment alert (mismatch or unverified) to the medical director and the cashiers, in-app and by email: bill number, amounts and the
    /// Paystack reference only.
    /// </summary>
    public async Task PaymentAlertAsync(string kind, string title, string body, Guid billId, string reference, CancellationToken ct)
    {
        if (kind is not (PaymentMismatch or PaymentUnverified)) throw new ArgumentOutOfRangeException(nameof(kind));
        var tenantId = TenantId;
        var users = await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active && (m.Role == MemberRole.Owner || m.Role == MemberRole.Accountant))
            .Select(m => m.UserId).Distinct().ToListAsync(ct);
        var link = $"/app/bills/{billId}";
        foreach (var userId in users) feed.Add(userId, Products.Care, kind, title, body, link, tenantId);
        await EmailAsync(users, kind, title, body, $"{urls.Web(Products.Care)}{link}", new() { ["reference"] = reference }, ct);
    }

    /// <summary>
    /// An import finished, to the person who ran it (in-app only): the counts, never the uploaded file's name (it can name a condition or a
    /// ward; carry, Task 12).
    /// </summary>
    public void ImportFinishedFor(Guid userId, string title, string counts, string link) =>
        feed.Add(userId, Products.Care, ImportFinished, title, $"{counts} from your file.", link, TenantId);

    // ---------- Internals ----------

    private async Task<string> ClinicNameAsync(CancellationToken ct) =>
        await db.ClinicProfiles.AsNoTracking().Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "the clinic";

    /// <summary>One outbox email per user with an address. The payload holds the clinic, the title, the notice text and the link only.</summary>
    private async Task EmailAsync(IReadOnlyCollection<Guid> userIds, string kind, string title, string text, string link,
        Dictionary<string, string> extra, CancellationToken ct, string? clinicName = null)
    {
        if (userIds.Count == 0) return;
        var tenantId = TenantId;
        var clinic = clinicName ?? await ClinicNameAsync(ct);
        var accounts = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        foreach (var account in accounts)
        {
            var payload = new Dictionary<string, string>(extra) { ["clinic"] = clinic, ["title"] = title, ["message"] = text, ["link"] = link };
            outbox.Email(account.Email!, kind, payload, tenantId, account.Id);
        }
    }
}
