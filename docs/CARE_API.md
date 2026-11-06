# API contract: CareLink (clinics and small hospitals)

Web app: `care-web` on port 4400 (staff app under `/app`, patient portal under `/portal`). Product code `care`, policy `product:care`.
Spec: `docs/superpowers/specs/2026-10-26-carelink-clinic-core-design.md`. Design: `docs/design/carelink.md`.

The route lists below are taken from `api/src/Saas.Api/Endpoints/CareEndpoints.cs` and `CarePortalEndpoints.cs`. The roles come from
the role table that `CarePlatformTests.Care_role_table` checks against every `/api/care` route, so a new route without a row fails the
suite.

## Conventions

- **Money** is in kobo (`…Minor`). The web shows naira.
- **Instants** (`DateTimeOffset`) are UTC. **Clinic dates** (`DateOnly`, "YYYY-MM-DD") and bed days are Africa/Lagos dates. Doctor hours
  are Lagos wall-clock times; slots and appointments travel as UTC instants.
- **Errors** are ProblemDetails with plain sentences (show them as they come). Fixed sentences:

  | Status | When | Sentence |
  |---|---|---|
  | 400 | Field errors | `errors: { field: [sentence] }` |
  | 402 | Plan limit (staff seats, beds, online payments) | the limit's sentence, `code: "plan_limit"` |
  | 403 | Clinical access refused | "Only doctors and nurses can see clinical notes." |
  | 403 | Support view writes | "Support view is read only." |
  | 403 | Suspended clinic, portal routes | "This clinic's portal is unavailable." |
  | 404 | Another clinic's patient, or a patient not linked to the portal account | |
  | 409 | Note already signed | "This note is signed. Add an addendum instead." |
  | 409 | Bed taken | "That bed is occupied." |
  | 409 | Allergy match not acknowledged | "{Patient} is recorded as allergic to {allergen}." |
  | 409 | Emergency access without a reason | "Give a reason to open this patient's record." (`code: "emergency_reason_required"`) |
  | 409 | Portal cancel under 2 hours | "This appointment starts soon. Call the clinic to change it." (`code: "appointment_starts_soon"`) |
  | 409 | Deceased patient (booking, invite) | "This patient is recorded as deceased." (`code: "patient_deceased"`) |

