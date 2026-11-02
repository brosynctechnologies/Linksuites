# SchoolLink (School Management System) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add SchoolLink (product `school`): academic setup, students/parents/staff with Excel import, offline-tolerant attendance, results and report cards, fees paid to the school's own Paystack, announcements, messages, in-app notifications, and parent/student portals, with its own web app `school-web`.

**Architecture:** A `School/` module in each API layer, following the property and invoices modules: tenant-owned entities under the global tenant filter, an `IProductProvisioner` for signup, staff on the shared team roles, parents and secondary students as tenantless customer accounts linked to records, the school's own Paystack key through `IMerchantPayments` (as InvoiceLink), QuestPDF renderers, and one hosted job service with an advisory lock. In-app notifications are a small shared-core feature (`UserNotification`) that SchoolLink uses first.

**Tech Stack:** ASP.NET Core 10, EF Core + Npgsql, PostgreSQL 17 + PostGIS image, QuestPDF, ClosedXML (xlsx import/export), SkiaSharp (photos), xUnit v3 + Testcontainers, Next.js 16.4 + Tailwind 4 (school-web), IndexedDB for the offline register.

**Spec:** `docs/superpowers/specs/2026-10-13-school-management-design.md`

## Global Constraints

- Do not commit. The user asked for all work to stay uncommitted; there are no commit steps. Never kill all node processes.
- Product code `school`, name SchoolLink; policy `product:school`; web app `school-web` on port 4200; `App:ProductWebUrls.school = http://localhost:4200`; CORS for 4200; `.claude/launch.json` entry `school-web`.
- Staff roles are the shared `MemberRole` values, labelled in school-web: Owner "Proprietor", Admin "Principal" or "School admin" (from `StaffProfile.JobTitle`), Accountant "Bursar", Staff "Teacher". No change to `MemberRole`.
- Defaults (all editable per school): 3 terms; levels Creche, Nursery 1, Nursery 2, Primary 1–6, JSS 1–3, SSS 1–3 (`HasStudentLogins` true for JSS/SSS); assessment CA1 20, CA2 20, Exam 60 (components must total 100); grading A1 75–100 Excellent, B2 70–74 Very good, B3 65–69 Good, C4 60–64 Credit, C5 55–59 Credit, C6 50–54 Credit, D7 45–49 Pass, E8 40–44 Pass, F9 0–39 Fail.
- Limits: import max 2,000 rows and 5 MB; guardian invite token 14 days, single use; teachers may edit registers up to 7 days back; max 6 fee reminders per invoice; minimum online part-payment ₦1,000 (100_000 minor) or the balance if smaller; reminders daily at 08:00 Africa/Lagos, default 3 days before each due date then every 7 days while overdue.
- Plans (insert-only seed; prices editable in the console): Free ₦0 (students 50, team_seats 3, remove_branding 0, online_fees 0); Standard ₦60,000/term = 6_000_000 minor (300, 15, 0, 1); Premium ₦150,000/term = 15_000_000 minor (1000, 50, 1, 1); Enterprise via comps. Limit keys `students`, `team_seats`, `remove_branding`, `online_fees`. Plan interval: term (use the existing interval enum; add `Term` = 4 months if no suitable value exists).
- Money in minor units (kobo). `DateTimeOffset` written to Postgres is UTC. School dates are `DateOnly` in `Africa/Lagos`.
- Errors are ProblemDetails: 400 field errors, 402 plan limit, 403 role or ownership, 404 unknown, 409 conflict (locked term, duplicate session, payment already reversed).
- Private files (student photos, attachments, receipts, signature) use the existing private storage + short-lived links, never public URLs.
- Paystack references: school fees `sfee_`, school subscriptions use the existing platform billing.
- Copy: sentence case, plain verbs, no ALL-CAPS labels, no exclamation marks. Results-hold text: "Results are held. Please see the school office." Suspended portal text: "This school's portal is unavailable."
- C# strings containing `\n` are written with the Edit/Write tools, never through shell-script string replacement (known breakage in this repo).
- Stop the dev API (`preview_stop`) before building; it locks the DLLs.

## Review Focus

1. A parent signed in with one child at School A must never see another family's child, another school's records, or unpublished results, through any portal route or report-card URL. Test in Task 6 and Task 19 (`Parents_only_see_their_own_children`).
2. Score entry with a value above the component's maximum, a negative value, or by a teacher not assigned to that class-subject, or after the term is locked, must give 400/403/409, never save. Test in Task 11.
3. A Paystack webhook and the parent's return verify arriving together, or a webhook with the wrong amount or a bad signature, must record at most one payment and never mark a wrong amount paid. Test in Task 15.
4. An Excel import with blank rows, duplicate admission numbers, unknown classes, formula cells (`=HYPERLINK(...)`), dates as text or numbers, or more rows than the plan allows must preview errors and save nothing. Test in Task 5.
5. An offline register resent several times, or two teachers saving the same class and date, must leave exactly one register with the latest marks, and absence alerts must not repeat for the same student and day. Test in Task 9.

---

### Task 1: Product skeleton, plans and signup

