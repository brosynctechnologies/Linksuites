"use client";

import { useParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type {
  CbtAttemptOpenDto,
  CbtAttemptViewDto,
  CbtOpenItemDto,
  CbtStartDto,
  SaveCbtAnswerRequest,
  StudentCbtPaperDto,
} from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, ErrorBox, Notice, PageHeader, PageLoader, cx, panelClass } from "@/components/ui";
import { score } from "../../../parent/_shared";
import { clock, isMarkedAttempt, isOpenAttempt, windowState } from "../cbt-shared";

const TIME_UP = "Time is up. Your answers were saved.";

export default function StudentCbtPage() {
  const { paperId } = useParams<{ paperId: string }>();
  const list = useApi<StudentCbtPaperDto[]>("/api/student/cbt/papers");
  const [started, setStarted] = useState<{ paperId: string; attemptId: string } | null>(null);
  const paper = list.data?.find((p) => p.id === paperId);
  const attemptId = (started?.paperId === paperId ? started.attemptId : null) ?? paper?.attemptId ?? null;

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title={paper?.title ?? "Test"}
        subtitle={paper ? `${paper.subject} · ${paper.durationMinutes} min${paper.counted ? "" : " · Practice"}` : undefined}
        back="/student/cbt"
      />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && !paper && <ErrorBox error={TEST_MISSING} />}
      {paper && !attemptId && (
        <StartPanel
          paper={paper}
          onStarted={(s) => {
            setStarted({ paperId, attemptId: s.attemptId });
            list.reload();
          }}
        />
      )}
      {paper && attemptId && (
        <Attempt
          key={attemptId}
          attemptId={attemptId}
          onRestart={() => {
            setStarted(null);
            list.reload();
          }}
        />
      )}
    </div>
  );
}

const TEST_MISSING = "This test was not found.";
const REOPENED = "This sitting was reopened. Start the test again.";

function isReopened(error: ApiError | null | undefined): boolean {
  return !!error && error.status === 409 && (error.detail === REOPENED || error.message === REOPENED);
}

function StartPanel({ paper, onStarted }: { paper: StudentCbtPaperDto; onStarted: (started: CbtStartDto) => void }) {
  const start = useAction();
  const now = useNow();
  const state = windowState(paper, now);
  async function begin() {
    if (!window.confirm("Start the test now? The clock starts as soon as you begin, and you can only start once.")) return;
    const res = await start.run(() => api<CbtStartDto>(`/api/student/cbt/papers/${paper.id}/start`, { method: "POST" }));
    if (res.ok) onStarted(res.data);
  }
  return (
    <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")}>
      <p className="tnum text-sm text-slate-700">
        Opens {formatStamp(paper.openAt)}, closes {formatStamp(paper.closeAt)}
      </p>
      <p className="text-sm text-slate-800">
        Each question has one correct option. Your answer is saved as soon as you choose it. When you finish, hand the test in to see your total. If time runs out first, it is handed in for you.
      </p>
      <p className="text-sm text-muted">The clock starts when you press Start, and the test ends when time runs out or the test closes.</p>
      <ApiErrorView error={start.error} />
      {state === "upcoming" && <Notice>This test isn&apos;t open yet. Come back when it opens.</Notice>}
      {state === "closed" && <Notice>This test has closed.</Notice>}
      {state === "open" && (
        <Button onClick={begin} loading={start.busy}>
          Start the test
        </Button>
      )}
    </section>
  );
}

function Attempt({ attemptId, onRestart }: { attemptId: string; onRestart: () => void }) {
  const view = useApi<CbtAttemptViewDto>(`/api/student/cbt/attempts/${attemptId}`);

  if (view.error) return <ApiErrorView error={view.error} onRetry={view.reload} />;
  if (!view.data) return <PageLoader label="Opening your test…" />;
  if (isOpenAttempt(view.data)) return <OpenAttempt attempt={view.data} onSubmitted={(next) => view.setData(next)} onRestart={onRestart} />;
  return <Result view={view.data} />;
}

function useNow(): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, []);
  return now;
}

