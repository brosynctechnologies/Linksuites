# API contract — SchoolLink (school management)

Same API and conventions as the other products: camelCase JSON, string enums, money in **kobo** (integer minor units), ProblemDetails
(`detail` is a sentence to show; `errors` keyed by camelCase field), dates `"YYYY-MM-DD"`, instants ISO 8601 with offset. The
business time zone is `Africa/Lagos`. Web app: `school-web`, `http://localhost:4200` (`App:ProductWebUrls.school`).

Staff tokens carry the school (`AuthResponse.tenant.product = "school"`). Parent and student tokens are customer accounts:
`tenant` is **null**. Tokens from other products get **403**. A suspended school: staff get the shared **403** "This business is
suspended. Contact support."; parent and student routes that reach that school get **403** "This school's portal is unavailable."
A parent asking for a child they are not linked to (including a leaver) gets **404** "Child was not found."

Private files (photos, logos, report cards, receipts, announcement attachments, message images) are never public storage URLs.
Staff and family download PDFs with the bearer token (`Cache-Control: private, no-store`). Images and attachments use short-lived
signed links under `/api/school/.../{id}/{expires}/{signature}` (about 10 minutes).

Copy on the wire is sentence case. Results hold: **403** "Results are held. Please see the school office."

## Who

Staff roles are the shared team roles, labelled in school-web: Owner **Proprietor**, Admin **Principal**, Accountant **Bursar**,
Staff **Teacher**. In the tables below: **leadership** = Owner or Admin; **fees** = Owner, Admin or Accountant; **staff** = any
school member. Teachers only read the classes they teach, except where a row says otherwise.

| Who | Sign in | Token |
|---|---|---|
| Staff | `POST /api/auth/signup` / `/login` / `/otp/verify` with `product: "school"` | school tenant token |
| Parent | `POST /api/auth/parent/signup`, `/login`, `/otp/verify` (code from `POST /api/auth/otp/request`) | customer token (`tenant` null) |
| Student (a level with student logins: Primary, JSS and SSS on the template) | `POST /api/school/{slug}/student-login` `{ admissionNo, password }` | customer token (`tenant` null) |

`GET /api/me` returns `{ user, tenant | null }`. Refresh and logout are the shared `/api/auth/refresh` and `/logout`.

## Plans (`/api/billing`, product `school`)

| Plan | Price/term | Students | Staff seats | Online fees | Branding |
|---|---|---|---|---|---|
| Free | ₦0 | 50 | 3 | — | SchoolLink mark |
| Standard | ₦60,000 | 300 | 15 | ✓ | SchoolLink mark |
| Premium | ₦150,000 | 1,000 | 50 | ✓ | school's own |

Limit keys: `students`, `team_seats`, `online_fees`, `remove_branding`. Hitting a limit is **402** `code: "plan_limit"`; existing
records are never blocked. School fee payments go to the **school's own** Paystack account, not the platform's.

## Public (no sign-in)

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/school/{slug}/public` | anyone | School name, motto, short-lived logo link for the `/{slug}` sign-in page. 404 if unknown or suspended. |
| POST | `/api/school/{slug}/student-login` | student | `{ admissionNo, password }` → `AuthResponse`. Rate limited. |
| POST | `/api/school-demo-requests` | anyone | "Book a demo"; emailed to super admins. `{ schoolName, contactName, email, phone, students }`. |
| GET | `/api/school-invites/{token}` | anyone | Invite preview: `{ schoolName, guardianName, children }`. |
| POST | `/api/webhooks/paystack/school/{tenantId}` | Paystack | School fee charges, signed with that school's key. |

Signed file links (signature is the gate; `Cache-Control: private, no-store`):

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/school/photos/{id}/{expires}/{signature}` | Student photo |
| GET | `/api/school/logos/{tenantId}/{expires}/{signature}` | School logo |
| GET | `/api/school/report-cards/bundles/{id}/{expires}/{signature}` | Class report-card zip/PDF when Ready |
| GET | `/api/school/expense-receipts/{id}/{expires}/{signature}` | Expense receipt photo |
| GET | `/api/school/announcement-files/{id}/{expires}/{signature}` | Announcement attachment |
| GET | `/api/school/message-images/{id}/{expires}/{signature}` | Message image |

## Parent auth and invites

| Method | Path | Who | Purpose |
|---|---|---|---|
| POST | `/api/auth/parent/signup` | parent | Create a customer account. |
| POST | `/api/auth/parent/login` | parent | Email and password. |
| POST | `/api/auth/parent/otp/verify` | parent | Emailed code (requested with `POST /api/auth/otp/request`). |
| POST | `/api/school-invites/{token}/accept` | signed-in parent | Link this account to the guardian record. |

## Parent portal (`/api/parent`, signed-in parent)

