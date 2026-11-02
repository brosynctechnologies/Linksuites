# SchoolLink next academic release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend SchoolLink so nursery reports, psychomotor and conduct ratings, broadsheets, homework, lesson plans, exam sittings, CBT (with a reusable question bank) and a student Play area all use the existing score → lock → compute → publish path.

**Architecture:** New tenant entities hang off session, term, class, class-subject and enrolment. Counted assignment marks and CBT totals upsert the existing `Score` row. `Level.AssessmentMode` chooses numeric vs developmental cards. Primary student logins use the existing `HasStudentLogins` flag.

**Tech Stack:** ASP.NET Core 10, EF Core + Npgsql, xUnit v3 + Testcontainers, QuestPDF, ClosedXML (CSV/xlsx already in the API), Next.js 16.4 + Tailwind 4 (`school-web`).

**Spec:** `docs/superpowers/specs/2026-10-18-school-next-release-design.md`

## Global Constraints

- Do not commit. Never kill all node processes. Stop this checkout's API (`Saas.Api.exe` on 5080) before `dotnet build` if it is running; do not stop other checkouts.
- Product `school`, `school-web` port 4200. Staff labels: Owner Proprietor, Admin Principal, Accountant Bursar, Staff Teacher.
- Copy: sentence case, no ALL-CAPS labels, no exclamation marks. Results hold: "Results are held. Please see the school office."
- Errors: ProblemDetails. Wrong class, paper or child for that teacher/student is **404** with the spec's `detail`, not 403.
- Money in kobo. School dates `DateOnly` Africa/Lagos.
- Private files: bearer or signed links, never public URLs.
- C# strings containing `\n` via Edit/Write, never shell-script replacement.
- Tests: `cd api && dotnet test tests/Saas.IntegrationTests --filter "FullyQualifiedName~{ClassName}"`. After each task, full `cd api && dotnet test` must pass. Existing `SchoolResultTests` that assume late counts as present are updated in Task 2, not left failing.

## File structure

| File | Responsibility |
|---|---|
| `api/src/Saas.Domain/School/School.cs` | New enums/entities; `Level.AssessmentMode`; attendance columns; nullable subject comment |
| `api/src/Saas.Application/Common/Abstractions.cs` | New `DbSet`s |
| `api/src/Saas.Infrastructure/Persistence/AppDbContext.cs` | `ConfigureSchool` mappings |
| `api/src/Saas.Application/School/SchoolTemplates.cs` | Primary logins, assessment mode, trait/developmental/comment-bank defaults |
| `api/src/Saas.Application/School/AcademicService.cs` | Level DTO includes `AssessmentMode` |
| `api/src/Saas.Application/School/ResultService.cs` | Four attendance counts; no auto subject remark; `GetChildAsync`; subject-teacher column filter |
| `api/src/Saas.Application/School/CommentBankService.cs` | Bank CRUD + suggestions |
| `api/src/Saas.Application/School/TraitService.cs` | Psychomotor/affective criteria and ratings |
| `api/src/Saas.Application/School/DevelopmentalService.cs` | Areas, scale, ratings |
| `api/src/Saas.Application/School/BroadsheetService.cs` | Class PDF/CSV from `ResultsDto` |
| `api/src/Saas.Application/School/AssignmentService.cs` | Assignments, submissions, counted `Score` upsert |
| `api/src/Saas.Application/School/LessonPlanService.cs` | Draft/submit/review |
| `api/src/Saas.Application/School/ExamTimetableService.cs` | Periods, sittings, clashes |
| `api/src/Saas.Application/School/CbtService.cs` | Bank, papers, sit, auto-mark, `Score` upsert |
| `api/src/Saas.Application/School/PlayService.cs` | Word search + teasers |
| `api/src/Saas.Application/School/ReportCardService.cs` | `ReportCardData` extended; developmental layout |
| `api/src/Saas.Infrastructure/Schools/SchoolPdf.cs` | Card + broadsheet layouts |
| `api/src/Saas.Api/Endpoints/SchoolEndpoints.cs` | New routes on `/api/school` |
| `api/src/Saas.Api/Endpoints/StudentEndpoints.cs` | Sit CBT, assignments, Play, exam timetable |
| `api/src/Saas.Api/Endpoints/ParentEndpoints.cs` | Homework status, exam timetable, published comments |
| `api/src/Saas.Application/DependencyInjection.cs` | Register new services |
| `school-web/src/app/app/...` | Staff screens per slice |
| `school-web/src/app/student/...` | CBT sit, assignments, Play |
| `docs/SCHOOL_API.md` | Contract (Task 16) |

