# SchoolLink: next academic release design

Date: 18 Oct 2026. Extends `docs/superpowers/specs/2026-10-13-school-management-design.md`. Same stack, product `school`,
`school-web` on port 4200. Staff labels unchanged: Owner = Proprietor, Admin = Principal, Accountant = Bursar, Staff = Teacher.

Copy on the wire and in the app stays sentence case, with no all-caps labels and no exclamation marks. Results hold is still
"Results are held. Please see the school office."

## Intent

Make SchoolLink usable for nursery through secondary in one school: developmental nursery reports, psychomotor and conduct
ratings, a printable broadsheet, homework, lesson plans, an exam timetable, computer-based tests that feed the existing score
row, Primary student logins, and a small Play area. Success: one totals path for typed scores, counted assignments and CBT;
nursery cards never show WAEC-style marks; a teacher never sees another teacher's class.

This is one deployment, built as three slices (A then B then C). It is not a second results engine.

## Decisions agreed

| Topic | Decision |
|---|---|
| Architecture | Extend the existing academic pipeline (session, term, class, class-subject, enrolment, `Score`, lock → compute → publish) |
| Nursery / Creche | Level assessment mode `Developmental`. Performance levels and comments; no CA/exam total, no WAEC grade, no class position |
| Primary / JSS / SSS | Mode `Numeric`. Existing components and grading bands |
| Psychomotor | Configurable 1–5 criteria per level; not added into exam totals |
| Affective / appearance | Same 1–5 machinery, separate set (appearance, punctuality, honesty, cooperation, conduct) |
| Attendance on the card | Four counts from the register: present, absent, late, excused. Late is not merged into present on the card |
| Subject comment | Optional teacher text. Compute must not force the grade-band word into the cell |
| Overall comments | Class-teacher and principal remarks stay optional; suggestion picker for both |
| Result views | Per class (broadsheet) and per child. Subject teacher sees their subject; class teacher and leadership see the child |
| Assignments | Tied to class-subject and term; counted marks write the same `Score` row; practice stays on the assignment |
| Lesson plans | Draft → submitted → accepted or returned. School may turn review off |
| Exam timetable | Separate from the weekly grid. Leadership writes; class and venue overlap rejected; invigilator overlap is a warning |
| CBT | Objective only. Question bank per subject (optional level tag). Paper may pick items and/or draw N at random. Shuffle per attempt |
| Who sits CBT | Any enrolled student whose level has `HasStudentLogins` (Primary, JSS, SSS). Nursery and Creche do not sit |
| Play | Student app after login: word search and brain teasers. Does not write academic scores. Proprietor may hide Play |
| Wrong class / paper | 404, not 403, so ids cannot be probed |
| Out of this release | Theory CBT, Nursery student logins, parent-submitted homework, extra game genres, a second results store |

## What already exists (reuse)

- Levels, classes and arms, subjects, class-subjects with a teacher, enrolment, promotion.
- Teacher scope: class teacher and subject teacher ids; `GET /api/school/my-classes`.
- Assessment components and grading bands, including per-level override.
- Scores, lock, compute, publish, report-card PDF, fee hold.
- Weekly timetable with teacher-clash warnings (save not blocked).
- Register statuses Present, Absent, Late, Excused. Term result today stores only days present (late counted as present) and days open.
- Class-teacher remark and principal remark on `TermResult` (optional, frozen while published).
- Student login when `Level.HasStudentLogins` (template currently JSS/SSS only).
- Parent and student portals for published results and the weekly timetable.

## Architecture

- Same bounded context: `Saas.Domain/School`, `Saas.Application/School`, `school-web`. New tables are `TenantEntity` under the
  global filter.
- **One score row.** Typed entry, a counted assignment, and a counted CBT paper all upsert `Score` for that enrolment, class-subject,
  term and component. Compute, broadsheet, per-child view and PDF read only `SubjectResult` / `TermResult`.
- **Level.AssessmentMode:** `Numeric` (default for Primary, JSS, SSS) or `Developmental` (default for Creche and Nursery). The mode
  on the student's current level chooses the card workflow. A school may switch a level; existing numeric rows stay in the database
  but that level's new cards follow the new mode.
- **HasStudentLogins:** template default **true** for Primary, JSS and SSS; false for Creche and Nursery. Existing schools keep
  their flags; Principal turns Primary on under setup → levels, then sets admission-number passwords as today.
