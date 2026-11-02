# SchoolLink: School Management System design

Date: 13 Oct 2026. Product 6 of the Six SaaS plan, built on the shared multi-tenant core (ASP.NET Core 10, EF Core, PostgreSQL,
Next.js 16, Tailwind 4, Paystack, QuestPDF, R2, Brevo), like OrderLink, InvoiceLink, BookLink, LearnLink and PropertyLink.

## Intent

Private nursery, primary and secondary schools (Lagos first) buy a per-term or yearly licence priced by student count. They pay for
three things: **fees collected without chasing** (invoices, instalments, online payment, automatic reminders), **results and printed
report cards** without manual computation, and **parents kept informed**. Attendance supports those; it doesn't sell on its own.
Success: a school can sign up, import its students from Excel, run a term (attendance, scores, report cards, fees) and have parents
paying and reading results in the portal, without help from us.

## Decisions agreed

| Topic | Decision |
|---|---|
| Scope | All 8 MVP modules in one spec and plan, delivered in phases A–F |
| Fee money | Straight to the school through its own Paystack key (as InvoiceLink); the platform earns only the licence |
| Messaging | Email (Brevo) plus WhatsApp click-to-send links; paid SMS and WhatsApp API later |
| Curriculum | Nigerian defaults (3 terms, Creche–SSS 3, CA1/CA2/Exam, WAEC-style grades), all configurable |
| Pricing | Plans by student count through the shared plans and Paystack subscription checkout |
| Parents | Invite link (email, or a WhatsApp message staff send); sign in with an emailed code or password; one account across schools |
| Students | Logins for JSS/SSS only (admission number + password set by the school) |
| Attendance | Offline-tolerant register |
| Architecture | Approach A: staff use the shared team roles; parents and students are customer accounts linked to records |
| In-app notifications | A shared-core notification feed (bell + unread count, polled), written alongside the emails; SchoolLink uses it first |

## Architecture

- Product key `school`, name **SchoolLink**, app `school-web` on port 4200 (`App:ProductWebUrls:school`).
- API: `Saas.Domain/School`, `Saas.Application/School`, `Saas.Infrastructure/School` (jobs, PDFs), `Saas.Api/Endpoints/SchoolEndpoints.cs`,
  policy `product:school`. Every school record is a `TenantEntity` under the global tenant filter.
- **Staff** are members of the school tenant with the shared roles, labelled in the school app:
  Owner = Proprietor, Admin = Principal or School admin (job title), Accountant = Bursar, Staff = Teacher.
  Team invites, seats, the admin console, suspension, view-as and billing work unchanged.
- **Parents and students** are customer accounts (no membership), like LearnLink learners and PropertyLink seekers. Access is by link:
  a parent account links to guardian records (any number, any schools); a student account links to one student record.
- Permissions inside a school:

| Area | Proprietor / Principal / School admin | Bursar | Teacher | Parent | Student |
|---|---|---|---|---|---|
| Setup, students, staff, timetable | edit | read students | read own classes | — | — |
| Attendance | read all, edit | — | take/edit own classes | own children | — |
| Scores | read all, lock/unlock, publish | — | enter for own class-subjects until locked | published only | own, published |
| Remarks | principal remark | — | class teacher remark (own class) | published | published |
| Fees, payments, expenses | read all; Owner/Admin may record payments | full | — | own children, pay | — |
| Announcements | post to any audience | post | post to own classes | read | read |
| Messages | school office inbox | school office inbox | own classes' threads | own threads | — |

## Data model