Do not split `SchoolEndpoints.cs` unless a task's diff makes it unreadable; add maps next to the results/timetable groups.

## Review Focus

1. Late on the register must not increase `daysPresent` on the card, grid, PDF or CSV. Test in Task 2 (`Late_is_not_counted_as_present`).
2. A nursery card and nursery broadsheet must have no total, WAEC grade or class position. Test in Task 5 (`Nursery_card_has_no_numeric_total`).
3. Typed score, counted assignment and counted CBT for the same component must produce the same `Score.Value` and the same published total. Test in Task 9 and Task 14 (`Counted_assignment_matches_typed_score`, `Counted_cbt_matches_typed_score`).
4. Student CBT JSON must not contain `correctIndex` (or any key). Test in Task 14 (`Student_payload_omits_the_key`).
5. A teacher of class A / a student of class A must 404 on class B's child, paper, assignment and sitting. Test in Tasks 6, 9, 11, 14 (`Foreign_id_is_404`).

---

## Slice A — academic completeness

### Task 1: Assessment mode and Primary student logins on the template

**Files:**
- Modify: `api/src/Saas.Domain/School/School.cs` (`enum AssessmentMode { Numeric, Developmental }`; `Level.AssessmentMode`)
- Modify: `SchoolTemplates.cs`, `AcademicService.cs` (`LevelDto`, `SaveLevelRequest`)
- Modify: `AppDbContext.cs`, `Abstractions.cs`; migration `School15AssessmentMode`
- Test: `SchoolAcademicTests.cs`; `SchoolKit.cs` (`ClassAsync` still works when the POST omits `assessmentMode`)

**Interfaces:**
- Consumes: existing `POST /api/school/levels`, `POST /api/school/setup/apply-template`
- Produces: `LevelDto(Guid Id, string Name, Stage Stage, int Order, bool HasStudentLogins, AssessmentMode AssessmentMode)`; `SaveLevelRequest(..., AssessmentMode AssessmentMode)` — if the JSON omits mode, Creche/Nursery → `Developmental`, else `Numeric`. Template: Creche/Nursery `Developmental` and `HasStudentLogins` false; Primary/JSS/SSS `Numeric`; Primary `HasStudentLogins` **true**. Existing rows: EF default `Numeric`; do not backfill Primary logins.

- [ ] **Step 1: Write the failing tests** `Template_creates_levels_and_subjects`: after apply-template, Nursery 1 `assessmentMode == "Developmental"` and `hasStudentLogins == false`; Primary 1 `assessmentMode == "Numeric"` and `hasStudentLogins == true`; JSS 1 still true. `Posted_level_without_mode_defaults_from_stage`: POST `{ name = "N1", stage = "Nursery", order = 1, hasStudentLogins = false }` → `assessmentMode` Developmental.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SchoolAcademicTests"`. Expected: FAIL (unknown property / Primary still false).
- [ ] **Step 3: Implement** enum, column, template, DTO, default-from-stage in `AcademicService.CreateAsync`/`UpdateAsync`.
- [ ] **Step 4: Run** that filter, then `cd api && dotnet test`. Expected: PASS.

### Task 2: Four attendance counts on compute and the card

**Files:**
- Modify: `TermResult` (`DaysAbsent`, `DaysLate`, `DaysExcused`; `DaysPresent` = Present only)
- Modify: `ResultStudentDto` add `DaysAbsent`, `DaysLate`, `DaysExcused`
- Modify: `ResultService.AttendanceAsync` / compute; `ReportCardService` attendance line; `SchoolPdf.cs` if it prints the old sentence
- Modify: `PortalService` portal DTOs that expose `daysPresent`
- Migration `School16AttendanceCounts`
- Test: `SchoolResultTests.cs`, `SchoolReportCardTests.cs`