Every child id is checked by `ParentAccess`: 404 if not linked or the child has left; 403 if the school is suspended; then the
request is scoped to that school. One request serves one school.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/parent/children` | Linked children across active schools (leavers and suspended schools omitted). `ChildRef[]`. |
| GET | `/api/parent/children/{studentId}` | One child; 404 / 403 as above. |
| GET | `/api/parent/children/{studentId}/overview` | Child, published results by term (empty figures when held), attendance, fees, timetable. |
| GET | `/api/parent/children/{studentId}/results/{termId}` | Published subject rows for this child. 403 when held. |
| GET | `/api/parent/children/{studentId}/report-card/{termId}.pdf?classId=` | Authenticated PDF. 403 when held. `classId` when the child changed class in the term. |
| GET | `/api/parent/children/{studentId}/attendance` | This term's days present, late, absent, excused and open, or null without a class. Late is not counted as present. |
| GET | `/api/parent/children/{studentId}/timetable` | This class's week, or null. |
| GET | `/api/parent/children/{studentId}/exam-timetable` | Published exam sittings of the child's class. `PortalExamTimetableDto`. Unpublished periods are left out; with nothing published (or no class this session) it is 200 `{ periods: [] }`. Another family's child is 404 "Child was not found." |
| GET | `/api/parent/children/{studentId}/assignments` | Homework: `{ id, title, dueAt, submitted, mark }[]`. `mark` is the teacher's mark, not the scaled score. No instructions, files or key. |
| GET | `/api/parent/children/{studentId}/invoices` | Invoices (kobo in `total` / `paid` / `balance`). Succeeded payments only. |
| POST | `/api/parent/children/{studentId}/invoices/{invoiceId}/pay` | `{ amountMinor }` → `{ authorizationUrl, reference }`. Return URL is `/parent/pay/return?reference=`. |
| POST | `/api/parent/payments/{reference}/verify` | Confirm after Paystack. `Abandoned` means the charge was not confirmed. |
| GET | `/api/parent/payments/{id}/receipt.pdf` | Receipt PDF for a succeeded payment of this parent's child. |
| GET | `/api/parent/children/{studentId}/announcements` | Announcements that reach this child. `attachmentUrl` is a signed link. |
| GET | `/api/parent/children/{studentId}/threads` | Conversations about this child. |
| POST | `/api/parent/children/{studentId}/threads` | `{ to: "class-teacher" \| "office", body }` starts (or continues) a thread. |
| GET | `/api/parent/threads/{id}/messages` | Messages, oldest first; marks the school's side read. |
| POST | `/api/parent/threads/{id}/messages` | `{ body }` text reply. |
| POST | `/api/parent/threads/{id}/messages/image` | multipart `file` + optional `body`. |

Pay now amounts: the balance, the next unpaid instalment, or at least ₦1,000 (or the balance if smaller). Online payment needs
the school's Paystack key and a plan with `online_fees`. Status `Abandoned`: show "Payment not confirmed. If you were charged, it will update automatically."

## Student portal (`/api/student`, signed-in student)

The student reaches only themselves, and only while they are active in an active school. 403 "Your account isn't active. Please see the school office." for a leaver; 403 "This school's portal is unavailable." when the school is suspended.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/student/me` | `{ name, admissionNo, className, schoolName }`. |
| GET | `/api/student/overview` | Same shape as the parent overview, with `fees` null. |
| GET | `/api/student/results/{termId}` | Published results. 403 when held. |
| GET | `/api/student/report-card/{termId}.pdf?classId=` | Authenticated PDF. 403 when held. |
| GET | `/api/student/timetable` | This class's week, or null. |
| GET | `/api/student/exam-timetable` | Published exam sittings of this class. `PortalExamTimetableDto`; 200 `{ periods: [] }` when nothing is published. |
| GET | `/api/student/announcements` | Announcements that reach this student. |
| GET | `/api/student/assignments` | Homework for this class: `StudentAssignmentDto[]` with the student's own answer, file link, mark and feedback. |
| POST | `/api/student/assignments/{id}/submit` | multipart `body` and/or `file` (PDF, JPEG, PNG or WebP, at most 5 MB). Sending again after marking is 409 until the teacher reopens it. 404 "This assignment was not found." for another class. |
| GET | `/api/student/cbt/papers` | Published papers of this class this term (open, upcoming and closed): `StudentCbtPaperDto[] { id, title, subject, subjectCode, durationMinutes, openAt, closeAt, counted, status: NotStarted\|InProgress\|Submitted, attemptId, deadlineAt, submittedAt, totalMarks }`. The student's own current attempt only; a reopened attempt shows as `NotStarted`. `totalMarks` once handed in. No questions and no key. `[]` without a class this session. |
| POST | `/api/student/cbt/papers/{id}/start` | Start a published paper of this class inside its window. → `CbtStartDto { attemptId, deadlineAt, items: [{ id, stem, options }] }`. Item and option order are shuffled per student; the key is never sent. `deadlineAt` is the start plus the duration, capped at the paper's close. 404 "This test was not found." when unpublished, outside the window, for another class, or unknown. 409 "You have already started this test." until a reopen. A counted paper in a locked term is 409 `term_locked`. |
| GET | `/api/student/cbt/attempts/{id}` | Before submit: `{ attemptId, deadlineAt, items: [{ id, stem, options, chosenIndex }] }`. After submit, before the key is released: `{ attemptId, deadlineAt, submittedAt, totalMarks, autoMarked }` only. After release (or once the window has closed): the same plus `items: [{ id, stem, options, chosenIndex, correct, marksAwarded }]`. Never a `correctIndex`. An attempt read after `deadlineAt` that was never handed in is marked first, exactly as a submit would (once; `submittedAt` is the deadline), and a counted paper writes its score; in a locked term the attempt is marked and the score is left alone. The student's paper list and the staff attempts list do the same. |
| PUT | `/api/student/cbt/attempts/{id}/answers` | `{ itemId, chosenIndex }` saves one answer and returns the open view. After `deadlineAt`: 400 "Time is up. Your answers were saved." and nothing changes. 409 "This test has already been handed in." after submit; 409 "This sitting was reopened. Start the test again." after a reopen. |
| POST | `/api/student/cbt/attempts/{id}/submit` | Marks the saved answers from the snapshot taken at the start. A counted paper writes the component score (scaled to the component's maximum, one decimal). A practice paper writes no score. Returns the view above. The sit page calls this itself when the clock reaches zero. |
| GET | `/api/student/play/word-search?classSubjectId=` | `{ grid: string[][], words }`. A square grid of A–Z; each word reads once, left to right or top to bottom. List: the class subject's newest (if it is in this class), else the school's (this level first), else a built-in list for the stage. |
| GET | `/api/student/play/teasers` | Up to 5 random unarchived teasers for every level or this level: `{ id, prompt }[]`. Never the answer. |

Play never writes a score or a report card. When the school has turned Play off, both Play routes are **404** "Play is turned off for this school." (`code: "play_off"`).

## In-app notifications (every signed-in token)

SchoolLink writes these; the routes are shared. A staff token sees this school's rows; a parent or student token sees all of
their own. Support view can read but not mark read.

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/me/notifications?unread=&page=` | any signed-in user | Newest first, 20 a page. `Paged<UserNotificationDto>`. |
| GET | `/api/me/notifications/unread-count` | any signed-in user | `{ count }` (stops at 99). |
| POST | `/api/me/notifications/{id}/read` | owner of the row | 204. 404 for someone else's. |
| POST | `/api/me/notifications/read-all` | owner | 204. |

`UserNotificationDto = { id, product, kind, title, body, link, tenantId, createdAt, readAt }`. `link` is an app path
(`/app/...`, `/parent/...`, `/student/...`). Read rows older than 90 days and unread older than 180 days are deleted by the school job.

## Staff — setup and people (`/api/school`, school token)

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/school/setup` | staff | School, sessions/terms, setup checklist. |
| POST | `/api/school/setup/apply-template` | leadership | `{ levels, subjects }` — Nigerian defaults. |
| GET / POST | `/api/school/levels` | staff / leadership | Levels (Creche…SSS). PUT/DELETE `/{id}`. Body `{ name, stage, order, hasStudentLogins, assessmentMode? }`; `assessmentMode` is `Numeric` or `Developmental` and on POST defaults from the stage (Creche and Nursery Developmental). On PUT an omitted `assessmentMode` keeps the stored mode. Changing the mode marks that level's unpublished results stale, so they are rebuilt in the new mode; published results keep the mode they were computed in. The template gives Primary, JSS and SSS student logins. |
| GET / POST | `/api/school/classes` | staff / leadership | `?sessionId=`. PUT/DELETE `/{id}`. |
| GET / POST | `/api/school/subjects` | staff / leadership | PUT/DELETE `/{id}`. |
| GET / POST | `/api/school/classes/{id}/subjects` | staff / leadership | Add a subject. PUT `/{subjectId}` sets the teacher; DELETE `/{subjectId}` removes the subject from the class. DELETE is 409 `class_subject_has_scores` / `class_subject_has_results` once scores or results exist, and 409 `class_subject_has_work` "This subject has homework, tests, lesson plans or exam sittings. Remove those first." while it has assignments, CBT papers, lesson plans or exam sittings. |
| GET | `/api/school/assessment?levelId=` | staff | Components (must total 100). PUT leadership; DELETE `/{levelId}` resets a level override. |
| GET | `/api/school/grading?levelId=` | staff | Grade bands. PUT leadership; DELETE `/{levelId}` resets a level override. |
| GET / PUT | `/api/school/traits?kind=Psychomotor\|Affective&levelId=` | staff / leadership | Trait criteria `{ criteria: [{ id, name }] }`. No `levelId` is the school default, seeded on the first read. A level PUT writes that level's override. Removing a rated criterion is 409. |
| GET / PUT | `/api/school/developmental/areas?levelId=` | staff / leadership | `{ areas: [{ id, name }] }`, seeded on the first read. A rated area can be renamed, not removed. |
| GET / PUT | `/api/school/developmental/scale?levelId=` | staff / leadership | `{ scale: [{ id, code, label }] }` (Excellent, Very Good, Good, Developing, Needs Support). A used point can be renamed, not removed. |
| GET / PUT | `/api/school/comment-bank` | staff / leadership | `{ entries: [{ scope, matchKey, text, order }] }`. Scopes: Subject, TeacherOverall, PrincipalOverall, Developmental. PUT replaces the whole bank. A school with none gets template lines on the first read. |
| GET | `/api/school/staff` | leadership | Staff directory. PUT `/{membershipId}` job title / staff no / phone. |
| GET | `/api/school/staff/me` | staff | `{ role, jobTitle, staffId }`. |
| GET | `/api/school/my-classes` | staff | Classes this member teaches. |
| PUT | `/api/school/classes/{id}/class-teacher` | leadership | `{ staffId }`. |
| PUT | `/api/school/class-subjects/{id}/teacher` | leadership | `{ staffId }`. |
| GET / PUT | `/api/school/timetable/{classId}` | staff / leadership | Week. PUT body is a **bare array** of slots, not `{ slots }`. |
| GET / POST | `/api/school/students` | staff / leadership | List `?q=&classId=&status=&skip=&take=`. POST create. GET/PUT/DELETE `/{id}`. |
| POST | `/api/school/students/{id}/photo` | leadership | multipart photo. |
| POST | `/api/school/students/{id}/transfer` | leadership | `{ classId }` this session. |
| POST | `/api/school/students/{id}/status` | leadership | `{ status, date, reason }` Active / Graduated / Withdrawn / Transferred. |
| POST / PUT / DELETE | `/api/school/students/{id}/guardians[/{guardianId}]` | leadership | Add, edit, remove. POST `.../primary` sets the primary guardian. |
| GET | `/api/school/guardians?withoutEmail=&classId=` | fees | Guardians of a class (invites page). |
| POST | `/api/school/guardians/{id}/invite` | leadership | `{ link, whatsAppUrl }`. |
| POST | `/api/school/guardians/invite-all` | leadership | `{ invited }`. |
| GET | `/api/school/students/import/template.xlsx` · `.csv` | leadership | Import templates. |
| POST | `/api/school/students/import/preview` | leadership | multipart file → preview token (30 min). |
| POST | `/api/school/students/import/{token}/confirm` | leadership | Saves all or none. 409 `import_errors` with `rows`. Every other kind imports through `/api/school/imports/{kind}`: see Imports below. |
| GET | `/api/school/students/id-cards.pdf?classId=&studentIds=` | fees | PDF cards. |
| POST | `/api/school/students/{id}/login` | leadership | `{ password }` for a student whose level has student logins (Primary, JSS and SSS on the template). Creche and Nursery have none. |
| GET | `/api/school/promotion/preview` | leadership | Next-session proposal. |
| POST | `/api/school/promotion` | leadership | `{ sessionName, start, end, overrides }` — all or nothing. |

## Staff — academic work

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/school/attendance/summary?date=` | leadership | Classes taken that day. |
| GET / PUT | `/api/school/attendance/{classId}/{date}` | class teachers + leadership | Register. PUT `{ clientId, marks, baseClientId?, priorClientId? }` is idempotent on `clientId`. `baseClientId` is the stored register's `clientId` the marks were based on (`""` when not taken); if the register changed since, 409 `register_changed`. |
| GET | `/api/school/students/{id}/attendance?termId=` | staff | One student's counts. |
| GET / PUT | `/api/school/scores` | subject teacher + leadership | GET `?classSubjectId=&termId=`. PUT `{ classSubjectId, termId, scores }` with component **ids**. A developmental class is 400 "This class uses developmental assessment." |
| POST | `/api/school/results/{classId}/{termId}/lock` · `/unlock` | leadership | Unlock needs unpublish first. After lock, missing scores count as 0. |
| GET | `/api/school/results/{classId}/{termId}` | class teacher, subject teacher of the class + leadership | Stored sheet. A subject teacher sees only the subjects they teach. Each student carries `daysPresent`, `daysLate`, `daysAbsent`, `daysExcused`, `daysOpen` (late is not present), `subjects[].remark` (the subject comment, or null), `developmental: [{ area, scale, comment }]` and `mode` (`Numeric` or `Developmental`). Each row is shown in the mode it was computed in, so a numeric row has empty `developmental` and a developmental row empty `subjects`; the sheet's `mode` is the rows' mode, or the level's current mode while nothing is computed. `daysLate`, `daysAbsent` and `daysExcused` are null on rows published before they were counted (School26); cards then print "Present X of Y days". Bursar 403 "The bursar does not read results." |
| GET | `/api/school/results/students/{studentId}?termId=&classId=&suggestions=` | class teacher, subject teacher + leadership | `ChildResultDto`: one child's term with filtered `subjects`, `developmental`, `psychomotor`, `affective`, remarks, the four counts (three may be null, as on the sheet) and the row's `mode`. `suggestions=true` adds `CommentSuggestionsDto`. A teacher who does not teach the child gets 404 "Child was not found." Bursar 403 before lookup. |
| GET | `/api/school/results/{classId}/{termId}/suggestions?enrolmentId=&classSubjectId=` | same | `{ subject, teacherOverall, principalOverall }` from the comment bank: the subject's grade, the average's grade, or the developmental scale labels. |
| PUT | `/api/school/results/{classId}/{termId}/subject-comments` | subject teacher / leadership | `{ enrolmentId, classSubjectId, comment }`. Empty clears it; 500 characters at most. 404 for a subject the teacher does not teach. |
| PUT | `/api/school/results/{classId}/{termId}/remarks` | class teacher / leadership | `{ enrolmentId, teacherRemark, principalRemark }`. |
| POST | `/api/school/results/{classId}/{termId}/compute` · `/publish` · `/unpublish` | leadership | Publish emails guardians; published rows are frozen. Writing a comment or remark on published results is 409 "Unpublish the results before changing remarks." |
| GET | `/api/school/results/{classId}/{termId}/broadsheet.pdf` · `.csv` | leadership | The class broadsheet (landscape PDF, or UTF-8 CSV with a BOM). A developmental sheet (by the rows' computed mode) prints developmental labels. On rows published before late, absent and excused were counted, those three cells are blank. A class teacher gets 403 "You need the Owner or Admin role for this." |
| GET / PUT | `/api/school/students/{id}/traits?termId=&kind=` | class teacher of the child's class + leadership | `{ ratings: [{ criterionId, value }] }`, value 1 to 5 (else 400 "Rate each item from 1 to 5."). A PUT changes only the items sent. Bursar 403 first; another teacher 404 "Student was not found." Traits never add into totals. |
| GET / PUT | `/api/school/students/{id}/developmental?termId=` | class teacher of the child's class + leadership | `{ ratings: [{ areaId, scaleId, comment? }] }`. A numeric class is 400 "This class uses numeric assessment." Same refusals as traits. Once the class's term is locked (publishing needs the lock), PUT is 409 `term_locked`, as for scores; unlocking opens them again. Psychomotor and affective traits stay editable. |
| GET / POST | `/api/school/assignments` | subject teacher + leadership | GET `?classSubjectId=&termId=` → `AssignmentDto[]` with the class roster in `submissions`. POST `{ classSubjectId, termId, title, instructions, dueAt, maxMarks?, assessmentComponentId? }`. A teacher who does not teach the class subject gets 403. Bursar 403. |
| PUT | `/api/school/assignments/{id}/submissions/{enrolmentId}` | same | `{ mark?, feedback?, reopen?, received? }`. A counted mark writes the same score cell as the sheet (scaled onto the component). `received` is for a developmental class. A locked term is 409 `term_locked`. 404 "This assignment was not found." |
| GET / POST | `/api/school/lesson-plans` | subject teacher + leadership | GET `?classSubjectId=&termId=`. A draft is visible only to its author; leadership lists Submitted, Accepted and Returned. POST `{ classSubjectId, termId, week, date, topic, objectives, materials, methods, activities, previousKnowledge, introduction, presentation, conclusion, evaluation, assignmentId?, teacherRemarks? }`. |
| GET / PUT | `/api/school/lesson-plans/{id}` | author (draft) / readers | 404 "This lesson plan was not found." for a draft that is not yours. PUT only while Draft or Returned. |
| POST | `/api/school/lesson-plans/{id}/submit` · `/accept` · `/return` | author / leadership / leadership | Submit asks leadership to review (or accepts at once when review is off). Return `{ note }`. Accept and return only from Submitted (409 `lesson_plan_status`). |
| GET / PUT | `/api/school/settings/lesson-plans` | leadership | `{ needReview }`. On for a new school. |
| GET / POST | `/api/school/exam-periods` | staff (read) / leadership | GET `?termId=`. POST `{ termId, name, kind: Test\|Examination, start, end }`. Teachers see only published periods; bursar 403. |
| GET | `/api/school/exam-periods/{id}` | leadership; teachers once published | `{ ...period, sittings }`. A teacher sees only sittings for subjects they teach. Unpublished or unknown is 404 "This timetable was not found." |
| POST | `/api/school/exam-periods/{id}/sittings` | leadership | `{ classSubjectId, date, start, end, durationMinutes, venue, instructions, invigilatorStaffId? }` (times `"HH:mm"`, Lagos). Overlap is half-open on the same date across every period. A class clash is 409 "This sitting overlaps another exam for the class."; a venue clash (trimmed, any case) 409 "This venue is already booked at that time." An invigilator already booked is saved with `warnings: [{ sittingId, detail: "This teacher is already invigilating another exam at that time." }]`. |
| POST | `/api/school/exam-periods/{id}/publish` | leadership | Publishing again keeps the first `publishedAt`. Teachers, students and parents then see the sittings. |
| GET | `/api/school/exam-sittings/{id}` | same as the period | One sitting. |
| PUT / DELETE | `/api/school/exam-sittings/{id}` | leadership | PUT takes the same body as adding a sitting and replaces it within its period. The class and venue clash checks and the invigilator warning leave the sitting itself out, so moving it frees its old slot. A CBT paper tied to the sitting that no student has started follows the new time; one tied to it for a different class subject is untied and keeps its window. DELETE → 204; a tied CBT paper keeps its window. Unknown or another school's sitting is 404 "This timetable was not found."; anyone else 403 "You need the Owner or Admin role for this." |
| GET / POST | `/api/school/questions` | subject teacher + leadership | GET `?subjectId=&levelId=` → `BankQuestionDto[]` (with `correctIndex`; staff only). `levelId` returns only questions tagged with that level. POST `{ subjectId, levelId?, stem, options, correctIndex, marks }` (2 to 8 options). A teacher of another subject gets 404 "This subject was not found." Bursar 403 "The bursar does not set tests." |
| POST | `/api/school/questions/{id}/archive` · `/unarchive` | subject teacher of the question's subject + leadership | → `BankQuestionDto` with `archived`. An archived question is never drawn, and a paper that picked it leaves it out of new attempts; attempts already started keep their snapshot. A start with nothing left to ask is 400 "There are not enough questions in the bank for this test." Unknown, another school's, or another subject's question is 404 "This question was not found."; the bursar 403 before lookup. Questions are not edited: archive a wrong one and add it again. |
| GET | `/api/school/cbt/papers?classSubjectId=&termId=` | subject teacher of the class subject + leadership | `CbtPaperDto[]` (with `publishedAt` and `releasedKeyAt`), newest window first. A teacher who does not teach it gets 404 "Class subject was not found."; the bursar 403 before lookup. |
| GET | `/api/school/cbt/papers/{id}/attempts` | same | `{ paperId, title, releasedKeyAt, students: [{ enrolmentId, name, admissionNo, attempts: [{ attemptId, startedAt, deadlineAt, submittedAt, reopenedAt, totalMarks, paperMarks, items: [{ order, stem, marks, marksAwarded }] }] }] }`. Every active student of the class (and anyone who has an attempt), attempts newest first; `marksAwarded` is null until handed in. Another teacher or an unknown paper: 404 "This test was not found." |
| POST | `/api/school/cbt/papers` | subject teacher of the class subject + leadership | `{ classSubjectId, termId, title, durationMinutes, examSittingId?, openAt?, closeAt?, assessmentComponentId?, drawCount, pickQuestionIds }` → 201 `CbtPaperDto`. `drawCount` is extra questions drawn per student on top of the picks, from unarchived questions of the subject that are untagged or tagged with the class's level. Too few is 400 "There are not enough questions in the bank for this test." With `examSittingId`, the sitting's date and times are the window; otherwise both window times are required. No `assessmentComponentId` is a practice paper. A developmental class is 400 "This class uses developmental assessment." |
| POST | `/api/school/cbt/papers/{id}/publish` | same | Students of the class can start it inside the window. Publishing again keeps the first time. 404 "This test was not found." |
| POST | `/api/school/cbt/papers/{id}/release` | same | `{ id, releasedKeyAt }`. Students who handed in see per-item correctness. The key is also released when the window closes. |
| POST | `/api/school/cbt/attempts/{id}/reopen` | same | `{ id, reopenedAt }`. The student may start the paper again; an earlier counted score stays until the new attempt is submitted. |
| GET | `/api/school/play/words` | staff (not the bursar) | `WordListDto[]`, newest first. Leadership sees every list; a teacher sees school and level lists and the lists of class subjects they teach. |
| GET | `/api/school/play/teasers` | staff (not the bursar) | `BrainTeaserDto[]` with `answer` and `archived`, newest first. |
| POST | `/api/school/play/words` | staff (not the bursar) | `{ classSubjectId?, levelId?, name, words }`: one scope, or neither for the whole school. 1 to 20 words, each 3 to 15 letters A–Z, no repeats; stored upper case. A class-subject list needs a teacher of that class subject (else 404 "Class subject was not found."). |
| POST | `/api/school/play/teasers` | staff (not the bursar) | `{ levelId?, prompt, answer }` → `BrainTeaserDto` (with `answer`). |
| GET / PUT | `/api/school/settings/play` | leadership | `{ enabled }`. On for a new school. |
| GET | `/api/school/report-cards/{enrolmentId}/{termId}.pdf?classId=` | class teacher + leadership | One card. |
| POST | `/api/school/report-cards/{classId}/{termId}/bundle` | class teacher + leadership | Queues a zip; requester is notified when Ready. |
| GET | `/api/school/report-cards/bundles/{id}` | requester + leadership | `{ status, url, classId, className, termId }` (`url` is a signed link when Ready). The ready/failed notifications link to `/app/results?bundle={id}`. |
| GET | `/api/school/assignment-files/{id}/{expires}/{signature}` | anyone with the link | The file a student attached to a submission (`id` is the submission), behind a short-lived signed link: the `fileUrl` in the staff and student assignment DTOs. Redirects to storage or streams the file with `Cache-Control: private, no-store`. A bad or expired signature is 403 "This link has expired. Open the assignment again."; a submission with no file 404. |

## Staff — fees

Money is kobo. Invoice `status`: `Unpaid`, `PartlyPaid`, `Paid`, `Cancelled`. Payment `status`: `Pending`, `Succeeded`, `Mismatch`,
`Abandoned`. Online `Abandoned` copy: "Payment not confirmed. If you were charged, it will update automatically."

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET / POST | `/api/school/fee-items` | fees | PUT/DELETE `/{id}`. |
| GET | `/api/school/fee-structure/{termId}` | fees | Per level. PUT `/{termId}/{levelId}`. POST `.../copy-from/{fromTermId}`. |
| POST | `/api/school/invoices/generate` | fees | `{ termId }` → created / refreshed / skipped / needsReview (flagged, persisted) / withPayments (left alone, lines differ from the structure; not flagged) / invoices (`{ id, number, studentName }` for both lists). |
| GET | `/api/school/invoices?termId=&classId=&status=&skip=&take=` | fees | List. GET `/{id}` detail. |
| PUT | `/api/school/invoices/{id}/optional-items` | fees | Chosen optional lines. |
| POST | `/api/school/invoices/{id}/discount` | fees | `{ kind: Percent\|Amount, value, reason }`. |
| PUT | `/api/school/invoices/{id}/instalments` | fees | Bare array of `{ dueDate, amountMinor }`. |
| POST | `/api/school/invoices/{id}/payments` | fees | Manual `{ amountMinor, method, reference, paidOn }`. |
| POST | `/api/school/payments/{id}/reverse` | fees | `{ reason }`. |
| GET | `/api/school/payments/{id}/receipt.pdf` | fees | Receipt PDF. |
| POST | `/api/school/invoices/{id}/cancel` | fees | `{ reason }`. Refused while it has payments. The balance becomes 0. |
| POST | `/api/school/invoices/{id}/reviewed` | fees | Clears the review flag (NeedsReview / mismatch). |
| GET | `/api/school/debtors?termId=&classId=&minDaysOverdue=` | fees | Outstanding. |
| GET | `/api/school/debtors/whatsapp?termId=&minDaysOverdue=` | fees | `{ studentName, guardianName, balance, whatsAppUrl }[]`. |
| GET | `/api/school/debtors.csv?...` | fees | Same filters. |
| GET | `/api/school/finance/summary?termId=` | fees | Expected / collected / outstanding / expenses. |
| GET | `/api/school/finance/collections?termId=&by=` | fees | `by` = class / item / method. |
| GET / PUT | `/api/school/settings/finance` | fees / leadership | Results hold and reminder cadence. |
| GET / POST | `/api/school/expenses` | fees | `?termId=&from=&to=&category=&skip=&take=`. GET/PUT/DELETE `/{id}`. |
| GET | `/api/school/expenses/categories` | fees | Known categories. |
| POST / DELETE | `/api/school/expenses/{id}/receipt` | fees | Receipt photo. |
| GET / PUT / DELETE | `/api/school/settings/paystack` | Owner | School's own secret key. Changing it still honours the previous key for 7 days. DELETE `?force=`. Keys starting `sk_test_schoollink` are local fixtures: accepted only with `Paystack:AllowLocalFixtureKeys` (Development), otherwise 400. |

## Staff — branding, announcements, messages

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/school/settings/profile` | leadership | `SchoolBrandingDto` `{ name, motto, address, logoUrl, hasSignature }`, for the school settings page. |
| PUT | `/api/school/settings/profile` | leadership | `{ name, motto, address }`. |
| PUT / DELETE | `/api/school/settings/logo` | leadership | Logo image. |
| PUT / DELETE | `/api/school/settings/signature` | leadership | Principal's signature (report cards). |
| GET / POST | `/api/school/announcements` | staff / leadership+bursar (teachers: their classes) | List `?skip=&take=`. POST `{ title, body, audience, levelIds, classIds, emailGuardians }`. DELETE `/{id}`. |
| POST | `/api/school/announcements/{id}/attachment` | same | multipart file. |
| GET | `/api/school/threads?mine=` | staff | Inbox. Leadership sees all; bursar the office; teachers their classes. |
| GET / POST | `/api/school/threads/{id}/messages` | same | Read / reply `{ body }`. |
| POST | `/api/school/threads/{id}/messages/image` | same | multipart `file` + optional `body`. |

Announcement `audience`: `School`, `Levels`, `Classes`, `Staff`. Staff announcements never appear in the parent or student portals.

## Imports (Excel and CSV)

Every list a school types on a screen can also be uploaded from a spreadsheet. There are three steps. First, download a template. Second,
preview the filled file: nothing is saved, and every row comes back with what it would do. Third, confirm the preview: every row is saved
in one transaction, or nothing is. One engine (`ImportEngine`) runs every kind. Each kind is an `IImporter` that names its columns, who
may use it, what each row would do, and how the rows are saved. Rows are always saved through the screen's own service method, so the
screen's rules, locks, messages and audit apply.

### Routes

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/school/imports/{kind}/template.xlsx` · `.csv` `?{scope}` | The template, named `schoollink-{kind}-template.xlsx` (or `.csv`). |
| POST | `/api/school/imports/{kind}/preview?{scope}` | multipart `file` → `{ token, kind, rows: [{ row, status, label, message }], counts: { new, update, unchanged, error, remove }, warnings: string[] }`. `remove` is the number of saved records the confirm would remove: the timetable's lessons missing from the file, 0 for every other kind. |
| POST | `/api/school/imports/{token}/confirm` | → `{ created, updated }`, plus `removed` for the timetable when it is above 0, plus `alreadyImported: true` on a repeat confirm (see below). |
| GET | `/api/school/students/import/template.xlsx` · `.csv` | Older student routes, kept with their own response shapes. They run the engine with the `students` kind. Leadership only. |
| POST | `/api/school/students/import/preview` | multipart `file` → `{ token, rows: [{ row, status, admissionNo, name, className, message }], newCount, updateCount, errorCount, fitsPlan, room }`. |
| POST | `/api/school/students/import/{token}/confirm` | → `{ created, updated }`. Any leader of the school may confirm a student preview. The generic confirm accepts only the user who uploaded the file. |

The scope is the whole query string, for example `?classSubjectId=…&termId=…`. It is held with the preview, so the confirm uses the
same scope. On the template and the preview, the access check runs first, before anything else is looked up. The confirm first resolves
the token (this school, this user on the generic route, the route's kind on the older student route, not expired), and only then runs the
access check with the held scope. A preview is held for **30 minutes** for the user who uploaded it. Another user's token, another
school's token, or an expired token gets **404** `import_expired`. When the file has no error rows, the confirm evaluates every row again
against the data as it is now. If any row is now an error, it saves nothing and returns **409** `import_errors` with `rows`. On the generic
route those rows have the preview row shape; on the older student route they have the older row shape.

**Confirming twice.** A confirmed batch is kept, with its result, until its 30 minutes are over. A repeat confirm of that token by the
same user (for example after a confirm whose response was lost on a slow connection) saves nothing and returns **200** with the first
confirm's result and `alreadyImported: true`, for example `{ created: 3, updated: 0, alreadyImported: true }`. The older student route
returns the first `{ created, updated }` again. Another user, or the same token after it expires, still gets the 404. The import dialog
shows "This file was already imported." above the result. Two confirms of one token at the same moment wait for each other: the second
returns the first one's result.

Every confirm writes an audit row (`imports.{kind}`, or `students.import` on the older route), including one where every row is
`Unchanged` and nothing changes. It also sends the user an in-app notification of type `school.import_finished`, for example "Score
import finished": "3 added and 2 updated from scores.xlsx." When a timetable import removes lessons, it says "… and N removed". A repeat
confirm (`alreadyImported`) writes neither again.

### Files

- `.xlsx` or `.csv`, at most **5 MB** and **2,000 rows**. Blank rows are skipped, and each row keeps its row number in the file.
- A workbook is read from the kind's own data sheet (the template's first sheet, matched in any case) when it has one. Otherwise it is
  read from the first sheet. The template also has a **How to fill in** sheet with the notes and a table of every column: required or
  not, and what to enter. In the `.xlsx` template every column is formatted as text, so values such as `007` keep their leading zeros.
- Cells are read as text. A formula is never run: it is read as its own text, for example `=SUM(A1:A3)`. A date cell reads as
  `yyyy-MM-dd`, and an Excel time cell reads as `HH:mm`.
- Headings are matched trimmed, in any case, with each run of spaces, tabs or hyphens read as one underscore. So `Mid term` is `mid_term`.
  A heading that matches no column of the kind gets a warning, "This column was ignored: …" or "These columns were ignored: a, b and c.".
  The rest of the file is still read.
- **Two columns with the same heading** (after that normalising) make the whole file a **400**: "The file has two columns called
  {heading}. Keep one and upload it again." This applies to every kind, including the older student route. That route used to keep only
  one of the two columns.
- **CSV apostrophe escaping.** The CSV templates put an apostrophe in front of any cell that starts with `=` `+` `-` `@`, a tab or a carriage
  return, so a spreadsheet never runs the cell as a formula. When the file is read back, exactly one apostrophe is taken off any CSV cell
  made of one or more apostrophes followed by one of those characters. So `'=x` reads as `=x`. A stored value that itself starts with
  apostrophes, such as `'=x`, is written with one more apostrophe (`''=x`), so the round trip is lossless. A **hand-made** CSV cell that
  starts with `''` before `= + - @` therefore loses one apostrophe: `''=x` is saved as `'=x`. An apostrophe before anything else is
  plain text: `'abc` reads as `'abc`, from CSV and from `.xlsx`. CSV **headings** get the same escape, since some are names the school
  typed (an assessment component, a subject, a trait item or an area): a component named `=CA1` is headed `'=CA1`, and the reader takes the
  apostrophe off a heading by the same rule, so the column still reads as `=CA1`. Plain headings such as `admission_no` are written as they are.
- A blank cell in a roster template keeps what is saved. To clear a value, use the screen.

### Statuses and warnings

| Status | Meaning |
|---|---|
| `New` | The row adds something, or (on a roster) fills only values that were blank. |
| `Update` | The row changes something already saved. |
| `Unchanged` | The row matches what is saved. It is not sent to the save. |
| `Error` | `message` says why. While any row is an error, the confirm saves nothing (409 `import_errors`). |

`warnings` are notes that don't block the confirm. Examples are the ignored columns, the plan's room for students, a teacher or
invigilator clash, the timetable lessons that will be removed, attendance days that will be marked present, and the parents who get the
absence message. A row that is not an error can still carry a `message`. On `timetable` and `exam-sittings` it is that row's clash
note, which is repeated in `warnings` as "Row {n}: …". On a `fee-items` update it says what changes, for example "Becomes optional.".

### Kinds

**Template types.** A **list** template is empty: one row per new or matched record. A **roster** template is prefilled from what is
saved: one row per student (or per fee item, or per lesson), with the saved values in the cells, a `sheet_for` column and (except
`attendance`) a hidden `row_version` column. Both are required on every roster kind; the table below lists only the other required columns.

| Kind | Scope | Who | Template | Columns (**required**, optional) |
|---|---|---|---|---|
| `students` | — | leadership | list | **admission_no, first_name, last_name, gender, class**, middle_name, date_of_birth, guardian1_name, guardian1_phone, guardian1_email, guardian1_relationship, guardian2_name, guardian2_phone, guardian2_email, guardian2_relationship. A known admission number updates that student. New students must fit the plan. |
| `staff` | — | leadership | list | **email, role** (Principal, Bursar or Teacher), job_title, staff_no, phone. A team member gets their profile updated (a blank cell keeps the saved value). Their role is never changed (warning). Anyone else is invited, within the plan's team seats. Invitation emails go out only after the import commits. The proprietor can't be imported. |
| `classes` | — | leadership | list | **level, arm**, class_teacher (staff email). Adds to the current session. A level and arm the session already has changes that class's class teacher. |
| `subjects` | — | leadership | list | **name, code**. A code the school already has renames that subject. A new code adds a subject. |
| `class-subjects` | — | leadership | list | **class, subject** (name or code), teacher (staff email). A pair the school already has changes its teacher. |
| `scores` | `classSubjectId`, `termId` | leadership; a teacher for the class subjects they teach | roster | **admission_no, sheet_for**, student, plus one column per assessment component, headed by its name (for example `CA1`, `Exam`). A number up to the component's maximum, with at most one decimal place. |
| `traits` | `classId`, `termId`, `kind=Psychomotor\|Affective` | the class teacher or leadership | roster | **admission_no, sheet_for**, student, plus one column per item on the class's list, each a whole number from 1 to 5. |
| `developmental` | `classId`, `termId` | the class teacher or leadership | roster | **admission_no, sheet_for**, student, plus `{area}` and `{area} comment` for each area. A rating is a scale label or its code, in any case. A comment needs a rating, in the file or already saved. |
| `remarks` | `classId`, `termId` | leadership, the class teacher, the class's subject teachers | roster | **admission_no, sheet_for**, student, one column per subject the caller sees, `class_teacher_remark` (class teacher or leadership), and `principal_remark` (leadership only; a filled cell in anyone else's file is an error row). |
| `attendance` | `classId`, `from`, `to` (at most 31 days, none after today) | leadership; teachers of the class, inside the register's own date window | roster | **admission_no, sheet_for**, student, plus one column per date headed `yyyy-MM-dd` (`26/10/2026` or an Excel date heading also works), each P, A, L or E in any case. The template ends with a `register_version` row. |
| `assignment-marks` | `assignmentId` | the subject teacher or leadership | roster | **admission_no, sheet_for**, student, feedback, and `mark` on a numeric class or `received` (yes or no) on a developmental class. |
| `timetable` | `classId` | leadership | roster (one row per lesson) | **day, period, start, end, subject, sheet_for**. Day is Monday–Saturday (or Mon–Sat), period is 1–12, times are `HH:mm`, and subject is the name or code of a subject taught in the class. |
| `exam-sittings` | `periodId` | leadership | list | **class, subject, date, start, end, venue, instructions**, invigilator (staff email). Checked by the add-sitting screen's checks, against saved sittings and the rows above it. |
| `questions` | — | staff except the bursar; a teacher only for the subjects they teach | list | **subject, question, option_a, option_b, correct** (A–H), **marks**, level, option_c … option_h. |
| `word-lists` | — | staff except the bursar | list | **name, words** (commas only), level, class and subject (both or neither). A teacher may target only the class subjects they teach. |
| `teasers` | — | staff except the bursar | list | **prompt, answer**, level. |
| `fee-items` | — | fees | list | **name**, optional (yes or no). A name the school already has updates its optional flag. Nothing is renamed. |
| `fee-structure` | `termId`, `levelId` | fees | roster (one row per fee item) | **fee_item, amount, sheet_for**. Amounts are in naira. A blank amount keeps the saved one. Zero is refused. |
| `payments` | optional `termId` | fees | list | **amount, method** (Cash, Transfer or Pos), **paid_on**, invoice_no, admission_no (enough on its own when the student has one open invoice, of that term when `termId` is given), reference. |
| `expenses` | — | fees | list | **date, category, amount**, note. |

