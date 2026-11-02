"use client";

import { useEffect, useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { formatDay, formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { AssessmentDto, ClassSubjectDto, ComponentDto, LevelDto } from "@/lib/types";
import type {
  BankQuestionDto,
  CbtAttemptsDto,
  CbtPaperDto,
  CbtReleaseDto,
  CbtReopenDto,
  CbtRosterRowDto,
  CreateCbtPaperRequest,
  ExamPeriodDto,
  ExamPeriodViewDto,
  ExamSittingDto,
} from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, Input, Notice, PageHeader, PageLoader, RuledSheet, Select, cx, panelClass } from "@/components/ui";
import { AcademicNoAccess, score, useReadOnly } from "../scores/academic-shared";
import { SubjectTermBar, useSubjectTerm } from "../teaching-shared";
import { BankList } from "../questions/bank-shared";

const MAX_TITLE = 150;
const MAX_DURATION = 480;

export default function CbtPage() {
  const role = useRole();
  const readOnly = useReadOnly();
  const state = useSubjectTerm();
  const [nonce, setNonce] = useState(0);
  const leadershipSubjects = useApi<ClassSubjectDto[]>(state.academic.leadership && state.classId ? `/api/school/classes/${state.classId}/subjects` : null);
  const assessment = useApi<AssessmentDto>(
    state.levelId && state.levelLoaded && !state.developmental ? `/api/school/assessment?levelId=${state.levelId}` : null,
  );

  if (role === "Accountant") return <AcademicNoAccess what="Computer-based tests" />;

  const error = state.academic.error ?? state.terms.error;
  const loaded = state.academic.loaded && state.terms.loaded;
  const subjectId = state.academic.leadership
    ? leadershipSubjects.data?.find((s) => s.id === state.classSubjectId)?.subjectId
    : state.academic.subjects.find((s) => s.classSubjectId === state.classSubjectId)?.subjectId;
  const ready = !!state.classSubjectId && !!state.termId && !state.developmental && state.levelLoaded;

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader title="Computer-based tests" subtitle="Set a test from the question bank, publish it for the class, then release the answers when everyone has sat it." />
      {error && (
        <ApiErrorView
          error={error}
          onRetry={() => {
            state.academic.reload();
            state.terms.reload();
          }}
        />
      )}
      {!loaded && !error && <PageLoader />}
      {loaded && state.classes.length === 0 && (
        <EmptyState
          title="No subjects to set tests for"
          text={state.academic.leadership ? "This session has no classes yet. Add them in Setup." : "You don't teach a subject in any class this session. Ask the principal to assign you."}
        />
      )}
      {loaded && state.classes.length > 0 && (
        <>
          <SubjectTermBar state={state} />
          <ApiErrorView error={state.subjectsError} onRetry={state.reloadSubjects} />
          <ApiErrorView error={state.lookupError} onRetry={state.reloadLookups} />
          <ApiErrorView error={assessment.error} onRetry={assessment.reload} />
          {!state.classId && <p className="text-sm text-muted">Choose a class to see its tests.</p>}
          {state.classId && state.developmental && <Notice>This class uses developmental assessment, so it has no computer-based tests.</Notice>}
          {ready && (
            <PaperList
              key={`${state.classSubjectId}:${state.termId}`}
              classSubjectId={state.classSubjectId}
              termId={state.termId}
              components={assessment.data?.components ?? []}
              readOnly={readOnly}
              nonce={nonce}
            />
          )}
          {ready && subjectId && !readOnly && (
            <PaperForm
              key={`form:${state.classSubjectId}:${state.termId}`}
              classSubjectId={state.classSubjectId}
              termId={state.termId}
              subjectId={subjectId}
              levelId={state.levelId}
              components={assessment.data?.components ?? []}
              onCreated={() => setNonce((n) => n + 1)}
            />
          )}
        </>
      )}
    </div>
  );
}

function PaperList({
  classSubjectId,
  termId,
  components,
  readOnly,
  nonce,
}: {
  classSubjectId: string;
  termId: string;
  components: ComponentDto[];
  readOnly: boolean;
  nonce: number;
}) {
  const list = useApi<CbtPaperDto[]>(`/api/school/cbt/papers${qs({ classSubjectId, termId })}`, { nonce });
  if (list.error) return <ApiErrorView error={list.error} onRetry={list.reload} />;
  if (!list.data) return <PageLoader label="Opening the tests…" />;
  if (list.data.length === 0) return <EmptyState title="No tests for this subject this term" text="Set the first one below." />;
  return (
    <ul className="min-w-0 space-y-3" aria-label="Tests">
      {list.data.map((paper) => (
        <li key={paper.id}>
          <PaperCard
            paper={paper}
            components={components}
            readOnly={readOnly}
            onChange={(next) => list.setData((current) => (current ?? []).map((p) => (p.id === next.id ? next : p)))}
          />
        </li>
      ))}
    </ul>
  );
}

