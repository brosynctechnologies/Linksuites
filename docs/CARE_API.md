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
| Lab scientist | Staff | LabScientist (wave 4) |
| Pharmacist | Staff | Pharmacist (wave 4) |

"Clinical" means the director, or a clinical role of Doctor or Nurse. Support's view-as never has clinical access and never writes.
Wave 4: a lab scientist and a pharmacist have no clinical access. They reach clinical data only through their department's own orders: a
lab order (the lab) or a prescription being dispensed (the pharmacy). Elsewhere they are staff like the front desk (they search patients
and check them in).

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
| Lab | clinical staff and lab scientists; never the support view (wave 4) |
| Lab catalogue | leadership and lab scientists (wave 4) |
| Lab reports | the lab and leadership; the support view reads (wave 4) |
| Stock | leadership and pharmacists; the support view reads (wave 4) |
| Stock writes | leadership and pharmacists (wave 4) |
| Dispensers | clinical staff and pharmacists; never the support view (wave 4) |

A patient portal token gets 403 on every `/api/care` route. Staff tokens get 403 on every `/api/care-portal` route that needs an account.

## Staff routes (`/api/care`, `Authorization: Bearer <clinic token>`)

### Setup, staff and price list

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/setup` | Everyone | Clinic profile and the setup checklist. |
| PUT | `/api/care/settings/profile` | Leaders write | `SaveClinicProfileRequest`, a full replace: resend both price-item ids. |
| GET | `/api/care/staff` | Leaders | Members with clinical role, title, licence number. |
| GET | `/api/care/staff/me` | Everyone | `CareMeDto`: member role, clinical role, the access flags the web uses and `hasSignature`. |
| PUT | `/api/care/settings/logo` | Leaders write | Wave 1. Multipart `file` (JPEG, PNG or WebP, at most 5 MB) → `ClinicDto` (`hasLogo`). Resized to fit 600 × 600, re-encoded WebP, stored under `private/care/{tenant}/branding/`; the old file is deleted. 400 `file` for anything else. Printed on prescriptions, discharge summaries and receipts. |
| DELETE | `/api/care/settings/logo` | Leaders write | Wave 1. 204; deletes the file. |
| GET | `/api/care/settings/logo` | Everyone | Wave 1. The logo image (`image/webp`, `no-store`); 404 when there is none. |
| PUT | `/api/care/staff/me/signature` | Everyone writes | Wave 1. The caller's own signature: multipart `file` as for the logo, fitted to 600 × 200, private (`private/care/{tenant}/signatures/`) → `{ hasSignature }`. Printed above the doctor's name on prescriptions and discharge summaries they sign, and above "Received by" on receipts for desk payments the member took. Wave 2: the signature is frozen when the paper is signed (a consultation, which its prescriptions use; a prescription written outside one; a discharge summary; a desk payment), so a later new signature, its removal, or the member leaving changes no signed paper. A replaced file is kept while a signed paper uses it. |
| DELETE | `/api/care/staff/me/signature` | Everyone writes | Wave 1. 204; deletes the file unless a signed paper uses it (Wave 2). |
| GET | `/api/care/staff/me/signature` | Everyone | Wave 1. The caller's own signature image; 404 when they have none. Nobody reads another member's. |
| PUT | `/api/care/staff/{userId}` | Leaders write | `{ clinicalRole, title?, licenceNumber? }`. |
| GET | `/api/care/price-list?active=&category=` | Everyone | Price items. |
| POST | `/api/care/price-list` | Money | `{ name, category, priceMinor, active? }`. |
| PUT | `/api/care/price-list/{id}` | Money | Same body. |

### Patients and portal invites

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/patients?q=` | Everyone | Search by name, number or phone. Demographics only. |
| POST | `/api/care/patients` | Desk writes | `SavePatientRequest`. Returns the patient and `possibleDuplicates`. |
| POST | `/api/care/patients/duplicates` | Desk writes | Wave 1. The registration form's check before saving: `{ firstName, lastName, dateOfBirth, phone }` → up to 10 `PatientSummaryDto` with the same first and last name (any case) and date of birth, or the same phone (normalised). Saves nothing; names travel in the body. |
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
| GET | `/api/care/appointments?date=&doctorUserId=&patientId=&status=` | Desk | Wave 1: each booked, upcoming appointment carries `whatsAppUrl`, a wa.me reminder "Hello {First}, this is a reminder of your appointment with {doctor} at {clinic} on {day} at {time}." (never the reason); null without a phone or for a deceased patient. |
| POST | `/api/care/appointments` | Desk writes | `{ patientId, doctorUserId, start, reason? }`; reason max 120, labelled "Reason for visit (no medical details)". 409 `slot_taken`. |
| PUT | `/api/care/appointments/{id}` | Desk writes | Reschedule: `{ doctorUserId, start, reason? }`. |
| POST | `/api/care/appointments/{id}/cancel` | Desk writes | |