Money cells are in naira: digits, an optional `₦`, thousands separators in groups of three, and up to two decimal places, for example
`12500` or `12,500.50`. `0,500` is refused. Dates may be `2026-10-26`, `26/10/2026` or an Excel date. Times may be `09:00`, `9:00`,
`09:00:00` or an Excel time.

Locks and freezes are checked on the preview and again on the confirm:

- `scores` and `developmental` on a locked term are **409** `term_locked`.
- `assignment-marks` is refused the same way when a counted mark would change.
- `remarks` on published results are **409** `results_published`.
- `scores` on a developmental class, and `developmental` on a numeric class, are **400**.

### `sheet_for`

Every roster template has a `sheet_for` column. It is filled in on every row and names the scope the file was made for, starting with the
session, joined by ` / ` (plain ASCII, so it survives CSV and an Excel re-save):

| Kind | `sheet_for` |
|---|---|
| `scores` | `2026/2027 / JSS 1A / Mathematics / First term` |
| `traits` | `2026/2027 / JSS 1A / Psychomotor / First term` |
| `developmental` | `2026/2027 / Nursery 1A / First term` |
| `remarks` | `2026/2027 / JSS 1A / Comments and remarks / First term` |
| `attendance` | `2026/2027 / JSS 1A / 2026-10-19 to 2026-10-23` |
| `assignment-marks` | `2026/2027 / JSS 1A / Mathematics / First term / Fractions homework / due 2026-10-30 / #1a2b3c4d`: the due date (Lagos) and the **last** 8 hex digits of the assignment id, so two assignments with the same title don't match |
| `timetable` | `2026/2027 / JSS 1A` |
| `fee-structure` | `2026/2027 / JSS 1 / First term` |

