# SchoolLink Excel and CSV import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every SchoolLink list input (19 importers plus students) can be loaded from `.xlsx` or `.csv` with template → preview → confirm, through the same service methods the screens use.

**Architecture:** One import engine in `Saas.Application/School/Imports/` owns file limits, parsing (existing `SpreadsheetReader`), the held batch (`ImportBatch` gains `Kind`, `Scope`), preview and all-or-nothing confirm. Each importer is one class implementing `IImporter` and calls existing service methods in Apply. school-web gets one `ImportDialog` opened from each page.

**Tech Stack:** ASP.NET Core 10, EF Core + Npgsql, ClosedXML (already referenced), xUnit v3 + Testcontainers, Next.js 16.4 + Tailwind 4 (`school-web`).

**Spec:** `docs/superpowers/specs/2026-10-18-school-excel-import-design.md`

## Global Constraints

- Do not commit to the project git. Never kill all node processes. Stop only this checkout's `Saas.Api.exe` on 5080 before a build; do not stop other checkouts.
- Product `school`, `school-web` port 4200. Staff labels: Owner Proprietor, Admin Principal, Accountant Bursar, Staff Teacher.
- Copy: sentence case, no ALL-CAPS labels, no exclamation marks.
- Errors: ProblemDetails. Wrong class, subject, paper or child for that user is **404** with the existing `detail`, not 403.
- Limits: 5 MB, 2,000 data rows, `.xlsx` or `.csv`, formulas never evaluated (reuse `SpreadsheetReader`).
- Money columns in naira (up to two decimals) stored in kobo. School dates `DateOnly` Africa/Lagos; dates accept `2026-10-26`, `26/10/2026`, Excel serials (reuse `StudentImport.TryParseDate`).
- Roster templates: blank cell = leave unchanged; equal value = `Unchanged`; an unchanged row is not written.
- Uploads add or update only; never delete.
- Apply only through existing service methods (permissions, locks, publish freeze, single score writer, plan limits, notifications unchanged). Attendance imports message parents only for today's date.
- A batch is bound to its tenant and creating user; other user's or expired token → 404 `This import has expired. Upload the file again.` Batches last 30 minutes.
- Confirm with any error row → 409 `import_errors`: `Some rows have errors. Fix them in the file and upload it again; nothing was imported.`
- Existing `/api/school/students/import/*` routes keep working unchanged.
- C# strings containing `\n` via Edit/Write, never shell-script replacement.
- Tests: from `api`, `dotnet test --filter-class *ClassName` (FullyQualifiedName~ runs zero tests). After each task the full `cd api && dotnet test` passes and `npx tsc --noEmit` in `school-web` exits 0.
- Map routes in `SchoolEndpoints.cs` (do not split); every new route goes in the role table in `SchoolPlatformTests.cs`.

## Review Focus

1. A roster template for class A uploaded with class B's scope must refuse rows (`This student is not in this class.`), never write class A's numbers onto class B. Test in Task 3 (`Template_for_another_class_is_refused`).
2. The same student twice in one roster file must be an error row on both (`This student appears more than once in the file.`), not last-row-wins. Test in Task 1 engine roster helper (`Duplicate_student_rows_are_errors`) and used by every roster importer.
3. Data changing between preview and confirm (term locked, student withdrawn, question subject reassigned) must be caught by re-evaluation on confirm. Test in Task 3 (`Lock_between_preview_and_confirm_refuses`).
4. Excel cells that look numeric (admission `007`, codes `0012`, phone numbers) and Excel serial dates/times must read as the person typed them. Templates set text format; parsers accept serials. Test in Task 1 (`Leading_zero_admission_numbers_survive`) and Task 6 (`Excel_dates_in_headers_parse`).
5. A blank roster cell must never clear a stored score, rating, comment or mark. Test in Task 3 (`Blank_cell_keeps_stored_score`) and Task 4.

---

## File structure

