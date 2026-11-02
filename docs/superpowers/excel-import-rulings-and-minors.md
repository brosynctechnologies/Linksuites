# SchoolLink Excel/CSV import: rulings and deferred minors

Taken from the build ledger on 23 Oct 2026. Final state:

- 20 import kinds
- API suite 875/875
- school-web tsc and lint clean
- final snapshot 4fc33a0
- everything uncommitted

## Rulings (in order)

- Ruling: no worktree and no project commits; snapshots go to a private shadow repo at `.superpowers/sdd/2026-10-18-school-excel-import/shadow.git` (helper `snap.sh`, baseline 302c956 = the finished next-release tree). Why: the user asked for no commits. Cost if wrong: the user can still commit the working tree whenever they like.
- Ruling: implementers never run git in the project; they snapshot with `snap.sh commit` (no colon in the subject; Git Bash truncated one before). Cost if wrong: none.
- Ruling: all subagents run on the session model (`inherit`). Why: grok-4.7-high-fast and claude-opus-5-5-medium hit the account usage limit earlier today. Cost if wrong: reviews are one tier below "most capable".
- Ruling: work in this checkout; the dev API on 5080 belongs to this checkout and implementers may stop that one process to build. Cost if wrong: localhost:4200 loses its backend until the controller restarts it.
- | 8 vs Global | "uploads add or update only; never delete" vs timetable "replaces the class's week" | Conflict. Ruling below. |
- | 5 self | spec "principal column absent for others" vs plan "filled extra column is an error row" | Not a conflict: template omits it; a hand-added filled column is an error row. Ruling below. |
- Rulings from scan:
- Ruling: timetable import replaces the class's week (spec importer 13 says so; it mirrors the timetable screen save, which replaces the week). This is the one exception to "uploads never delete". The template is prefilled with the current slots so an unchanged upload is a no-op. Cost if wrong: a timetable file missing a slot removes that slot.
- Ruling: remarks import — teachers' templates omit `principal_remark`; if a teacher's file has a filled `principal_remark` column anyway, that row is an error `Only the principal writes the principal's remark.` Cost if wrong: none.
- Task 1: Ruling: `Duplicate_student_rows_are_errors` tests the pure `RosterRows` helper directly (no test-only importer in DI). Cost if wrong: the fact is unit-level until Task 3 exercises it through scores.
- Task 1: Ruling: `Generic_and_old_student_routes_agree` compares row numbers, statuses and counts across the two shapes (old `ImportPreviewDto` stays byte-compatible; generic uses `ImportPreviewResult`). Student `New`/`Update`/`Error` map 1:1. Cost if wrong: none.
- Task 1: Ruling: the existing student import test class is `SchoolImportTests`; it must stay green unchanged.
- Task 1: Ruling: services that always open their own transaction (ScoreService.SaveAsync, ResultService, ExamTimetableService, CbtService, others) must join an ambient transaction when one is open, the way ScoreService.UpsertAsync already does. Each importer's task makes that change for the services it calls, with the screen's behaviour unchanged. Cost if wrong: an importer fails on confirm with a nested-transaction error.
- Task 1: Ruling: the legacy `/students/import/{token}/confirm` keeps "any leader of the school may confirm" (an existing test pins it); only the generic confirm binds to the creating user. Cost if wrong: a second leader can confirm a student batch through the old route.
- Task 1: Ruling: generic confirm resolves the token before EnsureAccessAsync (kind and scope live on the batch); made-up tokens are 404 for every role (`YYSS` in the role table). Generic file messages say "rows". Cost if wrong: none.
- Task 2: Ruling: also fix two carried Task 1 minors here because every later importer depends on them: (a) CSV template cells go through `Saas.Application.Platform.Csv.Cell` (formula-safe) like the broadsheet; (b) the reader prefers the importer's `ImportTemplate.SheetName` (falls back to the first sheet), not "Students" for every kind; student import still reads its "Students" sheet. Cost if wrong: a prefilled name could run as a formula in Excel; a questions workbook with a second sheet named Students would read the wrong sheet.
- Task 2: Ruling: question validation is shared with `CbtService` create (extract an internal core method; no duplicated rules). Subject column matches name or code, case-insensitive. Level matches level name ignoring spaces/case. Cost if wrong: none.
- Task 2: Ruling: `ImportDialog` is the one web component for every import; the students import page keeps its page but uses the dialog's preview table component. Cost if wrong: two preview UIs drift.
- Task 2: Ruling (carry to Task 3): CSV reader strips one leading apostrophe when it precedes = + - @ (the Csv.Cell escape), so a roster CSV round trip stays Unchanged. Cost if wrong: a name genuinely starting with '= loses its apostrophe.
- Task 2: Ruling (carry to Task 3): engine must not build the roster template just to learn the sheet name; take SheetName from importer metadata without TemplateAsync, and run the missing-column check before scope-dependent work. Cost if wrong: a slower preview.
- Task 3: Ruling: roster templates carry a prefilled `sheet_for` column naming the scope (e.g. "JSS 1A · Mathematics · First term"); preview errors on a row whose sheet_for does not match the upload scope ("This sheet was made for {x}. Download the template for this class and subject.") and the column is required. Why: a Mathematics template uploaded under English would otherwise write Maths scores into English. Applies to every roster importer (Tasks 3–11). Cost if wrong: one extra column in roster templates.
- Task 3: Ruling: import returns 404 for a teacher of another subject (Global Constraints) although the screen returns 403 — accepted.
- Task 3: Ruling: sheet_for separator is " / " (ASCII) so it survives CSV and Excel re-save. Cost if wrong: none.
- Task 3: Ruling (carry to Task 4): the sheet_for label includes the session, e.g. "2026/2027 / JSS 1A / Mathematics / First term", so last year's template can't pass for a repeater. Change the shared SheetFor helper and the score template in Task 4. Cost if wrong: a longer label.
- Task 4: Ruling (carry to Task 5): developmental scale codes are accepted as well as labels — document it in the template's "How to fill in" notes and add a test; add a test for the 500-character comment limit. Cost if wrong: none.
- Ruling (22 Oct, user asked to run the remaining tasks simultaneously): Tasks 6–10 run in parallel, each in its own shadow worktree (../6saas-wt/t6..t10, branches task6..task10, from 2a53034), rules in parallel-rules.md; Task 5 continues in the main tree. Controller merges branches into the main tree one at a time, runs the full suite on the merged tree, and reviews each task's diff; Task 11 (docs + click-through) runs after. Cost if wrong: merge conflicts in DependencyInjection.cs and shared pages, resolved by the controller.
- Task 5: Ruling: a hand-made CSV cell starting with '' before = + - @ reads with one apostrophe removed in every import kind (the template escape rule); document in Task 11 docs. Cost if wrong: a cell that genuinely starts with ''= loses one apostrophe.
- Task 5: Ruling (carry to Task 11): add tests for 'abc (xlsx + hand CSV) and ''=x; use ResultService.PublishedMessage for the four literals. Cost if wrong: none.
- Ruling: worktree branches carry CRLF line endings (system core.autocrlf=true on checkout); merges use `git diff --ignore-cr-at-eol` + CR-stripped patch applied 3-way to the shadow index. Cost if wrong: none (content identical).
- Task 9: Ruling (carry to Task 11): word-list template note "commas only"; docs line that confirming a file twice creates everything twice; a test row for a real subject the class doesn't take. Cost if wrong: none.
- Ruling: fix rounds for parallel tasks run in their own worktree and are merged as a delta (never build in the main tree while its suite runs). Cost if wrong: none.
- Task 6: Ruling: attendance templates carry each date's register version in a prefilled `register_version` row; a changed day is an error on preview and 409 on confirm ("Download the template again."). Why: teachers save registers from phones all day; last-confirm-wins would silently revert them. Cost if wrong: one extra row in the template.
- Task 6: Ruling: two file columns that normalise to the same heading are a whole-file 400 for every kind ("The file has two columns called {heading}. Keep one and upload it again."). Cost if wrong: none.
- Task 8: Ruling: assignment-marks sheet_for adds "due {date} / #{first 8 of id}". Cost if wrong: a longer label.
- Task 10: Ruling (carry to Task 11 docs): payments import holds the school's fee lock for the whole confirm, so screen payments and Paystack confirms for that school wait until it finishes; re-uploading cash rows records them twice. Cost if wrong: none.
- Task 6: Ruling (carry to Task 11 docs): a hand-made legacy student file with two same-named columns now gets 400 instead of silently dropping one. Cost if wrong: none.
- Task 8: Ruling: assignment sheet_for short id uses the LAST 8 hex of the id (UUIDv7's first 8 are a timestamp shared by assignments made within a minute). Cost if wrong: none.
- Final: Ruling (I1): every roster template carries a hidden per-row `row_version` column (a hash of that row's stored editable values when the template was made); preview/confirm refuse a row that would change something when its stored values no longer match — "Someone changed this row after you downloaded the template. Download it again." Missing column → missing-column 400. Why: same failure Task 6 ruled Important for attendance; one generic helper covers all roster kinds. Cost if wrong: one more hidden column.
- Final: Ruling (I2): confirmed batches are kept (status Confirmed + result) for their 30-minute life; a repeat confirm by the same user returns the original result with alreadyImported=true instead of 404 "expired"; the dialog says "This file was already imported." Cost if wrong: none.
- Final: Ruling (M-4): any staff role reaching the 50 MB inflated-size cap is accepted — the limits are unchanged and access follows each screen. Cost if wrong: a teacher can submit large files.

## Deferred minors

- Task 1: minor (deferred): old student route wording drift (multi-column missing message; audit gains `kind`).
- Task 1: minor (carried to Task 2): CSV templates do not neutralise cells starting with = + - @ (prefilled roster names). Must use Csv.Cell for CSV template cells.
- Task 1: minor (carried to Task 2): engine reuses student-named limits; reader prefers a sheet called Students for every kind; an unused field.
- Task 1: minor (deferred): scope message says "before uploading" on template downloads.
- Task 1: minor (advisory for Tasks 3–10): notices queued before commit; importers must not let services send messages inline before the transaction commits.
- Task 1: minor (deferred): roster prefilled rows and stored scope untested (Task 3 covers).
- Task 2: review → Needs fixes: Important ×2 (non-numeric level accepted; "1,5" marks parsed as 15). Fix round 1 dispatched with minors 4, 5, 7 and a code-match test.
- Task 2: minors (deferred): first-error order differs from screen on multi-problem rows; subject name==other code ambiguity; dialog close mid-upload stale state; both template buttons spin; "updated 0 rows" wording; MarksRule hard-codes 1000; bursar test checks status only; 409 rows via type cast; ~3 round trips per row.
- Task 3: review → Needs fixes: Important ×2 (sheet_for not implemented; typed edits lost on grid reload after import). Fix round 1 dispatched with minors 1, 4 (ignored-column warning in engine), 8, 9.
- Task 3: minors (deferred): lock test can't tell which check fired; no withdrawn-between-preview-and-confirm test; no chunk-rollback / Stale test; formula or 1E-05 cell message; stale template last-write-wins; SaveAsync grid rebuild per chunk wasted.
- Task 3: minors (deferred): ignored-column warning shows lower-case keys; preview loads the score sheet twice; SpreadsheetReader rewritten with BOM/line-ending churn; `void start()` unhandled if pre-open ever throws.
- Task 4: minors (deferred): colliding item keys dropped from template silently; per-student saves repeat class-wide work; label matching collapses space runs only.
- Task 5: minors (deferred): class teacher's out-of-subject cell on a no-result student shows the no-result message; teacher uploading owner's template errors on unchanged principal remarks; re-key discards typing on an unrelated reload; preview loads results twice.
- Task 9: minors (deferred): first-error order vs screen; space-only class-name collision; round trips per row.
- Task 7: review → Needs fixes: Important ×1 (no test pins "role never changes"). Fix round 1 dispatched in worktree t7 with minors 1–3 and the "Choose a level." wording.
- Task 7: minors (deferred): concurrent confirms can take one seat too many (team screen has the same race); file-wide profile-ignored warning; queries per invite row.
- Task 10: review → Needs fixes: Important ×2 (payment references re-checked outside the fee lock → concurrent confirms can double-record; missing preview→change→confirm, partial-failure rollback and numeric-xlsx tests). Fix round 1 dispatched in worktree t10 with minors (button label, dialog names term, 0,500, float-artefact message).
- Task 10: minors (deferred): N/NGN prefixes refused; case-variant admission numbers First(); re-upload records cash rows twice (docs); structure import vs screen save race (same as today); teacher-403-before-bad-scope test; feed-notice assertion.
- Task 6: minors (deferred): today's WhatsApp "Message parents" links not offered after an import; tests rely on the real clock where helpers don't allow a fixed date.
- Task 8: review → Needs fixes: Important ×1 (assignment-marks sheet_for can't distinguish same-titled assignments). Fix round 1 dispatched in worktree t8 with minors 1, 2, 4, 5.
- Task 8: minors (deferred): sitting clash lost at confirm is a bare 409 without a row; ImportTime drops seconds; 00:00 Excel cell reads blank; "New" vs "Update" label for first feedback; roster rebuild per student; failed reload unmounts typed state.
- Task 7: minors (deferred): rollback test asserts only "not 200" (pin the exact status); EnsureProfilesAsync duplicate-profile recovery can't recover inside the import transaction (fails safe with rollback).
- Task 10: minors (deferred): RecheckAsync balance and cancelled branches untested (race-only); N/NGN prefixes; case-variant admission numbers.
- Task 6: minors (deferred): offline register page hides the import panel instead of showing the offline sentence; no tests for the two spoof cases (code handles both).
- Task 8: minors (deferred): same-title test uses UtcNow+3 days for both due dates (pin dueAt to remove a midnight edge).
