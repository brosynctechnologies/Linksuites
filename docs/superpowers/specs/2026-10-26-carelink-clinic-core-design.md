# CareLink: clinic core design

Date: 26 Oct 2026. This is product 7 of LinkSuite and the first of three CareLink stages. It runs on the shared multi-tenant platform (ASP.NET Core 10, EF Core, PostgreSQL, Next.js 16, Tailwind 4, Paystack, QuestPDF, R2, Brevo), the same as OrderLink, InvoiceLink, BookLink, LearnLink, PropertyLink and SchoolLink.

## Intent

CareLink is for private clinics and small hospitals in Nigeria, starting in Lagos. They pay a monthly plan to run:

- the front desk and the waiting queue;
- patient records and consultations;
- prescriptions;
- admissions with wards, beds and nursing care;
- billing.

Patients use a portal to book appointments, read their own record and pay their bills.

Success: a clinic can sign up and set up its price list, wards and staff without our help. It can then register or import patients and run a full outpatient visit (check-in → vitals → consultation → prescription → bill → payment). It can admit a patient, run ward care, discharge with a summary, and have patients paying and reading their records in the portal.

## Stages

| Stage | Contents | Status |
|---|---|---|
| 1. Clinic core | This spec | Designing |
| 2. Telemedicine | Video and chat consultations with clinic doctors and independent doctors; patients pay to book; the platform takes a fee | Later, its own spec |
| 3. Clinic extensions | Lab module, in-house pharmacy and stock, HMO billing and claims | Later, its own spec |

## Decisions agreed

| Topic | Decision |
|---|---|
| Customers | Clinics with outpatients and admissions (wards, beds, nursing, discharge) |
| Architecture | A seventh product on the shared core, with extra health-data protection: access log, encrypted clinical fields, final signed notes with addenda, emergency access |
| Roles | Six clinic roles built on the platform roles (table below) |
| Patient money | Cash, POS and transfer recorded by the cashier; online payment through the clinic's own Paystack key; deposits for admissions; no HMO in this stage |
| Portal | Booking, payments and full access to the patient's own record once notes are signed |
| Pricing | Monthly plans by size: Free, Clinic, Hospital |
| Name | CareLink, product key `care`, app `care-web` on port 4400 |

## Architecture

- **API code:** `Saas.Domain/Care`, `Saas.Application/Care`, `Saas.Infrastructure/Care` (PDFs and jobs), and `Saas.Api/Endpoints/CareEndpoints.cs` with policy `product:care`.
- **Tenancy:** every clinic record is a `TenantEntity` under the global tenant filter.
- **Shared parts reused unchanged:** sign-in, team invites and seats, plans and Paystack subscription checkout, notifications (email, in-app feed, WhatsApp links), file storage (R2), the admin console (suspend, view-as, comps) and the Excel import engine.
- **Patients:** each patient is a clinic-owned `Patient` record. Portal users are customer accounts (no membership) linked to patient records, the same pattern as SchoolLink parents:
  - one login can link to several patients (a parent and their children) and to several clinics;
  - each clinic's records stay separate.
- **Home page:** CareLink gets a row on the LinkSuite home page, and the admin console lists it as a product.

### Roles

| Clinic role | Platform role | Clinical role on staff profile | Clinical notes | Main work |
|---|---|---|---|---|
| Medical director | Owner | Doctor (default) | Yes | Everything, including settings and plans |
| Admin | Admin | Optional | Only with a clinical role | Staff, settings, wards, prices, reports, waivers |
| Doctor | Staff | Doctor | Yes | Consultations, diagnoses, prescriptions, test requests, admissions, ward rounds, discharge |
| Nurse | Staff | Nurse | Yes | Vitals, triage, nursing notes, fluid chart, medication given |
| Front desk | Staff | FrontDesk | No | Registration, appointments, check-in, queue, bed assignment, admission paperwork |
| Cashier | Accountant | — | No | Bills, deposits, payments, receipts, reversals, debtors, reports |

The server checks the clinical role on every clinical route. "Clinical notes" here covers:

- consultation and ward-round notes;
- diagnoses, prescriptions and test results;
- nursing notes and the medication chart;
- discharge summaries;
- clinical documents.

Front desk and cashier see demographics, appointments, the queue, beds and bills only.

## Health-data protection (NDPA)