- Files (assignment uploads, CBT none in v1) use the existing private-file pattern: bearer or short-lived signed links, never a
  public URL.
- Bursar stays on fees. Bursar does not enter scores, comments, lesson plans, CBT or Play.

### Permissions

| Action | Teacher | Principal / Proprietor | Student (login) | Parent |
|---|---|---|---|---|
| Scores for class-subjects they teach | yes | yes | no | no |
| Psychomotor and affective 1–5 | class teacher of that class | yes | no | no |
| Subject comment on subjects they teach | yes | yes | no | published / not held |
| Class-teacher overall comment | class teacher of that class | yes | no | published / not held |
| Principal overall comment | no | yes | no | published / not held |
| Lock, compute, publish, broadsheet export | no | yes | no | no |
| Per-class broadsheet | own class: own subject column; class teacher: all subjects | all classes | no | no |
| Per-child report | class teacher: full child; subject teacher: subjects they teach | full child | own, published | own child, published |
| Assignments and CBT papers they teach | yes | yes | sit / submit if login | homework status, not the key |
| Lesson plans they teach | yes | review all | no | no |
| Exam timetable write / publish | no | yes | read own class when published | read child's class when published |
| Play | maintain word lists / teasers for own classes | hide Play; edit school lists | play | no |
| Question bank for subjects they teach | yes | yes | never the key | no |

A teacher who does not teach that class-subject, or a student on another class's paper, gets **404**.

## Data model (additions and changes)

| Entity | Key fields |
|---|---|
| `Level` (change) | add `AssessmentMode` (`Numeric`, `Developmental`). `HasStudentLogins` default true for Primary in the template |
| `TermResult` (change) | add `DaysAbsent`, `DaysLate`, `DaysExcused`. `DaysPresent` becomes Present marks only (not Late). Teacher and principal remarks unchanged, optional |
| `SubjectResult` (change) | `Remark` becomes optional teacher comment (`null` if none). Grade stays. Do not copy `GradingBand.Remark` on compute |
| `CommentBankEntry` | tenant, scope (`Subject`, `TeacherOverall`, `PrincipalOverall`, `Developmental`), match key (grade, band, or developmental level), text, order. School-editable; template seeds defaults |
| `TraitKind` | `Psychomotor`, `Affective` |
| `TraitCriterion` | tenant, kind, optional `LevelId` (null = school default), name, order, archived |
| `TraitRating` | enrolment, term, criterion, value 1–5, entered_by. Unique (enrolment, term, criterion) |
| `DevelopmentalArea` | tenant, optional `LevelId`, name, order, archived. Defaults: language, early numeracy, social, self-care, creativity, motor, participation, emotional |
| `DevelopmentalScale` | tenant, optional `LevelId`, code, label, order. Defaults: Excellent, Very Good, Good, Developing, Needs Support |
| `DevelopmentalRating` | enrolment, term, area, scale id, optional comment. Unique (enrolment, term, area) |
| `Assignment` | class-subject, term, title, instructions, due_at, optional `AssessmentComponentId` (null = practice), max_marks?, created_by, created_at |
| `AssignmentSubmission` | assignment, enrolment, body?, file key?, submitted_at, late, mark?, feedback?, marked_by?, status (`None`, `Received`, `Returned`). Unique (assignment, enrolment) |
| `LessonPlan` | class-subject, term, week, date, topic, objectives, materials, methods, activities, previous_knowledge, introduction, presentation, conclusion, evaluation, assignment_id?, teacher_remarks, status (`Draft`, `Submitted`, `Accepted`, `Returned`), reviewer_note?, submitted_at?, reviewed_by? |
| `SchoolProfile` (change) | `LessonPlansNeedReview` (bool, default true), `PlayEnabled` (bool, default true) |
| `ExamPeriod` | term, name, kind (`Test`, `Examination`), start date, end date, published_at? |
| `ExamSitting` | period, class-subject, date, start time, end time, duration minutes, venue, instructions, optional invigilator staff id |
| `BankQuestion` | subject, optional `LevelId`, stem, options (json list), correct_index, marks, archived, created_by, created_at |
| `CbtPaper` | class-subject, term, title, duration minutes, optional `ExamSittingId`, optional open/close window (required if no sitting), optional `AssessmentComponentId` (null = practice), `DrawCount` (0 if pick-only), published_at?, created_by |
| `CbtPaperPick` | paper, bank_question, order (for pick mode; draw mode stores nothing until attempt) |
| `CbtAttempt` | paper, enrolment, started_at, deadline_at, submitted_at?, total_marks, auto_marked, released_key_at? Unique open attempt per paper and enrolment (one attempt unless teacher reopens) |
| `CbtAttemptItem` | attempt, bank_question, order shown, option order (json permutation), chosen_index?, marks_awarded |
| `WordList` | tenant, optional class-subject or level, name, words (json), created_by |
| `BrainTeaser` | tenant, optional level, prompt, answer (staff only), archived |
| `PlayWordSearch` | generated at request time from a word list; not stored as academic data |