### Visits, triage and consultations

| Method | Path | Who | Notes |
|---|---|---|---|
| POST | `/api/care/visits` | Desk writes | Check in: `{ patientId, appointmentId? }`. Opens the visit bill. 409 for a deceased or inactive patient. |
| GET | `/api/care/queue?date=&mine=` | Everyone | Stage, urgency and wait; no clinical fields. Wave 1: each visit (and `GET /visits/{id}`) carries `whatsAppUrl`: waiting stages "Hello {First}, it is nearly your turn at {clinic}. Please come back to the waiting area.", waiting to pay "…please come to the cashier…"; null with the doctor, when done or without a phone. |
| GET | `/api/care/visits/{id}` | Everyone | |
| GET | `/api/care/visits/{id}/vitals` | Clinical | Logged open. |
| POST | `/api/care/visits/{id}/triage` | Clinical | `{ vitals, urgency, doctorUserId }`. Critical values notify the doctor. |
| POST | `/api/care/visits/{id}/stage` | Everyone writes | `{ stage }`. The cashier moves a visit to Done only once paid. Wave 1: runs under the visit lock (as triage and the consultation open); once a visit is WithDoctor or later only leadership (the medical director or an admin) sends it back to WaitingVitals or WaitingDoctor (a hand-over through re-triage; audited `care.visit_sent_back`, ids only); anyone else gets 409 `visit_seen` "The doctor has already seen this patient. Ask the medical director or an admin to send the visit back." Back to WithDoctor is allowed. |
| POST | `/api/care/visits/{id}/consultation` | Doctors (S) | Opens (or returns) the visit's consultation. The visit's doctor or the director. Logged open. |
| PUT | `/api/care/consultations/{id}` | Doctors (S) | Autosave the draft: complaint, history, examination, plan, diagnoses. The author only. |
| POST | `/api/care/consultations/{id}/sign` | Doctors (S) | Locks the note; it shows in the portal from now on. |
| POST | `/api/care/notes/{kind}/{id}/addenda` | Clinical | `{ text }`; `kind` is `consultation`, `ward-round` or `discharge`. Signed notes only. |
| GET | `/api/care/diagnosis-codes?q=` | Clinical | ICD-10 lookup. |

### Prescriptions and tests