| Entity | Key fields |
|---|---|
| `SchoolProfile` (1 per tenant) | name, slug, logo, address, motto, principal signature image key, `ResultsHoldForUnpaidFees` (bool, default false), reminder schedule, Paystack secret (encrypted, via `ISecretProtector`) |
| `AcademicSession` | name ("2026/2027"), start, end, is_current (one per school) |
| `Term` | session, number (1–3), name, start, end, next_term_starts, is_current |
| `Level` | name (JSS 1), stage (Creche/Nursery/Primary/JSS/SSS), order, `HasStudentLogins` (true for JSS/SSS) |
| `SchoolClass` | session, level, arm ("A"), name ("JSS 1A"), class_teacher_staff_id |
| `Subject` | name, code |
| `ClassSubject` | class, subject, teacher_staff_id |
| `AssessmentScheme` / `AssessmentComponent` | per school, optional override per level; components name, max_score, order (default CA1 20, CA2 20, Exam 60; must total 100) |
| `GradingBand` | per school, optional per level: min, max, grade, remark (default A1 75–100 Excellent … F9 0–39 Fail) |
| `Student` | admission_no (unique per school), first/middle/last name, gender, dob, photo key (private), status (Active, Graduated, Withdrawn, Transferred), admitted_at, user_id (student login, nullable) |
| `Enrolment` | student, session, class (one per student per session); history keys off this |
| `Guardian` | name, phone, email, user_id (nullable until invite accepted) |
| `StudentGuardian` | student, guardian, relationship, is_primary |
| `GuardianInvite` | guardian, token hash, expires_at, accepted_at |
| `StaffProfile` | membership, staff_no, job_title, phone |
| `TimetableSlot` | class, day, period, start, end, class_subject |
| `AttendanceRegister` | class, date, taken_by, client_id (idempotency); unique (class, date) |
| `AttendanceMark` | register, enrolment, status (Present, Absent, Late, Excused), note |
| `Score` | enrolment, class_subject, term, component, value, entered_by; unique (enrolment, class_subject, term, component) |
| `TermResultLock` | class, term, locked_at/by |
| `SubjectResult` | enrolment, class_subject, term, total, grade, remark, class_average, position_in_subject |
| `TermResult` | enrolment, term, total, average, position, class_size, days_present, days_open, teacher_remark, principal_remark, computed_at, stale (bool), published_at |
| `FeeItem` | name, is_optional |
| `FeeStructureLine` | term, level, fee_item, amount |
| `StudentInvoice` | enrolment, term, number, total, paid, balance, status (Unpaid, PartlyPaid, Paid, Cancelled), due_date |
| `InvoiceLine` | invoice, fee_item (nullable for discounts), description, amount (negative for discounts) |
| `Instalment` | invoice, due_date, amount |
| `FeePayment` | invoice, amount, method (Paystack, Cash, Transfer, Pos), reference, receipt_no, recorded_by, paid_at, reversed_at/by/reason |
| `Expense` | category, amount, date, note, receipt key, recorded_by |
| `Announcement` | title, body, attachment key, audience (School, Staff, Levels[], Classes[]), email_guardians, posted_by, posted_at |
| `MessageThread` / `Message` | thread: guardian, student, staff member or "office"; message: sender user, body, attachment key, read_at |
| `UserNotification` (shared core) | user_id, tenant_id (nullable), product, kind, title, body, link (app-relative path), created_at, read_at |

Money in minor units (kobo), currency from the tenant. Dates `DateOnly` in `Africa/Lagos`; timestamps UTC `DateTimeOffset`.

## Phase A: setup, people, import

- **Signup** (product `school`) creates the tenant, the proprietor (Owner), a `SchoolProfile`, the current session and three terms
  (dates proposed from today, editable).
- **Setup checklist** on the dashboard: levels and classes (from the Nigerian template, editable), subjects (template list), students,
  staff. Each item links to its page; the checklist hides when all are done.
- **Students**: list with search and class filter; add/edit; photo upload (JPEG/PNG/WebP, sniffed, resized, private);
  transfer class within a session; withdraw/graduate/transfer out with a date and reason (records kept, access ends).
- **Import**: download template (`.xlsx` and `.csv`); upload (max 2,000 rows, 5 MB) → **preview** with each row New, Update or Error
  (row number and message, e.g. "Row 14: class 'JSS 4B' doesn't exist"); confirm imports all valid rows in one transaction.
  Admission number is the key (re-import updates). Guardians matched by phone, then email, so siblings share one guardian.
  Formula-looking cells are treated as text. Student limit is checked against the result.
- **ID cards**: PDF (QuestPDF), 8 per A4 page, per class or selected students: photo, name, admission number, class, session, logo.
- **Promotion**: "Start next session" wizard proposes next class per class (by level order and arm; final level → Graduated); the
  principal can hold back or move named students; confirm creates the session, terms, classes (copied) and enrolments atomically.
- **Guardian invites**: sent on demand (single or all un-invited). Email when there's an address; every invite also offers
  "Send on WhatsApp" (`https://wa.me/{phone}?text=` with the school name and link). Token single use, 14 days. Accepting creates
  or signs in to the parent account (email code or password) and links the guardian. A parent already linked elsewhere just gains
  the new children.
- **Student logins** (levels with `HasStudentLogins`): the school sets/reset passwords; sign-in on the school page with
  admission number + password (`/api/school/{slug}/student-login`). Lockout and rate limits as other customer sign-ins.
- **Staff**: invite with role and job title; assign class teacher and subject teachers; staff seats from the plan.

## Phase B: attendance