function OpenAttempt({
  attempt,
  onSubmitted,
  onRestart,
}: {
  attempt: CbtAttemptOpenDto;
  onSubmitted: (view: CbtAttemptViewDto) => void;
  onRestart: () => void;
}) {
  const now = useNow();
  const [items, setItems] = useState<CbtOpenItemDto[]>(attempt.items);
  const [saving, setSaving] = useState<Record<string, boolean>>({});
  const [saveError, setSaveError] = useState<ApiError | null>(null);
  const [timeUp, setTimeUp] = useState(false);
  const submit = useAction();

  const remaining = new Date(attempt.deadlineAt).getTime() - now;
  const over = timeUp || remaining <= 0;
  const answered = items.filter((i) => i.chosenIndex !== null).length;

  async function choose(item: CbtOpenItemDto, chosenIndex: number) {
    if (over || item.chosenIndex === chosenIndex) return;
    const previous = item.chosenIndex;
    setSaveError(null);
    setItems((current) => current.map((i) => (i.id === item.id ? { ...i, chosenIndex } : i)));
    setSaving((s) => ({ ...s, [item.id]: true }));
    try {
      const body: SaveCbtAnswerRequest = { itemId: item.id, chosenIndex };
      await api<CbtAttemptOpenDto>(`/api/student/cbt/attempts/${attempt.attemptId}/answers`, { method: "PUT", body });
    } catch (e) {
      const err = toApiError(e);
      setSaveError(err);
      if (err.status === 400 && (err.detail === TIME_UP || err.message === TIME_UP)) setTimeUp(true);
      setItems((current) => current.map((i) => (i.id === item.id ? { ...i, chosenIndex: previous } : i)));
    } finally {
      setSaving((s) => ({ ...s, [item.id]: false }));
    }
  }

  async function handIn() {
    const unanswered = items.length - answered;
    const prompt = unanswered > 0 ? `Hand in now? ${unanswered} question${unanswered === 1 ? " is" : "s are"} not answered.` : "Hand in your test now?";
    if (!window.confirm(prompt)) return;
    const res = await submit.run(() => api<CbtAttemptViewDto>(`/api/student/cbt/attempts/${attempt.attemptId}/submit`, { method: "POST" }));
    if (res.ok) onSubmitted(res.data);
  }

  // When the clock reaches zero the test is handed in for the student. If that is refused (already handed in, or the term
  // was locked), reading the attempt again shows it marked, because the server marks a timed-out attempt when it is read.
  const autoHandedIn = useRef(false);
  useEffect(() => {
    if (!over || autoHandedIn.current) return;
    autoHandedIn.current = true;
    void (async () => {
      const res = await submit.run(() => api<CbtAttemptViewDto>(`/api/student/cbt/attempts/${attempt.attemptId}/submit`, { method: "POST" }));
      if (res.ok) {
        onSubmitted(res.data);
        return;
      }
      if (isReopened(res.error)) return;
      try {
        const latest = await api<CbtAttemptViewDto>(`/api/student/cbt/attempts/${attempt.attemptId}`);
        if (!isOpenAttempt(latest)) {
          submit.setError(null);
          onSubmitted(latest);
        }
      } catch {
        // The hand-in error stays on screen, with the button to try again.
      }
    })();
  }, [over, attempt.attemptId, submit, onSubmitted]);

  return (
    <div className="min-w-0 space-y-4">
      <div className={cx(panelClass, "sticky top-[calc(var(--support-bar,0px)+3.5rem)] z-20 flex w-full min-w-0 flex-wrap items-center justify-between gap-2 p-3 lg:top-[var(--support-bar,0px)]")}>
        <p className="text-sm text-slate-700" role="timer" aria-live="off">
          {over ? (
            <span className="font-bold text-brick">Time is up</span>
          ) : (
            <>
              Time left <span className="slab tnum text-lg font-bold text-ink">{clock(remaining)}</span>
            </>
          )}
        </p>
        <p className="tnum text-sm text-muted">
          {answered} of {items.length} answered
        </p>
      </div>
      {over && <Notice tone="warning">{TIME_UP}{submit.busy ? " Handing your test in…" : ""}</Notice>}
      <ErrorBox error={saveError} />
      <ol className="min-w-0 space-y-4" aria-label="Questions">
        {items.map((item, n) => (
          <li key={item.id} className={cx(panelClass, "w-full min-w-0 p-4")}>
            <fieldset className="min-w-0 space-y-2" disabled={over}>
              <legend className="mb-1 text-sm font-bold break-words whitespace-pre-line text-ink">
                {n + 1}. {item.stem}
              </legend>
              {item.options.map((option, i) => (
                <label
                  key={i}
                  className={cx(
                    "flex min-h-11 w-full min-w-0 items-start gap-2.5 rounded-control border px-3 py-2.5 text-sm",
                    item.chosenIndex === i ? "border-blazer bg-blazer-wash text-ink" : "border-rule bg-surface text-slate-800",
                  )}
                >
                  <input
                    type="radio"
                    name={item.id}
                    className="mt-0.5 h-4 w-4 shrink-0 accent-[#6b1d33]"
                    checked={item.chosenIndex === i}
                    onChange={() => choose(item, i)}
                  />
                  <span className="min-w-0 break-words">{option}</span>
                </label>
              ))}
              <p className="h-4 text-xs text-muted" aria-live="polite">
                {saving[item.id] ? "Saving…" : item.chosenIndex !== null ? "Saved" : ""}
              </p>
            </fieldset>
          </li>
        ))}
      </ol>
      <ApiErrorView error={submit.error} />
      {isReopened(saveError) || isReopened(submit.error) ? (
        <Button onClick={onRestart}>Start again</Button>
      ) : (
        <Button onClick={handIn} loading={submit.busy}>
          Hand in
        </Button>
      )}
    </div>
  );
}

function Result({ view }: { view: CbtAttemptViewDto }) {
  const marked = isMarkedAttempt(view) ? view : null;
  const submittedAt = "submittedAt" in view ? view.submittedAt : null;
  return (
    <div className="min-w-0 space-y-4">
      <section className={cx(panelClass, "w-full min-w-0 space-y-1 p-4")}>
        <p className="text-sm text-muted">Your total</p>
        <p className="slab tnum text-3xl font-bold text-ink">{"totalMarks" in view ? score(view.totalMarks) : ""}</p>
        {submittedAt && <p className="text-xs text-muted">Handed in {formatStamp(submittedAt)}</p>}
        {!marked && <p className="text-sm text-slate-700">Your teacher will show which answers were right after the test closes.</p>}
      </section>
      {marked && (
        <ol className="min-w-0 space-y-3" aria-label="Your answers">
          {marked.items.map((item, n) => (
            <li key={item.id} className={cx(panelClass, "w-full min-w-0 space-y-2 p-4")}>
              <div className="flex min-w-0 flex-wrap items-start justify-between gap-2">
                <p className="min-w-0 text-sm font-bold break-words whitespace-pre-line text-ink">
                  {n + 1}. {item.stem}
                </p>
                <Badge tone={item.correct ? "pass" : "brick"}>{item.correct ? "Right" : "Not right"}</Badge>
              </div>
              <p className="text-sm break-words text-slate-700">
                Your answer: {item.chosenIndex !== null ? item.options[item.chosenIndex] : "Not answered"}
              </p>
              <p className="tnum text-xs text-muted">Marks {score(item.marksAwarded)}</p>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