| Method | Path | Who | Notes |
|---|---|---|---|
| POST | `/api/care/prescriptions` | Doctors | `CreatePrescriptionRequest` (items with drug, strength, dose, frequency, duration, quantity, route). 409 on an allergy match until `allergyAcknowledged`. Adds bill lines. Wave 2: the allergy classes also cover cephalosporins, heparins and statins, with PCN and ASA as class names (whole words only); a name inside an unrelated drug (Statin in Nystatin) no longer matches. |
| GET | `/api/care/prescriptions/{id}/pdf` | Clinical | Logged open (area pdf). |
| POST | `/api/care/tests` | Doctors | `{ patientId, consultationId?, admissionId?, kind, description, priceItemId?, labTestId? }`. Wave 4: with `labTestId` (an active lab catalogue test; 400 `labTestId` otherwise) the kind defaults to Lab (400 `kind` for another), the description to the test's name and the price item to the test's own. |
| POST | `/api/care/tests/{id}/result` | Clinical, and lab scientists for lab tests | JSON `{ text }` or multipart `text` and `file`. Post-build: the result then waits for a doctor's review; nobody is told yet. Wave 4: a lab scientist may record a lab test's result (403 for imaging or other). |
| POST | `/api/care/tests/{id}/amend` | Clinical | Wave 1. Corrects a result entered in error: JSON `{ text?, reason, keepFile? }` or multipart `text`, `reason`, `keepFile`, `file`. 400 `reason` without one; 400 `text` when the correction has no text and no file (a kept file counts); 404; 409 `test_not_resulted`. The recorded result is kept unchanged (text, file, who recorded and reviewed it) with who replaced it, when and the reason (encrypted); the record's `results[]` shows it under `superseded` (newest first) and sets `amendedAt`. The correction is recorded by the caller now and waits for a doctor's review again before the portal shows it (the portal then marks it `corrected` and never shows the earlier one). Audited `care.result_amended` (ids only). |
| POST | `/api/care/prescriptions` and `/api/care/tests` (late orders) | Doctors | Wave 1: an order charged to a visit that is already Done sends the patient's portal users the bill-due notice again (bill number and balance only). |
| POST | `/api/care/tests/{id}/review` | Doctors | Post-build (user decision). A doctor (an admin with the doctor role included) or the medical director reviews a recorded result → `{ id, reviewedBy, reviewedByName, reviewedAt }`. This releases it to the patient's portal and notifies the patient's portal users ("A new result for {First} is ready…"). 403 "Only doctors and nurses can see clinical notes." without clinical access; 403 "Only doctors can review results." for a nurse; 404 unknown or another clinic's; 409 `test_not_resulted` "This test has no result to review yet.". Idempotent: a reviewed result answers with its first review and writes or sends nothing. Audited (`care.result_reviewed`, ids only). |
| GET | `/api/care/tests/awaiting-review` | Doctors | Post-build. Results not reviewed yet, oldest first (at most 200): `{ id, patientId, patientNumber, patientName, kind, consultationId, admissionId, recordedByName, recordedAt }[]`. No description or result text, so no access-log row; opening the record is the logged read. |
| GET | `/api/care/tests/{id}/file` | Clinical | Logged open (area file) → a 2-minute signed link. |
| GET | `/api/care/tests/{id}/superseded/{amendmentId}/file` | Clinical | Wave 2. The file of a result entered in error (an amendment of this test, from `superseded[]`): a logged open (area file) → `{ url, expiresAt, contentType }`, a 2-minute signed link to `/api/care/superseded-result-files/{amendmentId}/{expires}/{signature}`. 404 for an unknown test, an amendment of another test, or one without a file. |
| GET | `/api/care/result-files/{id}/{expires}/{signature}` | Anyone holding the link | The signed link itself (outside the signed-in group). 404 when forged or expired. |

### Lab (wave 4)

Built on tests and results: a catalogue order is a `TestRequest` with `labTestId`; its structured result becomes the test's result text
(one line per value: "Haemoglobin: 9.1 g/dL (low; ref 12-16)", then the comment), so the record, the doctor's review (which still releases
it to the portal) and amendments work as for any result. Ranges are picked by the patient's sex and age (whole years) on the collection
day: a range for the patient's sex beats one for both; ages run from `minAgeYears` (inclusive) to `maxAgeYears` (exclusive). Flags:
`CriticalLow`/`CriticalHigh` at or beyond a critical limit, `Low`/`High` outside the reference limits, `Normal`, or `None` (no numeric
range). Opening an order or printing its label is a **department read**: no care relationship or reason is needed (the order sends the
patient to the lab, and the read covers that order only); it is logged like any open (area `lab`, or `pdf` for the label) with the
caller's role ("Lab scientist").