function PaperCard({
  paper,
  components,
  readOnly,
  onChange,
}: {
  paper: CbtPaperDto;
  components: ComponentDto[];
  readOnly: boolean;
  onChange: (paper: CbtPaperDto) => void;
}) {
  const publish = useAction();
  const release = useAction();
  const [showAttempts, setShowAttempts] = useState(false);
  const component = components.find((c) => c.id === paper.assessmentComponentId);

  async function doPublish() {
    if (!window.confirm("Publish this test? Students in the class can start it while it is open.")) return;
    const res = await publish.run(() => api<CbtPaperDto>(`/api/school/cbt/papers/${paper.id}/publish`, { method: "POST" }));
    if (res.ok) onChange(res.data);
  }

  async function doRelease() {
    if (!window.confirm("Release the answers? Students who have handed in will see which of their answers were right.")) return;
    const res = await release.run(() => api<CbtReleaseDto>(`/api/school/cbt/papers/${paper.id}/release`, { method: "POST" }));
    if (res.ok) onChange({ ...paper, releasedKeyAt: res.data.releasedKeyAt });
  }

  return (
    <article className={cx(panelClass, "w-full min-w-0 space-y-2 p-4")}>
      <div className="flex min-w-0 flex-wrap items-start justify-between gap-2">
        <h2 className="min-w-0 font-bold break-words text-ink">{paper.title}</h2>
        <span className="flex flex-wrap gap-1.5">
          <Badge tone={paper.publishedAt ? "pass" : "neutral"}>{paper.publishedAt ? "Published" : "Not published"}</Badge>
          {paper.releasedKeyAt && <Badge tone="blazer">Answers released</Badge>}
        </span>
      </div>
      <p className="tnum text-sm text-slate-700">
        {formatStamp(paper.openAt)} to {formatStamp(paper.closeAt)} · {paper.durationMinutes} min
        {paper.examSittingId ? " · From the exam timetable" : ""}
      </p>
      <p className="text-sm text-slate-700">
        {paper.pickQuestionIds.length} picked
        {paper.drawCount > 0 ? `, ${paper.drawCount} drawn at random for each student` : ""} ·{" "}
        {component ? `Counts as ${component.name} (out of ${score(component.maxScore)})` : "Practice"}
      </p>
      <ErrorBox error={publish.error ?? release.error} />
      <div className="flex flex-wrap gap-2">
        {!readOnly && !paper.publishedAt && (
          <Button loading={publish.busy} onClick={doPublish}>
            Publish
          </Button>
        )}
        {!readOnly && paper.publishedAt && !paper.releasedKeyAt && (
          <Button variant="secondary" loading={release.busy} onClick={doRelease}>
            Release answers
          </Button>
        )}
        {paper.publishedAt && (
          <Button variant="secondary" onClick={() => setShowAttempts((v) => !v)} aria-expanded={showAttempts}>
            {showAttempts ? "Hide attempts" : "Attempts"}
          </Button>
        )}
      </div>
      {showAttempts && <AttemptsPanel paperId={paper.id} readOnly={readOnly} />}
    </article>
  );
}

function AttemptsPanel({ paperId, readOnly }: { paperId: string; readOnly: boolean }) {
  const view = useApi<CbtAttemptsDto>(`/api/school/cbt/papers/${paperId}/attempts`);
  if (view.error) return <ApiErrorView error={view.error} onRetry={view.reload} />;
  if (!view.data) return <PageLoader label="Opening the attempts…" />;
  const rows = view.data.students;
  const handedIn = rows.filter((r) => r.attempts[0]?.submittedAt && !r.attempts[0].reopenedAt).length;
  if (rows.length === 0) return <p className="text-sm text-muted">No students in this class yet.</p>;
  return (
    <div className="min-w-0 space-y-2 border-t border-rule-soft pt-3">
      <p className="tnum text-sm text-slate-700">
        {handedIn} of {rows.length} handed in
      </p>
      <RuledSheet label="Attempts" busy={view.loading}>
        {rows.map((row) => (
          <AttemptRow key={row.enrolmentId} row={row} readOnly={readOnly} onReopened={view.reload} />
        ))}
      </RuledSheet>
    </div>
  );
}