**Interfaces:**
- Consumes: `AttendanceStatus` Present/Absent/Late/Excused
- Produces: `ResultStudentDto` extra ints; report line from `ReportCardText.Attendance(int present, int late, int absent, int excused, int open)` → `"Present {present}, late {late}, absent {absent}, excused {excused} of {open} days"`

- [ ] **Step 1: Write the failing test** `Late_is_not_counted_as_present`: same register as today's Bayo row (day 1 Present, day 2 Late) → `daysPresent == 1`, `daysLate == 1`, `daysAbsent == 0`, `daysExcused == 0`, `daysOpen == 2`. Change `SchoolResultTests` assertion that currently expects Bayo `daysPresent == 2`. `SchoolReportCardTests` PDF text contains `Present 1, late 1, absent 0, excused 0 of 2 days` for that student, not `Present 2 of 2 days`.
- [ ] **Step 2: Run** `SchoolResultTests` and `SchoolReportCardTests`. Expected: FAIL.
- [ ] **Step 3: Implement** columns, compute mapping (Present only in `DaysPresent`; Late not added), `ReportCardText.Attendance`, portal fields.
- [ ] **Step 4: Run** those tests and full suite. Expected: PASS.

### Task 3: Optional subject comments and the comment bank

**Files:**
- Modify: `SubjectResult.Remark` to `string?` (no longer required; compute leaves null)
- Create: `CommentBankEntry` entity; `CommentBankService.cs`
- Modify: `ResultService` — `SetSubjectCommentAsync`; stop copying `band.Remark`
- Modify: `SchoolEndpoints.cs`; `SchoolTemplates.cs` seed bank from bands + overall lines
- Migration `School17Comments`
- Test: `SchoolResultTests.cs` (new facts), `SchoolCommentTests.cs`

**Interfaces:**
- Produces: `enum CommentScope { Subject, TeacherOverall, PrincipalOverall, Developmental }`
- `CommentBankEntry { CommentScope Scope, string MatchKey, string Text, int Order }`
- `GET /api/school/comment-bank` (leadership write PUT, staff read)
- `GET /api/school/results/{classId}/{termId}/suggestions?enrolmentId=&classSubjectId?` → `{ subject: string[], teacherOverall: string[], principalOverall: string[] }` matching grade or developmental level
- `PUT /api/school/results/{classId}/{termId}/subject-comments` `{ enrolmentId, classSubjectId, comment }` — subject teacher or leadership; 404 if they do not teach it; frozen 409 `"Unpublish the results before changing remarks."` (same as `ResultService.SetRemarksAsync`)
- `ResultSubjectDto.Remark` is `string?` (null after compute with no comment)

- [ ] **Step 1: Write the failing tests** `Compute_does_not_fill_subject_comment`: after compute, subject's `remark` is JSON null, `grade` is still `A1`. `Suggestion_matches_grade`: A1 student, GET suggestions, `subject` contains `"Excellent"`. `Teacher_picks_a_suggestion`: PUT subject-comments with that text → GET child/class shows it. `Published_subject_comment_is_frozen`.
- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement** nullable remark, bank seed, suggestion match = `GradingBand.Grade` (numeric) or developmental scale label, PUT.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 4: Psychomotor and affective ratings

**Files:**
- Create: `TraitKind`, `TraitCriterion`, `TraitRating` in `School.cs`; `TraitService.cs`
- Modify: endpoints, templates (default names from the spec), `AppDbContext`, DI, migration `School18Traits`
- Test: `SchoolTraitTests.cs`

**Interfaces:**
- `GET/PUT /api/school/traits?kind=Psychomotor|Affective&levelId=` (leadership write; same override pattern as assessment: null levelId = school default)
- `GET/PUT /api/school/students/{studentId}/traits?termId=&kind=` body `{ ratings: [{ criterionId, value }] }` — class teacher of the child's class or leadership; value 1–5 or 400 `"Rate each item from 1 to 5."`; bursar 403; subject-only teacher 404
- PUT does not touch `Score` or `TermResult.Total`