| Method | Route | Who | Notes |
|---|---|---|---|
| GET | `/api/care/lab/tests?active=` | Everyone | The catalogue by name: `{ id, name, sampleType, priceItemId, priceItemName, priceMinor, turnaroundHours, active, analytes: [{ id, name, unit, ranges: [{ sex, minAgeYears, maxAgeYears, low, high, criticalLow, criticalHigh }] }] }[]`. Catalogue data, no patient. |
| POST | `/api/care/lab/tests` | Lab catalogue | `{ name, sampleType, priceItemId?, turnaroundHours? (1-720, default 24), active?, analytes: [{ name, unit?, ranges? }] }` (1-40 analytes, 0-12 ranges each). 400 per field (`analytes[i].ranges[j].high`: high below low; `.criticalLow` above low; `.criticalHigh` below high; `.maxAgeYears` not after the minimum; `.low` with no limit at all). 409 `lab_test_exists`. Audited. |
| PUT | `/api/care/lab/tests/{id}` | Lab catalogue | As POST. Analytes sent with their `id` are kept, new ones added, the rest removed (recorded results copied their name, unit and range, so they never change). |
| GET | `/api/care/lab/worklist?stage=` | Lab | Lab orders (catalogue or kind Lab) not yet reviewed, oldest first (at most 300); `stage=AwaitingSample|AwaitingResult|AwaitingReview|Done` (Done: reviewed in the last 7 days). `{ id, patientId, patientNumber, patientName, sex, ageYears, labTestId, testName, sampleType, sampleNumber, stage, requestedByName, orderedAt, collectedAt, reportedAt, dueBy, overdue }[]`. No description or result: no access-log row. |
| GET | `/api/care/lab/orders/{id}` | Lab | Department read (area `lab`). `LabOrderDto`: patient demographics, the description, the catalogue test, stage, sample, `analytes[]` with the patient's range (`low, high, criticalLow, criticalHigh, rangeText`), `values[]` (current first, then `superseded` ones), the result text and review time. 404 for an unknown, another clinic's or a non-lab test. |
| POST | `/api/care/lab/orders/{id}/collect` | Lab | No body. Numbers the sample `S{yyyy}-{00000}` (per clinic), records who and when → `{ id, sampleNumber, collectedAt, collectedByName }`. 409 `not_lab`, `sample_collected`, `test_resulted`. |
| GET | `/api/care/lab/orders/{id}/label.pdf` | Lab | The sample label (70 × 35 mm PDF: sample number, patient name, number, sex and age, test and sample type, collection time). Department read (area `pdf`). 409 `sample_not_collected`. |
| POST | `/api/care/lab/orders/{id}/results` | Lab | `{ values: [{ analyteId, value }], comment? }`: every analyte of the test once. A value is a number (invariant culture) when the analyte has ranges, else text (encrypted). → `{ id, status, values[], resultText, anyCritical, resultedAt }`. 400 `values`, `values[i].analyteId`, `values[i].value`; 404; 409 `not_catalogue` (record free-text tests through `/tests/{id}/result`), `test_resulted`, `sample_not_collected`. A critical value tells the ordering doctor (`care.critical_result`) unless they entered it. |
| POST | `/api/care/lab/orders/{id}/amend` | Lab | `{ values, comment?, reason }`. 400 `reason`; 409 `not_catalogue`, `test_not_resulted`. The recorded result is kept as an amendment (as `/tests/{id}/amend`), its values marked superseded; the correction waits for review again. Audited `care.result_amended`. A text correction through `/tests/{id}/amend` also supersedes the structured values. |
| GET | `/api/care/lab/turnaround?from=&to=` | Lab reports | Lagos days (default the last 30, at most 366). Per catalogue test (then "Other lab tests", target 24 h): `{ labTestId, name, targetHours, ordered, collected, reported, reviewed, avgOrderToCollectMinutes, avgCollectToReportMinutes, avgReportToReviewMinutes, avgOrderToReportMinutes, withinTarget, late }`, and a `total` row. Times use the first result and first review, so a correction doesn't move them. |

### Pharmacy stock (wave 4)

Stock items are linked to a Drug price-list item (one item per price item), so a prescription item with that price item is dispensed from
it. Stock lives in batches (batch number, expiry date: the last day it may be used). Usable stock is the batches that haven't expired.
Every change takes the item's advisory lock (items in id order) inside one transaction, and a check constraint keeps each batch at 0 or
more, so concurrent dispensing never oversells. Stock reports never name a patient.