- **Emergency access.** A clinical read of a patient the caller has no care relationship with needs a reason in the `X-Care-Reason`
  header (URL-encoded). A `?reason=` query is refused with 400 on every `/api/care` route, so reasons never reach addresses or logs.
  Every clinical read writes a `RecordAccess` row (the patient's access log); emergency reads also tell the medical director.
- **Numbers.** Patients `{prefix}-{000000}` (prefix default `CL`, per clinic); bills `BILL-{yyyy}-{00000}`; receipts `RCP-{yyyy}-{00000}`;
  Paystack references start `care_`.
- **Encryption.** Every clinical free-text field is stored encrypted. Result files are private and served by 2-minute signed links.
- **Notifications** never contain diagnoses, results or drug names. Patient notices name the patient's first name and link to
  `/portal/...?patient={id}`.

## Roles

| Label | Member role | Clinical role |
|---|---|---|
| Medical director | Owner | Doctor by default (always has clinical access) |
| Admin | Admin | none, or Doctor/Nurse if the director gives one |
| Doctor | Staff | Doctor |
| Nurse | Staff | Nurse |
| Front desk | Staff | FrontDesk |
| Cashier | Accountant | none, ever |

"Clinical" means the director, or a clinical role of Doctor or Nurse. Support's view-as never has clinical access and never writes.

The **Who** column below uses these codes (from the role table):

| Code | Who may use the route |
|---|---|
| Everyone | every member; the support view may read |
| Everyone writes | every member (cashier too); never the support view |
| Leaders | the director and admins (reads also in the support view) |
| Leaders write | the director and admins |
| Desk | front desk, clinical staff and leadership (not the cashier); reads also in the support view |
| Desk writes | the same, without the support view |
| Clinical | doctors, nurses, the director, an admin with a clinical role; never the support view |
| Clinical (S) | clinical staff; the service also checks the care relationship with this admission |
| Doctors | doctors and the director |
| Doctors (S) | doctors and the director; the service also checks this note's author or this admission's doctors |
| Money | the cashier and leadership write money |
| Money reads | the cashier and leadership; the support view reads reports |
| Inviters | front desk and leadership (not the support view) |
| Director | the medical director only |
| Director reads | the medical director; the support view reads |

A patient portal token gets 403 on every `/api/care` route. Staff tokens get 403 on every `/api/care-portal` route that needs an account.

## Staff routes (`/api/care`, `Authorization: Bearer <clinic token>`)

### Setup, staff and price list

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/setup` | Everyone | Clinic profile and the setup checklist. |
| PUT | `/api/care/settings/profile` | Leaders write | `SaveClinicProfileRequest`, a full replace: resend both price-item ids. |
| GET | `/api/care/staff` | Leaders | Members with clinical role, title, licence number. |
| GET | `/api/care/staff/me` | Everyone | `CareMeDto`: member role, clinical role and the access flags the web uses. |
| PUT | `/api/care/staff/{userId}` | Leaders write | `{ clinicalRole, title?, licenceNumber? }`. |
| GET | `/api/care/price-list?active=&category=` | Everyone | Price items. |
| POST | `/api/care/price-list` | Money | `{ name, category, priceMinor, active? }`. |
| PUT | `/api/care/price-list/{id}` | Money | Same body. |

### Patients and portal invites

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/patients?q=` | Everyone | Search by name, number or phone. Demographics only. |
| POST | `/api/care/patients` | Desk writes | `SavePatientRequest`. Returns the patient and `possibleDuplicates`. |
| GET | `/api/care/patients/{id}` | Everyone | Clinical facts only for clinical callers, through the record guard (logged; `X-Care-Reason` for emergency). |
| GET | `/api/care/patients/{id}/access-log` | Director | Every record open: staff, role, area label, reason (emergency), time, `portal`, `relationship`, `who`. Portal reads (final review C1) show as `who` "Nkem W. (parent), patient portal" with area `record` or `bills`. |
| PUT | `/api/care/patients/{id}` | Desk writes | Demographics, a full replace. |
| PUT | `/api/care/patients/{id}/clinical-facts` | Clinical | `{ allergies, conditions, bloodGroup, genotype, version }`. `version` is the `factsVersion` the client loaded (left out: 0). A stale one gets 409 `clinical_facts_stale` "These clinical details were changed by someone else. Reload and try again." and nothing changes. The patient read gives clinical callers `factsVersion`. |
| POST | `/api/care/patients/{id}/inactive` | Desk writes | There is no delete. |
| POST | `/api/care/patients/{id}/active` | Desk writes | |
| GET | `/api/care/patients/{id}/record` | Clinical | The record timeline (drafts shown with status). Logged open. |
| POST | `/api/care/patients/{id}/invites` | Inviters | `{ email?, phone? }` (default: the patient's own) → `{ link, whatsAppUrl, expiresAt }`. Single use, 14 days, stored as a hash. Emailed when there is an address. 409 for a deceased patient. Final review C1: front desk sends emailed invites to the email on the patient's file only: 403 "Only the medical director or an admin can create an invitation without an email. Add the patient's email to send one." (no email), 403 "Front desk can send the invitation only to the email on the patient's file." (another address), 409 `invite_email_recently_changed` when the same clerk changed the patient's email in the last 24 hours. Leadership may send link-only invites and other addresses. |
| GET | `/api/care/patients/{id}/invites` | Inviters | Pending invites: masked email and phone, inviter, created, expiry. Never the token. |
| POST | `/api/care/patients/{id}/invites/{inviteId}/cancel` | Inviters | 204. A cancelled invite reads as expired. |
| GET | `/api/care/patients/{id}/portal-links` | Inviters | Linked portal accounts (active and revoked). |
| DELETE | `/api/care/patients/{id}/portal-links/{linkId}` | Inviters | 204. Revokes the link and expires the patient's pending invites. |

### Doctors, schedules and appointments

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/doctors` | Desk | Active doctors. |
| GET | `/api/care/doctors/{userId}/schedule` | Leaders | Leadership, or that doctor (service check). |
| PUT | `/api/care/doctors/{userId}/schedule` | Leaders write | `{ blocks: [{ weekday, start, end, slotMinutes }] }`, Lagos times. Leadership or that doctor. |
| GET | `/api/care/doctors/{userId}/slots?date=` | Desk | Open slots for a Lagos date. |
| GET | `/api/care/appointments?date=&doctorUserId=&patientId=&status=` | Desk | |
| POST | `/api/care/appointments` | Desk writes | `{ patientId, doctorUserId, start, reason? }`; reason max 120, labelled "Reason for visit (no medical details)". 409 `slot_taken`. |
| PUT | `/api/care/appointments/{id}` | Desk writes | Reschedule: `{ doctorUserId, start, reason? }`. |
| POST | `/api/care/appointments/{id}/cancel` | Desk writes | |

### Visits, triage and consultations

| Method | Path | Who | Notes |
|---|---|---|---|
| POST | `/api/care/visits` | Desk writes | Check in: `{ patientId, appointmentId? }`. Opens the visit bill. 409 for a deceased or inactive patient. |
| GET | `/api/care/queue?date=&mine=` | Everyone | Stage, urgency and wait; no clinical fields. |
| GET | `/api/care/visits/{id}` | Everyone | |
| GET | `/api/care/visits/{id}/vitals` | Clinical | Logged open. |
| POST | `/api/care/visits/{id}/triage` | Clinical | `{ vitals, urgency, doctorUserId }`. Critical values notify the doctor. |
| POST | `/api/care/visits/{id}/stage` | Everyone writes | `{ stage }`. The cashier moves a visit to Done only once paid. |
| POST | `/api/care/visits/{id}/consultation` | Doctors (S) | Opens (or returns) the visit's consultation. The visit's doctor or the director. Logged open. |
| PUT | `/api/care/consultations/{id}` | Doctors (S) | Autosave the draft: complaint, history, examination, plan, diagnoses. The author only. |
| POST | `/api/care/consultations/{id}/sign` | Doctors (S) | Locks the note; it shows in the portal from now on. |
| POST | `/api/care/notes/{kind}/{id}/addenda` | Clinical | `{ text }`; `kind` is `consultation`, `ward-round` or `discharge`. Signed notes only. |
| GET | `/api/care/diagnosis-codes?q=` | Clinical | ICD-10 lookup. |

### Prescriptions and tests

| Method | Path | Who | Notes |
|---|---|---|---|
| POST | `/api/care/prescriptions` | Doctors | `CreatePrescriptionRequest` (items with drug, strength, dose, frequency, duration, quantity, route). 409 on an allergy match until `allergyAcknowledged`. Adds bill lines. |
| GET | `/api/care/prescriptions/{id}/pdf` | Clinical | Logged open (area pdf). |
| POST | `/api/care/tests` | Doctors | `{ patientId, consultationId?, admissionId?, kind, description, priceItemId? }`. |
| POST | `/api/care/tests/{id}/result` | Clinical | JSON `{ text }` or multipart `text` and `file`. Post-build: the result then waits for a doctor's review; nobody is told yet. |
| POST | `/api/care/tests/{id}/review` | Doctors | Post-build (user decision). A doctor (an admin with the doctor role included) or the medical director reviews a recorded result → `{ id, reviewedBy, reviewedByName, reviewedAt }`. This releases it to the patient's portal and notifies the patient's portal users ("A new result for {First} is ready…"). 403 "Only doctors and nurses can see clinical notes." without clinical access; 403 "Only doctors can review results." for a nurse; 404 unknown or another clinic's; 409 `test_not_resulted` "This test has no result to review yet.". Idempotent: a reviewed result answers with its first review and writes or sends nothing. Audited (`care.result_reviewed`, ids only). |
| GET | `/api/care/tests/awaiting-review` | Doctors | Post-build. Results not reviewed yet, oldest first (at most 200): `{ id, patientId, patientNumber, patientName, kind, consultationId, admissionId, recordedByName, recordedAt }[]`. No description or result text, so no access-log row; opening the record is the logged read. |
| GET | `/api/care/tests/{id}/file` | Clinical | Logged open (area file) → a 2-minute signed link. |
| GET | `/api/care/result-files/{id}/{expires}/{signature}` | Anyone holding the link | The signed link itself (outside the signed-in group). 404 when forged or expired. |

### Bills, payments and the clinic's Paystack key

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/bills?patientId=&status=` | Everyone | At most 200, newest first. Clinical references only for clinical callers. |
| GET | `/api/care/bills/{id}` | Everyone | Lines (with removed lines and reasons), payments, adjustments, review flag. Final review I2: the cashier, front desk and support view get each line's `description` as its price category ("Drug", "Test"…) and `priceItemId` null; clinical staff and leadership get the item name. |
| POST | `/api/care/bills/{id}/lines` | Money | `{ priceItemId, quantity, reason }`. |
| POST | `/api/care/bills/{id}/lines/{lineId}/remove` | Money | `{ reason }`. |
| POST | `/api/care/bills/{id}/payments` | Money | `{ amountMinor, method, reference?, idempotencyKey? }` or the `Idempotency-Key` header: a retry returns the first payment. Overpaying puts the bill in credit. |
| POST | `/api/care/payments/{id}/reverse` | Money | `{ reason }`. |
| GET | `/api/care/payments/{id}/receipt.pdf` | Everyone | No bill lines, nothing clinical. |
| POST | `/api/care/bills/{id}/adjustments` | Leaders write | `{ kind: Discount\|Waiver, amountMinor, reason }`. |
| POST | `/api/care/bills/{id}/reviewed` | Money | Clears the review flag (a mismatched online payment, a change after payment). |
| POST | `/api/care/payments/online/{reference}/verify` | Money | Ask Paystack about an online checkout. |
| GET | `/api/care/settings/paystack` | Director reads | Whether a key is saved (never the key). |
| PUT | `/api/care/settings/paystack` | Director | `{ secretKey, force? }`. Clinic plan or higher (402 otherwise). Stored encrypted. A replaced key still confirms checkouts for 7 days. |
| DELETE | `/api/care/settings/paystack?force=` | Director | 204. |

### Wards, beds and admissions

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/wards` | Everyone | |
| POST | `/api/care/wards` | Leaders write | `{ name, kind, bedPriceItemId, minDepositMinor, nurseUserIds }`. |
| PUT | `/api/care/wards/{id}` | Leaders write | Same body. |
| DELETE | `/api/care/wards/{id}` | Leaders write | 204. |
| GET | `/api/care/wards/{id}/beds` | Everyone | |
| POST | `/api/care/wards/{id}/beds` | Leaders write | `{ label, active }`. Active beds count against the plan (402). |
| PUT | `/api/care/wards/{id}/beds/{bedId}` | Leaders write | |
| DELETE | `/api/care/wards/{id}/beds/{bedId}` | Leaders write | 204. |
| POST | `/api/care/beds/{id}/ready` | Desk writes | A cleaned bed becomes free. |
| GET | `/api/care/bed-board` | Everyone | Bed status and demographics only. |
| GET | `/api/care/wards/{id}/patients` | Everyone | |
| POST | `/api/care/admissions` | Doctors | `{ patientId, consultationId?, reason, admittingDiagnosis }` (`X-Care-Reason` for emergency). |
| GET | `/api/care/admissions?status=` | Desk | Current and recent; demographics, bed and status only. |
| GET | `/api/care/admissions/{id}` | Clinical | Logged open. |
| POST | `/api/care/admissions/{id}/bed` | Everyone writes | `{ bedId, depositMinor, method, reference?, overrideByOwner? }`. The deposit is a real payment with a receipt. 409 "That bed is occupied." |
| POST | `/api/care/admissions/{id}/transfer` | Desk writes | `{ bedId }`. |
| GET | `/api/care/admissions/{id}/care-team` | Clinical | Doctors' names; not a logged open. |
| POST | `/api/care/admissions/{id}/care-team` | Doctors (S) | `{ userId }` adds a covering doctor. The admitting doctor or the director. |
| DELETE | `/api/care/admissions/{id}/care-team/{userId}` | Doctors (S) | 204. |

### Ward care and discharge

| Method | Path | Who | Notes |
|---|---|---|---|
| POST | `/api/care/admissions/{id}/nursing-notes` | Clinical (S) | `{ note }`. |
| GET | `/api/care/admissions/{id}/nursing-notes` | Clinical | Logged open. Never shown in the portal. |
| POST | `/api/care/admissions/{id}/fluids` | Clinical (S) | `{ direction, kind, ml }`. |
| GET | `/api/care/admissions/{id}/fluids?date=` | Clinical | Logged open. Never shown in the portal. |
| POST | `/api/care/admissions/{id}/ward-rounds` | Doctors (S) | Starts a draft round `{ note }`. |
| GET | `/api/care/admissions/{id}/ward-rounds` | Clinical | Logged open. |
| PUT | `/api/care/ward-rounds/{id}` | Doctors (S) | Autosave; the author only. |
| POST | `/api/care/ward-rounds/{id}/sign` | Doctors (S) | |
| POST | `/api/care/ward-rounds/{id}/discard` | Doctors (S) | 204. An unsigned draft, before discharge. |
| GET | `/api/care/admissions/{id}/medication-chart?date=` | Clinical | Logged open. Never shown in the portal. |
| POST | `/api/care/doses/{id}` | Clinical (S) | `{ status: Given\|Held\|Refused, note? }`. |
| GET | `/api/care/admissions/{id}/discharge-summary` | Clinical | Logged open. |
| PUT | `/api/care/admissions/{id}/discharge-summary` | Doctors (S) | `{ diagnoses, treatment, condition, takeHome, followUp }` (upsert, autosave). `?draftId=` names the draft being edited: once that draft is gone (discarded by the director) the save gets 409 `note_discarded` "This draft was discarded. Start a new discharge summary to write one."; a save without it starts a draft. |
| POST | `/api/care/admissions/{id}/discharge-summary/sign` | Doctors (S) | Tells the portal "{First}'s discharge summary is ready…". |
| POST | `/api/care/admissions/{id}/discharge-summary/take-over` | Director | Takes over another doctor's unsigned draft. |
| POST | `/api/care/admissions/{id}/discharge-summary/discard` | Doctors (S) | 204. The author or the director. |
| POST | `/api/care/admissions/{id}/discharge` | Doctors (S) | `{ type: Home\|AgainstAdvice\|TransferredOut\|Died, amaReason?, deathTime?, deathCause?, acknowledgeUnsignedNotes? }`. Bills outstanding bed days, finalises the bill, frees the bed to Cleaning. A death cancels future appointments silently. |
| GET | `/api/care/admissions/{id}/discharge.pdf` | Clinical | Logged open (area pdf). |

### Reports and imports

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/reports/takings?from=&to=` | Money reads | By day and method. |
| GET | `/api/care/reports/takings.csv?from=&to=` | Money reads | CSV (formula-safe cells, byte order mark). |
| GET | `/api/care/reports/debtors` | Money reads | With WhatsApp reminder links. Bills flagged for review are left out. |
| GET | `/api/care/reports/debtors.csv` | Money reads | CSV. |
| GET | `/api/care/reports/revenue?from=&to=` | Money reads | By price category; bed lines by charge day. |
| GET | `/api/care/reports/revenue.csv?from=&to=` | Money reads | CSV. |
| GET | `/api/care/reports/deposits` | Money reads | Deposits held for current admissions. |
| GET | `/api/care/reports/deposits.csv` | Money reads | CSV. |
| GET | `/api/care/reports/unsigned-drafts` | Director reads | Patient number, doctor, date and age; never note text. |
| GET | `/api/care/imports/{kind}/template.xlsx` | Desk | `kind`: `care-patients`. |
| GET | `/api/care/imports/{kind}/template.csv` | Desk | The same template as CSV. |
| POST | `/api/care/imports/{kind}/preview` | Desk writes | Multipart `file`. Held 30 minutes for the uploader. |
| POST | `/api/care/imports/{token}/confirm` | Everyone writes | All rows or none. Another member's token is 404. |

## Care webhook

| Method | Path | Notes |
|---|---|---|
| POST | `/api/webhooks/paystack/care/{tenantId}` | Each clinic points its own Paystack webhook here. Signed with that clinic's key (`x-paystack-signature`, the current key or one retired in the last 7 days); 401 otherwise. Rate limited (`webhook`). A webhook and a portal return-verify for the same reference record the payment once; a wrong amount is recorded as Mismatch and flags the bill for review. |

## Patient portal sign in (`/api/auth/patient`, rate limited)

Portal accounts are the shared customer accounts (the ones parents and learners use): no clinic in the token.

| Method | Path | Body | Notes |
|---|---|---|---|
| POST | `/api/auth/patient/signup` | `{ fullName, email, password }` | → `{ confirmationRequired: true, email }` and emails a 6-digit code (final fix pass B). No tokens; the account can't be used until the code is entered. An address with an unconfirmed account gets the same answer and a new code; a confirmed one gets 400 "You already have an account with this email. Log in instead.". |
| POST | `/api/auth/patient/signup/confirm` | `{ email, code, password }` | → `AuthResponse` (tenant null). Rate limited as `otp`. Confirms the email and sets `password` (whoever proves the mailbox sets it); earlier refresh tokens are revoked. An emailed invite then accepts, and the password keeps working. |
| POST | `/api/auth/patient/login` | `{ email, password }` | → `AuthResponse`. **403** `email_unconfirmed` "Confirm your email first. Enter the 6-digit code we emailed you, or ask for a new one." for a sign-up whose code was never entered. |
| POST | `/api/auth/otp/request` | `{ email }` | 204. Sends the 6-digit code (shared route, rate limited as `otp`). |
| POST | `/api/auth/patient/otp/verify` | `{ email, code }` | → `AuthResponse`. Confirms the email. A confirmed account's password is never removed. Only an account from before codes at sign-up (a password, never confirmed) is taken over on its first confirmation: the password is removed, the security stamp rotated and every refresh token revoked. |

The full sign-up confirmation contract (business sign-up too, resets, team invites) is in `PLATFORM_API.md`, "Sign-up confirmation".

Staff invites (post-build): for a CareLink clinic the team invite email reads "…invited you to join {clinic} as {role}." and `GET /api/public/invites/{token}` adds `roleLabel`, both with the clinic role: Doctor, Nurse, Front desk, Cashier, Admin, Admin (doctor) or Admin (nurse) (Staff with no clinical role stays "Staff"). Other products keep the platform role in the email and `roleLabel: null`.

Refresh and sign out are the shared `/api/auth/refresh` and `/api/auth/logout`.

## Patient portal (`/api/care-portal`)

Every route under `/patients/{id}` goes through `PatientPortalAccess.RequirePatientAsync`: **404** when the account has no active link
to the patient (another family's child, another clinic's record, a revoked link, or a session older than the account's email
confirmation), then **403** "This clinic's portal is unavailable." for a suspended clinic, then the request acts as that clinic. Staff
and support-view tokens get 403 "Sign in to your patient portal account to use the patient portal." Portal reads never write
`RecordAccess`. Writes are rate limited as `public-write`.

| Method | Path | Auth | Returns and notes |
|---|---|---|---|
| GET | `/api/care-portal/invites/{token}` | none (rate limited `preview`) | `{ clinicName, patientFirstName, expiresAt }`. 404 unknown; 403 suspended clinic; 409 "This invitation was already used." or "This invitation has expired. Ask the clinic for a new one." |
| POST | `/api/care-portal/invites/{token}/accept` | portal account | `{ relationship: Self\|Parent\|Guardian }` → the account's patients (`PortalPatientRef[]`). 400 "Choose self, parent or guardian."; 401 "Please sign in again." (a session from before the email was confirmed); 403 "Staff accounts can't be linked to patients. Use a personal account." (any membership of that clinic); 409 "This invitation was sent to another email address." (an emailed invite needs that confirmed email); 409 used, expired, or "This patient is recorded as deceased.". Tells the medical director in-app. |
| GET | `/api/care-portal/patients` | portal account | `PortalPatientRef[]`: `{ id, clinicId, clinicName, clinicSlug, number, firstName, lastName, fullName, relationship, status }` across clinics. Suspended clinics are left out. |
| GET | `/api/care-portal/patients/{id}/record` | portal account | `PortalRecordDto`: profile (with the patient's own allergies, conditions, blood group, genotype), signed consultations with diagnoses and addenda, prescriptions from admissions or signed consultations, resulted tests that a doctor has reviewed (post-build), vitals, admissions (no against-advice reason or death cause), signed ward rounds and discharge summaries. Never drafts, staff ids, nursing notes, fluids, the medication chart or result files. Writes a portal-read access-log row (final review C1). |
| GET | `/api/care-portal/patients/{id}/access-log` | portal account | `[{ staffName, role, what, at, portal, who }]`, newest 500. A portal read has `portal` true, the account's short name, its relationship as `role`, and `who` "Nkem W. (parent), patient portal". `what` is the patient-facing area label ("Your record", "A consultation note", "Test results", "Vital signs at triage"…). Never the reason or a staff id. |
| GET | `/api/care-portal/patients/{id}/bills` | portal account | `[{ id, number, status, totalMinor, adjustedMinor, paidMinor, balanceMinor, creditMinor, createdAt, finalisedAt, lines: [{ description, quantity, unitPriceMinor, amountMinor }], payments: [{ id, amountMinor, method, receiptNumber, paidAt, status }] }]`. Live lines only, with item names (the patient's own); payments with a receipt (Succeeded or Reversed); no reasons. Writes a portal-read access-log row (area `bills`, final review C1). |
| POST | `/api/care-portal/patients/{id}/bills/{billId}/pay` | portal account | `{ amountMinor? }` (the balance when left out, or a part of at least ₦1,000) → `{ authorizationUrl, reference }`. Paystack returns to `/portal/pay/return?reference=`. The clinic needs online payments on its plan and a saved key. |
| POST | `/api/care-portal/payments/{reference}/verify` | portal account | → `{ id, reference, billId, amountMinor, status: Succeeded\|Pending\|Mismatch\|Abandoned, receiptNumber, billBalanceMinor }`. Needs an active link to the payment's patient (404 otherwise). Repeating it never records twice. Post-build: when the reference is still open after asking Paystack (another request, the webhook or a second verify, may be applying it), the verify waits on the clinic's bill lock and reads again, so a racing pair both answer the settled state. The return page asks once on load and, while the answer is Pending, again every 3 seconds for about 30 seconds before showing "Still waiting". |
| GET | `/api/care-portal/patients/{id}/receipts/{paymentId}.pdf` | portal account | The receipt PDF (`no-store`). 404 for another patient's payment. |
| GET | `/api/care-portal/patients/{id}/doctors` | portal account | `[{ userId, name }]` bookable doctors. |
| GET | `/api/care-portal/patients/{id}/doctors/{doctorId}/slots?date=` | portal account | `[{ start, end }]`. 400 without a date. |
| GET | `/api/care-portal/patients/{id}/appointments` | portal account | `{ reasonLabel: "Reason for visit (no medical details)", appointments: [{ id, doctorUserId, doctorName, start, end, reason, status, source, canCancel }] }`. `canCancel`: booked and at least 2 hours away. |
| POST | `/api/care-portal/patients/{id}/appointments` | portal account | `{ doctorUserId, start, reason? }` → the appointment (source Portal). 409 `slot_taken`; 409 for a deceased patient. |
| POST | `/api/care-portal/patients/{id}/appointments/{appointmentId}/cancel` | portal account | → the appointment. 409 "This appointment starts soon. Call the clinic to change it." under 2 hours. |
| PUT | `/api/care-portal/patients/{id}/contact` | portal account | `{ phone, email, address }`, a full replace: a field left out is cleared. Audited with ids only. |
| GET | `/api/care-portal/patients/{id}/notifications` | portal account | `[{ id, kind, title, body, link, createdAt, readAt }]`, newest 50: this account's care notices at this clinic for this patient. Mark one read with `POST /api/me/notifications/{id}/read`. |

There is no portal reschedule route: the portal cancels and books again (spec gap, see Task 20 report).

### Patient notices

| Kind | Sent when | Text (no clinical detail) | Link |
|---|---|---|---|
| `care.appointment_booked` | a booking (desk or portal) | "{First}'s appointment at {clinic} is booked for {day} at {time}." | `/portal/appointments?patient={id}` |
| `care.appointment_changed` | a time move or a cancel | "…has moved to…" / "…was cancelled." | `/portal/appointments?patient={id}` |
| `care.appointment_reminder` | the day before | "{First} has an appointment at {clinic} tomorrow at {time}." | `/portal/appointments?patient={id}` |
| `care.result_ready` | a doctor or the medical director reviews a recorded result (post-build) | "A new result for {First} is ready in your CareLink portal." | `/portal/results?patient={id}` |
| `care.summary_ready` | a discharge summary is signed | "{First}'s discharge summary is ready in your CareLink portal." | `/portal/record?patient={id}` |
| `care.bill_due` | a bill waits for payment | "Bill {n} for {First} at {clinic} has {amount} to pay." | `/portal/bills?patient={id}` |
| `care.payment_received` | a desk or online payment succeeds | "{clinic} received {amount} for {First} on bill {n}. Receipt {r}." | `/portal/bills?patient={id}` |

Nothing goes to a deceased patient's portal accounts. Notices go in-app and by email.

## care-web

| Path | For | Notes |
|---|---|---|
| `/login`, `/signup`, `/invite/{token}` | staff | Staff sign in (password or code), clinic signup, staff invite. |
| `/app/...` | staff | Guarded by `RequireSession`: a clinic session only. A portal session sees "This is the staff app". |
| `/portal/sign-in` | patients | Code (default) or password sign in, and new accounts. |
| `/portal/invite/{token}` | patients | Public preview, then sign in and choose the relationship to accept. |
| `/portal`, `/portal/record`, `/portal/results`, `/portal/access-log`, `/portal/bills`, `/portal/pay/return`, `/portal/appointments`, `/portal/contact`, `/portal/notifications` | patients | Guarded by `RequirePortalSession`: a customer session only. A staff or support session sees "You're signed in as staff". The chosen patient travels as `?patient={id}`. |
