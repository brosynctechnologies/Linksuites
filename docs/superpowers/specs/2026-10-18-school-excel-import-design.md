# SchoolLink — Excel and CSV upload for every data input

Date: 2026-10-18. Status: draft for review. Builds on `2026-10-18-school-next-release-design.md` (now implemented).

## Goal

Every SchoolLink input that is a list of records can be filled in from Excel (`.xlsx`) or CSV as well as on its screen. The school
downloads a template, fills it in, uploads it, sees a preview that marks each row, and confirms. A confirm saves every row or none.

Success: a school can load a term's setup, question bank, scores, ratings, comments, attendance, homework marks, exam sittings,
timetable, fee items, fee structure, payments and expenses from spreadsheets, and an upload can never do something the matching
screen would refuse.

## What the user asked

"All data input should have an Excel upload method." Students already have one (template, preview, confirm). Use that pattern.

## Out of scope

- Settings and switches (review on/off, Play on/off, results hold), publish, lock, unlock, compute, logo and signature uploads,
  report card bundles: they are not lists of records.
- Messages, announcements and lesson plans: free-form writing, one at a time.
- Exporting data other than through templates (the broadsheet CSV and debtors CSV already exist).
- Deleting records through an upload. An upload adds or updates; removals stay on the screens.

## How it works

### One engine, many importers

A shared import engine owns the file handling that student import has today: 5 MB limit, at most 2,000 data rows, `.xlsx` (first
sheet, or the sheet named for the importer) or `.csv`, zip-bomb guard, formulas never evaluated, headers normalised (lower case,
spaces to underscores), blank rows skipped, row numbers kept for messages. It reuses `SpreadsheetReader` and the `ImportBatch` table
(gaining `Kind` and `Scope` columns).

Each importer declares:

| Part | Meaning |
|---|---|
| Kind | Route key, for example `questions`, `scores` |
| Columns | Headings in order, which are required, and help text for the "How to fill in" sheet |
| Scope | Query values the importer needs (for example `classSubjectId`, `termId`). Validated and stored on the batch |
| Access | Who may download, preview and confirm. Same rule as the matching screen; checked on all three steps |
| Template | List (blank sheet) or roster (pre-filled from current data) |
| Evaluate | Checks every row against current data; returns a status and message per row |
| Apply | Saves through the existing service methods inside one transaction |

### Routes

- `GET /api/school/imports/{kind}/template.xlsx?{scope}` and `.csv`
- `POST /api/school/imports/{kind}/preview?{scope}` (multipart `file`) → preview
- `POST /api/school/imports/{token}/confirm` → result

The existing student routes keep working. Student import moves onto the engine as kind `students`; its routes become thin aliases.

### Preview and confirm

Preview returns `{ token, kind, rows: [{ row, status, label, message }], counts: { new, update, unchanged, error }, warnings }`.
Status is `New`, `Update`, `Unchanged`, `Error`. `label` names the row for a person (admission number and name, question stem,
subject code). Warnings do not block (for example an invigilator clash).

Confirm re-reads the held rows, evaluates them again (data may have changed), and:

- any `Error` → 409 `import_errors` with the error rows: "Some rows have errors. Fix them in the file and upload it again; nothing was imported."
- otherwise applies all rows in one transaction, writes one audit entry, drops the batch, and returns counts.

A batch belongs to the user who previewed it and to the school. Another user's token is 404
"This import has expired. Upload the file again." (same as an expired token). Batches last 30 minutes.

### Two kinds of template

- **List template**: the headings, text-formatted columns, frozen header row, and a "How to fill in" sheet. Used for questions,
  word lists, teasers, subjects, classes, class subjects, staff invites, exam sittings, fee items, payments, expenses.
- **Roster template**: one row per student on the class roster for the chosen scope, with `admission_no` and `student` filled in and
  locked as the row key, one column per thing to enter, and the current values already in the cells. Used for scores, trait
  ratings, developmental ratings, comments and remarks, attendance, assignment marks, fee structure (one row per item), timetable
  (current slots). A teacher can download, edit in Excel and upload back.

Roster rules:

- Rows match on `admission_no`. A student not on the roster for that scope is an error row: "This student is not in this class."
- A blank cell means leave unchanged. Clearing a value stays on the screen.
- A cell equal to the stored value is `Unchanged`. A row with nothing changed is `Unchanged` and is not written.
- The scope in the upload query must match the one the template was made for; the "How to fill in" sheet names the class, subject
  and term so a person can check.

### Same rules as the screens

Apply calls the same service methods the screens call, so permissions, locks, publish freeze, the single score writer, plan seat
limits and notifications behave exactly as on the screen. Specifically:

- Bursar is refused before any lookup wherever the screen refuses the bursar.
- A teacher only reaches class subjects they teach; other ids are 404 with the existing sentence.
- A locked class-term refuses score, developmental and assignment-mark imports with the existing `term_locked` 409.
- Published results refuse comment and remark imports with "Unpublish the results before changing remarks."
- Attendance only for today or earlier and within the teacher's window, as on the register.

Copy: sentence case, no ALL-CAPS labels, no exclamation marks; Proprietor, Principal, Bursar, Teacher.

## Importers

Money columns are in naira with up to two decimals and are stored in kobo. Dates accept `2026-10-26`, `26/10/2026` and Excel dates
(as student import). Times accept `09:00` and Excel times. Names of classes match ignoring spaces and case (as student import).
Staff are named by email.

### Stage 1 — engine and question bank

1. **Questions** (`questions`, list). Page `/app/questions`. Teacher of the subject or leadership; bursar refused.
   Columns: `subject` (name or code, required), `level` (optional; blank = every level), `question` (required), `option_a` …
   `option_h` (at least two), `correct` (letter, required), `marks` (required). Errors: subject the teacher does not teach, unknown
   level, fewer than two options, duplicate options, letter with no option, marks out of range, the same question twice in the file.
   Every valid row is `New`. Apply: the bank create path.

### Stage 2 — results and attendance (roster templates)

2. **Scores** (`scores`, roster). Scope `classSubjectId`, `termId`. Page `/app/scores`. Columns: one per assessment component
   (`CA1`, `CA2`, `Exam` …) with the maximum in the heading note. Errors: not a number, over the maximum, more than one decimal.
   Locked term → whole import 409. Developmental class → 400 "This class uses developmental assessment." Apply: the score sheet save.
3. **Trait ratings** (`traits`, roster). Scope `classId`, `termId`, `kind` (Psychomotor or Affective). Page: class results.
   Class teacher or leadership. One column per criterion; values 1–5. Apply: the trait save per student.
4. **Developmental ratings** (`developmental`, roster). Scope `classId`, `termId`. Nursery and creche only. One column per area with
   the scale label (Excellent … Needs Support), plus an optional `<area> comment` column per area. Errors: unknown label; a comment
   without a rating. Locked term → 409. Apply: the developmental save per student.
5. **Comments and remarks** (`remarks`, roster). Scope `classId`, `termId`. One column per subject the caller may comment on
   (subject teachers see only their own subjects; leadership and the class teacher see all), `class_teacher_remark`,
   `principal_remark` (leadership only; the column is absent for others). Max 500 characters per comment. Published → 409.
   Apply: the subject comment and remark saves.
6. **Attendance** (`attendance`, roster). Scope `classId`, `from`, `to` (at most 31 days, not after today). One column per date in
   the range with `P`, `A`, `L` or `E` (Present, Absent, Late, Excused); blank = not taken. Errors: other letters, a date outside
   the term or the teacher's window. Apply: the register save per date. Absence messages to parents go only for today's date;
   imported earlier dates do not message parents.

### Stage 3 — setup, classroom, Play, fees

7. **Subjects** (`subjects`, list). Leadership. `name`, `code`. An existing code updates the name.
8. **Classes** (`classes`, list). Leadership. `level`, `arm`, `class_teacher` (staff email, optional). Current session. An existing
   level and arm updates the class teacher.
9. **Class subjects** (`class-subjects`, list). Leadership. `class`, `subject` (name or code), `teacher` (staff email, optional).
   Existing pair updates the teacher.
10. **Staff invites** (`staff`, list). Leadership. `email`, `role` (Principal, Bursar or Teacher; Proprietor cannot be imported),
    `job_title`, `staff_no`, `phone`. Existing member → update profile fields; new → invite. Preview errors when the plan's seats
    would be exceeded (same rule and sentence as the team screen).