- Class teacher (or admin) opens the register for a class and date (today by default; past dates editable within the term by
  admin; teachers within 7 days). All Present by default; tap to Absent/Late/Excused with an optional note.
- **Offline**: the register is saved in the browser (IndexedDB) with a client id; a banner shows "Saved on this phone. It will send
  when you're back online." A service worker / online listener retries. `PUT /api/school/attendance/{classId}/{date}` with the
  client id is idempotent (upsert by class+date); a later edit wins.
- **Absence alerts**: when a mark becomes Absent for the first time that day, the primary guardian is emailed (job, deduplicated per
  student per day); the teacher gets a "Message parents" list with WhatsApp links.
- Reports: daily % per class (dashboard), term attendance per student (days present / days the register was taken).

## Phase C: results and report cards

- **Score entry** grid: class-subject × term; columns are the scheme's components; values validated 0..max (decimals to 1 place);
  autosave per cell; teachers edit only their class-subjects and only until the class's term is locked.
- **Compute** (on demand and automatically when stale before viewing/publishing):
  subject total = sum of components; grade/remark from the band; subject class average and position;
  term total, average (over subjects with scores), position in class with competition ranking (1, 2, 2, 4), class size;
  attendance days from registers.
  Students missing a component get it counted as 0 only after lock; before lock the result shows "incomplete".
- **Remarks**: class teacher remark for their class; principal remark; both from a short editable picklist or free text.
- **Lock / publish**: principal locks a class's term (no more score edits; unlock is audited), reviews, publishes. Publishing emails
  guardians ("{child}'s {term} results are ready") and offers the WhatsApp list. Unpublish is allowed and audited.
- **Report card PDF**: logo, school name, motto; student photo and details; per-subject CA columns, total, grade, remark, class average;
  overall total, average, position / class size; attendance; remarks; principal signature; next term begins; "Powered by SchoolLink"
  unless the plan removes branding. Per student, or a class bundle (generated by a job; download when ready).

## Phase D: fees and payments

- Fee items; fee structure per term × level (copy from previous term).
- **Generate invoices** for a term: one per active enrolment; lines from the level's structure; optional items added per student;
  discounts as negative lines with a reason (percentage of tuition or fixed). Idempotent: existing invoices without payments are
  refreshed; invoices with payments are left alone and listed as "needs review".
- **Instalments**: optional schedule per invoice (default single due date); must total the invoice.
- **Online payment** (portal "Pay now"): amount = balance, next instalment, or any amount up to the balance (min ₦1,000 or the
  balance if smaller). Paystack initialised with the **school's** secret key; confirmation by webhook (signature checked with the
  school's key) and by verify on return; conditional claim so both arriving records one payment; amount/currency mismatch is not
  applied and alerts the bursar. Schools without a key, or on Free, don't show Pay now.
- **Manual payment**: bursar (or Owner/Admin) records amount, method, reference, date. Overpayment refused.
- **Receipts**: numbered per school (atomic counter), PDF, emailed to the guardian; reprint any time.
- **Reversal**: with a reason, audited; invoice balance restored; receipt marked reversed. No edits to payments.
- **Debtors**: by class, overdue days, amount; CSV export (formula-escaped).
- **Reminders** job daily 08:00 Lagos: emails per the school's schedule (default 3 days before each due date, then every 7 days while
  overdue, max 6 per invoice), plus the bursar's WhatsApp list.
- **Results hold** (setting, default off): while a term invoice has a balance, parents and students see "Results are held. Please see the
  school office." instead of results and report cards; staff are unaffected.
- **Expenses**: category, amount, date, note, receipt photo (private). Finance dashboard per term: expected, collected, outstanding,
  expenses. Reports by class, fee item and method.

## Phase E: communication and portals

- **Announcements** to School / Staff / chosen levels / chosen classes; optional attachment; optional email to matching guardians
  (job, batched); WhatsApp share text after posting. Visible in portals by audience.
- **Messages**: thread per (guardian, student, staff member) or (guardian, student, office). Teachers see their classes' threads; office =
  Owner/Admin. New message emails the other side with a link. Text + one image per message; read receipts; polling every 30 s while
  open; rate limited.
- **Parent portal** (mobile first, in school-web): child switcher; results and report cards (published, respecting results hold);
  attendance this term; fees (invoices, instalments, receipts, Pay now); announcements; messages; timetable; profile (phone, email).
- **Student portal** (JSS/SSS): timetable, published results (respecting results hold), announcements.
- **Timetable**: weekly grid per class (day × period); clash warning when a teacher is in two classes at the same period.

### In-app notifications (shared core, used by SchoolLink first)