**Files:**
- Modify: `api/src/Saas.Application/Billing/PlanService.cs` (`Products.School = "school"`, add to `All`; `LimitKeys.Students`, `LimitKeys.OnlineFees`)
- Modify: `api/src/Saas.Infrastructure/Persistence/Support.cs` (seed school plans, insert-only)
- Create: `api/src/Saas.Domain/School/School.cs` (`SchoolProfile`, `AcademicSession`, `Term`, `Level`, `Stage`)
- Create: `api/src/Saas.Application/School/SchoolCore.cs` (`SchoolProvisioner : IProductProvisioner`, `SchoolUsage : IUsageProvider`)
- Modify: `AppDbContext.cs`, `IAppDbContext`, `Saas.Application/DependencyInjection.cs`, `Saas.Api/Program.cs` (policy `product:school`), `appsettings.json` (ProductWebUrls, CORS)
- Create: `api/src/Saas.Api/Endpoints/SchoolEndpoints.cs` (group `/api/school`, `RequireAuthorization(Policies.SchoolProduct)`)
- Migration `School1Setup`
- Test: `api/tests/Saas.IntegrationTests/SchoolKit.cs` (helpers), `SchoolSetupTests.cs`

**Interfaces:**
- Produces: `enum Stage { Creche, Nursery, Primary, Jss, Sss }`; `SchoolProfile { Name, Slug, LogoKey?, Address?, Motto?, SignatureKey?, ResultsHoldForUnpaidFees, ReminderDaysBefore = 3, ReminderEveryDays = 7, PaystackSecretKeyProtected? }`; `AcademicSession { Name, StartDate, EndDate, IsCurrent }`; `Term { SessionId, Number, Name, StartDate, EndDate, NextTermStarts?, IsCurrent }`; `Level { Name, Stage, Order, HasStudentLogins }`.
- `SchoolKit` (tests): `SchoolAsync(string name) -> School(HttpClient Owner, Guid TenantId, string Slug)`; `StaffAsync(School, MemberRole, string jobTitle) -> (HttpClient, Guid UserId)`; `ParentAsync(...)`, `StudentLoginAsync(...)` added by later tasks.

- [ ] **Step 1: Failing test** `SchoolSetupTests.Signup_creates_the_school_session_and_three_terms`: signup with `product = "school"`; `GET /api/school/setup` returns `sessions[0].isCurrent == true`, 3 terms numbered 1–3, `checklist` with 4 items all `done == false`; `GET /api/billing` current plan code `free` with `students` limit 50.
- [ ] **Step 2: Run** `cd api && dotnet test tests/Saas.IntegrationTests --filter "FullyQualifiedName~SchoolSetupTests"`. Expected: FAIL (unknown product).
- [ ] **Step 3: Implement** the provisioner (session named from today: Sept–Aug, e.g. "2026/2027"; terms Sep–Dec, Jan–Apr, Apr–Jul), plans seed, usage provider (`students` = active students, `team_seats` via the shared `TeamUsage`), DbSets, migration, policy, `GET /api/school/setup`.
- [ ] **Step 4: Run** the test, then the full suite `cd api && dotnet test`. Expected: all pass (300 existing + new).

### Task 2: Levels, classes, subjects, assessment and grading

**Files:**
- Modify: `Saas.Domain/School/School.cs` (`SchoolClass`, `Subject`, `ClassSubject`, `AssessmentComponent`, `GradingBand`)
- Create: `Saas.Application/School/AcademicService.cs`, `SchoolTemplates.cs` (Nigerian defaults from Global Constraints)
- Modify: `SchoolEndpoints.cs`; migration `School2Academic`
- Test: `SchoolAcademicTests.cs`

**Interfaces:**
- Produces: `SchoolClass { SessionId, LevelId, Arm, Name, ClassTeacherStaffId? }`; `Subject { Name, Code }`; `ClassSubject { ClassId, SubjectId, TeacherStaffId? }`; `AssessmentComponent { LevelId?, Name, MaxScore, Order }`; `GradingBand { LevelId?, Min, Max, Grade, Remark }`.
- Routes (Owner/Admin write, staff read): `POST /api/school/setup/apply-template { levels: bool, subjects: bool }`, CRUD `/levels`, `/classes`, `/subjects`, `/classes/{id}/subjects`, `PUT /assessment { levelId?, components[] }`, `PUT /grading { levelId?, bands[] }`.
- `AcademicService.SchemeFor(Guid levelId) -> IReadOnlyList<AssessmentComponent>` and `BandsFor(Guid levelId) -> IReadOnlyList<GradingBand>` (level override else school default) — used by Task 12.

- [ ] **Step 1: Failing tests**: `Template_creates_levels_and_subjects` (15 levels, JSS/SSS `hasStudentLogins`); `Assessment_components_must_total_100` (CA1 20 + Exam 60 → 400 field `components`); `Grading_bands_must_not_overlap_or_leave_gaps` (0–100 covered once → else 400); `Teacher_cannot_edit_setup` (Staff → 403 "You need the Owner or Admin role for this.").
- [ ] **Step 2: Run** them. Expected: FAIL (404s).
- [ ] **Step 3: Implement** service, endpoints, migration. Class name is the level name followed directly by the arm: "JSS 1" + "A" → "JSS 1A" (`$"{level.Name}{arm}"`); test `Class_names_join_level_and_arm` asserts "JSS 1A" and "Primary 4B".
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 3: Staff profiles and teaching assignments

**Files:**
- Modify: `School.cs` (`StaffProfile { MembershipId, UserId, StaffNo?, JobTitle, Phone? }`)
- Create: `Saas.Application/School/StaffService.cs`, `SchoolAccess.cs`
- Modify: `SchoolEndpoints.cs`; migration `School3Staff`
- Test: `SchoolStaffTests.cs`

**Interfaces:**
- Produces: `SchoolAccess` (scoped): `Task<Guid?> StaffIdAsync(ct)`; `Task<bool> TeachesClassAsync(Guid classId, ct)` (class teacher or any subject teacher of it); `Task<bool> TeachesClassSubjectAsync(Guid classSubjectId, ct)`; `bool IsLeadership` (Owner or Admin); `bool IsBursar` (Accountant). Every later task's teacher checks go through this.
- Routes: `GET /api/school/staff`, `PUT /api/school/staff/{membershipId} { jobTitle, staffNo, phone }`, `PUT /classes/{id}/class-teacher { staffId }`, `PUT /class-subjects/{id}/teacher { staffId }`. Staff invites use the existing `/api/team/invites`; a `StaffProfile` is created on accept by a school hook in the existing accept flow (or lazily on first `/api/school/staff` read).