The value is compared ignoring case and extra spaces. A row whose `sheet_for` names another scope is an error: "This sheet was made for
{value}. Download the template for {this class and subject \| this class and term \| this class and these dates \| this assignment \|
this class \| this level and term}." A blank cell gives "Keep the sheet_for cell as the template filled it. Download the template for …".
Because the session comes first, last session's template can't pass for this session's, even for a student who repeats with the same
class, subject and term names.

### `row_version`

Every roster template except `attendance` (which has its `register_version` row instead) ends with a **required, hidden** `row_version`
column: hidden in the `.xlsx` template, the last column of the `.csv`. Each row's cell is a short hash (`v` and 12 hex digits) of the saved
values that row can change, taken when the template was made:

| Kind | What the version covers |
|---|---|
| `scores` | the student's score for each component column |
| `traits` | the student's rating for each item column |
| `developmental` | the child's rating and comment for each area |
| `remarks` | the comments of the subjects the caller writes, then the class teacher's and the principal's remark when the caller writes them |
| `assignment-marks` | the mark (or received, on a developmental class) and the feedback |
| `fee-structure` | the item's amount for the level and term |
| `timetable` | the lesson at that day and period: start, end and subject (one version per lesson) |

A row with nothing saved has a blank version. On the preview, and again on the confirm, a row that would change something (`New` or
`Update`) is an error when the saved values no longer match its `row_version`: "Someone changed this row after you downloaded the
template. Download it again." So a template downloaded on Monday can't put back a score the principal corrected on Tuesday. A change on
the screen between the preview and the confirm makes the confirm the usual **409** `import_errors`, and nothing is saved. A row that
changes nothing (`Unchanged`, including blank cells) is never refused, and a template uploaded as it was downloaded is all `Unchanged`, from
`.xlsx` and from `.csv`. On the timetable, a row for a new lesson (no lesson saved at that day and period) leaves `row_version` blank, and
a saved lesson left out of the file is not checked: it is removed as before. A file without the `row_version` column is the
missing-column 400: "The file is missing the column row_version. Download the template to see every column."