- [ ] **Step 1: Write the failing tests** `Template_seeds_psychomotor_and_affective_criteria` (names from the spec). `Class_teacher_rates_appearance`: PUT value 4, GET returns 4. `Subject_teacher_cannot_rate_traits` → 404. `Trait_value_6_is_400`. `Traits_are_not_added_into_the_exam_total`: numeric compute total unchanged after ratings.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** service, endpoints, unique (enrolment, term, criterion).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 5: Developmental (nursery) ratings and report card

**Files:**
- Create: `DevelopmentalArea`, `DevelopmentalScale`, `DevelopmentalRating`; `DevelopmentalService.cs`
- Modify: `ResultService.ComputeAsync` — if the class level's mode is `Developmental`, write no total/average/grade/position (`Total`/`Average` 0, `Position` 0, no subject numeric rows); incomplete if an area has no rating
- Modify: `ReportCardData` add `AssessmentMode Mode`, trait blocks, developmental rows; `SchoolPdf.cs` skip numeric table when Developmental
- Modify: `GET /api/school/results/{classId}/{termId}` students have empty `subjects` numeric list, plus `developmental` array
- Test: `SchoolDevelopmentalTests.cs`, `SchoolReportCardTests.cs`

**Interfaces:**
- `GET/PUT /api/school/developmental/areas?levelId=` and `/scale?levelId=` (leadership)
- `GET/PUT /api/school/students/{studentId}/developmental?termId=` `{ ratings: [{ areaId, scaleId, comment? }] }` — class teacher or leadership
- Defaults: areas and scale labels from the spec
- Asking numeric compute semantics on a developmental class for score entry: `PUT /api/school/scores` → 400 `"This class uses developmental assessment."`
- Reverse: developmental PUT on numeric class → 400 `"This class uses numeric assessment."`

- [ ] **Step 1: Write the failing tests** `Nursery_card_has_no_numeric_total`: Nursery class, rate areas, compute, GET results: `total == 0`, no `grade` on student, `position == 0`; PDF has no `A1` and no `1st`. `Numeric_scores_refused_on_nursery` 400 with that detail. `Developmental_put_refused_on_jss` 400.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** entities, compute branch, PDF branch, errors.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 6: Per-child results and subject-teacher class grid

**Files:**
- Modify: `ResultService.GetAsync` — allow subject teachers; filter `Subjects` to class-subjects they teach unless class teacher or leadership
- Add: `GetChildAsync(Guid studentId, Guid termId, Guid? classId, CancellationToken ct)`
- Modify: `SchoolEndpoints.cs` `GET /api/school/results/students/{studentId}`
- Extend child DTO with traits, developmental, four attendance counts, comments, suggestions optional via query
- Test: `SchoolResultTests.cs`

**Interfaces:**
- Produces: `ChildResultDto` (student identity, class, term, locked/published, attendance counts, `subjects` filtered, `developmental`, `psychomotor`, `affective`, `teacherRemark`, `principalRemark`)
- Foreign student for that teacher → 404 `"Child was not found."` (same family as parent 404 tone; spec: 404 not 403)

- [ ] **Step 1: Write the failing tests** `Subject_teacher_sees_only_their_column_on_the_class_grid`. `Class_teacher_sees_every_subject_on_the_child`. `Subject_teacher_child_omits_other_subjects`. `Foreign_id_is_404` (teacher of class A GET child in class B). `Bursar_cannot_read_results` (existing 403/hide — keep 403 for bursar as today if `GetAsync` already rejects bursar; do not change bursar to 404).
- [ ] **Step 2: Run**. Expected: FAIL (subject teacher still 403 on class GET).
- [ ] **Step 3: Implement** `EnsureCanReadAsync` to include `TeachesClassAsync`; filter subjects in `ViewAsync` / `GetChildAsync`.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 7: Broadsheet PDF and CSV

**Files:**
- Create: `BroadsheetService.cs`
- Modify: `ISchoolPdf` + `SchoolPdf.cs` `Task<byte[]> BroadsheetAsync(BroadsheetData data, CancellationToken ct)`
- Modify: `SchoolEndpoints.cs` leadership-only GET `.pdf` and `.csv`
- Test: `SchoolBroadsheetTests.cs`