- [ ] **Step 1: Failing tests**: `Teachers_are_assigned_to_classes_and_subjects`; `A_teacher_sees_only_their_classes` (`GET /api/school/my-classes` lists exactly the assigned ones); `Seat_limit_applies_to_staff_invites` (Free: 3 seats → 4th invite 402).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 4: Students, guardians and enrolments

**Files:**
- Modify: `School.cs` (`Student`, `StudentStatus`, `Enrolment`, `Guardian`, `StudentGuardian`)
- Create: `Saas.Application/School/StudentService.cs`
- Modify: `SchoolEndpoints.cs`; migration `School4Students` (unique `(tenant_id, admission_no)`, unique `(student_id, session_id)` on enrolments)
- Test: `SchoolStudentTests.cs`

**Interfaces:**
- Produces: `Student { AdmissionNo, FirstName, MiddleName?, LastName, Gender, DateOfBirth?, PhotoKey?, Status, AdmittedAt, UserId? }`; `Enrolment { StudentId, SessionId, ClassId }`; `Guardian { FullName, Phone?, Email?, UserId? }`; `StudentGuardian { StudentId, GuardianId, Relationship, IsPrimary }`.
- `StudentService.CurrentEnrolmentAsync(Guid studentId, ct) -> Enrolment?`; `GuardianMatchAsync(string? phone, string? email, ct) -> Guardian?` (phone normalised to `+234…`, then email) — used by Task 5.
- Routes: CRUD `/api/school/students` (list with `q`, `classId`, `status`), `POST /students/{id}/photo`, `POST /students/{id}/transfer { classId }`, `POST /students/{id}/status { status, date, reason }`, guardians add/edit/remove/primary.

- [ ] **Step 1: Failing tests**: `Student_is_created_with_an_enrolment_in_the_current_session`; `Admission_numbers_are_unique_per_school` (409); `Student_limit_is_enforced` (51st on Free → 402); `Withdrawn_students_keep_their_records_but_leave_the_class_list`; `Photos_are_private` (the returned photo URL is a short-lived signed link; the storage key URL without signature → 404/403).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** (photo via the existing SkiaSharp resize, 512 px, private storage).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 5: Excel/CSV import with preview

**Files:**
- Modify: `api/src/Saas.Infrastructure/Saas.Infrastructure.csproj` (add `ClosedXML`, latest stable)
- Create: `Saas.Application/School/StudentImport.cs` (pure parsing/validation over rows), `Saas.Infrastructure/School/SpreadsheetReader.cs` (`ISpreadsheetReader.Read(Stream, string fileName) -> IReadOnlyList<IReadOnlyDictionary<string,string>>` for xlsx and csv), template download
- Modify: `SchoolEndpoints.cs`
- Test: `SchoolImportTests.cs`

**Interfaces:**
- Produces: `POST /api/school/students/import/preview` (multipart) → `{ token, rows: [{ row, status: "New"|"Update"|"Error", admissionNo, name, className, message? }], newCount, updateCount, errorCount, fitsPlan }`; `POST /api/school/students/import/{token}/confirm` → `{ created, updated }` (409 if any error rows or over the plan); `GET /api/school/students/import/template.xlsx` and `.csv`. Preview is held server-side for 30 minutes (table `ImportBatch { Token, Payload jsonb, ExpiresAt }`).
- Columns: `admission_no, first_name, middle_name, last_name, gender, date_of_birth, class, guardian1_name, guardian1_phone, guardian1_email, guardian1_relationship, guardian2_name, guardian2_phone, guardian2_email, guardian2_relationship`.

- [ ] **Step 1: Failing tests**: `Preview_shows_new_update_and_error_rows_and_saves_nothing`; `Confirm_imports_all_rows_in_one_transaction`; `Reimport_updates_by_admission_number`; `Siblings_share_a_guardian_matched_by_phone`; `Duplicate_admission_numbers_in_the_file_are_errors`; `Formula_cells_are_stored_as_text` (`=HYPERLINK("x")` name → stored literally, no evaluation); `Dates_as_text_or_excel_serials_parse` (`2012-03-04`, `04/03/2012`, `40972`); `Over_the_plan_preview_says_how_many_fit_and_confirm_is_refused`; `Files_over_2000_rows_or_5_MB_are_refused`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Dates: ISO, then `dd/MM/yyyy` (Nigerian order), then Excel serial. Blank rows skipped. Read cell values as stored text (ClosedXML `GetFormattedString` of the value, never formulas).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 6: Guardian invites and parent accounts

**Files:**
- Modify: `School.cs` (`GuardianInvite { GuardianId, TokenHash, ExpiresAt, AcceptedAt? }`)
- Create: `Saas.Application/School/GuardianInviteService.cs`, `Saas.Application/School/ParentAccess.cs`
- Modify: `SchoolEndpoints.cs` (staff routes) and a new tenantless group `/api/parent` (signed-in customer accounts), `Notifications.cs` templates `school.guardian_invite`
- Test: `SchoolParentTests.cs`

