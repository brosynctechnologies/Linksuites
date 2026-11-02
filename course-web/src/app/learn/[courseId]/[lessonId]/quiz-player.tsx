"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatCountdown, formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { AttemptDto, LessonPlaybackDto, QuizResultDto } from "@/lib/types";
import { CheckIcon, ClockIcon, CloseIcon } from "@/components/icons";
import { Markdown } from "@/components/markdown";
import { Button, ErrorBox, Notice, StatusBadge, cx, inputClass } from "@/components/ui";

type QuizInfo = NonNullable<LessonPlaybackDto["quiz"]>;
type Answer = { optionIds: string[]; text: string };

/**
 * Quiz lessons: start an attempt, a countdown when the quiz is timed, answer, submit, then the results
 * with explanations (correct answers are only revealed after a submitted attempt).
 */
export function QuizPlayer({ lessonId, info, intro, onResult }: { lessonId: string; info: QuizInfo; intro?: string | null; onResult: (r: QuizResultDto) => void }) {
  const history = useApi<QuizResultDto[]>(`/api/learn/quizzes/${lessonId}/attempts`);
  const attempts = asList(history.data);
  const [attempt, setAttempt] = useState<AttemptDto | null>(null);
  const [result, setResult] = useState<{ res: QuizResultDto; attempt: AttemptDto | null } | null>(null);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const used = Math.max(info.attemptsUsed ?? 0, attempts.length);
  const left = info.maxAttempts ? Math.max(0, info.maxAttempts - used) : null;
  const passed = info.passed || attempts.some((a) => a.passed);
  const best = Math.max(info.bestScorePercent ?? 0, ...attempts.map((a) => a.scorePercent ?? 0));

  async function start() {
    setStarting(true);
    setError(null);
    try {
      const a = await api<AttemptDto>(`/api/learn/quizzes/${lessonId}/attempts`, { method: "POST" });
      setResult(null);
      setAttempt(a);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setStarting(false);
    }
  }

  if (attempt) {
    return (
      <AttemptForm
        attempt={attempt}
        onSubmitted={(res) => {
          setResult({ res, attempt });
          setAttempt(null);
          history.reload();
          onResult(res);
        }}
      />
    );
  }

  return (
    <div className="space-y-6">
      {result && <ResultView result={result.res} attempt={result.attempt} passMark={info.passMarkPercent} />}

      {!result && (
        <div className="max-w-[68ch]">
          {intro && <Markdown source={intro} variant="lesson" className="mb-5" />}
          <dl className="grid grid-cols-2 gap-x-6 gap-y-3 rounded-panel border border-line bg-surface p-4 text-sm sm:grid-cols-4">
            <Fact label="Questions" value={String(info.questionsCount)} />
            <Fact label="Pass mark" value={`${info.passMarkPercent}%`} />
            <Fact label="Time limit" value={info.timeLimitMinutes ? `${info.timeLimitMinutes} min` : "None"} />
            <Fact label="Attempts" value={info.maxAttempts ? `${used} of ${info.maxAttempts} used` : `${used} used`} />
          </dl>
        </div>
      )}

      {passed && (
        <Notice tone="success">
          Passed, best score <span className="tnum font-semibold">{Math.round(best)}%</span>. You can retake it to practise{left === 0 ? " once more attempts are allowed" : ""}.
        </Notice>
      )}

      <div className="flex flex-wrap items-center gap-3">
        {left === 0 ? (
          <p className="text-sm text-slate-700">You&apos;ve used every attempt for this quiz. Ask the tutor in Q&amp;A if you need another.</p>
        ) : (
          <Button onClick={start} loading={starting} className="px-6">
            {used === 0 ? "Start quiz" : result ? "Try again" : "Start a new attempt"}
          </Button>
        )}
        {left !== null && left > 0 && <span className="text-sm text-muted">{left === 1 ? "1 attempt left" : `${left} attempts left`}</span>}
        {info.timeLimitMinutes ? (
          <span className="flex items-center gap-1.5 text-sm text-muted">
            <ClockIcon className="h-4 w-4" /> The timer starts when you press start.
          </span>
        ) : null}
      </div>
      {error && <ErrorBox error={error} />}

      {attempts.length > 0 && (
        <section aria-labelledby="past-attempts">
          <h3 id="past-attempts" className="mb-2 text-sm font-semibold text-ink">
            Your attempts
          </h3>
          <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
            {attempts.map((a) => (
              <li key={a.attemptId} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm">
                <span className="text-slate-700">{formatStamp(a.submittedAt)}</span>
                <span className="flex items-center gap-3">
                  <span className="tnum font-semibold text-ink">{Math.round(a.scorePercent)}%</span>
                  <StatusBadge tone={a.passed ? "lime" : "brick"}>{a.passed ? "Passed" : "Not passed"}</StatusBadge>
                  <button type="button" className="font-semibold text-lagoon-text hover:underline" onClick={() => setResult({ res: a, attempt: null })}>
                    Review
                  </button>
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="tnum font-semibold text-ink">{value}</dd>
    </div>
  );
}

function AttemptForm({ attempt, onSubmitted }: { attempt: AttemptDto; onSubmitted: (r: QuizResultDto) => void }) {
  const [answers, setAnswers] = useState<Record<string, Answer>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const submittedRef = useRef(false);
  const endsAt = attempt.endsAt ? new Date(attempt.endsAt).getTime() : null;
  const remaining = endsAt ? endsAt - now : null;
  const answered = attempt.questions.filter((q) => {
    const a = answers[q.id];
    return a && (a.optionIds.length > 0 || a.text.trim());
  }).length;

  const submit = useCallback(async () => {
    if (submittedRef.current) return;
    submittedRef.current = true;
    setBusy(true);
    setError(null);
    try {
      const res = await api<QuizResultDto>(`/api/learn/attempts/${attempt.attemptId}/submit`, {
        method: "POST",
        body: {
          answers: attempt.questions.map((q) => ({
            questionId: q.id,
            optionIds: answers[q.id]?.optionIds ?? [],
            text: q.type === "ShortAnswer" ? (answers[q.id]?.text ?? "").trim() || null : null,
          })),
        },
      });
      onSubmitted(res);
    } catch (e) {
      submittedRef.current = false;
      setError(toApiError(e));
      setBusy(false);
    }
  }, [answers, attempt, onSubmitted]);

  const submitRef = useRef(submit);
  useEffect(() => {
    submitRef.current = submit;
  });

  // Tick the countdown; when time is up, hand in what's answered.
  useEffect(() => {
    if (!endsAt) return;
    const t = setInterval(() => {
      const n = Date.now();
      setNow(n);
      if (n >= endsAt) {
        clearInterval(t);
        void submitRef.current();
      }
    }, 1000);
    return () => clearInterval(t);
  }, [endsAt]);

  const setOption = (qid: string, oid: string, multi: boolean) =>
    setAnswers((a) => {
      const cur = a[qid]?.optionIds ?? [];
      const next = multi ? (cur.includes(oid) ? cur.filter((x) => x !== oid) : [...cur, oid]) : [oid];
      return { ...a, [qid]: { optionIds: next, text: a[qid]?.text ?? "" } };
    });

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
      className="max-w-[68ch]"
    >
      <div className="sticky top-0 z-10 -mx-4 mb-5 flex items-center justify-between gap-3 border-b border-line bg-mist/95 px-4 py-2.5 backdrop-blur sm:mx-0 sm:rounded-control sm:border sm:px-3">
        <span className="tnum text-sm text-slate-700">
          {answered} of {attempt.questions.length} answered
        </span>
        {remaining !== null && (
          <span
            className={cx("tnum flex items-center gap-1.5 text-sm font-semibold", remaining < 60_000 ? "text-brick" : "text-ink")}
            role="timer"
            aria-live={remaining < 60_000 ? "polite" : "off"}
          >
            <ClockIcon className="h-4 w-4" />
            {formatCountdown(remaining)} left
          </span>
        )}
      </div>

      <ol className="space-y-6">
        {attempt.questions.map((q, i) => {
          const multi = q.type === "MultipleChoice";
          const a = answers[q.id];
          return (
            <li key={q.id} className="rounded-panel border border-line bg-surface p-4 sm:p-5">
              <fieldset>
                <legend className="w-full">
                  <span className="tnum text-xs text-muted">
                    Question {i + 1}
                    {q.points ? `, ${q.points} point${q.points === 1 ? "" : "s"}` : ""}
                    {multi ? ". Choose all that apply." : ""}
                  </span>
                  <span className="mt-1 block font-serif text-[1.0625rem] leading-snug text-ink">{q.prompt}</span>
                </legend>
                {q.type === "ShortAnswer" ? (
                  <div className="mt-3">
                    <label htmlFor={`q-${q.id}`} className="sr-only">
                      Your answer
                    </label>
                    <input
                      id={`q-${q.id}`}
                      className={inputClass}
                      autoComplete="off"
                      value={a?.text ?? ""}
                      onChange={(e) => setAnswers((x) => ({ ...x, [q.id]: { optionIds: [], text: e.target.value } }))}
                      placeholder="Type your answer"
                    />
                  </div>
                ) : (
                  <div className="mt-3 space-y-2">
                    {q.options.map((o) => {
                      const checked = !!a?.optionIds.includes(o.id);
                      return (
                        <label
                          key={o.id}
                          className={cx(
                            "flex cursor-pointer items-start gap-3 rounded-control border px-3 py-2.5 text-[0.9375rem]",
                            checked ? "border-lagoon bg-lagoon-wash" : "border-line hover:bg-slate-50",
                          )}
                        >
                          <input
                            type={multi ? "checkbox" : "radio"}
                            name={`q-${q.id}`}
                            className="mt-1 accent-[var(--lagoon)]"
                            checked={checked}
                            onChange={() => setOption(q.id, o.id, multi)}
                          />
                          <span>{o.text}</span>
                        </label>
                      );
                    })}
                  </div>
                )}
              </fieldset>
            </li>
          );
        })}
      </ol>

      {error && (
        <div className="mt-4">
          <ErrorBox error={error} />
        </div>
      )}
      <div className="mt-6 flex flex-wrap items-center gap-3">
        <Button type="submit" loading={busy} className="px-6">
          Submit answers
        </Button>
        {answered < attempt.questions.length && <span className="text-sm text-muted">Unanswered questions count as wrong.</span>}
      </div>
    </form>
  );
}

function ResultView({ result, attempt, passMark }: { result: QuizResultDto; attempt: AttemptDto | null; passMark: number }) {
  const optionText = (qid: string, oid: string) => attempt?.questions.find((q) => q.id === qid)?.options.find((o) => o.id === oid)?.text;
  const correctCount = result.questions.filter((q) => q.correct).length;
  return (
    <section aria-labelledby="quiz-result" className="max-w-[68ch]">
      <div className={cx("rounded-panel border-l-[3px] p-4 sm:p-5", result.passed ? "border-lime bg-lime-wash" : "border-brick bg-brick-wash")}>
        <h3 id="quiz-result" className={cx("text-lg font-semibold", result.passed ? "text-lime-dark" : "text-brick-dark")}>
          {result.passed ? "Passed" : "Not passed this time"}
        </h3>
        <p className="mt-1 text-slate-700">
          You scored <span className="tnum font-bold text-ink">{Math.round(result.scorePercent)}%</span> ({correctCount} of {result.questions.length} right). The pass
          mark is <span className="tnum">{passMark}%</span>.
        </p>
      </div>
      <ol className="mt-4 space-y-3">
        {result.questions.map((q, i) => {
          const yours = q.yourOptionIds.map((id) => optionText(q.id, id)).filter(Boolean);
          const right = q.correctOptionIds.map((id) => optionText(q.id, id)).filter(Boolean);
          return (
            <li key={q.id} className="rounded-panel border border-line bg-surface p-4">
              <div className="flex items-start gap-3">
                <span
                  className={cx("mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-white", q.correct ? "bg-lime" : "bg-brick")}
                  aria-hidden="true"
                >
                  {q.correct ? <CheckIcon className="h-3 w-3" strokeWidth={3} /> : <CloseIcon className="h-3 w-3" strokeWidth={3} />}
                </span>
                <div className="min-w-0">
                  <p className="text-xs text-muted">
                    Question {i + 1}: {q.correct ? "correct" : "incorrect"}
                  </p>
                  <p className="mt-0.5 font-serif leading-snug text-ink">{q.prompt}</p>
                  {(yours.length > 0 || q.yourText) && (
                    <p className="mt-2 text-sm text-slate-700">
                      Your answer: <span className="font-medium">{q.yourText || yours.join(", ")}</span>
                    </p>
                  )}
                  {!q.correct && right.length > 0 && (
                    <p className="mt-1 text-sm text-lime-dark">
                      Correct answer: <span className="font-medium">{right.join(", ")}</span>
                    </p>
                  )}
                  {q.explanation && <p className="mt-2 border-l-2 border-line pl-3 text-sm text-slate-700">{q.explanation}</p>}
                </div>
              </div>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