### Behaviour worth knowing

- **Uploads never delete, except the timetable.** A blank cell keeps what is saved. Imports never remove a fee item from a level,
  never rename or delete a fee item, never change a team member's role, and never remove a subject, class, student or rating.
- **Timetable replaces the week.** The file is the class's whole week. On confirm it replaces the timetable through the timetable
  screen's save, so a saved lesson that is missing from the file is removed. The preview warns: "This lesson is not in the file and will
  be removed: …" (or "These lessons …"), and `counts.remove` gives their number, so the dialog's confirm is enabled for a file that only
  removes lessons. A lesson whose day and period has an error row is not counted as removed. The confirm returns `removed` and the notice
  says "… and N removed". A template uploaded unchanged is a no-op: nothing is saved, and the result is
  `{ created: 0, updated: 0 }`. The same day and period twice is an error on both rows. A teacher clash is a warning, worded as on the
  screen.
- **Attendance.** Each date with a change goes through the register's own save. Absences on **today's** date message the parents, as on
  the register (the preview warns). **Earlier dates send no message.** On a day nobody has taken the register yet, students left blank are
  marked present, as on the register (the preview warns and names the weekday). The last template row, `register_version`, holds the
  version of each day's register the file was made from. If a day's register was saved after the file was downloaded, that day's marks
  are refused: on the preview each affected row is an error, "{day}: Someone else updated this register. Open it to review your marks.
  Download the template again." A save that lands between the preview and the confirm makes the confirm a **409**, and the whole import
  rolls back. So a stale file never lays old letters over newer marks. A file without the row is a 400.