| File | Responsibility |
|---|---|
| `api/src/Saas.Application/School/Imports/ImportEngine.cs` | Limits, parse, hold batch, preview, confirm, audit |
| `api/src/Saas.Application/School/Imports/IImporter.cs` | Importer contract, row result types, roster helper |
| `api/src/Saas.Application/School/Imports/ImportTemplates.cs` | List and roster `.xlsx`/`.csv` builders (`IImportTemplateWriter` in Infrastructure via ClosedXML) |
| `api/src/Saas.Application/School/Imports/Importers/*.cs` | One file per importer |
| `api/src/Saas.Infrastructure/Schools/ImportTemplateWriter.cs` | ClosedXML workbook writer (text columns, frozen header, notes sheet, prefilled roster) |
| `api/src/Saas.Domain/School/...` `ImportBatch` | add `Kind`, `Scope` (json) |
| `api/src/Saas.Api/Endpoints/SchoolEndpoints.cs` | generic import routes |
| `school-web/src/components/import-dialog.tsx` | Shared dialog |
| `school-web/src/lib/types/imports.ts` | Preview/result types |
| `api/tests/Saas.IntegrationTests/SchoolImport*Tests.cs` | Engine and per-importer facts |
| `docs/SCHOOL_API.md` | Imports section (Task 11) |

---

## Stage 1 — engine and question bank

### Task 1: Import engine, generic routes, students on the engine

**Files:**
- Create: `Imports/IImporter.cs`, `Imports/ImportEngine.cs`, `Imports/ImportTemplates.cs`, `Imports/Importers/StudentImporter.cs`
- Create: `Saas.Infrastructure/Schools/ImportTemplateWriter.cs`
- Modify: `ImportBatch` (+ `Kind` string(40) default `students`, `Scope` jsonb null); `AppDbContext`; migration `School27Imports`
- Modify: `StudentImport.cs` — `StudentImportService` delegates to the engine with kind `students`; keep its routes and DTO shapes
- Modify: `SchoolEndpoints.cs`, `DependencyInjection.cs`
- Test: `SchoolImportEngineTests.cs`; existing `SchoolStudentImportTests` (or the current student import test class) must stay green unchanged

**Interfaces:**
- Produces:
  - `public interface IImporter { string Kind { get; } ImportColumns Columns(ImportScope scope); Task EnsureAccessAsync(ImportScope scope, CancellationToken ct); Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct); Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct); Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct); }`
  - `ImportScope` = `IReadOnlyDictionary<string,string>` wrapper with `Guid RequireGuid(string key)`, `DateOnly RequireDate(string key)`, `string? Get(string key)`.
  - `ImportColumns(IReadOnlyList<ImportColumn> All)`; `ImportColumn(string Key, string Heading, bool Required, string Help)`.
  - `ImportTemplate(string SheetName, IReadOnlyList<ImportColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string> Notes)` — `Rows` empty for list templates, prefilled for rosters.
  - `enum ImportRowStatus { New, Update, Unchanged, Error }`; `ImportRowResult(int Row, ImportRowStatus Status, string Label, string? Message, object? Plan)` — `Plan` is the importer's typed apply payload.
  - `ImportApplied(int Created, int Updated)`.
  - `ImportPreviewResult(string Token, string Kind, IReadOnlyList<ImportPreviewRow> Rows, ImportCounts Counts, IReadOnlyList<string> Warnings)`; `ImportPreviewRow(int Row, string Status, string Label, string? Message)`; `ImportCounts(int New, int Update, int Unchanged, int Error)`.
  - `RosterRows` helper: given roster `(admissionNo → enrolment)` and rows, returns per-row enrolment or error (`This student is not in this class.` / `This student appears more than once in the file.`).
  - Routes: `GET /api/school/imports/{kind}/template.xlsx`, `GET .../template.csv`, `POST /api/school/imports/{kind}/preview` (multipart `file`, scope as query), `POST /api/school/imports/{token}/confirm` → `{ created, updated }`.
  - Unknown kind → 404 `Import was not found.`
- Engine order: `EnsureAccessAsync` (before any lookup) → size/type → read (`maxRows + 1`) → required columns → `EvaluateAsync` → hold batch (Kind, Scope, rows, CreatedBy) → preview. Confirm: token + tenant + `CreatedBy == user` else 404 expired sentence → `EnsureAccessAsync` → `EvaluateAsync` again → errors 409 → `ApplyAsync` inside one transaction → audit `imports.{kind}` → feed notice → remove batch.

