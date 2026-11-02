# CareLink (clinic core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CareLink (product `care`), the clinic core. It covers:

- clinic setup and the six clinic roles;
- patients and Excel import;
- appointments, check-in, the queue and triage;
- consultations, prescriptions and test results;
- admissions, ward care and discharge;
- bills, desk payments and online payments to the clinic's own Paystack account;
- reports and background jobs;
- the patient portal and in-app notifications;
- its own web app, `care-web`.

**Architecture:** A `Care/` module in each API layer, following the `School/` module.

- **Data and tenancy:** tenant-owned entities under the global tenant filter, and an `IProductProvisioner` for signup.
- **Staff:** on the shared team roles, plus a clinical role on `CareStaffProfile`.
- **Patients:** tenantless customer accounts linked to clinic-owned `Patient` records (as SchoolLink parents are).
- **Payments:** the clinic's own Paystack key through `IMerchantPayments`, using SchoolLink's fee-payment patterns.
- **Shared engines reused:** the shared import engine, `UserNotification`, QuestPDF renderers, and one hosted job service with an advisory lock.
- **Health data:**
  - every clinical read goes through one guard that writes `RecordAccess`;
  - clinical free text is encrypted with `ISecretProtector`.

**Tech Stack:** ASP.NET Core 10, EF Core with Npgsql, PostgreSQL 17 (PostGIS image), QuestPDF, ClosedXML, xUnit v3 with Testcontainers, Next.js 16.4 with Tailwind 4 (`care-web`).

**Spec:** `docs/superpowers/specs/2026-10-26-carelink-clinic-core-design.md`

## Global Constraints

**Process**

- Do not commit. All work stays uncommitted, so there are no commit steps. Snapshot through the plan workspace's shadow repo instead.
- Never kill all node processes.
- Never touch other projects' processes or containers.
- C# strings containing `\n` are written with the Edit/Write tools, never by shell string replacement.
- Stop this checkout's dev API before building, because it locks the DLLs.
- Do not run `next build`.

**Product wiring**

- Product code `care`, name CareLink, policy `product:care` (`Policies.CareProduct`).
- Web app `care-web` on port 4400, with `App:ProductWebUrls.care = http://localhost:4400`, CORS for 4400 and a `.claude/launch.json` entry `care-web`.
- `Products.All` gains `Care`.

**Roles**

- No change to `MemberRole`.
- `CareStaffProfile.ClinicalRole` is one of `Doctor | Nurse | FrontDesk | None`.
- Clinic role labels:

| Label | MemberRole | ClinicalRole |
|---|---|---|
| Medical director | Owner | Doctor by default |
| Admin | Admin | |
| Doctor | Staff | Doctor |
| Nurse | Staff | Nurse |
| Front desk | Staff | FrontDesk |
| Cashier | Accountant | |

- "Clinical access" means ClinicalRole is Doctor or Nurse. The Owner always has it.

**Plans**

- Seeded insert-only; prices are editable in the console.
- Free: ₦0, team_seats 3, beds 0, online_payments 0, remove_branding 0.
- Clinic: ₦25,000 a month (2_500_000 minor), 15, 20, 1, 0.
- Hospital: ₦75,000 a month (7_500_000 minor), 60, 150, 1, 1.
- New limit keys: `beds`, `online_payments`.
- Monthly interval.

**Money and time**

- Money is in kobo.
- `DateTimeOffset` values are UTC.
- Clinic dates and bed days use `DateOnly` in Africa/Lagos.

**Errors**

ProblemDetails, using these exact sentences:

| Status | When | Sentence |
|---|---|---|
| 400 | Field errors | |
| 402 | Plan limit | |
| 403 | Clinical access refused | "Only doctors and nurses can see clinical notes." |
| 404 | Another clinic's or an unlinked patient | |
| 409 | Note already signed | "This note is signed. Add an addendum instead." |
| 409 | Bed taken | "That bed is occupied." |
| 409 | Allergy match not acknowledged | "{Patient} is recorded as allergic to {allergen}." |

**Numbers and references**

- Patient numbers: `{prefix}-{000000}`, prefix default `CL`, counter per clinic.
- Bill numbers: `BILL-{yyyy}-{00000}`.
- Receipt numbers: `RCP-{yyyy}-{00000}`.
- Paystack reference prefix: `care_`.

**Encryption**

- Every clinical free-text field is stored through `ISecretProtector` (protect on write, unprotect on read).
- Clinical files are private, served by short-lived signed links.

**Notifications**

- Notification text never contains diagnoses, results or drug names.
- Patient result template text: "A new result is ready in your CareLink portal."

**Copy**

- Sentence case, plain verbs, no all-caps labels, no exclamation marks.
- Suspended clinic portal text: "This clinic's portal is unavailable."

## Review Focus