- **Access log.** Each opening of a patient's clinical record (record page, consultation, admission) writes a `RecordAccess` row: who, role, patient, what was opened, when, and the reason when one is required.
  - The medical director can read the log for any patient.
  - The patient sees who viewed their record (staff name and role only).
- **Care relationship.** Staff who opened the visit or admission, or are assigned to it, can open the record freely. These are the patient's doctor, the triage nurse, or a nurse on the patient's ward.
- **Emergency access.** Any other clinical staff member must enter a reason. That is logged, and the medical director gets an in-app notice.
- **Encryption.** Clinical free text (notes, histories, examinations, plans, nursing notes, discharge summaries, result text) is encrypted at rest with the platform's data-protection key, on top of database encryption. Clinical files are private in R2 and served only through short-lived signed links.
- **Signed means final.** A signed consultation note, ward-round note or discharge summary cannot be edited. A correction is an addendum with author and time, and the original stays visible.
- **Notification content.** Notifications never contain diagnoses, results or drug names, only "A new result is ready in your CareLink portal".
- **Audit.** Waivers, discounts, deposit overrides, reversals, emergency access and record exports go to the audit log.

## Data model

| Entity | Key fields |
|---|---|
| `ClinicProfile` (1 per tenant) | name, slug, logo, address, phone, letterhead details, patient number prefix, Paystack secret (encrypted), minimum deposit default, low-deposit threshold |
| `CareStaffProfile` (separate from SchoolLink's `StaffProfile`) | membership, clinical role (Doctor, Nurse, FrontDesk or none), title, licence number (optional), signature image key |
| `Patient` | clinic number (`CL-000123`), names, sex, date of birth, phone, email, address, next of kin (name, phone, relationship), blood group, genotype, allergies (list), key conditions (list), status (active, deceased) |
| `PatientLink` | customer user ↔ patient, relationship (self, parent, guardian) |
| `PatientInvite` | patient, contact, token, expiry, accepted at |
| `DoctorSchedule` | doctor, weekday, start, end, slot minutes |
| `Appointment` | patient, doctor, start, end, reason, status (booked, checked in, done, cancelled, no-show), source (desk or portal) |
| `Visit` | patient, arrived at, stage (waiting vitals, waiting doctor, with doctor, waiting payment, done), urgency (routine, urgent, emergency), doctor, appointment (optional) |
| `VitalSigns` | patient, visit or admission, taken by, time; temperature, BP systolic and diastolic, pulse, respiratory rate, SpO2, weight, height, BMI (computed); each value flagged when out of range |
| `Consultation` | visit, doctor, complaint, history, examination, plan (encrypted), status (draft, signed), signed at |
| `Diagnosis` | consultation or admission, text, optional code from the built-in list, primary flag |
| `Addendum` | note type and id, author, text (encrypted), time |
| `Prescription` | patient, consultation or admission, doctor; items: drug, strength, dose, frequency, duration, quantity, route, instructions; allergy warning acknowledged flag |
| `TestRequest` | patient, consultation or admission, requested by, kind (lab, imaging, other), description, status (requested, resulted), result text (encrypted), result file, resulted by and at |
| `Ward` | name, kind, daily bed charge (price-list item) |
| `Bed` | ward, label, status (free, occupied, cleaning) |
| `Admission` | patient, admitting doctor, reason, admitting diagnosis, bed, admitted at, deposit, status (admitted, discharged), discharge type (home, against medical advice, transferred out, died), discharged at |
| `BedMove` | admission, from bed, to bed, time, by |
| `NursingNote` | admission, nurse, time, text (encrypted) |
| `FluidEntry` | admission, time, in or out, kind, millilitres |
| `MedicationDose` | admission, prescription item, due at, status (due, given, held, refused, missed), recorded by and at, note |
| `WardRound` | admission, doctor, note (encrypted), status (draft, signed) |
| `DischargeSummary` | admission, diagnoses, treatment, condition, take-home drugs, follow-up date, status (draft, signed) |
| `PriceItem` | name, category (consultation, procedure, test, drug, bed, registration, other), price in kobo, active |
| `PatientBill` | patient, visit or admission, number, status (unpaid, part paid, paid, waived), total, paid, balance |
| `BillLine` | bill, price item, description, quantity, unit price in kobo (frozen at charge time), amount, source (auto or manual), removed with reason |
| `BillAdjustment` | bill, kind (discount, waiver), amount, reason, by |
| `CarePayment` | bill, amount in kobo, method (cash, POS, transfer, online), reference, receipt number, status (succeeded, mismatch, reversed), reversal reason, Paystack reference |
| `RecordAccess` | staff, patient, area, reason, emergency flag, time |

All money is stored in kobo. A bill line keeps the price it was charged at. Receipt and bill numbers come from the atomic counter, per clinic.

## Phase 1: setup, roles, patients

- **Signup:** product `care` creates the clinic with a Free plan and a setup checklist (clinic profile, price list, wards, staff).
- **Staff:**
  - Invites carry the clinic role. The staff screen sets the clinical role, title, licence number and signature.
  - Seats come from the plan.
- **Price list:** create, edit, deactivate, by category.
- **Patients:**
  - Register with the fields above. Duplicate warning on the same name and date of birth, or the same phone.
  - Search by name, number or phone.
  - Edit demographics (front desk and above). Allergies and key conditions are clinical staff only.
- **Excel import** (engine kinds `patients`, `price-list`, `staff`): template, preview and confirm, with the same rules as the screens.

## Phase 2: appointments, check-in, queue, vitals

- **Doctor schedules:** set by the admin or the doctor. Slots are computed from them minus booked appointments.
- **Appointments:**
  - Front desk books, reschedules and cancels.
  - Reminders the day before go in-app and by email, plus a WhatsApp link for the desk.
  - Unattended appointments become no-shows at day end (job).
- **Check-in:** creates a `Visit` from an appointment, or as a walk-in, and adds the registration or consultation charge to the visit bill per the clinic's settings.
- **Queue screen:**
  - Today's visits by stage, with waiting time.
  - Doctors see their own queue; the front desk sees all.
  - Polls every 30 seconds.
- **Triage:**
  - The nurse records vitals and urgency and assigns the doctor.
  - Out-of-range vitals are flagged. Critical values notify the assigned doctor.

## Phase 3: consultations and records

- **Consultation screen:**
  - Shows the patient summary first: allergies, conditions, recent vitals, last visits, current medications.
  - The doctor writes complaint, history, examination, diagnoses and plan.
  - Drafts autosave.
- **Diagnoses:** free text, with an optional code from a built-in searchable list of common conditions. A national coding standard is not required in this stage.
- **Prescriptions:**
  - Structured items. A warning appears when a drug matches a recorded allergy (by name match). Prescribing anyway needs an explicit acknowledgement, which is stored.
  - A printable prescription PDF on the clinic's letterhead.
  - Drug charges are added to the bill when the clinic's setting says the clinic dispenses.
- **Tests:**
  - Free-text request; the charge comes from the price list.
  - The result is typed or uploaded as a file by a doctor or nurse.
  - The patient is notified, with no detail in the message.
- **Sign:** locks the note; addenda after that. Signing makes the note visible in the portal.
- **Patient record page:**
  - A timeline of visits, admissions, vitals trends (chart), diagnoses, prescriptions, results, documents and bills.
  - An allergy and condition banner at the top.
- **Access log and emergency access:** as in the Health-data protection section.

## Phase 4: admissions, wards, discharge

- **Wards and beds:** set up by the admin. A bed board shows each bed as free, occupied or cleaning.
- **Admit:**
  - The doctor admits from a consultation with a reason and an admitting diagnosis.
  - The front desk assigns a bed and records the deposit.
  - A deposit below the ward minimum needs a medical director override (audited).
- **Ward care:**
  - Nursing notes and vitals.
  - Fluid chart (in and out, with a 24-hour total).
  - Ward-round notes (sign and addenda).
  - New prescriptions and tests.
- **Medication chart:**
  - Each admission prescription item is expanded into scheduled doses from its frequency and duration.
  - Nurses record each dose as given, held or refused.
  - A job marks doses missed 60 minutes after they are due and notifies the ward's nurses.
- **Transfer:** a bed or ward move is recorded. The bed charge follows the ward from that day.
- **Daily bed charge:** a job adds one bed line per admission per calendar day (Lagos time), using the ward the patient was in at the start of that day. The admission day is charged; the discharge day is not.
- **Low deposit:** when the running balance passes the clinic threshold, the cashier and front desk are notified once per threshold crossing.
- **Discharge:**
  - A discharge summary, signed and printable as a PDF on the clinic's letterhead.
  - The discharge type covers against medical advice (with a reason) and death (with time and cause).
  - The bed goes to cleaning and the bill is finalised. A balance stays open as a debt.

## Phase 5: billing and payments

- **Bills:**
  - One per outpatient visit and one per admission.
  - Automatic lines come from check-in, consultations, tests, drugs (when dispensing), procedures and bed days.
  - Manual lines and removals are made by the cashier or admin, with a reason.
- **Adjustments:** discounts and waivers by an admin or the medical director, with a reason, audited.
- **Payments:**
  - Cash, POS or transfer is recorded with a reference and gets a numbered receipt (PDF).
  - Online payment from the portal goes through the clinic's Paystack key, the same way as SchoolLink fees:
    - webhook, then verify;
    - a conditional claim so a payment is recorded at most once;
    - a wrong amount becomes Mismatch and needs review;
    - an overpayment is recorded and flagged.
  - The bill lock is held while a payment is applied.
  - Reversal needs a reason and is audited.
- **Reports** (cashier, admin, medical director), each with CSV export:
  - daily takings by method;
  - debtors with WhatsApp reminder links;
  - revenue by category;
  - deposits held for current admissions.
- **Plans:** limits block new staff or beds above the plan with an upgrade prompt (402). Existing data is never blocked. Online payment needs the Clinic plan or higher.

## Phase 6: portal, notifications, platform wiring

- **Portal sign-in:** invite by email or a WhatsApp link, then sign in with an emailed code or a password. A parent links their children.
- **Portal features:**
  - Book, cancel or reschedule within the clinic's rules.
  - Read signed notes, diagnoses, prescriptions, results, vitals history, admissions and discharge summaries.
  - See who viewed their record.
  - View and pay bills; download receipts.
  - Update contact details.
- **Notifications** (in-app feed and email; WhatsApp links for staff to send):
  - Patients: booked, changed, reminder, result ready, summary ready, bill due, payment received.
  - Staff: patient waiting, critical vital, missed dose, low deposit, emergency access (medical director).
- **Admin console:** CareLink businesses with usage (staff, beds, patients), plans, comps, suspension and view-as. Support view is read-only and never shows clinical notes.
- **Wiring:** the LinkSuite home page row, docs (`docs/CARE_API.md`, README row, PLATFORM_API), and the browser walkthrough.

## Plans

| Plan | Price | Staff logins | Beds | Online payments | Branding |
|---|---|---|---|---|---|
| Free | ₦0 | 3 | 0 (outpatient only) | No | Shown |
| Clinic | ₦25,000 / month | 15 | 20 | Yes | Shown |
| Hospital | ₦75,000 / month | 60 | 150 | Yes | Removed |

Plans are seeded insert-only, with prices editable in the admin console. Limit keys: `team_seats`, `beds`, `online_payments`, `remove_branding`.

## Errors and edge cases

- **Clinical notes, non-clinical staff:** 403 "Only doctors and nurses can see clinical notes."
- **Editing a signed note:** 409 "This note is signed. Add an addendum instead."
- **Another clinic's patient, or an unlinked patient in the portal:** 404.
- **Admitting to an occupied bed:** 409 "That bed is occupied." Two admissions racing for the last bed: the bed claim is conditional, so one wins.
- **Discharged admission:** no new charges, doses or notes, only addenda. Final.
- **Deceased patient:** no new appointments or visits.
- **Allergy match:** 409 with the allergy named, unless acknowledged.
- **Payment twice, or webhook and return verify together:** recorded once.
- **Deleting a patient with any history:** refused. Patients are made inactive instead.

## Testing

Integration tests on Testcontainers, test-first for every feature, full suite green. They must prove that:

- front desk and cashier can never read clinical data, on every clinical route;
- one clinic never sees another's patients;
- a portal user sees only linked patients' signed records;
- every clinical record open writes a `RecordAccess` row, and emergency access requires a reason and notifies the medical director;
- signed notes are immutable and addenda work;
- payments are never doubled, and Mismatch is flagged;
- bed-day charges are correct across transfers, and the discharge day is excluded;
- missed doses are flagged once;
- deposits below the minimum need an override;
- notification text carries no clinical detail.

A browser walkthrough at the end covers:

- register → triage → consult → prescribe → bill → pay;
- admit → ward round → medication given → discharge;
- the patient views their record and pays online.

## Out of scope for this stage

- Telemedicine (stage 2).
- Lab module, pharmacy stock and HMO billing and claims (stage 3).
- Paid SMS and the WhatsApp Business API.
- National health ID integration.
- Theatre scheduling.
- Maternity charts.
- Multi-branch clinics under one account.