function AttemptRow({ row, readOnly, onReopened }: { row: CbtRosterRowDto; readOnly: boolean; onReopened: () => void }) {
  const reopen = useAction();
  const latest = row.attempts[0];
  const live = latest && !latest.reopenedAt ? latest : null;
  const status = !live ? (latest ? "Reopened, not started again" : "Not started") : live.submittedAt ? "Handed in" : "In progress";

  async function doReopen() {
    if (!live) return;
    if (!window.confirm(`Reopen this test for ${row.name}? They can start it again, and their new attempt replaces the counted score when handed in.`)) return;
    if ((await reopen.run(() => api<CbtReopenDto>(`/api/school/cbt/attempts/${live.attemptId}/reopen`, { method: "POST" }))).ok) onReopened();
  }

  return (
    <li className="ruled-pad w-full min-w-0 space-y-1.5 py-3">
      <div className="flex min-w-0 flex-wrap items-start justify-between gap-2">
        <span className="min-w-0">
          <span className="block text-sm font-bold break-words text-ink">{row.name}</span>
          <span className="block text-xs text-muted">{row.admissionNo}</span>
        </span>
        <span className="flex flex-wrap items-center gap-1.5">
          <Badge tone={live?.submittedAt ? "pass" : live ? "crest" : "neutral"}>{status}</Badge>
          {live?.submittedAt && (
            <span className="slab tnum text-sm font-bold text-ink">
              {score(live.totalMarks)} / {score(live.paperMarks)}
            </span>
          )}
        </span>
      </div>
      {live && (
        <p className="tnum text-xs text-muted">
          Started {formatStamp(live.startedAt)}
          {live.submittedAt ? `, handed in ${formatStamp(live.submittedAt)}` : `, ends ${formatStamp(live.deadlineAt)}`}
        </p>
      )}
      {live?.submittedAt && (
        <ul className="flex flex-wrap gap-1.5" aria-label={`${row.name}'s marks per question`}>
          {live.items.map((item) => (
            <li key={item.order}>
              <Badge tone={item.marksAwarded && item.marksAwarded > 0 ? "pass" : "brick"}>
                Q{item.order} {score(item.marksAwarded ?? 0)}/{score(item.marks)}
              </Badge>
            </li>
          ))}
        </ul>
      )}
      <ErrorBox error={reopen.error} />
      {!readOnly && live && (
        <Button variant="secondary" loading={reopen.busy} onClick={doReopen}>
          Reopen for this student
        </Button>
      )}
    </li>
  );
}

/** Sittings of this class subject in the term's exam periods. Teachers only reach published periods; leadership sees drafts too. */
function useSittings(termId: string, classSubjectId: string) {
  const periods = useApi<ExamPeriodDto[]>(`/api/school/exam-periods${qs({ termId })}`);
  const [sittings, setSittings] = useState<{ key: string; list: (ExamSittingDto & { periodName: string; published: boolean })[] } | null>(null);
  const key = `${termId}:${classSubjectId}:${(periods.data ?? []).map((p) => p.id).join(",")}`;
  useEffect(() => {
    if (!periods.data) return;
    let live = true;
    Promise.allSettled(periods.data.map((p) => api<ExamPeriodViewDto>(`/api/school/exam-periods/${p.id}`))).then((results) => {
      if (!live) return;
      const list = results.flatMap((r) =>
        r.status === "fulfilled"
          ? r.value.sittings.filter((s) => s.classSubjectId === classSubjectId).map((s) => ({ ...s, periodName: r.value.name, published: !!r.value.publishedAt }))
          : [],
      );
      setSittings({ key, list });
    });
    return () => {
      live = false;
    };
  }, [periods.data, classSubjectId, key]);
  return { list: sittings?.key === key ? sittings.list : null, error: periods.error };
}