1. **Non-clinical staff reaching clinical data indirectly.** Front desk or cashier must never get clinical fields through any route: the record timeline, the patient summary, a bill line that names a drug, a PDF or a portal-shaped route. Every such route gives 403, or omits the clinical fields. Test in Task 3 (`Non_clinical_roles_see_no_clinical_fields_anywhere`, which walks every `/api/care` GET route).
2. **Portal leaks.** A portal user must never see a draft note, an unlinked patient (another family's child) or another clinic's record. Test in Task 15.
3. **Bed-day charges.** These are wrong if counted naively across a transfer at 23:30 Lagos time, an admission and discharge on the same day, or a job run twice in one day. The expected result is exactly one charge per Lagos day at the ward occupied at the start of that day, admission day included, discharge day excluded, and a same-day stay charged one day. Test in Task 13.
4. **Payment races.** A webhook and a portal return-verify for the same reference arriving together, or a wrong amount, must record the payment at most once and flag the mismatch. Test in Task 11.
5. **Access log gaps.** Every staff path that opens clinical data must write `RecordAccess`, including PDF downloads and result-file links. Emergency access without a reason must be refused. Test in Task 3 (`Every_clinical_read_writes_access_log`), checked against Task 16's route table.

---

### Task 1: Product skeleton, roles, plans and signup

**Files:**
- Modify: `api/src/Saas.Application/Billing/PlanService.cs` (`Products.Care = "care"`, add to `All`; `LimitKeys.Beds = "beds"`, `LimitKeys.OnlinePayments = "online_payments"`)
- Modify: `api/src/Saas.Infrastructure/Persistence/Support.cs` (seed the care plans, insert-only)
- Create: `api/src/Saas.Domain/Care/Care.cs` (`ClinicProfile`, `CareStaffProfile`, `ClinicalRole`)
- Create: `api/src/Saas.Application/Care/CareCore.cs` (`CareProvisioner : IProductProvisioner`, `CareUsage : IUsageProvider`), `CareAccess.cs`
- Modify: `AppDbContext.cs`, `IAppDbContext`, `Saas.Application/DependencyInjection.cs`, `Saas.Api/Program.cs` (policy), `CoreEndpoints.cs` (`Policies.CareProduct = "product:care"`), `appsettings.json` (ProductWebUrls, CORS)
- Create: `api/src/Saas.Api/Endpoints/CareEndpoints.cs` (group `/api/care`, `RequireAuthorization(Policies.CareProduct)`)
- Migration `Care1Setup`
- Test: `api/tests/Saas.IntegrationTests/CareKit.cs`, `CareSetupTests.cs`

**Interfaces:**
- Produces: `enum ClinicalRole { None, Doctor, Nurse, FrontDesk }`.
- Produces: `ClinicProfile { Name, Slug, LogoKey?, Address?, Phone?, PatientPrefix = "CL", PaystackSecretKeyProtected?, PaystackPreviousKeyProtected?, PreviousKeyUntil?, DispensesDrugs = true, MinDepositMinor = 0, LowDepositThresholdMinor = 0, RegistrationPriceItemId?, ConsultationPriceItemId? }`.
- Produces: `CareStaffProfile { MembershipId, UserId, ClinicalRole, Title?, LicenceNumber?, SignatureKey? }`.
- `CareAccess` (scoped):
  - `Task<CareCaller> CallerAsync(ct)`, which carries `UserId`, `MemberRole`, `ClinicalRole`, `IsClinical`, `IsLeadership` (Owner or Admin), `IsCashier` (Accountant) and `IsSupportView`.
  - `EnsureClinical(CareCaller)`, which throws 403 with the exact sentence.
  - `EnsureLeadership(CareCaller)`.
- Routes:
  - `GET /api/care/setup` returns the profile, a checklist (profile, price list, wards, staff) and the caller.
  - `PUT /api/care/settings/profile` (leadership).
  - Staff profile: `GET /api/care/staff` and `PUT /api/care/staff/{userId} { clinicalRole, title, licenceNumber }` (leadership).
  - `GET /api/care/staff/me`.
- `CareKit` (tests):
  - `ClinicAsync(string name) -> Clinic(HttpClient Owner, Guid TenantId, string Slug)`.
  - `StaffAsync(Clinic, string clinicRole) -> (HttpClient, Guid UserId)`, where `clinicRole` is one of `"Doctor" | "Nurse" | "FrontDesk" | "Cashier" | "Admin"`.
  - Later tasks add `PatientAsync`, `PortalAsync` and `AdmitAsync`.

- [ ] **Step 1: Failing tests**
  - `Signup_creates_the_clinic_with_free_plan`: signup with `product = "care"`; `GET /api/care/setup` shows a 4-item checklist with every item `done == false`; billing plan is `free` with `beds` 0; the owner's `clinicalRole == "Doctor"`.
  - `Invited_staff_get_clinic_roles`: Doctor, Nurse, FrontDesk, Cashier and Admin each report the right `clinicalRole` and `memberRole` from `/staff/me`.
  - `Only_leadership_sets_clinical_roles`: a Doctor calling `PUT /staff/{id}` gets 403.
- [ ] **Step 2: Run** `cd api && dotnet test --project tests/Saas.IntegrationTests --filter "FullyQualifiedName~CareSetupTests"`. Expected: FAIL (unknown product).
- [ ] **Step 3: Implement**
  - Provisioner: creates the profile and the owner's staff profile with Doctor.
  - Team invites accept `clinicalRole` through a care hook. Mirror the school staff backfill: a profile is created at accept.
  - Plans seed, usage provider (`team_seats` via `TeamUsage`; `beds` = active beds), DbSets, migration, policy, routes.
- [ ] **Step 4: Run** the tests, then the full suite with `cd api && dotnet test`. Expected: all pass.

### Task 2: Price list, patients and bills core

**Files:**
- Modify: `Care.cs` (`PriceItem`, `PriceCategory`, `Patient`, `PatientBill`, `BillLine`, `BillStatus`)
- Create: `Saas.Application/Care/PriceListService.cs`, `PatientService.cs`, `BillingService.cs`
- Modify: `CareEndpoints.cs`; migration `Care2Patients`
- Test: `CarePatientTests.cs`, `CareBillTests.cs`

**Interfaces:**
- Produces:
  - `PriceItem { Name, Category, PriceMinor, Active }`, with `enum PriceCategory { Consultation, Procedure, Test, Drug, Bed, Registration, Other }`.
  - `Patient { Number, FirstName, MiddleName?, LastName, Sex, DateOfBirth, Phone?, Email?, Address?, NextOfKinName?, NextOfKinPhone?, NextOfKinRelationship?, BloodGroup?, Genotype?, Allergies (List<string>), Conditions (List<string>), Status (Active | Inactive | Deceased) }`.
  - `PatientBill { PatientId, VisitId?, AdmissionId?, Number, Status, TotalMinor, PaidMinor, BalanceMinor }`.
  - `BillLine { BillId, PriceItemId?, Description, Quantity, UnitPriceMinor, AmountMinor, Source (Auto | Manual), ClinicalRef?, RemovedReason? }`.
- Routes:
  - `GET/POST/PUT /api/care/price-list` (leadership or cashier write; all staff read).
  - `GET /api/care/patients?q=` (search by name, number or phone).
  - `POST /api/care/patients` returns `{ patient, possibleDuplicates[] }`. Duplicates are the same name and date of birth, or the same phone. The patient is still created; the caller is warned.
  - `GET/PUT /api/care/patients/{id}` for demographics (front desk and above).
  - `PUT /api/care/patients/{id}/clinical-facts { allergies, conditions, bloodGroup, genotype }` (clinical only).
  - `POST /api/care/patients/{id}/inactive`.
- `BillingService.AddLineAsync(Guid billId, Guid priceItemId, int qty, string? clinicalRef, ct)`:
  - freezes the item's current price, recomputes the totals, and is called by Tasks 5, 7, 8 and 13;
  - `OpenBillForVisitAsync(Guid visitId)` and `OpenBillForAdmissionAsync(Guid admissionId)` create a bill on first use.
- Bill line descriptions shown to non-clinical staff are the price-item name only. `ClinicalRef` (for example a prescription item id) is never returned to non-clinical callers.

- [ ] **Step 1: Failing tests**
  - `Patient_numbers_count_up_per_clinic`: the first two patients get `CL-000001` and `CL-000002`, and another clinic starts again at `CL-000001`.
  - `Duplicate_patient_is_warned_not_blocked`.
  - `Front_desk_cannot_set_allergies` (403 with the exact sentence).
  - `Patient_with_history_cannot_be_deleted`: there is no DELETE route; going inactive keeps the record.
  - `Bill_keeps_the_price_charged`: add a line at ₦5,000, change the price item to ₦7,000, and the line stays at 500_000.
  - `Bill_totals_follow_lines_and_removals`.
- [ ] **Step 2: Run** the tests. Expected: FAIL.
- [ ] **Step 3: Implement.** Patient numbers come from the atomic counter (`NextDocumentNumberAsync` with a new `DocumentCounter.CarePatient`; also add `CareBill` and `CareReceipt`).
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 3: Clinical record guard, access log, encryption and emergency access

**Files:**
- Modify: `Care.cs` (`RecordAccess`, `CareAssignment`)
- Create: `Saas.Application/Care/RecordGuard.cs`, `ClinicalText.cs`
- Modify: `CareEndpoints.cs` (`GET /api/care/patients/{id}/access-log`, leadership only); migration `Care3Access`
- Test: `CareAccessTests.cs`

**Interfaces:**
- `ClinicalText`:
  - `string Protect(string? plain)` and `string? Read(string? stored)`, wrapping `ISecretProtector`;
  - EF value converters are not used, because encryption must be visible in the service code.
- `RecordGuard.OpenAsync(Guid patientId, string area, string? reason, ct) -> CareCaller`:
  - requires clinical access;
  - if the caller has no care relationship (Task 5 and Task 8 add `CareAssignment` rows: the visit doctor, the triage nurse, the ward nurse, the admitting doctor), it requires a non-empty `reason`, otherwise 409 `emergency_reason_required` "Give a reason to open this patient's record.";
  - writes `RecordAccess { StaffUserId, Role, PatientId, Area, Reason?, Emergency, At }`;
  - on emergency, notifies the Owner in-app ("Emergency access was used on a patient record.");
  - every later clinical read route calls it first.
- `area` values: `record`, `consultation`, `admission`, `prescription`, `result`, `discharge`, `pdf`, `file`.

- [ ] **Step 1: Failing tests**
  - `Every_clinical_read_writes_access_log`: a doctor opens the record and the log has 1 row with area `record`. Later tasks extend this test with their routes.
  - `Emergency_access_needs_a_reason_and_alerts_the_director`.
  - `Clinical_text_is_encrypted_at_rest`: write a note through `ClinicalText`; the raw column read via `SqlQuery` does not contain the plain text.
  - `Non_clinical_roles_see_no_clinical_fields_anywhere`: enumerate every `/api/care` GET route from the endpoint data source and call each as FrontDesk and as Cashier with real ids. Each returns 403 or a body with no clinical field names (`complaint`, `history`, `examination`, `plan`, `diagnoses`, `allergies`, `conditions`, `prescriptions`, `result`, `note`, `summary`). A route missing from the test's table fails. Later tasks register their routes in this table.
- [ ] **Step 2: Run** the tests. Expected: FAIL.
- [ ] **Step 3: Implement.** Patient GET returns `allergies` and `conditions` only for clinical callers. Demographics are shown to everyone.
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 4: Doctor schedules and appointments

**Files:**
- Modify: `Care.cs` (`DoctorSchedule`, `Appointment`, `AppointmentStatus`)
- Create: `Saas.Application/Care/AppointmentService.cs`
- Modify: `CareEndpoints.cs`; migration `Care4Appointments`
- Test: `CareAppointmentTests.cs`

**Interfaces:**
- Produces:
  - `DoctorSchedule { DoctorUserId, Weekday, Start (TimeOnly), End, SlotMinutes = 15 }`;
  - `Appointment { PatientId, DoctorUserId, Start, End, Reason?, Status (Booked | CheckedIn | Done | Cancelled | NoShow), Source (Desk | Portal) }`.
- Routes:
  - `GET/PUT /api/care/doctors/{userId}/schedule` (leadership or that doctor);
  - `GET /api/care/doctors/{userId}/slots?date=`;
  - `GET/POST /api/care/appointments` (front desk and above);
  - `PUT /api/care/appointments/{id}` (reschedule);
  - `POST /api/care/appointments/{id}/cancel`.
- `AppointmentService.BookAsync(BookRequest, AppointmentSource, ct)` is reused by the portal (Task 15).
- A slot is free when no non-cancelled appointment for that doctor overlaps it. Booking takes a per-doctor advisory lock, so two bookings can never take one slot.

- [ ] **Step 1: Failing tests**
  - `Slots_follow_schedule_minus_bookings`.
  - `Two_bookings_cannot_take_one_slot`: two at once, one gets 409 "That time was just booked. Choose another."
  - `Cashier_cannot_book` (403).
  - `Booking_for_a_deceased_patient_is_refused` (409 "This patient is recorded as deceased.").
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 5: Visits, check-in, queue and triage

**Files:**
- Modify: `Care.cs` (`Visit`, `VisitStage`, `Urgency`, `VitalSigns`)
- Create: `Saas.Application/Care/VisitService.cs`, `Vitals.cs` (ranges)
- Modify: `CareEndpoints.cs`; migration `Care5Visits`
- Test: `CareVisitTests.cs`

**Interfaces:**
- Produces:
  - `Visit { PatientId, ArrivedAt, Stage (WaitingVitals | WaitingDoctor | WithDoctor | WaitingPayment | Done), Urgency (Routine | Urgent | Emergency), DoctorUserId?, AppointmentId? }`;
  - `VitalSigns { PatientId, VisitId?, AdmissionId?, TakenBy, TakenAt, TemperatureC?, Systolic?, Diastolic?, Pulse?, RespRate?, SpO2?, WeightKg?, HeightCm?, Bmi?, Flags (List<string>) }`.
- `Vitals.Flag(VitalSigns) -> List<string>`, using these ranges:
  - temperature normal 36.1–37.8 °C, critical below 35 or at or above 40;
  - systolic normal 90–139, critical below 80 or at or above 180;
  - diastolic normal 60–89, critical at or above 120;
  - pulse normal 60–100, critical below 40 or above 140;
  - respiratory rate normal 12–20, critical below 8 or above 30;
  - SpO2 normal 95 or above, critical below 90.
  Flags read like "High temperature" and "Critical SpO2".
- Routes:
  - `POST /api/care/visits { patientId, appointmentId? }` (front desk). This checks the patient in, adds the registration line if the patient's first visit and the consultation line from `ClinicProfile`, and sets the appointment to CheckedIn.
  - `GET /api/care/queue?date=` returns today's visits by stage with minutes waited. Doctors get `mine=true` by default. The queue shows demographics only.
  - `POST /api/care/visits/{id}/triage { vitals, urgency, doctorUserId }` (nurse or doctor). This writes a `CareAssignment` for the nurse and the doctor.
  - `POST /api/care/visits/{id}/stage { stage }`.
- A critical flag notifies the assigned doctor in-app: "A patient you are seeing has a critical vital sign." No values are included.

- [ ] **Step 1: Failing tests**
  - `Check_in_adds_registration_once_and_consultation_charge`.
  - `Triage_flags_and_notifies_on_critical_values`: SpO2 85 gives the "Critical SpO2" flag and a notice to the doctor whose text has no numbers.
  - `Queue_shows_stages_and_wait`.
  - `Front_desk_cannot_record_vitals` (403).
  - Add the queue and visit GET routes to the Task 3 role-table test.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 6: Consultations, diagnoses, signing, addenda and the record timeline

**Files:**
- Modify: `Care.cs` (`Consultation`, `NoteStatus`, `Diagnosis`, `Addendum`)
- Create: `Saas.Application/Care/ConsultationService.cs`, `DiagnosisList.cs` (built-in list of about 200 common conditions with codes, in a code constant), `RecordTimelineService.cs`
- Modify: `CareEndpoints.cs`; migration `Care6Consultations`
- Test: `CareConsultationTests.cs`

**Interfaces:**
- Produces:
  - `Consultation { VisitId, PatientId, DoctorUserId, Complaint, History, Examination, Plan (all protected), Status (Draft | Signed), SignedAt? }`;
  - `Diagnosis { PatientId, ConsultationId?, AdmissionId?, Text, Code?, Primary }`;
  - `Addendum { NoteKind (Consultation | WardRound | Discharge), NoteId, AuthorUserId, Text (protected), At }`.
- Routes:
  - `POST /api/care/visits/{id}/consultation` opens or creates the consultation (doctor; stage becomes WithDoctor).
  - `PUT /api/care/consultations/{id}` (draft only, author only).
  - `POST /api/care/consultations/{id}/sign`.
  - `POST /api/care/notes/{kind}/{id}/addenda { text }` (any clinical).
  - `GET /api/care/diagnosis-codes?q=`.
  - `GET /api/care/patients/{id}/record` returns a timeline of visits, consultations with addenda, vitals series, diagnoses, prescriptions, results, admissions, discharge summaries and bills. It goes through `RecordGuard`.
- `ConsultationService.EnsureDraft(...)` throws 409 with the exact signed-note sentence.

- [ ] **Step 1: Failing tests**
  - `Signed_note_cannot_change`: PUT after sign returns 409 with the exact sentence; an addendum is accepted and appears in the timeline with author and time.
  - `Only_author_edits_draft`.
  - `Record_timeline_is_logged_and_clinical_only`.
  - `Diagnosis_code_search`: "malaria" returns a coded match.
  - Add these routes to the Task 3 tests.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 7: Prescriptions, allergy warning, tests and results, PDFs

**Files:**
- Modify: `Care.cs` (`Prescription`, `PrescriptionItem`, `TestRequest`)
- Create: `Saas.Application/Care/PrescriptionService.cs`, `TestService.cs`; `Saas.Infrastructure/Care/CarePdf.cs` (prescription PDF on letterhead)
- Modify: `CareEndpoints.cs`; migration `Care7Orders`
- Test: `CareOrderTests.cs`

**Interfaces:**
- Produces:
  - `Prescription { PatientId, ConsultationId?, AdmissionId?, DoctorUserId, AllergyAcknowledged, Items }`;
  - `PrescriptionItem { Drug, Strength?, Dose, Frequency (enum OD | BD | TDS | QDS | Q4H | Q6H | Q8H | Nocte | Stat | PRN), DurationDays?, Quantity?, Route?, Instructions?, PriceItemId? }`;
  - `TestRequest { PatientId, ConsultationId?, AdmissionId?, RequestedBy, Kind (Lab | Imaging | Other), Description, PriceItemId?, Status (Requested | Resulted), ResultText? (protected), ResultFileKey?, ResultedBy?, ResultedAt? }`.
- `FrequencyDosesPerDay`: OD 1, BD 2, TDS 3, QDS 4, Q4H 6, Q6H 4, Q8H 3, Nocte 1, Stat 1 once, PRN 0 (as needed). Used by Task 9.
- Routes:
  - `POST /api/care/prescriptions`. An allergy match where `allergyAcknowledged` is false returns 409 with the exact sentence.
  - `GET /api/care/prescriptions/{id}/pdf`.
  - `POST /api/care/tests`.
  - `POST /api/care/tests/{id}/result` (text and/or multipart file).
  - `GET /api/care/tests/{id}/file` (signed link).
- An allergy matches when a drug name or the allergen, compared case-insensitively, contains the other.
- Drug lines go on the bill only when `ClinicProfile.DispensesDrugs` and the item has `PriceItemId`.
- A result notifies linked portal users with the exact generic sentence.

- [ ] **Step 1: Failing tests**
  - `Allergy_match_needs_acknowledgement` (allergy "Penicillin", drug "Amoxicillin/penicillin").
  - `Drug_lines_billed_only_when_dispensing`.
  - `Result_notice_has_no_clinical_detail`.
  - `Prescription_pdf_is_logged_and_clinical_only`.
  - Add these routes to the Task 3 tests.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 8: Wards, beds, admissions, deposits and transfers

**Files:**
- Modify: `Care.cs` (`Ward`, `Bed`, `BedStatus`, `Admission`, `AdmissionStatus`, `DischargeType`, `BedMove`)
- Create: `Saas.Application/Care/WardService.cs`, `AdmissionService.cs`
- Modify: `CareEndpoints.cs`; migration `Care8Admissions`
- Test: `CareAdmissionTests.cs`

**Interfaces:**
- Produces:
  - `Ward { Name, Kind, BedPriceItemId, MinDepositMinor? }`;
  - `Bed { WardId, Label, Status (Free | Occupied | Cleaning), Active }`;
  - `Admission { PatientId, AdmittingDoctorUserId, Reason, AdmittingDiagnosis, BedId, AdmittedAt, DepositOverrideBy?, Status (Admitted | Discharged), DischargeType?, DischargedAt?, DeathTime?, DeathCause?, AmaReason? }`;
  - `BedMove { AdmissionId, FromBedId, ToBedId, At, ByUserId }`.
- Routes:
  - `CRUD /api/care/wards`, `/wards/{id}/beds` (leadership). Active beds count against the plan's `beds` limit (402).
  - `GET /api/care/bed-board`.
  - `POST /api/care/admissions { patientId, consultationId?, reason, admittingDiagnosis }` (doctor).
  - `POST /api/care/admissions/{id}/bed { bedId, depositMinor, method, reference? }` (front desk). The deposit is recorded as a payment on the admission bill through Task 11's `PaymentService.RecordDeskAsync`. Until Task 11 lands, this task records it through a `DepositRecorder` interface that Task 11 implements.
  - `POST /api/care/admissions/{id}/transfer { bedId }`.
  - `GET /api/care/wards/{id}/patients`.
- A deposit below the ward minimum (or the clinic default) needs `overrideByOwner` from an Owner caller. Otherwise it is 409 "A deposit of at least {amount} is needed for this ward." The override is audited.
- The bed claim is a conditional update on `Status == Free`, so of two admissions racing for one bed, one gets 409 "That bed is occupied."
- Admitting writes `CareAssignment` for the admitting doctor and the ward's nurses (as a ward assignment).

- [ ] **Step 1: Failing tests**
  - `Two_admissions_race_for_one_bed`.
  - `Deposit_below_minimum_needs_director`.
  - `Beds_above_plan_are_refused` (402 on Free).
  - `Transfer_moves_bed_and_frees_old`: the old bed goes to Cleaning.
  - Add these routes to the Task 3 tests.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 9: Ward care — nursing notes, fluids, ward rounds and the medication chart

**Files:**
- Modify: `Care.cs` (`NursingNote`, `FluidEntry`, `WardRound`, `MedicationDose`, `DoseStatus`)
- Create: `Saas.Application/Care/WardCareService.cs`, `MedicationChart.cs`
- Modify: `CareEndpoints.cs`; migration `Care9WardCare`
- Test: `CareWardCareTests.cs`

**Interfaces:**
- `MedicationChart.Schedule(PrescriptionItem item, DateTimeOffset start) -> IReadOnlyList<DateTimeOffset>`. It produces doses evenly spaced across 24 hours from `start`, rounded to the hour, for `DurationDays`, or 3 days when that is null. Stat gives one dose at start; PRN gives none. When an admission prescription is created (Task 7 hook), `MedicationDose` rows are written.
- Routes:
  - `POST /api/care/admissions/{id}/nursing-notes`.
  - `POST /api/care/admissions/{id}/fluids { direction, kind, ml }`.
  - `GET /api/care/admissions/{id}/fluids?date=` returns entries plus 24-hour in and out totals.
  - `POST /api/care/admissions/{id}/ward-rounds`, then sign as in Task 6.
  - `GET /api/care/admissions/{id}/medication-chart?date=`.
  - `POST /api/care/doses/{id} { status: Given | Held | Refused, note? }` (nurse). A dose already recorded gets 409 "This dose is already recorded."
- Nothing can be written on a discharged admission: 409 "This patient has been discharged." The exception is addenda.

- [ ] **Step 1: Failing tests**
  - `Tds_for_3_days_schedules_9_doses`.
  - `Dose_recorded_once`.
  - `Fluid_totals_per_day`.
  - `Ward_round_signing_matches_consultation_rules`.
  - `Discharged_admission_is_final`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 10: Discharge

**Files:**
- Modify: `Care.cs` (`DischargeSummary`)
- Create: `Saas.Application/Care/DischargeService.cs`; `CarePdf.cs` (discharge PDF)
- Modify: `CareEndpoints.cs`; migration `Care10Discharge`
- Test: `CareDischargeTests.cs`

**Interfaces:**
- Produces `DischargeSummary { AdmissionId, Diagnoses, Treatment, Condition, TakeHome, FollowUp (DateOnly?), Status (Draft | Signed) }`. Text fields are protected.
- Routes:
  - `PUT /api/care/admissions/{id}/discharge-summary` (draft).
  - `POST /api/care/admissions/{id}/discharge { type, amaReason?, deathTime?, deathCause? }` (doctor). Home and Transferred-out need a signed summary; AMA needs `amaReason`; Died needs time and cause and sets the patient to Deceased.
  - `GET /api/care/admissions/{id}/discharge.pdf`.
- Discharge frees the bed to Cleaning and finalises the bill (no new lines). A balance stays open.

- [ ] **Step 1: Failing tests**
  - `Home_discharge_needs_signed_summary`.
  - `Died_sets_patient_deceased`.
  - `Discharge_frees_bed_and_keeps_balance_open`.
  - `Discharge_pdf_logged`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 11: Payments, receipts, adjustments and online payment

**Files:**
- Modify: `Care.cs` (`CarePayment`, `BillAdjustment`)
- Create: `Saas.Application/Care/PaymentService.cs`, `OnlinePaymentService.cs`; `CarePdf.cs` (receipt)
- Modify: `CareEndpoints.cs` (Paystack webhook `POST /api/webhooks/paystack/care/{tenantId}`, rate limit `RateLimits.Webhook`); migration `Care11Payments`
- Test: `CarePaymentTests.cs`

**Interfaces:**
- `PaymentService`:
  - `RecordDeskAsync(Guid billId, long amountMinor, PayMethod method, string? reference, ct) -> CarePaymentDto`. It takes the per-tenant care bill lock (`pg_advisory_xact_lock`) and issues a receipt number. This also implements Task 8's `DepositRecorder`.
  - `ReverseAsync(Guid paymentId, string reason)`.
- `POST /api/care/bills/{id}/adjustments { kind: Discount | Waiver, amountMinor, reason }` (leadership). Audited.
- Online payment mirrors SchoolLink `FeePaymentService`:
  - the clinic's key is set with `PUT /api/care/settings/paystack` (Clinic plan or higher, otherwise 402);
  - the portal starts a checkout with reference `care_{billId}_{random}`;
  - a webhook signed with the clinic key, or the previous key within 7 days through verify, is accepted;
  - return-verify;
  - a conditional claim, so a payment is recorded at most once;
  - a wrong amount gives Mismatch;
  - an overpayment is recorded and flagged;
  - Abandoned is a soft state that a later confirmation still accepts.

- [ ] **Step 1: Failing tests**
  - `Desk_payment_issues_receipt_and_updates_balance`.
  - `Webhook_and_verify_together_record_once`.
  - `Wrong_amount_is_mismatch`.
  - `Reversal_restores_balance_and_is_audited`.
  - `Online_needs_clinic_plan` (402 on Free).
  - `Cashier_sees_no_clinical_refs_on_bills`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 12: Reports and Excel import

**Files:**
- Create: `Saas.Application/Care/CareReportService.cs`; `Imports/Importers/CarePatientImporter.cs`, `CarePriceListImporter.cs`, `CareStaffImporter.cs`
- Modify: `CareEndpoints.cs` (imports through the shared generic routes under `/api/care/imports/...`, using the shared engine), `DependencyInjection.cs`
- Test: `CareReportTests.cs`, `CareImportTests.cs`

**Interfaces:**
- Reports (cashier and leadership), each with a CSV (`Csv.Cell`, BOM, local phone format):
  - `GET /api/care/reports/takings?from&to` gives totals by method and day;
  - `/debtors` gives open balances with a WhatsApp link: "Hello {name}, you have a balance of {amount} at {clinic}.";
  - `/revenue?from&to` gives revenue by price category;
  - `/deposits` gives deposits held for current admissions.
- The import engine is reused unchanged. These are list kinds, with the same access as each screen:
  - `care-patients` (columns: first_name, middle_name, last_name, sex, date_of_birth, phone, email, address, next_of_kin_name, next_of_kin_phone, next_of_kin_relationship);
  - `care-price-list` (name, category, price);
  - `care-staff` (email, role, title).
- The engine's routes may be generalised to take the product's route prefix. If the engine is school-specific, add the smallest product-neutral seam and name it in the report.

- [ ] **Step 1: Failing tests**
  - `Takings_report_totals_by_method`.
  - `Debtors_csv_escapes_and_formats_phone`.
  - `Patient_import_preview_and_confirm`.
  - `Import_refuses_clinical_columns` (an allergies column is "ignored" with a warning).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 13: Care jobs

**Files:**
- Create: `Saas.Application/Care/CareJobs.cs`; `Saas.Infrastructure/Care/CareJobsHostedService.cs` (every minute, `pg_try_advisory_lock(0x1_1A5_0007)`, `JobRuns.TrackAsync("care")`, `Jobs:CareEnabled` default true; set to false in `ApiFixture`)
- Modify: `Care.cs` (`ClinicProfile.LastBedChargeDay`, `Admission.LowDepositNotifiedAt`); migration `Care13Jobs`
- Test: `CareJobTests.cs`

**Interfaces:**
- `CareJobs.RunOnceAsync(DateTimeOffset now)`. It covers active clinics only, with each step in its own tenant scope and its own try/catch. Steps:
  1. Appointment reminders, the day before at 08:00 Lagos time.
  2. No-shows: a Booked appointment whose end was before today in Lagos time becomes NoShow.
  3. Missed doses: Due and 60 minutes past becomes Missed, with one notice to the ward's nurses.
  4. Bed charges: for each admitted (or discharged-later) admission, add one bed line for each Lagos day from admission day up to yesterday that has none yet. The charge uses the ward occupied at 00:00 Lagos time that day; on the admission day itself, use the admission ward. A stay that starts and ends on the same day gets one charge, at discharge. The discharge day is otherwise excluded. Each bed line is keyed (admission, day), so the step is idempotent.
  5. Low deposit: when the balance owed passes the threshold, notify once per crossing.

- [ ] **Step 1: Failing tests**
  - `Bed_charges_one_per_day_across_transfer_at_2330`: admitted on day 1 in ward A; moved to ward B at 23:30 on day 2; run on day 4. Expected charges: day 1 A, day 2 A, day 3 B.
  - `Same_day_stay_charged_once`.
  - `Job_twice_adds_nothing`.
  - `Missed_dose_flagged_once`.
  - `No_shows_marked`.
  - `Low_deposit_notified_once`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 14: Notifications and suspension

**Files:**
- Modify: `Saas.Infrastructure/Notifications/Notifications.cs` (care templates), `CareCore.cs` (notification helper)
- Test: `CareNotificationTests.cs`

**Interfaces:**
- `CareNotices` uses `InAppNotifications.Add` plus email templates, all written in the same transaction:
  - `care.appointment_booked`, `care.appointment_changed`, `care.appointment_reminder`;
  - `care.result_ready`, `care.summary_ready`;
  - `care.bill_due`, `care.payment_received`;
  - staff: `care.patient_waiting`, `care.critical_vital`, `care.missed_dose`, `care.low_deposit`, `care.emergency_access`.
- A suspended clinic: staff routes return 403 through the existing tenant status. Portal routes return 403 "This clinic's portal is unavailable."

- [ ] **Step 1: Failing tests**
  - `No_notice_contains_clinical_detail`: render every care template with real data and assert that no drug name, diagnosis text or result text appears.
  - `Suspended_clinic_blocks_staff_and_portal`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 15: Patient portal API

**Files:**
- Modify: `Care.cs` (`PatientLink`, `PatientInvite`)
- Create: `Saas.Application/Care/PatientInviteService.cs`, `PortalService.cs`, `PatientPortalAccess.cs`
- Create: `Saas.Api/Endpoints/CarePortalEndpoints.cs` (`/api/care-portal`, customer tokens); migration `Care15Portal`
- Test: `CarePortalTests.cs`

**Interfaces:**
- `POST /api/care/patients/{id}/invites { email?, phone? }` returns `{ link, whatsAppUrl }`. The token lasts 14 days and is single use.
- `POST /api/care-portal/invites/{token}/accept`: the customer signs in with an emailed code or a password. A `PatientLink` is created with relationship `self | parent | guardian`.
- `PatientPortalAccess.RequirePatientAsync(Guid patientId)` returns:
  - 404 if the patient isn't linked to the caller;
  - 403 with the suspended sentence if the clinic is suspended;
  - otherwise it switches the tenant to that patient's clinic.
- Portal routes:
  - `GET /api/care-portal/patients` (linked patients across clinics);
  - `GET /api/care-portal/patients/{id}/record` (signed notes only, no drafts; addenda included);
  - `GET .../access-log` (staff name and role only);
  - `GET .../bills`;
  - `POST .../bills/{billId}/pay` (Task 11 checkout);
  - `GET .../receipts/{paymentId}.pdf`;
  - `POST .../appointments` (`AppointmentService.BookAsync` with Source Portal);
  - `POST .../appointments/{id}/cancel`, at least 2 hours before the start, otherwise 409 "This appointment starts soon. Call the clinic to change it.";
  - `PUT .../contact`.
- Portal reads don't write `RecordAccess`, because the patient reading their own record is not staff access.

- [ ] **Step 1: Failing tests**
  - `Portal_never_shows_drafts`.
  - `Unlinked_child_is_404`.
  - `Other_clinic_record_is_404`.
  - `Parent_sees_both_children`.
  - `Portal_pays_bill_online`.
  - `Late_cancel_refused`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 16: Platform wiring and the role table

**Files:**
- Modify:
  - the admin console API: `PlatformBusinessService` usage for care (staff, beds, patients) and the product list;
  - `admin-web/src/lib/products.ts` (CareLink name, limit keys and labels);
  - `hub-web/src/lib/products.ts` (CareLink row, accent `#0F766E`, white text, `NEXT_PUBLIC_CARELINK_URL` default `http://localhost:4400`);
  - `hub-web/.env.example`.
- Test: `CarePlatformTests.cs`

**Interfaces:**
- `Care_role_table` follows SchoolLink's `School_role_table`. It enumerates every `/api/care` route and gives the expected allow or deny for Owner, Admin, Doctor, Nurse, FrontDesk, Cashier, a portal token and a support view. A route missing from the table fails the test.
- Support view never receives clinical fields. It is read-only and behaves like a non-clinical role.

- [ ] **Step 1: Failing tests**
  - `Care_role_table`.
  - `Clinic_appears_in_console_with_usage`.
  - `Support_view_sees_no_clinical_fields`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** In hub-web, run tsc and lint.
- [ ] **Step 4: Run** the tests and the full suite. Expected: PASS.

### Task 17: care-web scaffold, auth, setup, staff, patients and price list

**Files:**
- Create `care-web/`, copying school-web's structure and config: Next 16.4, Tailwind 4, `src/lib/api.ts`, `auth.ts`, `use-api.ts`, `components/ui.tsx`, `import-dialog.tsx`, and a notification bell.
- Write `docs/design/carelink.md` following the frontend-design skill (its own palette, with accent `#0F766E`).
- Add a `care-web` entry to `.claude/launch.json` and a CI job.

**Pages:**
- Public: landing, pricing, login, signup, forgot/reset password, invite accept.
- Staff app `/app`: setup checklist, settings (profile, Paystack), staff (clinical roles), price list (with Excel import), patients (search, register with duplicate warning, edit, clinical facts for clinical roles only), patient import.

- [ ] **Step 1:** Build the pages. `npx tsc --noEmit` and `npm run lint` must pass.
- [ ] **Step 2:** Check that navigation hides clinical items for front desk and cashier.

### Task 18: care-web clinical screens

**Pages:**
- **Queue:** by stage, polled every 30 seconds.
- **Check-in.**
- **Appointments:** calendar by doctor, booking.
- **Triage:** vitals with flags.
- **Consultation:** patient summary banner, autosaving draft, diagnoses search, prescriptions with the allergy acknowledgement dialog, tests, sign, addenda.
- **Test results:** entry and upload.
- **Patient record timeline:** with a vitals chart.
- **Emergency access:** a reason dialog shown on a 409 `emergency_reason_required`.

- [ ] **Step 1:** Build the pages. tsc and lint must pass.

### Task 19: care-web inpatients, billing and reports

**Pages:**
- **Inpatients:** wards and beds setup; bed board; admit (bed and deposit, director override); ward list; nursing notes; fluid chart; medication chart with dose buttons; ward rounds; transfer; discharge summary and discharge types.
- **Billing:** bills (lines, adjustments, desk payment, receipt PDF, reversal); reports with CSV; debtors with WhatsApp links.

- [ ] **Step 1:** Build the pages. tsc and lint must pass.

### Task 20: care-web patient portal, notifications and docs

**Pages:**
- **Portal `/patient`:**
  - patient switcher (self and children, across clinics);
  - record (signed only);
  - who viewed it;
  - bills with "Pay now" and the return verify;
  - receipts;
  - appointments (book and cancel);
  - contact details;
  - notification bell (staff and portal).
- **Docs:** `docs/CARE_API.md` (every route), README product table row, `docs/PLATFORM_API.md`.

- [ ] **Step 1:** Build the pages. tsc and lint must pass.

### Task 21: Final review and walkthrough

- [ ] **Step 1:** Run the full suite with `cd api && dotnet test`. Expected: all pass.
- [ ] **Step 2:** Run a fresh whole-branch review on the most capable model, against the spec and this plan's Review Focus. Fix Critical and Important findings with a failing test first, and record rulings in the ledger.
- [ ] **Step 3:** Browser walkthrough on this checkout's API (`api-local-db`) and care-web on 4400:
  - register → triage → consult → prescribe → bill → pay at the desk;
  - admit → ward round → dose given → discharge with the PDF;
  - invite the patient → the portal shows the signed record and pays online with the local fixture Paystack key (Development only);
  - front desk cannot open a note.

  Record the results in the ledger.