**Interfaces:**
- `GET /api/school/results/{classId}/{termId}/broadsheet.pdf` and `.csv` — `Policies.Admin` only
- CSV formula-escaped (same helper as debtors export)
- Nursery sheet: developmental labels, not a 100-mark grid
- Figures identical to `GET /results/{classId}/{termId}` for that caller role… wait, export is leadership so full grid. Assert totals match compute.

- [ ] **Step 1: Write the failing tests** `Broadsheet_csv_matches_computed_totals`; `Teacher_cannot_export_broadsheet` 403 `"You need the Owner or Admin role for this."`; `Nursery_broadsheet_has_no_waec_grade`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** PDF table + CSV from `ResultService` leadership view.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 8: school-web slice A

**Files:**
- Modify: `school-web/src/lib/types.ts`, `app/setup/levels/page.tsx` (mode + Primary login checkbox already there — show mode)
- Modify: `app/setup/` add traits + developmental + comment-bank pages or sections under setup
- Modify: `app/results/page.tsx` (attendance columns, subject comment, export buttons for leadership, link to child)
- Create: `app/results/students/[studentId]/page.tsx` (per-child: suggestions, traits, developmental, overall comments)
- Parent/student result views: four attendance counts, optional comments, no key

**Interfaces:** Consumes Task 1–7 JSON as camelCase. No new API.

- [ ] **Step 1:** Staff levels page can save `assessmentMode`. Results class page shows present/late/absent/excused. Child page loads `GET /api/school/results/students/{id}`. Leadership sees PDF/CSV download. Phone width 375: child page usable (no horizontal trap on the main comment fields).
- [ ] **Step 2:** Manual check against running `school-web` 4200 + API 5080, or Playwright if the repo already has a school-web e2e pattern; if none, staff pages compile (`cd school-web && npx tsc --noEmit`) and the API tests remain green.

---

## Slice B — classroom work

### Task 9: Assignments

**Files:**
- Create: `Assignment`, `AssignmentSubmission`; `AssignmentService.cs`; migration `School19Assignments`
- Modify: `SchoolEndpoints.cs`, `StudentEndpoints.cs`, `ParentEndpoints.cs`, DI, `IAppDbContext`
- Reuse private upload like announcement files
- Test: `SchoolAssignmentTests.cs`

**Interfaces:**
- `POST /api/school/assignments` `{ classSubjectId, termId, title, instructions, dueAt, maxMarks?, assessmentComponentId? }` — subject teacher or leadership
- `GET /api/school/assignments?classSubjectId=&termId=`
- `PUT /api/school/assignments/{id}/submissions/{enrolmentId}` teacher mark/feedback/reopen/received (nursery roster)
- Student: `GET /api/student/assignments`, `POST /api/student/assignments/{id}/submit` multipart
- Parent: `GET /api/parent/children/{studentId}/assignments` status only
- Counted: `Score.Value = component.MaxScore * (mark / maxMarks)` when `maxMarks` set; else mark must be 0..component max
- Locked term counted write: 409 `"This term is locked for the class, so scores can't change. Ask the principal to unlock it."` (`ScoreService` `term_locked`)
- Foreign assignment 404 `"This assignment was not found."`

- [ ] **Step 1: Write the failing tests** `Counted_assignment_matches_typed_score`: component max 20, assignment max 10, mark 10 → `Score.Value == 20`, same as typing 20. `Practice_assignment_does_not_write_score`. `Locked_term_refuses_counted_mark` detail exact. `Foreign_id_is_404`. `Nursery_teacher_records_received_without_student_login`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** upsert via the same path `ScoreService` uses (extract a shared `ScoreWriter` only if both files would duplicate the unique-key upsert; otherwise call an internal method on `ScoreService` e.g. `UpsertAsync(enrolmentId, classSubjectId, termId, componentId, decimal value, ct)` and consume it from Task 14 too).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 10: Lesson plans