11. **Assignment marks** (`assignment-marks`, roster). Scope `assignmentId`. Subject teacher or leadership. Numeric classes: `mark`
    and `feedback`. Nursery and creche: `received` (yes or no) and `feedback`. Apply: the mark path (counted marks write the score
    through the single writer; locked term refuses counted marks).
12. **Exam sittings** (`exam-sittings`, list). Scope `periodId`. Leadership. `class`, `subject`, `date`, `start`, `end`, `venue`,
    `instructions`, `invigilator` (staff email, optional). Errors: the existing class and venue clash sentences, including clashes
    between rows of the same file. Invigilator clashes are warnings. Apply: the sitting create path.
13. **Timetable** (`timetable`, roster of current slots). Scope `classId`. Leadership. `day`, `period`, `start`, `end`, `subject`.
    Replaces the class's week, as the timetable screen save does. Teacher clashes are warnings.
14. **Word lists** (`word-lists`, list). Teacher or leadership. `name`, `level` (optional), `class` and `subject` (optional, together),
    `words` (comma separated). Apply: the word list create path.
15. **Brain teasers** (`teasers`, list). Teacher or leadership. `level` (optional), `prompt`, `answer`.
16. **Fee items** (`fee-items`, list). Proprietor, Principal and Bursar (the fees policy). `name`, `optional` (yes or no). Existing
    name updates the optional flag.
17. **Fee structure** (`fee-structure`, roster of items). Scope `termId`, `levelId`. One row per fee item, `amount`. Apply: the
    structure save for that term and level.
18. **Payments** (`payments`, list). Fees policy. `invoice_no` (or `admission_no` when the student has one open invoice that term),
    `amount`, `method`, `reference`, `paid_on`. Errors: unknown or cancelled invoice, amount over the balance, a reference already
    used. Apply: the record-payment path (receipt numbers and notifications as on the screen).
19. **Expenses** (`expenses`, list). Fees policy. `date`, `category`, `amount`, `note`.

Students (existing) move onto the engine without behaviour change.

## school-web

One shared `ImportDialog`: buttons for the `.xlsx` and `.csv` template, a file picker, the preview table (row, status, label,
message), counts, warnings, Confirm, and the result. It opens from an "Import from Excel" button on each matching page, passing
the kind and the page's current scope (class, subject, term, period, assignment). Hidden where the user cannot use that import.
Works at 375px (the table scrolls inside its box). The student import page uses the same dialog.

## Errors

| Case | detail |
|---|---|
| Wrong file type | Upload an .xlsx or .csv file. Download the template to start. |
| Too large | This file is larger than 5 MB. Split it into smaller files and upload them one at a time. |
| Too many rows | The file has more than 2,000 rows. Import at most 2,000 rows at a time and upload the rest in another file. |
| Missing column | The file is missing the column {names}. Download the template to see every column. |
| No rows | The file has no rows. Fill in the template and upload it again. |
| Rows with errors on confirm | Some rows have errors. Fix them in the file and upload it again; nothing was imported. |
| Expired or someone else's token | This import has expired. Upload the file again. |
| Student not in scope | This student is not in this class. |

Plus every sentence the matching screen already returns (locked term, published results, not found, seat limit).

## Testing

Per importer, integration tests:

- a valid file previews with the right counts and confirm saves exactly what the screen would;
- an error row is listed on preview and blocks confirm, saving nothing;
- access: bursar refused, teacher of another class or subject refused with the screen's status and sentence;
- the screen's lock or publish rule refuses the import;
- roster round trip: download the template, upload it unchanged → every row `Unchanged`, nothing written.

Engine tests: formula cells stay text; a zip bomb is refused; 2,001 rows refused; another user's token is 404; an expired token
is 404; CSV with a byte order mark and quoted commas reads correctly.

school-web: `npx tsc --noEmit`. API: the full suite still passes. A click-through after each stage on this checkout's 4200/5080:
one import per stage end to end (questions; scores and attendance; payments and exam sittings).

## Build order

1. Engine, generic routes, `ImportDialog`, students moved onto the engine, questions.
2. Scores, traits, developmental, remarks, attendance.
3. Subjects, classes, class subjects, staff, assignment marks, exam sittings, timetable, word lists, teasers, fee items, fee
   structure, payments, expenses, docs.