Money, dates and files follow the parent spec.

## Slice A — academic completeness

### Assessment mode

- Setup → levels shows mode. Switching a numeric level to developmental hides CA columns on new cards for that level; the score grid
  for that level is replaced by developmental areas. Switching the other way restores numeric components (existing developmental
  ratings remain visible historically).
- Numeric compute unchanged except: subject comment is not auto-filled; attendance writes four counts; positions still competition
  ranking for numeric classes only.
- Developmental compute writes no total, average, grade or position. Incomplete means an area has no rating.

### Psychomotor and affective

- Setup lists criteria per kind, optional per level (same override pattern as assessment components). Defaults:
  - Psychomotor: handwriting, drawing, hand coordination, use of tools, physical coordination, practical tasks.
  - Affective: appearance / neatness, punctuality, honesty, cooperation, conduct in class.
- Class teacher (or leadership) rates each child 1–5 on the per-child page. Subject teachers do not edit these. Values print on the
  card in their own block, labelled so they cannot be read as exam marks.

### Attendance on the card

- Register unchanged (Present, Absent, Late, Excused).
- On compute: `DaysPresent` = Present; `DaysLate` = Late; `DaysAbsent` = Absent; `DaysExcused` = Excused; `DaysOpen` = registers
  taken while the student was enrolled in that class (same open-day rule as today).
- Report card, broadsheet and per-child view print all four plus open days. Do not print only "present X of Y".

### Comments and suggestions

- Subject comment: optional, max 500 characters, subject teacher or leadership, frozen while published.
- Class-teacher overall and principal overall: existing fields, still optional, same freeze rule. Principal field is leadership only.
- Comment bank: seeded from grade-band wording plus longer overall lines. On the per-child page, suggestions matching the child's
  grade (numeric) or developmental level (nursery) appear next to each empty comment box. Choosing one fills the box; the teacher
  may edit. Suggestions are never published on their own.
- Grade-band `Remark` (Excellent, Credit, …) remains on the band for suggestions and setup; it is not copied onto `SubjectResult`.

### Views and broadsheet

- **Per class:** existing results grid, plus print (PDF) and export (CSV, formula-escaped). Same `SubjectResult` / `TermResult`
  numbers as the card. Nursery class sheet shows developmental levels, not a 100-mark grid.
- **Per child:** `/app/results/students/{studentId}?termId=&classId=` — components or developmental rows, optional subject comments,
  trait ratings, attendance counts, overall comments. Class teacher and leadership see every subject. Subject teacher sees and may
  edit comments only for class-subjects they teach; other subjects on that child are omitted (not 403).
- PDF report card: numeric layout as today plus optional subject comments, trait blocks, four attendance counts, overall comments.
  Developmental layout: areas, chosen level, area comments, traits, attendance, overall comments; no WAEC grade, no position.

## Slice B — classroom work

### Assignments

- Subject teacher creates for one class-subject and term: title, instructions, deadline, optional max marks, optional component
  (counted) or practice.
- Students with a login (Primary, JSS, SSS) submit text and/or one file. Late is allowed and labelled. After a mark is saved the
  student cannot replace the file unless the teacher reopens.
- Creche and Nursery: teacher records received / not received and feedback on the assignment roster (no student login).
- Counted mark upserts `Score` as `componentMax * (mark / assignmentMax)` when the assignment has a max; otherwise the mark must
  already lie on the component's scale. After the class-term is locked, marks that would write scores are refused
  ("Scores are locked for this term.").
- Parent portal: for each child, assignment title, due date, submitted or not, mark if released. Not the marking key.

### Lesson plans

- Fields: session/term (from the class-subject), class, subject, week, date, topic, objectives, materials, methods, activities,
  previous knowledge, introduction, presentation, conclusion, evaluation, optional link to an assignment, teacher remarks.