- A small shared feature so any product can notify a signed-in user inside the app; SchoolLink is the first to use it, the other
  products can adopt it later without changes to the core.
- `UserNotification` rows are written by the same code paths that queue emails (in the same transaction), one per recipient user.
  Recipients without an account yet (un-invited guardians) get email only.
- API (any signed-in user, scoped to themselves and, for business users, the current tenant):
  `GET /api/me/notifications?unread=&page=` → paged list, newest first; `GET /api/me/notifications/unread-count`;
  `POST /api/me/notifications/{id}/read`; `POST /api/me/notifications/read-all`. Support-view tokens can read but not mark read.
- UI: a bell in the school-web header (staff, parent and student layouts) with an unread badge (9+ cap); a dropdown of the latest 10
  with "Mark all as read" and a full page. Clicking an item marks it read and opens its link. The count refreshes every 60 seconds
  and on window focus; no real-time connection.
- SchoolLink events:

| Event | Who is notified |
|---|---|
| Results published | guardians of the class's students; JSS/SSS students |
| Report card bundle ready | staff member who requested it |
| Invoice issued / instalment due soon / overdue reminder | guardians (same schedule as the emails) |
| Payment received (online or manual) / payment reversed | guardians of the student; bursars for online payments |
| Payment amount mismatch | bursars, proprietor |
| Student marked absent | primary guardian |
| Announcement posted | guardians, students and staff in the audience |
| New message | the other side of the thread (office messages: Owner/Admin) |
| Guardian accepted invite / import finished | the staff member who sent or ran it |

- Retention: read notifications older than 90 days and unread older than 180 days are deleted by the daily job.
- In-app is in addition to email, never instead of it: every email this spec sends is still sent.

## Phase F: public pages, plans, platform wiring

- Public: landing, pricing (student-count tiers), "Book a demo" form (rate limited; emails the platform team), sign in, and the school
  sign-in page `/{slug}` with the school's logo (staff, parents, students).
- Plans (seeded insert-only; prices editable in the admin console):
  Free (50 students, 3 staff, branding, no online fees), Standard ₦60,000/term (300, 15), Premium ₦150,000/term (1,000, 50,
  branding removed), Enterprise via comps. Limit keys `students`, `team_seats`, `remove_branding`, `online_fees`.
  Hitting a limit refuses new students/staff with an upgrade prompt (402); existing data is never blocked.
- Admin console: schools appear as businesses (usage: students, staff); plans, comps, suspension and view-as unchanged; payments list
  includes school subscription payments (fee payments are the school's own money and are not platform payments).
- Suspended school: staff blocked as elsewhere; parent/student portals show "This school's portal is unavailable."; reminders stop.
- Jobs (advisory lock, `job_runs`): `school-reminders` (daily 08:00), `school-absence-alerts`, `school-broadcasts`, `school-report-cards`.

## Privacy and audit (Nigeria Data Protection Act)

- Student photos, documents, attachments and receipts are private storage objects served by short-lived links.
- Parents see only their linked children; students only themselves; teachers only their classes.
- Audited: score changes after lock/unlock, publish/unpublish, payments and reversals, discounts, student status changes, imports.
- Support view-as stays read only and is shown to the school in account activity.

## Errors and edge cases

- Score above a component's maximum → field error. Scheme components must total 100.
- Two teachers editing the same cell → last write wins, both audited.
- Register submitted twice (offline retry) → one register.
- Paystack webhook and return verify together → one payment. Wrong amount → not applied, bursar alerted.
- Payment that would overpay → refused. Reversal of a reversed payment → 409.
- Import with duplicate admission numbers in the file → error rows, nothing imported until fixed.
- Promotion run twice for the same session → refused once the next session exists.
- Student limit reached during import → preview shows how many fit; confirm refused until upgraded or rows removed.

## Testing

Integration tests per phase (Testcontainers Postgres), tests first: role permissions per area (table test like the admin console),
import preview/confirm, promotion, attendance idempotency, results computation (positions with ties, grades, averages, incomplete
before lock), report card PDF generation, invoice generation idempotency, instalments, online/manual payments with webhook + verify race,
reversal, reminders schedule, results hold, portal access isolation (parent A can't see child of parent B), suspension,
in-app notifications (written with each event, visible only to their user, unread count, mark read, support view can't mark read,
retention cleanup). A fresh review
and a browser click-through at the end.

## Out of scope (phase 2)

Assignments and homework, per-subject attendance, paid SMS, WhatsApp Business API, CBT/online exams, hostel, library, transport routes,
payroll, multi-branch schools, parent mobile app (the portal is mobile-first web).