- **Uploading the same file twice creates list records twice.** A token saves only once: confirming it again returns the first result
  with `alreadyImported: true` (see Routes). But uploading the same file again makes a new preview, and confirming that adds every row again for `word-lists`, `teasers`, `questions` (the bank is not checked for repeats, only the file)
  and `expenses`. `payments` rows without a `reference` (typically cash) are recorded twice. A reference that is already used refuses
  the row. Roster kinds are safe to re-upload: matching values are `Unchanged`. `exam-sittings` clash with the sittings already saved.
- **Payments hold the fee lock.** A payments confirm takes the school's fee lock (the same lock the payments screen, invoice generation,
  reversals and Paystack confirms take) for the whole confirm. It re-checks references and balances under the lock, then records every
  row. Screen payments and Paystack confirms for that school wait until it finishes. Each row gets its own receipt, the receipt email and
  the parents' notice, queued in the import's transaction, so nothing goes out unless the import commits.
- **Staff invitations** are queued in the outbox in the import's transaction, and are sent only after the import commits.

### Errors

The `detail` (whole-file) or row `message` sentences the import code sends. `{…}` is filled in.

| Case | Status | Sentence |
|---|---|---|
| Unknown kind | 404 | Import was not found. |
| Scope value missing (for example `classSubjectId`) | 400 | Choose the {class subject} before uploading the file. |
| No file | 400 | Choose a file to upload. |
| Over 5 MB | 400 | This file is larger than 5 MB. Split it into smaller files and upload them one at a time. |
| Not `.xlsx` or `.csv` | 400 | Upload an .xlsx or .csv file. Download the template to start. |
| Workbook that inflates past 50 MB | 400 | This file is too large to import. Split it into files of up to 2,000 rows. |
| Unreadable file | 400 | We couldn't read this file. Upload the .xlsx or .csv template with your rows filled in. |
| No rows | 400 | The file has no rows. Fill in the template and upload it again. |
| Over 2,000 rows | 400 | The file has more than 2,000 rows. Import at most 2,000 rows at a time and upload the rest in another file. |
| Required column missing | 400 | The file is missing the column {x}. Download the template to see every column. (Or: "the columns a, b and c".) A roster file with none of its scope columns (components, items, areas, subjects, dates) is the same 400. |
| Two columns with one heading | 400 | The file has two columns called {heading}. Keep one and upload it again. |
| Confirm with error rows | 409 `import_errors` (`rows`) | Some rows have errors. Fix them in the file and upload it again; nothing was imported. |
| Expired or another user's token | 404 `import_expired` | This import has expired. Upload the file again. (A token already confirmed by the same user returns 200 with its result and `alreadyImported: true`.) |
| Roster: stored values changed since the download (`row_version`) | row; 409 `import_errors` on confirm | Someone changed this row after you downloaded the template. Download it again. |
| Roster: no `row_version` column | 400 | The file is missing the column row_version. Download the template to see every column. |
| Roster: admission number blank / not in the class / twice | row | Enter the admission number. · This student is not in this class. · This student appears more than once in the file. |
| Roster: wrong or blank `sheet_for` | row | This sheet was made for {value}. Download the template for {…}. · Keep the sheet_for cell as the template filled it. Download the template for {…}. |
| Students: teacher or bursar | 403 | Only the proprietor or principal can import students. |
| Students: over the plan | warning; 409 `import_over_plan` on confirm | Your plan has room for {n} more students and this file adds {m}. Upgrade your plan or remove rows, then upload the file again. |
| Students: row | row | `Row {n}: ` followed by each problem as a sentence, for example "Enter the gender as M or F.", "Class '{x}' doesn't exist.", "The date of birth must look like 2012-03-04 or 04/03/2012.", "Admission number {x} is on rows 2 and 5. Each student needs their own admission number.", "{x} is not an active student, so they can't move class. Set them to active first." |
| Setup kinds (staff, classes, subjects, class-subjects): not leadership | 403 | Only the proprietor or principal can import {staff \| classes \| subjects \| class subjects}. |
| Staff email not on the team (classes, class-subjects) | row | No active staff member of this school has this email. |
| Classes | row | Choose a level. · This level was not found. · This class appears more than once in the file. · {name} already exists in this session. |
| Subjects | row | This code appears more than once in the file. · This subject name appears more than once in the file. · A subject with that name already exists. |
| Class subjects | row | Enter the class. · This class was not found. · Enter the subject. · This subject was not found. · This class and subject appear more than once in the file. |
| Staff | row | Enter a valid email address. · Enter the role: Principal, Bursar or Teacher. · The proprietor can't be added from a file. Use Principal, Bursar or Teacher. · This email appears more than once in the file. · This staff number appears more than once in the file. · This person already has an invitation. Resend it instead. · (seats) the team screen's plan-limit sentence |
| Staff warnings | warning | Job title, staff number and phone are saved only for people already on the team. Set them on the staff page after they accept the invitation. · The roles of people already on the team are not changed by an import. Change a role on the staff page. |
| Scores: bursar | 403 | You can only enter scores for subjects you teach. |
| Scores: another teacher's class subject | 404 | Class subject was not found. |
| Scores | row | Enter a number for {component}, such as 12 or 12.5. · {component} scores must be between 0 and {max}. · Scores can have at most one decimal place. |
| Traits / developmental / remarks: not the class's teacher | 404 | Class was not found. |
| Traits: bursar / bad kind / value | 403 / 400 / row | The bursar does not rate traits. · Choose Psychomotor or Affective. · Rate each item from 1 to 5. |
| Developmental: bursar / value | 403 / row | The bursar does not rate developmental areas. · {area} must be one of {labels}. · Choose a rating for {area} before the comment. · The {area} comment can have at most 500 characters. |
| Traits / developmental: term of another session | 400 | Choose a term of this class's session. |
| Remarks: bursar | 403 | The bursar does not read results. |
| Remarks: published | 409 `results_published` | Unpublish the results before changing remarks. |
| Remarks | row | Only the principal writes the principal's remark. · Only the class teacher writes the class teacher's remark. · The subject teacher writes the {subject} comment. · This student has no {subject} result yet. Enter their scores first. · The {subject} comment can have at most {max} characters. · The class teacher's remark can have at most {max} characters. · The principal's remark can have at most {max} characters. |
| Attendance: bursar | 403 | Attendance is for the school's leadership and teachers. |
| Attendance: not a teacher of the class (or no such class) | 404 | Class was not found. |
| Attendance: scope | 400 | Choose a start date on or before the end date. · Choose today or an earlier date. · Choose at most 31 days. |
| Attendance: date window (the register's own rule, checked for the first and the last date) | 409 `register_locked` · 409 `no_current_term` | You can't change registers from that date. (a date before the current term's start or after its end; for a teacher, also a date more than 7 days before today) · Set the current term first. |
| Attendance: file | 400 | Download the template again; this file is missing its register_version row. · The file has more than one register_version row. Download the template again. · The file has a column for {day}, which is outside the dates you chose ({from} to {to}). Choose those dates or remove the column. · The file has two columns for {day}. Keep one and upload it again. · The file has no column for any date from {from} to {to}. Download the template for these dates. |
| Attendance | row | Mark {day} with P, A, L or E (present, absent, late or excused). · {day}: Someone else updated this register. Open it to review your marks. Download the template again. |
| Attendance warnings | warning | No register was taken on {weekday day month year}. {n} students left blank that day will be marked present, as on the register. · Parents of students marked absent today get the absence message, as on the register. Earlier dates send no message. |
| Assignment marks: bursar / not yours | 403 / 404 | The bursar does not set assignments. · This assignment was not found. |
| Assignment marks | row | Enter a mark such as 8 or 8.5. · Enter yes or no for received. · The student sent this work, so it stays received. · Use {max} characters or fewer for the feedback. · the marking screen's mark sentence |
| Timetable: not leadership | 403 | Only the principal can change the timetable. |
| Timetable | row | The class already has a lesson at this day and period. Choose another period. · the timetable screen's slot sentences (for example "Choose a subject taught in this class.") |
| Exam sittings: not leadership / unknown period | 403 / 404 | Only the principal can set the exam timetable. · This timetable was not found. |
| Exam sittings | row | Enter the class. · There is no class called {x} this session. · Enter the subject. · {x} is not a subject of {class}. · Choose the date of the exam. · Enter the date as 2026-11-16 or 16/11/2026. · Choose an active staff member from this school. · the add-sitting screen's sentences (for example "This sitting overlaps another exam for the class."); an invigilator clash is a warning |
| Questions: bursar | 403 | The bursar does not set tests. |
| Questions | row | Enter the subject. · This subject was not found. (also a subject the teacher doesn't teach) · This level was not found. · Fill in the options from option_a with no gaps. · Enter the letter of the correct option, from A to H. · There is no option {X} for this question. · This question appears more than once in the file. · the question screen's sentences |
| Word lists / teasers: bursar | 403 | The bursar does not set Play activities. |
| Word lists | row | Enter the subject as well as the class. · Enter the class as well as the subject. · This class was not found. · This subject was not found. · This class subject was not found. (a real subject the class doesn't take, or one the teacher doesn't teach) · This level was not found. · the Play screen's sentences (for example "Use the letters A to Z only, with no spaces.") |
| Teasers | row | This level was not found. · the Play screen's sentences |
| Fee kinds: a teacher | 403 | Only the proprietor, principal and bursar manage fees. |
| Money cells | row | Enter the amount in naira, such as 12500 or 12,500.50. · Enter the amount with at most two decimal places, for example 12500.50. · Enter an amount above zero. |
| Fee items | row | Enter a name for the fee item. · Use 80 characters or fewer for the name. · Enter yes or no for optional. · This fee item appears more than once in the file. |
| Fee structure | row | Enter the fee item. · This fee item was not found. Add it on the fee items page first. · This fee item appears more than once in the file. · Each amount must be more than zero. |
| Payments | row | Enter the invoice number or the admission number. · This invoice was not found. · This invoice is cancelled, so it can't take payments. · This invoice is for another student. Check the invoice and admission numbers. · This student was not found. · This student has no open invoice[ for {term}]. Enter the invoice number. · This student has more than one open invoice[ for {term}]. Enter the invoice number. · Choose Cash, Transfer or Pos. · Enter the date the money was received. · Enter the date as 2026-10-26 or 26/10/2026. · The payment date can't be in the future. · Use 100 characters or fewer for the reference. · This reference has already been used for a payment. · This reference is used on an earlier row of the file. · That is more than the balance of {₦x}. Enter that amount or less. |
| Expenses | row | Enter the date the money was spent. · Enter the date as 2026-10-26 or 26/10/2026. · The date can't be in the future. · Enter a category, such as Transport. · Use 60 characters or fewer for the category. · That amount is too large. Check it and try again. · Use 300 characters or fewer for the note. |

## Errors (academic, exams, CBT, Play)

The `detail` sentences the API sends. Where the code's sentence differs from the design spec's table, this is what the code returns.

| Case | Status | `detail` |
|---|---|---|
| Locked term: score sheet, developmental ratings, counted assignment mark, or counted CBT start/submit | 409 `term_locked` | This term is locked for the class, so scores can't change. Ask the principal to unlock it. |
| CBT paper unpublished, outside its window, another class, or unknown; another student's attempt | 404 | This test was not found. |
| CBT answer after `deadlineAt` (the answer is not saved) | 400 | Time is up. Your answers were saved. |
| CBT started twice without a reopen | 409 | You have already started this test. |
| CBT answer or submit after handing in | 409 | This test has already been handed in. |
| CBT answer or submit on a reopened attempt | 409 | This sitting was reopened. Start the test again. |
| CBT draw larger than the bank | 400 | There are not enough questions in the bank for this test. |
| Bursar on the question bank or CBT | 403 | The bursar does not set tests. |
| Class sitting overlap | 409 | This sitting overlaps another exam for the class. |
| Venue overlap | 409 | This venue is already booked at that time. |
| Invigilator already booked (saved, not refused) | 200 warning | This teacher is already invigilating another exam at that time. |
| Unpublished or unknown exam period or sitting (also editing or removing another school's sitting) | 404 | This timetable was not found. |
| Archiving an unknown question, or one of a subject the teacher does not teach | 404 | This question was not found. |
| Removing a class subject that has assignments, CBT papers, lesson plans or exam sittings | 409 `class_subject_has_work` | This subject has homework, tests, lesson plans or exam sittings. Remove those first. |
| Numeric score or CBT paper on a developmental class | 400 | This class uses developmental assessment. |
| Developmental rating on a numeric class | 400 | This class uses numeric assessment. |
| Trait outside 1–5 | 400 | Rate each item from 1 to 5. |
| Results hold (existing) | 403 | Results are held. Please see the school office. |
| Play turned off | 404 `play_off` | Play is turned off for this school. |
| Assignment for another class, or unknown | 404 | This assignment was not found. |
| Lesson plan draft that is not yours, or unknown | 404 | This lesson plan was not found. |

## Portal DTOs (family)

```
ChildRef { studentId, tenantId, schoolName, schoolSlug, name, className }
PortalOverviewDto { child, results: PortalTermDto[], attendance, fees, timetable }
PortalTermDto { termId, termName, sessionName, isCurrent, published, held, classes: [{ classId, className, total, average, position, classSize }] }
PortalResultsDto { termId, termName, sessionName, published, entries: [{ classId, className, total, average, position, classSize, daysPresent, daysLate, daysAbsent, daysExcused, daysOpen, teacherRemark, principalRemark, subjects, mode }] }
  (daysLate, daysAbsent and daysExcused are null on rows published before they were counted)
PortalAttendanceDto { termId, termName, classId, className, present, absent, late, excused, open }
PortalExamTimetableDto { periods: [{ id, name, kind, start, end, sittings: [{ id, subject, subjectCode, date, start, end, durationMinutes, venue, instructions, invigilator }] }] }
StudentAssignmentDto { id, title, instructions, dueAt, maxMarks, submitted, late, body, mark, feedback, fileName, fileUrl }
CbtStartDto { attemptId, deadlineAt, items: [{ id, stem, options }] }
WordSearchDto { grid: string[][], words: string[] }
PlayTeaserDto { id, prompt }
ParentInvoiceDto { id, number, termId, termName, total, paid, balance, status, dueDate, canPayOnline, nextInstalmentMinor, minimumPartPaymentMinor, lines, instalments, payments }
PaymentStatusDto { id, reference, studentId, invoiceId, amountMinor, status, receiptNumber, invoiceBalance }
```

`fees` is null for students. `attendance` and `timetable` are null without a class this term. Invoice `total` / `paid` / `balance`
are kobo (they are not named `*Minor`).