- Draft visible only to the author. Submit notifies leadership. Accepted, or returned with a note. If `LessonPlansNeedReview` is
  false, submit sets accepted immediately.
- Plans do not create timetable slots and do not appear on the report card.

### Exam timetable

- Leadership creates an `ExamPeriod` in a term (name, test or examination, date range) and sittings (class-subject, date, start, end,
  duration, venue, instructions, optional invigilator).
- Reject save when the same class has overlapping sittings, or the same venue is overlapping. Invigilator in two places: warning
  list, save allowed (same idea as weekly teacher clashes).
- Unpublished: office only. Published: teachers see their class-subjects; students and parents see their class. Later edits update
  the same period; everyone sees the latest version.
- Teachers do not create school exam sittings. They create CBT papers; leadership may link a paper to a sitting.

## Slice C — CBT and Play

### Question bank and papers

- Bank is per subject, optional level tag (so Primary 4 Maths can draw from Primary 4 items only, or from all Maths if untagged).
- Teachers add items at any time. Archive hides an item from new draws; old attempts keep a snapshot via `CbtAttemptItem`.
- Paper: pick specific items and/or `DrawCount` N from the subject (+ level). Picked items always appear; the draw fills extra
  items from the bank excluding those already picked. A paper may be draw-only (reuse old items, no new writing required). New
  items join the next draw.
- Each attempt shuffles question order and option order. The student payload has stems and options, never `correct_index`.
- Window: sitting start/end if linked; otherwise teacher open/close. Start before the window, unknown paper, or wrong class: 404
  "This test was not found." Clock starts on first open; after duration, further answers refused and last answers saved:
  "Time is up. Your answers were saved."
- One attempt unless the teacher reopens. Locked class-term: sitting and score writes refused.
- On submit, auto-mark objectives. Practice totals stay on the attempt. Counted totals upsert `Score` for the chosen component.
- Teacher and leadership: attempts, per-question marks, class performance. Student sees their mark after submit. The key is released
  when the teacher releases it or the window has closed.

### Play

- Student app section Play, only if `PlayEnabled` and the student has a login.
- **Word search:** grid built from a word list (class/teacher list, else school list, else a built-in list for the stage).
- **Brain teasers:** short items from `BrainTeaser`; teacher may add; draw may randomise from the bank (same reuse idea as CBT).
- No crossword, sudoku or other genres. No write to `Score` or report cards. High scores need not be persisted this release.

## Errors

| Case | `detail` |
|---|---|
| Locked term score/CBT/assignment write | Scores are locked for this term. |
| CBT before window / wrong class / unpublished | This test was not found. |
| CBT after duration | Time is up. Your answers were saved. |
| Class sitting overlap | This sitting overlaps another exam for the class. |
| Venue overlap | This venue is already booked at that time. |
| Numeric grade asked on a developmental class | This class uses developmental assessment. |
| Developmental rating on a numeric class | This class uses numeric assessment. |
| Trait outside 1–5 | Rate each item from 1 to 5. |
| Results hold (existing) | Results are held. Please see the school office. |
| Draw N larger than bank | There are not enough questions in the bank for this test. |
| Play disabled | Play is turned off for this school. |

## Testing

Tests first, Testcontainers, then school-web.

- Mode: nursery card has no total/grade/position; numeric card unchanged except optional subject comment and four attendance counts.
- Compute: typed score = counted assignment = counted CBT for the same component; broadsheet PDF/CSV matches the child PDF.
- Psychomotor and affective never added into totals.
- Late is not included in `DaysPresent` on the card; all four counts match the register.
- Subject comment may be null after compute; suggestion accept writes the comment; publish freezes comments.
- Permissions matrix: subject teacher vs class teacher vs bursar vs student vs parent; 404 on foreign ids.
- Exam clash rejects; invigilator warning does not reject.
- CBT: draw-only paper, shuffle differs across two students, no key in student JSON, window 404, lock refused, Primary login can sit.
- Play: does not insert `Score`; hidden when `PlayEnabled` is false.
- Phone width: per-child report, CBT sit, Play word search.

Browser click-through after slice C: Primary student login, sit a drawn paper, nursery card, broadsheet export, assignment submit,
lesson plan review, exam timetable, Play.

## Out of scope

Theory or essay CBT marking; Nursery or Creche student logins; parent-submitted homework; invigilator as a hard clash; extra Play
genres; HOD extra approval beyond lock/publish; per-subject attendance; a second results store; LearnLink quiz reuse.