**Files:**
- Create: `LessonPlan`; `LessonPlanService.cs`; `SchoolProfile.LessonPlansNeedReview` default true; migration `School20LessonPlans`
- Test: `SchoolLessonPlanTests.cs`

**Interfaces:**
- `POST/PUT/GET /api/school/lesson-plans` fields from the spec; status `Draft|Submitted|Accepted|Returned`
- `POST /api/school/lesson-plans/{id}/submit`, `/accept`, `/return` `{ note }`
- `PUT /api/school/settings/lesson-plans` `{ needReview: bool }` leadership
- Draft GET 404 for anyone but the author (and leadership may 404 on drafts too — spec: drafts only visible to the teacher; leadership does not see drafts)
- Bursar 403 on the group

- [ ] **Step 1: Write the failing tests** `Draft_is_hidden_from_principal`. `Submit_then_accept`. `Need_review_false_submit_is_accepted`. `Teacher_cannot_open_another_teachers_draft` 404.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 11: Exam timetable

**Files:**
- Create: `ExamPeriod`, `ExamSitting`, `ExamPeriodKind { Test, Examination }`; `ExamTimetableService.cs`; migration `School21ExamTimetable`
- Modify: student/parent GET
- Test: `SchoolExamTimetableTests.cs`

**Interfaces:**
- `POST /api/school/exam-periods` `{ termId, name, kind, start, end }` leadership
- `POST /api/school/exam-periods/{id}/sittings` `{ classSubjectId, date, start, end, durationMinutes, venue, instructions, invigilatorStaffId? }`
- Overlap class → 409 `"This sitting overlaps another exam for the class."`
- Overlap venue → 409 `"This venue is already booked at that time."`
- Invigilator overlap → sitting saved, `warnings: [{ sittingId, detail }]`
- `POST /api/school/exam-periods/{id}/publish`
- Unpublished GET as teacher/student/parent: 404 `"This timetable was not found."`
- `GET /api/student/exam-timetable`, `GET /api/parent/children/{id}/exam-timetable` published only
- Teacher cannot POST sittings (403 Owner or Admin)

- [ ] **Step 1: Write the failing tests** for class clash, venue clash, invigilator warning (200/201 with warnings), unpublished 404, teacher 403, `Foreign_id_is_404`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** overlap as half-open intervals `[start, end)` on the same calendar date.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 12: school-web slice B

**Files:** `school-web` pages: `/app/assignments`, `/app/lesson-plans`, `/app/exams` (leadership write, teachers read); student `/student/assignments`; parent homework list; nav items with `academic.read` / `school.manage`

- [ ] **Step 1:** Wire the Task 9–11 endpoints. `npx tsc --noEmit` in `school-web`.
- [ ] **Step 2:** Full API suite still passes.

---

## Slice C — CBT and Play

### Task 13: Question bank and papers

**Files:**
- Create: `BankQuestion`, `CbtPaper`, `CbtPaperPick`; `CbtService.cs` (authoring only this task); migration `School22CbtBank`
- Test: `SchoolCbtTests.cs` (authoring facts)

**Interfaces:**
- `POST /api/school/questions` `{ subjectId, levelId?, stem, options: string[], correctIndex, marks }`
- `GET /api/school/questions?subjectId=&levelId=`
- `POST /api/school/cbt/papers` `{ classSubjectId, termId, title, durationMinutes, examSittingId?, openAt?, closeAt?, assessmentComponentId?, drawCount, pickQuestionIds: Guid[] }`
- Validate: sitting xor window (if `examSittingId` set, ignore separate window and use sitting times); `drawCount` extra from bank excluding picks; if `drawCount > remaining` 400 `"There are not enough questions in the bank for this test."`
- `POST /api/school/cbt/papers/{id}/publish`
- Student GET unpublished 404 `"This test was not found."` (Task 14)

- [ ] **Step 1: Write the failing tests** `Draw_only_paper_does_not_need_new_questions` (bank has 5, drawCount 3, picks empty → 201). `Draw_count_too_high` 400 exact detail. `Teacher_cannot_author_another_subjects_bank` 404.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** authoring + publish.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 14: CBT sit, shuffle, auto-mark, score write, Primary login