function PaperForm({
  classSubjectId,
  termId,
  subjectId,
  levelId,
  components,
  onCreated,
}: {
  classSubjectId: string;
  termId: string;
  subjectId: string;
  levelId: string | null;
  components: ComponentDto[];
  onCreated: (paper: CbtPaperDto) => void;
}) {
  const bank = useApi<BankQuestionDto[]>(`/api/school/questions${qs({ subjectId })}`);
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const sittings = useSittings(termId, classSubjectId);
  const [title, setTitle] = useState("");
  const [duration, setDuration] = useState("40");
  const [sittingId, setSittingId] = useState("");
  const [openAt, setOpenAt] = useState("");
  const [closeAt, setCloseAt] = useState("");
  const [componentId, setComponentId] = useState("");
  const [drawCount, setDrawCount] = useState("0");
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const create = useAction();

  const pool = (bank.data ?? []).filter((q) => !q.archived && (q.levelId === null || q.levelId === levelId));
  const draw = Number(drawCount) || 0;
  const left = pool.length - picked.size;
  const sitting = sittings.list?.find((s) => s.id === sittingId) ?? null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: CreateCbtPaperRequest = {
      classSubjectId,
      termId,
      title: title.trim(),
      durationMinutes: Number(duration),
      examSittingId: sitting?.id ?? null,
      openAt: !sitting && openAt ? new Date(openAt).toISOString() : null,
      closeAt: !sitting && closeAt ? new Date(closeAt).toISOString() : null,
      assessmentComponentId: componentId || null,
      drawCount: draw,
      pickQuestionIds: pool.filter((q) => picked.has(q.id)).map((q) => q.id),
    };
    const res = await create.run(() => api<CbtPaperDto>("/api/school/cbt/papers", { method: "POST", body }));
    if (res.ok) {
      onCreated(res.data);
      setTitle("");
      setPicked(new Set());
      setDrawCount("0");
      setSittingId("");
    }
  }

  return (
    <form className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} onSubmit={submit} aria-labelledby="new-paper">
      <h2 id="new-paper" className="font-semibold text-ink">
        Set a test
      </h2>
      <ErrorBox error={create.error} />
      <Input label="Title" required maxLength={MAX_TITLE} value={title} onChange={(e) => setTitle(e.target.value)} />
      <Select
        label="Exam sitting"
        value={sittingId}
        onChange={(e) => {
          setSittingId(e.target.value);
          const next = sittings.list?.find((s) => s.id === e.target.value);
          if (next) setDuration(String(next.durationMinutes));
        }}
        hint={sittings.list && sittings.list.length === 0 ? "No exam sitting for this subject this term. Choose when the test opens and closes." : "A sitting sets when the test opens and closes."}
      >
        <option value="">None, choose the times</option>
        {(sittings.list ?? []).map((s) => (
          <option key={s.id} value={s.id}>
            {s.periodName}: {formatDay(s.date)} {s.start}–{s.end}
            {s.published ? "" : " (not published)"}
          </option>
        ))}
      </Select>
      <ApiErrorView error={sittings.error} />
      <div className="grid w-full min-w-0 gap-3 sm:grid-cols-3">
        {!sitting && (
          <>
            <Input label="Opens" type="datetime-local" required value={openAt} onChange={(e) => setOpenAt(e.target.value)} />
            <Input label="Closes" type="datetime-local" required value={closeAt} onChange={(e) => setCloseAt(e.target.value)} />
          </>
        )}
        <Input
          label="Minutes for each student"
          type="number"
          inputMode="numeric"
          min="1"
          max={MAX_DURATION}
          required
          value={duration}
          onChange={(e) => setDuration(e.target.value)}
        />
      </div>
      {sitting && (
        <p className="tnum text-sm text-slate-700">
          Opens and closes with the sitting: {formatDay(sitting.date)}, {sitting.start} to {sitting.end}
          {sitting.venue ? `, ${sitting.venue}` : ""}.
        </p>
      )}
      <Select label="Counted as" value={componentId} hint="A practice test writes no score." onChange={(e) => setComponentId(e.target.value)}>
        <option value="">Practice</option>
        {components.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name} (out of {score(c.maxScore)})
          </option>
        ))}
      </Select>
      <Input
        label="Questions to draw at random"
        type="number"
        inputMode="numeric"
        min="0"
        value={drawCount}
        onChange={(e) => setDrawCount(e.target.value)}
        hint={`On top of the questions you pick. ${Math.max(0, left)} more in the bank for this class.`}
      />
      <div className="min-w-0 space-y-2">
        <p className="text-sm font-medium text-slate-800">Pick questions</p>
        {bank.error && <ApiErrorView error={bank.error} onRetry={bank.reload} />}
        {!bank.data && !bank.error && <PageLoader label="Opening the bank…" />}
        {bank.data && pool.length === 0 && (
          <p className="text-sm text-muted">The bank has no questions for this subject and level yet. Add them on the Questions page.</p>
        )}
        {pool.length > 0 && (
          <BankList
            questions={pool}
            levels={levels.data ?? []}
            picked={picked}
            onPick={(id, on) =>
              setPicked((current) => {
                const next = new Set(current);
                if (on) next.add(id);
                else next.delete(id);
                return next;
              })
            }
          />
        )}
      </div>
      <p className="tnum text-sm text-slate-700" role="status">
        {picked.size} picked, {draw} drawn: {picked.size + draw} questions for each student.
      </p>
      <Button type="submit" loading={create.busy} disabled={picked.size + draw === 0}>
        Save test
      </Button>
    </form>
  );
}