| Method | Route | Who | Notes |
|---|---|---|---|
| GET | `/api/care/pharmacy/items?active=` | Stock | Stock on hand: `{ id, name, form, unit, priceItemId, priceItemName, reorderLevel, active, onHand, expiredOnHand, nextExpiry, low, valueMinor }[]`. |
| POST / PUT | `/api/care/pharmacy/items`, `/api/care/pharmacy/items/{id}` | Stock writes | `{ name, form?, unit?, priceItemId?, reorderLevel?, active? }`. 400 `priceItemId` unless an active Drug price item; 409 `stock_item_exists`, `price_item_taken`. Audited. |
| GET | `/api/care/pharmacy/items/{id}`, `/api/care/pharmacy/items/{id}/batches?all=` | Stock | One item; its batches holding stock (all with `all=true`), first expiry first: `{ id, itemId, itemName, unit, batchNumber, expiresOn, received, onHand, unitCostMinor, expired, daysToExpiry, receivedAt }[]`. |
| POST | `/api/care/pharmacy/receipts` | Stock writes | `{ supplier?, reference?, receivedOn?, lines: [{ itemId, batchNumber, expiresOn, quantity, unitCostMinor? }] }` (1-50 lines). Each line is a new batch with a receipt movement. 400 per line (an expired batch is refused). A receipt that lifts an item above its reorder level re-arms the low-stock notice. |
| GET | `/api/care/pharmacy/receipts` | Stock | The last 100 receipts with their batches. |
| POST | `/api/care/pharmacy/counts` | Stock writes | `{ reason, lines: [{ batchId, counted }] }`. Each difference becomes an `Adjustment` movement with the reason → `{ adjusted, movements[] }`. 400 `reason`, `lines[i].batchId` (unknown, another clinic's or repeated), `lines[i].counted`. Audited `care.stock_counted` (ids and quantities). |
| GET | `/api/care/pharmacy/movements?from=&to=&itemId=` | Stock | Newest first, at most 500: `{ id, itemId, itemName, batchId, batchNumber, kind (Receipt, Dispense, Adjustment), quantity (signed), balanceAfter, reason, byName, at, receiptId, dispensingId }[]`. |
| GET | `/api/care/pharmacy/expiring?days=` | Stock | Batches holding stock that expire within `days` (default 90, 0-730), expired ones included. |
| GET | `/api/care/pharmacy/alerts` | Stock | `{ days: 90, lowStock: StockItem[], nearExpiry: Batch[], expired: Batch[] }`. |
| GET | `/api/care/pharmacy/queue` | Dispensers | Prescriptions of the last 30 days with a drug still to dispense, oldest first: `{ prescriptionId, patientId, patientNumber, patientName, doctorName, writtenAt, items, itemsDone, inpatient }[]`. No drug names: no access-log row. |
| GET | `/api/care/pharmacy/prescriptions/{id}` | Dispensers | Department read (area `dispensing`). The drugs with `quantity, dispensed, remaining`, the stock item sold under the drug's price item with its usable stock, and past dispensings with the batches used. |
| POST | `/api/care/pharmacy/prescriptions/{id}/dispense` | Dispensers | `{ lines: [{ prescriptionItemId, itemId?, quantity? }] }`. The stock item defaults to the one sold under the drug's price item; the quantity to what is left of the prescribed quantity (required when none was written). Takes stock first-expiry-first-out from unexpired batches, under the prescription's lock and each item's lock → `{ id, dispensedByName, at, lines: [{ prescriptionItemId, itemId, itemName, batchId, batchNumber, expiresOn, quantity }] }`. 400 per line; 404; 409 `over_dispense` (beyond the prescribed quantity, or already dispensed in full) and `insufficient_stock` (nothing is taken). Audited `care.dispensed` (ids only). Dispensing doesn't bill: drugs are charged when prescribed. |

The care job (every minute) flags batches holding stock that expire within 90 days once (`care.near_expiry`) and re-checks low stock as
batches expire.

### Bills, payments and the clinic's Paystack key

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/care/bills?patientId=&status=&q=&offset=&limit=` | Everyone | Newest first. Wave 1: `q` matches part of the bill number, the patient number or name; `offset` (default 0) and `limit` (1 to 200, default 200) page it, a full page meaning there may be more; 400 `offset`/`limit` out of range. Clinical references only for clinical callers. |
| GET | `/api/care/bills/{id}` | Everyone | Lines (with removed lines and reasons; Wave 1: `chargeDay`, the Lagos day a bed line charges for, null otherwise), payments (Wave 1: `providerPaidAt`, Paystack's own time for an online payment; `paidAt` is when the clinic confirmed it), adjustments, review flag. Final review I2: the cashier, front desk and support view get each line's `description` as its price category ("Drug", "Test"…) and `priceItemId` null; clinical staff and leadership get the item name. |
| POST | `/api/care/bills/{id}/lines` | Money | `{ priceItemId, quantity, reason }`. |
| POST | `/api/care/bills/{id}/lines/{lineId}/remove` | Money | `{ reason }`. |
| POST | `/api/care/bills/{id}/payments` | Money | `{ amountMinor, method, reference?, idempotencyKey? }` or the `Idempotency-Key` header: a retry returns the first payment. Overpaying puts the bill in credit. |
| POST | `/api/care/payments/{id}/reverse` | Money | `{ reason }`. |
| GET | `/api/care/payments/{id}/receipt.pdf` | Everyone | No bill lines, nothing clinical. Wave 2: the balance printed is the one right after that payment (or "₦X in credit"), not today's; payments from before Wave 2 print the current balance less any credit. |
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
| POST | `/api/care/admissions/{id}/transfer` | Desk writes | `{ bedId, depositMinor?, method?, reference?, overrideByOwner? }`. Wave 2: moving to another ward checks its minimum deposit against the deposits already received on the admission plus `depositMinor` (recorded as a deposit with a receipt): 409 `deposit_below_minimum` naming the minimum and what was paid, unless the medical director sends `overrideByOwner` (audited `care.deposit_override`). A move within the same ward is not checked. |
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
| POST | `/api/care/doses/{id}` | Clinical (S) | `{ status: Given|Held|Refused, note? }`. Wave 2: Given is accepted from an hour before the dose is due; earlier is 409 `dose_not_due` "This dose is due at HH:mm on d MMMM. It can be given from an hour before." Held and Refused may be recorded ahead. |\|Held\|Refused, note? }`. |
| POST | `/api/care/admissions/{id}/prn-doses` | Clinical (S) | Wave 1. An as-needed dose now: `{ prescriptionItemId, status: Given\|Held\|Refused, note? }` → `DoseRecordedDto`; it shows on today's chart. Write rules as nursing notes (404, 409 discharged, 403 "This patient isn't on your ward."); 400 `prescriptionItemId` unless it is a PRN item prescribed on this admission. |
| POST | `/api/care/admissions/{id}/vitals` | Clinical (S) | Wave 1. Ward vital signs `{ vitals }`, the same checks and flags as triage (400 `vitals`, `vitals.{name}`); write rules as nursing notes. A critical reading tells the admitting doctor in-app (no value) unless they took it. Not an open. |
| GET | `/api/care/admissions/{id}/vitals` | Clinical | Wave 1. The ward's sets, oldest first (500). Logged open (area `vitals`, "Ward vital signs"; portal "Vital signs on the ward"). |
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
| GET | `/api/care/reports/takings?from=&to=` | Money reads | By day and method. Wave 1: a closed day never changes. A payment counts on the day it was taken even if reversed later (an online payment on the day the clinic confirmed it); a reversal is a negative row on the day it was made (`reversals` per day and for the range, negative amounts); `totalMinor` is net. The CSV writes reversals as "Reversal: {method}" rows. |
| GET | `/api/care/reports/takings.csv?from=&to=` | Money reads | CSV (formula-safe cells, byte order mark). |
| GET | `/api/care/reports/debtors` | Money reads | With WhatsApp reminder links. Bills flagged for review are left out. Wave 2: credit the patient holds on other bills (not flagged) is netted off; a patient whose credit covers the debt is not listed. |
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
| POST | `/api/care-portal/patients/{id}/appointments` | portal account | `{ doctorUserId, start, reason? }` → the appointment (source Portal). 409 `slot_taken`; 409 for a deceased patient. Wave 2: an account may hold at most 5 open (booked, not yet started) portal bookings across its patients and clinics: 409 `too_many_bookings` "You already have 5 appointments booked online. Cancel one, or call the clinic to book more." The desk can still book. |
| POST | `/api/care-portal/patients/{id}/appointments/{appointmentId}/cancel` | portal account | → the appointment. 409 "This appointment starts soon. Call the clinic to change it." under 2 hours. |
| POST | `/api/care-portal/patients/{id}/appointments/{appointmentId}/reschedule` | portal account | Wave 1 (rate limited `public-write`). `{ start, doctorUserId?, reason? }` → the appointment, moved under the booking rules (the doctor's slots and hours, 400 `start`; 409 `slot_taken`; 409 for a deceased patient). The patient's own booked appointment only (404 otherwise); 409 "This appointment starts soon. Call the clinic to change it." under 2 hours; the patient hears the new time. | Wave 2: under the doctor's lock the appointment is re-read (409 `appointment_changed` when it moved to another doctor meanwhile) and the doctor is checked again (400 `doctorUserId` when they left the clinic or are no longer a doctor), also for the same doctor.
| GET | `/api/care-portal/patients/{id}/contact` | portal account | Wave 1. `{ phone, email, address }` as stored. Not a record read. |
| PUT | `/api/care-portal/patients/{id}/contact` | portal account | `{ phone, email, address }`, a full replace: a field left out is cleared. Audited with ids only. |
| GET | `/api/care-portal/patients/{id}/notifications` | portal account | `[{ id, kind, title, body, link, createdAt, readAt }]`, newest 50: this account's care notices at this clinic for this patient. Mark one read with `POST /api/me/notifications/{id}/read`. |
| GET | `/api/care-portal/patients/{id}/notifications/unread-count` | portal account | Wave 1. `{ count }`: every unread notice for this patient at this clinic (not only the newest 50). |
| GET | `/api/care-portal/patients/{id}/results` | portal account | Wave 1. Reviewed results only: `[{ id, kind, description, resultText, hasFile, resultedAt, corrected }]`. A portal read (access-log area `result`). `corrected`: it replaced a result entered in error (the earlier one is never shown); the record's `results` carry it too. |

There is no portal reschedule route: the portal cancels and books again (spec gap, see Task 20 report).

### Patient notices

| Kind | Sent when | Text (no clinical detail) | Link |
|---|---|---|---|
| `care.appointment_booked` | a booking (desk or portal) | "{First}'s appointment at {clinic} is booked for {day} at {time}." | `/portal/appointments?patient={id}` |
| `care.appointment_changed` | a time move or a cancel | "…has moved to…" / "…was cancelled." | `/portal/appointments?patient={id}` |
| `care.appointment_reminder` | the day before | "{First} has an appointment at {clinic} tomorrow at {time}." | `/portal/appointments?patient={id}` |
| `care.result_ready` | a doctor or the medical director reviews a recorded result (post-build) | "A new result for {First} is ready in your CareLink portal." | `/portal/results?patient={id}` |
| `care.summary_ready` | a discharge summary is signed | "{First}'s discharge summary is ready in your CareLink portal." | `/portal/record?patient={id}` |
| `care.bill_due` | a bill waits for payment | "Bill {n} for {First} at {clinic} has {amount} to pay." | `/portal/bills?patient={id}` (Wave 2: once per balance; going back to payment with the same amount owed sends nothing new) |
| `care.payment_received` | a desk or online payment succeeds | "{clinic} received {amount} for {First} on bill {n}. Receipt {r}." | `/portal/bills?patient={id}` |

Nothing goes to a deceased patient's portal accounts. Notices go in-app and by email.

Wave 4 staff notices (in-app only):

| Kind | To | Text |
|---|---|---|
| `care.critical_result` | the doctor who ordered a lab test with a critical value (not when they entered it) | "A lab test you ordered has a critical result." → `/app/results-to-review` |
| `care.low_stock` | pharmacists and the medical director, once per fall to the reorder level | "{item} is low: {n} {unit} left (reorder at {level})." → `/app/pharmacy/items/{id}` |
| `care.near_expiry` | pharmacists and the medical director, once per batch | "{n} batches expire within 90 days." → `/app/pharmacy/alerts` |

## care-web

| Path | For | Notes |
|---|---|---|
| `/login`, `/signup`, `/invite/{token}` | staff | Staff sign in (password or code), clinic signup, staff invite. |
| `/app/...` | staff | Guarded by `RequireSession`: a clinic session only. A portal session sees "This is the staff app". |
| `/app/lab`, `/app/lab/orders/{id}`, `/app/lab/catalogue`, `/app/lab/turnaround` | lab, leadership | Wave 4: the worklist, one order (collect, label, results with ranges and flags, corrections), the catalogue and turnaround. |
| `/app/pharmacy`, `/app/pharmacy/items/{id}`, `/app/pharmacy/dispense`, `/app/pharmacy/dispense/{id}`, `/app/pharmacy/receive`, `/app/pharmacy/counts`, `/app/pharmacy/alerts`, `/app/pharmacy/reports` | pharmacy, leadership | Wave 4: stock, dispensing, receipts, counts, alerts and reports. |
| `/portal/sign-in` | patients | Code (default) or password sign in, and new accounts. |
| `/portal/invite/{token}` | patients | Public preview, then sign in and choose the relationship to accept. |
| `/portal`, `/portal/record`, `/portal/results`, `/portal/access-log`, `/portal/bills`, `/portal/pay/return`, `/portal/appointments`, `/portal/contact`, `/portal/notifications` | patients | Guarded by `RequirePortalSession`: a customer session only. A staff or support session sees "You're signed in as staff". The chosen patient travels as `?patient={id}`. |