**Files:**
- Create: `CbtAttempt`, `CbtAttemptItem`; finish `CbtService` sit/submit; `StudentEndpoints`
- Modify: `StudentLoginService` already gates on `HasStudentLogins` — add test that Primary template login works after Task 1
- Test: `SchoolCbtTests.cs`, `SchoolStudentLoginTests.cs`

**Interfaces:**
- `POST /api/student/cbt/papers/{id}/start` → `{ attemptId, deadlineAt, items: [{ id, stem, options }] }` no `correctIndex`
- Shuffle: two students, same paper, `items` order or option order not both identical (retry assert up to 5 times if tiny N; use drawCount 5)
- `PUT /api/student/cbt/attempts/{id}/answers` `{ itemId, chosenIndex }`
- `POST /api/student/cbt/attempts/{id}/submit`
- After duration: 400 `"Time is up. Your answers were saved."` and persist last answers
- Before window / wrong class: 404 `"This test was not found."`
- Counted submit upserts `Score` via Task 9's `ScoreService.UpsertAsync`
- `Counted_cbt_matches_typed_score`: all items correct, component max 60, paper marks sum 60 → Score 60
- Key release: student GET attempt after submit without release → total only; after `POST /api/school/cbt/papers/{id}/release` or window end → per-item correctness
- Locked term: counted paper start 409 `"This term is locked for the class, so scores can't change. Ask the principal to unlock it."`
- One attempt: second start 409 unless `POST /api/school/cbt/attempts/{id}/reopen` (teacher)

- [ ] **Step 1: Write the failing tests** listed above plus `Student_payload_omits_the_key` (JSON string of start payload does not contain `correctIndex` or the correct option letter as a field) and `Primary_student_can_sit_cbt` (template Primary 1, set login, start paper).
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** attempts, clock from `TimeProvider`, Fisher–Yates shuffle with `Random.Shared` or `RandomNumberGenerator`.
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 15: Play (word search and brain teasers)

**Files:**
- Create: `WordList`, `BrainTeaser`; `SchoolProfile.PlayEnabled` default true; `PlayService.cs`; migration `School23Play`
- Modify: student endpoints; leadership `PUT /api/school/settings/play { enabled }`
- Test: `SchoolPlayTests.cs`

**Interfaces:**
- `GET /api/student/play/word-search?classSubjectId?` → `{ grid: string[][], words: string[] }` (grid square, words placed horizontally/vertically)
- `GET /api/student/play/teasers` random from bank (no `answer` field)
- `POST /api/school/play/words`, `/teasers` teacher/leadership
- Disabled: student GET 404 `"Play is turned off for this school."`
- After a teaser GET, `Score` count for that enrolment is unchanged

- [ ] **Step 1: Write the failing tests** `Play_does_not_write_a_score`; `Play_hidden_when_disabled` 404 exact; `Word_search_contains_the_word_list`; nursery student without login still cannot call student routes (existing 401/403).
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement** generator (place each word once; fill other cells with random A–Z).
- [ ] **Step 4: Run** tests and full suite. Expected: PASS.

### Task 16: school-web slice C, API docs, click-through

**Files:**
- `school-web`: `/app/cbt`, `/app/questions`, `/app/play` (staff lists); `/student/cbt/[paperId]`, `/student/assignments`, `/student/play`; exam timetable on student/parent
- Modify: `docs/SCHOOL_API.md` with every new route and the error table from the spec
- Modify: `README.md` SchoolLink row if it still says JSS/SSS-only logins

- [ ] **Step 1:** Types + pages; `npx tsc --noEmit`.
- [ ] **Step 2:** Update `SCHOOL_API.md`.
- [ ] **Step 3:** Full `cd api && dotnet test`. Expected: PASS.
- [ ] **Step 4:** Browser click-through on this checkout's 4200/5080: Primary login, sit a draw-only paper, nursery card, broadsheet CSV, assignment submit, lesson plan review, exam clash 409, Play word search. Record gaps in `.superpowers/sdd/` only if that ledger is in use; otherwise list them in the task notes. Do not stop other checkouts' APIs.