- [ ] **Step 1: Write the failing tests** in `SchoolImportEngineTests`: `Unknown_kind_is_404`; `Other_users_token_is_404` (exact expired sentence); `Expired_token_is_404`; `Formula_cells_stay_text` (via student kind: a name cell `=HYPERLINK("x")` previews as text); `Two_thousand_one_rows_refused` (exact sentence); `Missing_column_is_named` (exact sentence with the column); `Leading_zero_admission_numbers_survive` (xlsx template's columns are text; a `007` admission round-trips); `Duplicate_student_rows_are_errors` (roster helper unit-level fact through a tiny test importer registered only in tests, or through the Task 3 scores importer if preferred — keep the fact in this class); `Generic_and_old_student_routes_agree` (same file previews identically on `/students/import/preview` and `/imports/students/preview`).
- [ ] **Step 2: Run** `--filter-class *SchoolImportEngineTests`. Expected: FAIL.
- [ ] **Step 3: Implement** engine, template writer (list: headings bold, all columns text format, frozen row, "How to fill in" sheet; roster: same plus prefilled rows), migration `School27Imports`, students as the first importer with no behaviour change.
- [ ] **Step 4: Run** focused tests, the existing student import tests, then full suite. Expected: PASS.

### Task 2: Question bank import and the shared dialog

**Files:**
- Create: `Imports/Importers/QuestionImporter.cs`
- Create: `school-web/src/components/import-dialog.tsx`, `school-web/src/lib/types/imports.ts`
- Modify: `school-web/src/app/app/questions/page.tsx` (Import from Excel button), `school-web/src/app/app/students/import/page.tsx` (use the dialog's preview table component or the dialog itself; keep the page)
- Test: `SchoolImportQuestionTests.cs`

**Interfaces:**
- Consumes: Task 1 engine; `CbtService` create-question path (extract an internal `CreateQuestionCoreAsync` if needed so validation is shared, not duplicated).
- Produces: kind `questions`; columns `subject, level, question, option_a … option_h, correct, marks`; dialog props `{ kind: string; scope?: Record<string,string>; title: string; onDone?: () => void }`.

- [ ] **Step 1: Write the failing tests**: `Valid_questions_import` (3 rows → preview New 3 → confirm → bank has 3 with right options and correct index); `Wrong_letter_is_an_error` (`correct` = E with four options → error row, confirm 409 exact sentence, bank unchanged); `Duplicate_options_error`; `Same_question_twice_in_file_error`; `Teacher_other_subject_row_error` (subject teacher importing a row for a subject they do not teach → error row with `This subject was not found.`); `Bursar_refused_before_lookup` (403 on template, preview, confirm).
- [ ] **Step 2: Run** `--filter-class *SchoolImportQuestionTests`. Expected: FAIL.
- [ ] **Step 3: Implement** importer and dialog (template buttons xlsx/csv via `downloadFile`, file picker, upload, preview table with status chips and messages inside `overflow-x-auto w-full min-w-0`, counts, warnings, Confirm, result line, error list from 409). Wire `/app/questions`.
- [ ] **Step 4: Run** tests, full suite, `npx tsc --noEmit`. Expected: PASS.

---

## Stage 2 — results and attendance (roster templates)

### Task 3: Scores import

**Files:** Create `Imports/Importers/ScoreImporter.cs`; modify `school-web/src/app/app/scores/page.tsx`; test `SchoolImportScoreTests.cs`

**Interfaces:**
- Consumes: `ScoreService.SaveAsync(SaveScoresRequest)` (sheet save; one call with every changed cell), `AcademicService.EnsureSchemeAsync`, roster helper.
- Produces: kind `scores`; scope `classSubjectId`, `termId`; columns `admission_no, student, <component name>…`; template prefilled with stored scores.

- [ ] **Step 1: Write the failing tests**: `Scores_import_matches_typed_save` (upload CA1 15, Exam 50 → `Score` rows equal what PUT /scores writes); `Blank_cell_keeps_stored_score`; `Unchanged_round_trip_writes_nothing` (download template, upload unchanged → all Unchanged, no score rows touched); `Over_maximum_is_error`; `Template_for_another_class_is_refused`; `Lock_between_preview_and_confirm_refuses` (preview OK → lock term → confirm 409 `term_locked` exact detail); `Teacher_of_other_subject_404`; `Developmental_class_400` (exact detail).
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** and wire the button on the score sheet (current class subject + term as scope).
- [ ] **Step 4: Run** tests, full suite, tsc. Expected: PASS.

### Task 4: Trait and developmental rating imports

**Files:** Create `TraitImporter.cs`, `DevelopmentalImporter.cs`; modify the class results page and the per-child page entry points as fits (class results page buttons per kind); test `SchoolImportRatingTests.cs`

**Interfaces:**
- Consumes: `TraitService.SaveRatingsAsync` (per student), `DevelopmentalService.SaveRatingsAsync` (per student; refuses locked term).
- Produces: kinds `traits` (scope `classId`, `termId`, `kind`), `developmental` (scope `classId`, `termId`); trait columns one per criterion (1–5); developmental columns one per area (scale label) and `<area> comment`.

- [ ] **Step 1: Write the failing tests**: `Trait_import_saves_ratings`; `Trait_value_outside_1_to_5_error` (`Rate each item from 1 to 5.`); `Subject_teacher_refused_traits` (404 `Student was not found.` or the screen's status); `Developmental_import_saves_labels`; `Unknown_label_error`; `Comment_without_rating_error`; `Developmental_locked_term_409`; `Blank_rating_keeps_stored` (both kinds).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run tests, full suite, tsc (PASS).

### Task 5: Comments and remarks import

**Files:** Create `RemarkImporter.cs`; modify class results page; test `SchoolImportRemarkTests.cs`

**Interfaces:**
- Consumes: `ResultService.SetSubjectCommentAsync`, `ResultService.SetRemarksAsync`.
- Produces: kind `remarks`; scope `classId`, `termId`; columns `admission_no, student, <subject name>…` (only subjects the caller may comment on), `class_teacher_remark`, `principal_remark` (leadership only).

- [ ] **Step 1: Write the failing tests**: `Subject_teacher_template_has_only_own_subjects`; `Remarks_import_saves`; `Comment_over_500_error`; `Published_results_refused` (409 `Unpublish the results before changing remarks.`); `Principal_column_rejected_for_teacher` (a teacher's file with `principal_remark` filled → error row or ignored column per spec: the column is absent from their template; a filled extra column is an error row `Only the principal writes the principal's remark.`).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run (PASS).

### Task 6: Attendance import

**Files:** Create `AttendanceImporter.cs`; modify `school-web/src/app/app/attendance/page.tsx`; modify `AttendanceService` only to expose an internal save that can suppress parent messages for dates before today (default behaviour unchanged); test `SchoolImportAttendanceTests.cs`

**Interfaces:**
- Consumes: register save per date.
- Produces: kind `attendance`; scope `classId`, `from`, `to` (≤ 31 days, ≤ today); columns `admission_no, student, <yyyy-MM-dd>…`; cell `P|A|L|E` or blank.

- [ ] **Step 1: Write the failing tests**: `Attendance_import_saves_registers`; `Other_letter_error`; `Range_over_31_days_400`; `Future_date_400` (`Choose today or an earlier date.`); `Past_absence_sends_no_message` (no `school.absent` notification for yesterday's A); `Today_absence_messages_parent` (same as the register); `Excel_dates_in_headers_parse` (header cells stored as Excel dates map to the right day).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run (PASS).

---

## Stage 3 — setup, classroom, Play, fees

### Task 7: Setup imports (subjects, classes, class subjects, staff)

**Files:** Create `SubjectImporter.cs`, `ClassImporter.cs`, `ClassSubjectImporter.cs`, `StaffImporter.cs`; modify setup subjects/classes pages and staff page; test `SchoolImportSetupTests.cs`

**Interfaces:** Consumes `AcademicService` create/update subject, class, class subject, set teacher; `StaffService`/`TeamService.InviteAsync(InviteRequest)` and staff profile update; plan seat check. Produces kinds `subjects`, `classes`, `class-subjects`, `staff`.

- [ ] **Step 1: Write the failing tests**: `Subjects_import_creates_and_updates_by_code`; `Classes_import_sets_class_teacher_by_email`; `Unknown_level_error`; `Class_subjects_import_sets_teacher`; `Unknown_staff_email_error`; `Staff_import_invites_and_updates`; `Proprietor_role_refused` (error row); `Seat_limit_error` (same sentence as the team screen); `Teacher_refused_setup_imports` (403 on all four).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run (PASS).

### Task 8: Classroom imports (assignment marks, exam sittings, timetable)

**Files:** Create `AssignmentMarkImporter.cs`, `ExamSittingImporter.cs`, `TimetableImporter.cs`; modify `/app/assignments`, `/app/exams/[id]`, `/app/timetable`; test `SchoolImportClassroomTests.cs`

**Interfaces:** Consumes `AssignmentService.MarkAsync`, `ExamTimetableService` add-sitting path (clash checks), `TimetableService.SaveAsync`. Produces kinds `assignment-marks` (scope `assignmentId`), `exam-sittings` (scope `periodId`), `timetable` (scope `classId`).

- [ ] **Step 1: Write the failing tests**: `Counted_mark_import_writes_score` (same Score as the screen); `Nursery_received_import`; `Mark_import_locked_term_409`; `Sitting_import_creates_sittings`; `Clash_within_file_error` (two rows same class overlapping → both or the second is an error with `This sitting overlaps another exam for the class.`); `Invigilator_clash_warning_not_error`; `Timetable_import_replaces_week`; `Teacher_refused_sittings_and_timetable` (403).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run (PASS).

### Task 9: Play imports (word lists, brain teasers)

**Files:** Create `WordListImporter.cs`, `TeaserImporter.cs`; modify `/app/play`; test `SchoolImportPlayTests.cs`

**Interfaces:** Consumes `PlayService` create word list / teaser paths. Produces kinds `word-lists`, `teasers`.

- [ ] **Step 1: Write the failing tests**: `Word_lists_import`; `Words_must_be_letters_error` (same rule as the screen); `Class_without_subject_error`; `Teasers_import`; `Bursar_refused` (403).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run (PASS).

### Task 10: Fees imports (fee items, fee structure, payments, expenses)

**Files:** Create `FeeItemImporter.cs`, `FeeStructureImporter.cs`, `PaymentImporter.cs`, `ExpenseImporter.cs`; modify the fees pages (items, structure, invoices/payments, expenses); test `SchoolImportFeesTests.cs`

**Interfaces:** Consumes `FeeService.CreateItemAsync/UpdateItemAsync/SaveStructureAsync`, `FeePaymentService.RecordAsync(invoiceId, RecordPaymentRequest)`, `ExpenseService.CreateAsync`. Produces kinds `fee-items`, `fee-structure` (scope `termId`, `levelId`), `payments`, `expenses`.

- [ ] **Step 1: Write the failing tests**: `Fee_items_import`; `Fee_structure_import_matches_screen` (kobo amounts); `Payment_import_records_receipts` (receipt numbers issued, same as the screen); `Payment_over_balance_error`; `Admission_number_with_two_open_invoices_error`; `Duplicate_reference_error`; `Naira_amount_parses_to_kobo` (`12,500.50` → 1250050); `Expenses_import`; `Teacher_refused_fees_imports` (403 as the fees policy).
- [ ] **Step 2–4:** Run (FAIL), implement + wire, run (PASS).

### Task 11: Docs and click-through

**Files:** Modify `docs/SCHOOL_API.md` (Imports section: routes, kinds, scopes, columns, statuses, errors table); `README.md` SchoolLink row mention of Excel import.

- [ ] **Step 1:** Update docs against the endpoint files.
- [ ] **Step 2:** Full `cd api && dotnet test` and `npx tsc --noEmit`. Expected: PASS.
- [ ] **Step 3:** Browser click-through on this checkout's 4200/5080 (controller): question import; score import round trip and a locked-term refusal; attendance import for a past week; payments import; exam sittings import with a clash row. Record gaps in the SDD ledger.