**Interfaces:**
- Produces: `POST /api/school/guardians/{id}/invite` → `{ link, whatsAppUrl }` (email sent when the guardian has one); `POST /api/school/guardians/invite-all` → `{ invited }`; public `GET /api/school-invites/{token}` → `{ schoolName, guardianName, children: [names] }`; `POST /api/school-invites/{token}/accept` (signed-in customer token) links `Guardian.UserId`.
- `ParentAccess` (scoped): `Task<IReadOnlyList<ChildRef>> ChildrenAsync(ct)` where `ChildRef(Guid StudentId, Guid TenantId, string SchoolName, string SchoolSlug, string Name, string ClassName)`; `Task<ChildRef> RequireChildAsync(Guid studentId, ct)` (404 when not theirs; switches tenant context to the child's school via `UseTenantOf`). Every parent route goes through it.
- `GET /api/parent/children` → `ChildRef[]` across schools.
- WhatsApp URL: `https://wa.me/{digits}?text=` + URL-encoded "Hello {guardian}, {school} has set up SchoolLink for {child}. Join here: {link}".

- [ ] **Step 1: Failing tests**: `Guardian_invite_links_a_parent_account_to_their_children`; `Invite_is_single_use_and_expires_after_14_days`; `Parents_only_see_their_own_children` (parent A requesting parent B's child → 404; other school's child → 404); `One_parent_account_sees_children_in_two_schools`; `A_suspended_school_hides_its_children_from_the_portal` (403 "This school's portal is unavailable.").
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Parents sign up/sign in with the existing customer auth (email code or password); the invite page passes the token through sign-up.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 7: Student logins (JSS/SSS)

**Files:**
- Create: `Saas.Application/School/StudentLoginService.cs`
- Modify: `AuthService.cs` (issue a tenantless customer token for a student user; reuse `IssueAsync`), `SchoolEndpoints.cs`, new group `/api/student`
- Test: `SchoolStudentLoginTests.cs`

**Interfaces:**
- Produces: `POST /api/school/students/{id}/login { password }` (Owner/Admin; only when the student's level `HasStudentLogins`; creates or updates an `AppUser` with email `{admissionNo}.{slug}@students.schoollink.invalid` normalised, never emailed); `POST /api/school/{slug}/student-login { admissionNo, password }` → auth response; `StudentAccess.RequireSelfAsync(ct) -> Student` used by Task 19.
- Lockout and rate limit as other customer sign-ins (`RateLimits.Auth`, Identity lockout).
- Public `GET /api/school/{slug}/public` → `{ name, logoUrl, motto }` for the school sign-in page (404 for unknown or suspended schools).

- [ ] **Step 1: Failing tests**: `A_secondary_student_signs_in_with_admission_number`; `Primary_students_cannot_get_logins` (400 field `level`); `Wrong_password_five_times_locks_the_student_for_10_minutes`; `A_student_token_cannot_reach_staff_routes` (403).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 8: Promotion to the next session and ID cards

**Files:**
- Create: `Saas.Application/School/PromotionService.cs`, `Saas.Infrastructure/School/SchoolPdf.cs` (QuestPDF: ID cards now, report cards and receipts later) implementing `ISchoolPdf`
- Modify: `SchoolEndpoints.cs`
- Test: `SchoolPromotionTests.cs`

**Interfaces:**
- Produces: `GET /api/school/promotion/preview` → `{ classes: [{ fromClassId, toClassName | "Graduate", students: [{ studentId, name, action }] }], unplaced: [{ studentId, name }] }` (unplaced must be placed or graduated via overrides before commit); `POST /api/school/promotion { sessionName, start, end, overrides: [{ studentId, action, classId? }] }` creates session, 3 terms, copies classes and class-subjects (teachers kept), creates enrolments, marks graduates; 409 when a later session exists.
- `ISchoolPdf.IdCardsAsync(IReadOnlyList<IdCardData>, ct) -> byte[]` (8 per A4); `GET /api/school/students/id-cards.pdf?classId=&studentIds=`.

- [ ] **Step 1: Failing tests**: `Promotion_moves_classes_up_and_graduates_the_final_level`; `Held_back_students_repeat`; `Promotion_runs_once_per_session` (409); `Id_cards_render_a_pdf_for_a_class` (content type `application/pdf`, `%PDF` header, page count = ceil(n/8)).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** (all promotion writes in one transaction).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 9: Attendance registers and absence alerts

**Files:**
- Modify: `School.cs` (`AttendanceRegister { ClassId, Date, TakenByStaffId, ClientId }` unique (class, date); `AttendanceMark { RegisterId, EnrolmentId, Status, Note? }`; `AbsenceAlert { StudentId, Date }` unique)
- Create: `Saas.Application/School/AttendanceService.cs`
- Modify: `SchoolEndpoints.cs`, `Notifications.cs` (`school.absent`); migration `School5Attendance`
- Test: `SchoolAttendanceTests.cs`

**Interfaces:**
- Produces: `GET /api/school/attendance/{classId}/{date}` → register (all Present when new); `PUT /api/school/attendance/{classId}/{date} { clientId, marks: [{ enrolmentId, status, note? }] }` → upsert (last write wins); teachers only for classes they teach and dates within 7 days; leadership any date in the current term; `GET /api/school/attendance/summary?date=` (% per class); `GET /api/school/students/{id}/attendance?termId=` → `{ daysOpen, present, absent, late, excused }`.
- `AttendanceService.TermDaysAsync(Guid enrolmentId, Guid termId, ct) -> (int Present, int Open)` — used by Task 12 (Late counts as present).
- Absence: first time a student is Absent for a date → insert `AbsenceAlert` (unique) and email the primary guardian with an email; the response includes `messageParents: [{ guardian, whatsAppUrl }]`.

- [ ] **Step 1: Failing tests**: `Register_defaults_to_present_and_saves_marks`; `Resending_the_same_register_keeps_one_register_with_the_latest_marks`; `Two_saves_at_once_leave_one_register` (Task.WhenAll of two PUTs → one row; no 500); `Absence_alert_is_sent_once_per_student_per_day`; `Teachers_cannot_take_other_classes_registers` (403); `Teachers_cannot_edit_registers_older_than_7_days` (409).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** (upsert with `INSERT … ON CONFLICT (tenant_id, class_id, date)` via a conditional insert + retry on unique violation; marks replaced in the same transaction).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 10: school-web scaffold, auth and setup screens

**Files:**
- Create: `school-web/` (copy the structure of `property-web`: Next.js 16.4, Tailwind 4, `src/lib/api.ts`, `auth.ts`, `support.ts`, components, forgot/reset/account pages, support banner, account activity), `.env.example`, `.claude/launch.json` entry `school-web` (port 4200), README row
- Pages: `/` landing, `/pricing`, `/demo` (Book a demo form, posts to the Task 21 route), `/login`, `/signup`, `/{slug}` school sign-in (staff, parent, student tabs), `/app` dashboard with checklist, `/app/setup/*` (levels, classes, subjects, assessment, grading), `/app/students` (+ import wizard, ID cards), `/app/staff`, `/app/promotion`
- Design: run the frontend-design skill's process once for the school-web visual identity (distinct from the other five apps), recorded in `docs/design/schoollink.md`

**Interfaces:**
- Consumes: Tasks 1–8 routes.

- [ ] **Step 1:** Scaffold; `npx tsc --noEmit` and `npm run lint` pass.
- [ ] **Step 2:** Build the pages above against the API, mobile first; import wizard shows the preview table with row errors and a disabled confirm while errors exist.
- [ ] **Step 3: Verify** in the browser pane: sign up a school, apply the template, add a class and a student, import the template file with one error row, fix it, confirm. Expected: no console errors; counts match.

### Task 11: Score entry and locking

**Files:**
- Modify: `School.cs` (`Score { EnrolmentId, ClassSubjectId, TermId, ComponentId, Value, EnteredByStaffId }` unique; `TermResultLock { ClassId, TermId, LockedAt, LockedBy }`)
- Create: `Saas.Application/School/ScoreService.cs`
- Modify: `SchoolEndpoints.cs`; migration `School6Scores`
- Test: `SchoolScoreTests.cs`

**Interfaces:**
- Produces: `GET /api/school/scores?classSubjectId=&termId=` → grid `{ components[], students: [{ enrolmentId, name, values: { componentId: number|null } }], locked }`; `PUT /api/school/scores { classSubjectId, termId, entries: [{ enrolmentId, componentId, value|null }] }` (partial save, autosave-friendly); `POST /api/school/results/{classId}/{termId}/lock` and `/unlock` (leadership; audited).
- Any score change marks the class's `TermResult` rows stale (Task 12).

- [ ] **Step 1: Failing tests**: `Scores_save_and_reload`; `Values_above_the_max_or_negative_are_400`; `Only_the_assigned_subject_teacher_or_leadership_can_enter_scores` (403); `Locked_terms_refuse_score_changes` (409) `and_unlock_is_audited`; `Decimals_are_kept_to_one_place` (12.25 → 400; 12.5 ok).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 12: Result computation, remarks and publishing

**Files:**
- Modify: `School.cs` (`SubjectResult`, `TermResult` as in the spec)
- Create: `Saas.Application/School/ResultRules.cs` (pure), `ResultService.cs`
- Modify: `SchoolEndpoints.cs`, `Notifications.cs` (`school.results_published`); migration `School7Results`
- Test: `api/tests/Saas.UnitTests/ResultRulesTests.cs`, `SchoolResultTests.cs`

**Interfaces:**
- Produces: `ResultRules.Grade(decimal total, IReadOnlyList<GradingBand>) -> GradingBand`; `ResultRules.Positions(IReadOnlyList<(Guid Id, decimal Score)>) -> IReadOnlyDictionary<Guid,int>` (competition ranking 1,2,2,4); `ResultRules.Average(IEnumerable<decimal?>) -> decimal?` (over subjects with any score; 2 dp, MidpointRounding.AwayFromZero).
- `ResultService.ComputeAsync(Guid classId, Guid termId, ct)`; `GET /api/school/results/{classId}/{termId}` (leadership and the class teacher) → per student totals, average, position, class size, attendance, remarks, `incomplete` flag; `PUT …/remarks { enrolmentId, teacherRemark?, principalRemark? }` (class teacher: teacher remark for their class; leadership: principal remark); `POST …/publish` and `/unpublish` (leadership; publish requires lock; audited).
- Missing components: before lock the subject is `incomplete`; after lock missing counts as 0.

- [ ] **Step 1: Failing unit tests**: `Ties_share_a_position_and_the_next_is_skipped` (90, 85, 85, 70 → 1, 2, 2, 4); `Grades_come_from_the_band_containing_the_total` (75 → A1, 74.5 → B2, 39.9 → F9); `Average_ignores_subjects_with_no_scores`.
- [ ] **Step 2: Failing integration tests**: `Computed_results_have_totals_positions_and_attendance`; `Edits_make_results_stale_and_they_recompute_before_publish`; `Publish_requires_a_locked_term` (409); `Publishing_emails_guardians_once`; `Unpublished_results_are_invisible_to_parents` (covered again in Task 19).
- [ ] **Step 3: Run.** Expected: FAIL.
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run** tests and full suite. Expected: PASS.

### Task 13: Report card PDFs

**Files:**
- Modify: `Saas.Infrastructure/School/SchoolPdf.cs` (`ReportCardAsync(ReportCardData) -> byte[]`, `ReportCardsAsync(IReadOnlyList<ReportCardData>) -> byte[]`)
- Create: `Saas.Application/School/ReportCardService.cs`; job hook for class bundles (Task 20 runs the queue)
- Modify: `SchoolEndpoints.cs`
- Test: `SchoolReportCardTests.cs`

**Interfaces:**
- Produces: `GET /api/school/report-cards/{enrolmentId}/{termId}.pdf` (staff with access; parent/student routes in Task 19); `POST /api/school/report-cards/{classId}/{termId}/bundle` → `{ id }`, `GET /api/school/report-cards/bundles/{id}` → `{ status, url? }`.
- `ReportCardData` holds every field the spec lists; branding line "Powered by SchoolLink" unless the plan's `remove_branding` is 1.

- [ ] **Step 1: Failing tests**: `Report_card_pdf_renders_with_results_and_attendance` (PDF text contains the student name, a subject, the position "2nd of 30" form, "Powered by SchoolLink" on Free); `Premium_report_cards_have_no_branding`; `Class_bundle_is_generated_by_the_job_and_downloadable`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** (ordinal positions: 1st, 2nd, 3rd, 4th, 11th–13th, 21st…).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 14: Fee items, structures, invoices and instalments

**Files:**
- Modify: `School.cs` (`FeeItem`, `FeeStructureLine`, `StudentInvoice`, `InvoiceLine`, `Instalment`; invoice number counter on `SchoolProfile`)
- Create: `Saas.Application/School/FeeService.cs`
- Modify: `SchoolEndpoints.cs`; migration `School8Fees`
- Test: `SchoolFeeTests.cs`

**Interfaces:**
- Produces: routes (Bursar + leadership): CRUD `/api/school/fee-items`; `PUT /api/school/fee-structure/{termId}/{levelId} { lines: [{ feeItemId, amountMinor }] }`; `POST /fee-structure/{termId}/copy-from/{fromTermId}`; `POST /api/school/invoices/generate { termId }` → `{ created, refreshed, needsReview: [invoiceId] }`; `PUT /invoices/{id}/optional-items { feeItemIds }`; `POST /invoices/{id}/discount { kind: "Percent"|"Amount", value, reason }`; `PUT /invoices/{id}/instalments [{ dueDate, amountMinor }]`; `GET /api/school/invoices?termId=&classId=&status=`.
- `FeeService.RecalculateAsync(Guid invoiceId, ct)` keeps `Total`, `Paid`, `Balance`, `Status` consistent — used by Task 15.

- [ ] **Step 1: Failing tests**: `Generating_invoices_uses_the_class_structure`; `Generating_twice_refreshes_unpaid_and_leaves_paid_invoices_for_review`; `Optional_items_and_percentage_discounts_apply` (10% of tuition only); `Instalments_must_total_the_invoice` (400); `Teachers_cannot_see_fees` (403 "You need the Owner or Admin or Accountant role…" — use the role naming from the 403 handler).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 15: Payments, receipts and reversals

**Files:**
- Modify: `School.cs` (`FeePayment`, receipt counter)
- Create: `Saas.Application/School/FeePaymentService.cs`
- Modify: `SchoolEndpoints.cs` (Paystack key settings mirroring `InvoiceSetupService`; webhook `POST /api/webhooks/paystack/school/{tenantId}`), `SchoolPdf.cs` (`ReceiptAsync`), `Notifications.cs` (`school.receipt`, `school.payment_mismatch`); migration `School9Payments`
- Test: `SchoolPaymentTests.cs`

**Interfaces:**
- Produces: `PUT /api/school/settings/paystack { secretKey }` / `DELETE` (Owner); parent `POST /api/parent/children/{studentId}/invoices/{invoiceId}/pay { amountMinor }` → `{ authorizationUrl, reference }` (reference `sfee_…`); `POST /api/parent/payments/{reference}/verify`; staff `POST /api/school/invoices/{id}/payments { amountMinor, method, reference?, paidOn }`; `POST /api/school/payments/{id}/reverse { reason }`; `GET /api/school/payments/{id}/receipt.pdf`.
- Online confirmation: `FeePaymentService.ConfirmAsync(Guid tenantId, VerifiedCharge charge, ct)` — conditional claim on a pending `FeePayment` row created at initialise (status Pending → Succeeded), amount/currency must match, else Mismatch + bursar notification; never applies twice.

- [ ] **Step 1: Failing tests**: `Parent_pays_part_of_an_invoice_online_and_gets_a_receipt`; `Webhook_and_verify_together_record_one_payment`; `Bad_signature_is_401_and_records_nothing`; `Wrong_amount_is_not_applied_and_alerts_the_bursar`; `Minimum_part_payment_is_1000_naira_unless_the_balance_is_smaller`; `Overpayment_is_refused`; `Manual_payment_issues_a_numbered_receipt`; `Reversal_restores_the_balance_and_cannot_repeat` (409); `Free_plan_or_no_key_hides_pay_now` (`canPayOnline == false`; POST pay → 409 "Online payment isn't set up for this school.").
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** with `IMerchantPayments` and the school's decrypted key (`ISecretProtector`), as `PublicDocumentsService` does for invoices.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 16: Debtors, reminders, results hold, expenses and finance reports

**Files:**
- Modify: `School.cs` (`Expense`, `FeeReminder { InvoiceId, SentAt, Kind }`, `SchoolProfile` settings)
- Create: `Saas.Application/School/FinanceService.cs`, `ReminderRules.cs` (pure)
- Modify: `SchoolEndpoints.cs`, `Notifications.cs` (`school.fee_reminder`); migration `School10Finance`
- Test: `api/tests/Saas.UnitTests/ReminderRulesTests.cs`, `SchoolFinanceTests.cs`

**Interfaces:**
- Produces: `ReminderRules.Due(DateOnly today, IReadOnlyList<(DateOnly DueDate, long OutstandingMinor)> instalments, int sentCount, DateOnly? lastSent, int daysBefore, int everyDays) -> bool` (max 6 per invoice).
- `GET /api/school/debtors?classId=&minDaysOverdue=` and `.csv` (shared CSV escaping); `PUT /api/school/settings/finance { resultsHoldForUnpaidFees, reminderDaysBefore, reminderEveryDays }`; CRUD `/api/school/expenses` (receipt photo private); `GET /api/school/finance/summary?termId=` → `{ expected, collected, outstanding, expenses }`; `GET /api/school/finance/collections?termId=&by=class|item|method`.
- `FinanceService.IsHeldAsync(Guid studentId, Guid termId, ct) -> bool` (setting on and that term's invoice balance > 0) — used by Task 19.

- [ ] **Step 1: Failing unit tests**: `Reminder_three_days_before_due`; `Then_every_seven_days_while_overdue`; `Never_more_than_six`; `Nothing_when_paid`.
- [ ] **Step 2: Failing integration tests**: `Debtors_list_and_csv_match_balances`; `Expenses_show_in_the_term_summary`; `Results_hold_setting_is_off_by_default`.
- [ ] **Step 3: Run.** Expected: FAIL.
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run** tests and full suite. Expected: PASS.

### Task 17: In-app notifications (shared core)

**Files:**
- Create: `api/src/Saas.Domain/Core/UserNotification.cs`, `api/src/Saas.Application/Common/InAppNotifications.cs`, endpoints in `api/src/Saas.Api/Endpoints/CoreEndpoints.cs` (group `/api/me/notifications`)
- Modify: `AppDbContext.cs`, `IAppDbContext`; migration `UserNotifications` (index `(user_id, read_at, created_at desc)`)
- Test: `InAppNotificationTests.cs`

**Interfaces:**
- Produces: `UserNotification { UserId, TenantId?, Product, Kind, Title, Body, Link, CreatedAt, ReadAt? }` (not a tenant entity; filtered by `UserId`); `InAppNotifications.Add(Guid userId, string product, string kind, string title, string body, string link, Guid? tenantId = null)` (adds to the context; the caller's `SaveChangesAsync` writes it with the event); `GET /api/me/notifications?unread=&page=` (`Paged<UserNotificationDto>`, newest first, page size 20); `GET /api/me/notifications/unread-count` → `{ count }`; `POST /api/me/notifications/{id}/read`; `POST /api/me/notifications/read-all`. Support-view tokens: GET allowed, POST 403 (existing read-only middleware).
- Retention in Task 20's job: delete read > 90 days, unread > 180 days.

- [ ] **Step 1: Failing tests**: `A_user_sees_only_their_own_notifications`; `Unread_count_and_mark_read`; `Read_all_marks_everything`; `Support_view_cannot_mark_read` (403); `Old_notifications_are_cleaned_up` (call the cleanup method with a clock 181 days on).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Wire SchoolLink events** listed in the spec's table into the services from Tasks 6, 9, 12, 13, 15, 16 and 18 (each next to its email); add one test per event kind to `SchoolNotificationEventsTests.cs` asserting the right recipients get exactly one notification with a link into school-web (e.g. results published → each linked parent user and JSS/SSS student user; absent → primary guardian user; payment received online → guardians and bursars).
- [ ] **Step 5: Run** tests and full suite. Expected: PASS.

### Task 18: Announcements and messages

**Files:**
- Modify: `School.cs` (`Announcement { Title, Body, AttachmentKey?, Audience, LevelIds[], ClassIds[], EmailGuardians, PostedBy, PostedAt }`, `MessageThread { GuardianId, StudentId, StaffId?, IsOffice }`, `Message { ThreadId, SenderUserId, Body, AttachmentKey?, ReadAt? }`)
- Create: `Saas.Application/School/AnnouncementService.cs`, `MessageService.cs`
- Modify: `SchoolEndpoints.cs`, `/api/parent` group, `Notifications.cs` (`school.announcement`, `school.message`); migration `School11Communication`
- Test: `SchoolCommunicationTests.cs`

**Interfaces:**
- Produces: staff `POST/GET /api/school/announcements` (teachers: only their classes as audience; bursar: any); parent/student `GET /api/parent/children/{id}/announcements`, `GET /api/student/announcements`; `whatsAppText` in the post response.
- Messages: parent `POST /api/parent/children/{studentId}/threads { to: "class-teacher"|"office", body }`, `GET/POST /api/parent/threads/{id}/messages`; staff `GET /api/school/threads?mine=`, `GET/POST /api/school/threads/{id}/messages`; one image per message (private); rate limit `PublicWrite`; teachers see threads for their classes, office threads go to Owner/Admin.
- Email broadcasts queue rows for the Task 20 job (batch 100).

- [ ] **Step 1: Failing tests**: `Announcement_to_a_class_reaches_only_that_class's_guardians`; `Teacher_cannot_announce_to_other_classes` (403); `Parent_messages_the_class_teacher_and_the_teacher_replies`; `Office_threads_are_seen_by_leadership_not_teachers`; `Parents_cannot_read_other_parents_threads` (404).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 19: Parent and student portal API, timetable

**Files:**
- Modify: `School.cs` (`TimetableSlot { ClassId, Day, Period, Start, End, ClassSubjectId }`)
- Create: `Saas.Application/School/PortalService.cs`, `TimetableService.cs`
- Modify: `/api/parent`, `/api/student`, `SchoolEndpoints.cs` (timetable CRUD); migration `School12Timetable`
- Test: `SchoolPortalTests.cs`

**Interfaces:**
- Produces: parent `GET /api/parent/children/{id}/overview` → `{ child, results: [{ termId, termName, published, held }], attendance, fees: { balance, invoices }, timetable }`; `GET /api/parent/children/{id}/results/{termId}` and `/report-card/{termId}.pdf` (published only; `FinanceService.IsHeldAsync` → 403 "Results are held. Please see the school office."); student `GET /api/student/overview`, `/results/{termId}`, `/report-card/{termId}.pdf`, `/timetable`.
- Timetable: `PUT /api/school/timetable/{classId} [slots]` returns `warnings: [{ teacher, day, period, otherClass }]` for teacher clashes (not blocking).

- [ ] **Step 1: Failing tests**: `Parent_overview_shows_results_attendance_fees_and_timetable`; `Unpublished_results_are_invisible_to_parents_and_students`; `Results_hold_blocks_results_until_fees_are_paid` (setting on, balance > 0 → 403 text; pay → visible); `Student_sees_only_their_own_results`; `Report_card_urls_for_another_child_are_404`; `Timetable_warns_on_teacher_clash`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 20: School jobs

**Files:**
- Create: `Saas.Infrastructure/School/SchoolJobsHostedService.cs` (advisory lock key `0x1_1A5_0005`, `JobRuns.TrackAsync("school", …)`, every minute; reminders only at/after 08:00 Lagos once per day)
- Modify: `Saas.Infrastructure/DependencyInjection.cs` (register, `Jobs:SchoolEnabled` default true; false in tests)
- Test: `SchoolJobTests.cs`

**Interfaces:**
- Consumes: `ReminderRules.Due`, `ReportCardService` bundle queue, announcement broadcast queue, `InAppNotifications` cleanup.
- Produces: `SchoolJobs.RunOnceAsync(DateTimeOffset now, CancellationToken ct)` callable from tests.

- [ ] **Step 1: Failing tests**: `Reminders_send_once_per_day_per_invoice`; `Suspended_schools_get_no_reminders`; `Broadcast_emails_are_sent_in_batches`; `Report_card_bundles_are_built`; `Job_run_is_recorded_for_the_health_page` (`job_runs` row `school`).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 21: Platform wiring, suspension and role table

**Files:**
- Modify: `TenantStatusMiddleware` users (parent/student portal checks via `ParentAccess`/`StudentAccess` already refuse suspended schools), `PlatformBusinessService` usage (students, staff), admin-web `src/lib/products.ts` (SchoolLink name and colour)
- Test: `SchoolPlatformTests.cs`, extend `NextReleaseRoleTableTests` only if new admin routes were added (none expected)

**Interfaces:**
- Consumes: everything above.

- Also: public `POST /api/school-demo-requests { schoolName, contactName, email, phone, students }` (rate limit `PublicWrite`, validated, emails every active SuperAdmin with template `school.demo_request`).

- [ ] **Step 1: Failing tests**: `Demo_request_emails_the_platform_team`; `School_appears_in_the_console_with_student_and_staff_usage`; `Suspended_school_blocks_staff_and_portals_and_stops_reminders`; `Support_view_of_a_school_is_read_only`; `School_role_table` (every `/api/school` route × Owner, Admin, Accountant, Staff, parent token, student token → expected allowed/403, in the style of `NextReleaseRoleTableTests`, with routes missing from the table failing the test).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the gaps the tests find.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 22: school-web — attendance (offline), results, fees, communication, portals, notifications

**Files:**
- `school-web/src/app/app/attendance/*` (register with IndexedDB queue: `src/lib/offline-register.ts` storing `{ classId, date, clientId, marks }`, retry on `online` and every 30 s, banner "Saved on this phone. It will send when you're back online."), `app/scores/*` (grid with autosave and per-cell errors), `app/results/*` (compute, remarks, lock, publish, report cards and bundles), `app/fees/*` (items, structures, invoices, instalments, payments, receipts, debtors, expenses, reports, Paystack settings), `app/messages/*`, `app/announcements/*`, `app/timetable/*`; parent portal `/parent/*` (child switcher, overview, results, attendance, fees with Pay now and return verify, announcements, messages, timetable, account); student portal `/student/*`; notification bell component (unread badge capped "9+", dropdown of 10, "Mark all as read", full page `/notifications`, poll every 60 s and on focus) in staff, parent and student layouts
- Docs: `docs/SCHOOL_API.md` (every route), README product table row 8 "Done", `docs/PLATFORM_API.md` (school in the console; `/api/me/notifications`)

**Interfaces:**
- Consumes: Tasks 9–20 routes.

- [ ] **Step 1:** Build the pages; `npx tsc --noEmit` and `npm run lint` pass.
- [ ] **Step 2: Verify offline** in the browser pane: stop the API (`preview_stop`), open a cached register page, save marks, see the banner "Saved on this phone. It will send when you're back online.", start the API again, and confirm within 30 s that exactly one register with those marks exists on the server.
- [ ] **Step 3: Verify end to end** in the browser pane: teacher enters scores; principal locks, computes, publishes; parent (invited) sees results and report card, pays part of a fee with the fake/test Paystack key on localhost, sees the receipt and a notification; bursar sees the payment and the debtor list update.

### Task 23: Final review

- [ ] **Step 1:** Run the full suite `cd api && dotnet test`. Expected: all pass.
- [ ] **Step 2:** Fresh whole-branch review (opus) against the spec and this plan's Review Focus; fix Critical/Important with a failing test first; record rulings in the ledger.
- [ ] **Step 3:** Browser click-through of the main flows with the app window visible; record results in the ledger.
