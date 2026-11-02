"use client";

import { useId, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import type { QuestionType, QuizDto, QuizOption, QuizQuestion, SaveQuizRequest, StudioLessonDto } from "@/lib/types";
import { IconButton, ListField } from "@/components/editors";
import { ArrowDownIcon, ArrowUpIcon, CloseIcon, PlusIcon, TrashIcon } from "@/components/icons";
import { Button, Card, ErrorBox, Input, Notice, SectionTitle, Toggle, cx, inputClass } from "@/components/ui";

const TYPES: { value: QuestionType; label: string }[] = [
  { value: "SingleChoice", label: "One correct answer" },
  { value: "MultipleChoice", label: "Several correct answers" },
  { value: "TrueFalse", label: "True or false" },
  { value: "ShortAnswer", label: "Short typed answer" },
];

let seq = 0;
const newKey = () => `k${++seq}`;

type Opt = QuizOption & { key: string };
type Q = Omit<QuizQuestion, "options"> & { key: string; options: Opt[] };

function fromDto(quiz: QuizDto | null): { settings: Omit<SaveQuizRequest, "questions">; questions: Q[] } {
  return {
    settings: {
      timeLimitMinutes: quiz?.timeLimitMinutes ?? null,
      passMarkPercent: quiz?.passMarkPercent ?? 70,
      maxAttempts: quiz?.maxAttempts ?? null,
      shuffle: quiz?.shuffle ?? false,
    },
    questions: (quiz?.questions ?? []).map((q) => ({
      ...q,
      key: newKey(),
      explanation: q.explanation ?? "",
      acceptedAnswers: q.acceptedAnswers ?? [],
      options: (q.options ?? []).map((o) => ({ ...o, key: newKey() })),
    })),
  };
}

function blankQuestion(type: QuestionType = "SingleChoice"): Q {
  return {
    key: newKey(),
    id: null,
    type,
    prompt: "",
    points: 1,
    explanation: "",
    acceptedAnswers: [],
    options: optionsFor(type, []),
  };
}

function optionsFor(type: QuestionType, current: Opt[]): Opt[] {
  if (type === "ShortAnswer") return [];
  if (type === "TrueFalse") {
    const wasTrue = current.find((o) => o.text.toLowerCase() === "true")?.isCorrect ?? true;
    return [
      { key: newKey(), id: null, text: "True", isCorrect: wasTrue },
      { key: newKey(), id: null, text: "False", isCorrect: !wasTrue },
    ];
  }
  const base = current.length >= 2 && !(current.length === 2 && current[0].text === "True" && current[1].text === "False") ? current : [
    { key: newKey(), id: null, text: "", isCorrect: true },
    { key: newKey(), id: null, text: "", isCorrect: false },
  ];
  if (type === "SingleChoice") {
    let seen = false;
    return base.map((o) => {
      const keep = o.isCorrect && !seen;
      if (keep) seen = true;
      return { ...o, isCorrect: keep };
    });
  }
  return base;
}

function problems(questions: Q[]): string[] {
  const out: string[] = [];
  questions.forEach((q, i) => {
    const n = `Question ${i + 1}`;
    if (!q.prompt.trim()) out.push(`${n} needs a question.`);
    if (q.type === "ShortAnswer") {
      if (!q.acceptedAnswers.some((a) => a.trim())) out.push(`${n} needs at least one accepted answer.`);
      return;
    }
    if (q.options.filter((o) => o.text.trim()).length < 2) out.push(`${n} needs at least two answers to choose from.`);
    const correct = q.options.filter((o) => o.isCorrect && o.text.trim()).length;
    if (correct === 0) out.push(`${n} needs a correct answer marked.`);
    if (q.type === "SingleChoice" && correct > 1) out.push(`${n} can only have one correct answer.`);
  });
  return out;
}

/** Quiz settings and questions; saved together with PUT /quiz. */
export function QuizBuilder({ lesson, onSaved, canEdit }: { lesson: StudioLessonDto; onSaved: (l: StudioLessonDto) => void; canEdit: boolean }) {
  const [initial] = useState(() => fromDto(lesson.quiz));
  const [settings, setSettings] = useState(initial.settings);
  const [questions, setQuestions] = useState<Q[]>(initial.questions);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [issues, setIssues] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);

  const update = (key: string, patch: Partial<Q>) => {
    setSaved(false);
    setQuestions((qs) => qs.map((q) => (q.key === key ? { ...q, ...patch } : q)));
  };
  const move = (i: number, d: number) => {
    const j = i + d;
    if (j < 0 || j >= questions.length) return;
    setSaved(false);
    setQuestions((qs) => {
      const next = qs.slice();
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    });
  };

  async function save() {
    const found = problems(questions);
    setIssues(found);
    if (found.length) return;
    const body: SaveQuizRequest = {
      ...settings,
      questions: questions.map((q) => ({
        id: q.id,
        type: q.type,
        prompt: q.prompt.trim(),
        points: Math.max(0, Number(q.points) || 0),
        explanation: q.explanation?.trim() || null,
        options: q.type === "ShortAnswer" ? [] : q.options.filter((o) => o.text.trim()).map((o) => ({ id: o.id, text: o.text.trim(), isCorrect: o.isCorrect })),
        acceptedAnswers: q.type === "ShortAnswer" ? q.acceptedAnswers.map((a) => a.trim()).filter(Boolean) : [],
      })),
    };
    setBusy(true);
    setError(null);
    try {
      const res = await api<StudioLessonDto>(`/api/studio/lessons/${lesson.id}/quiz`, { method: "PUT", body });
      onSaved(res);
      const fresh = fromDto(res.quiz);
      setSettings(fresh.settings);
      setQuestions(fresh.questions);
      setSaved(true);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  const total = questions.reduce((n, q) => n + (Number(q.points) || 0), 0);

  return (
    <div className="space-y-4">
      <Card className="space-y-4">
        <SectionTitle>Quiz settings</SectionTitle>
        <fieldset disabled={!canEdit} className="grid gap-4 sm:grid-cols-3">
          <Input
            label="Pass mark (%)"
            type="number"
            min={0}
            max={100}
            value={settings.passMarkPercent}
            onChange={(e) => setSettings((s) => ({ ...s, passMarkPercent: Math.max(0, Math.min(100, Number(e.target.value) || 0)) }))}
          />
          <Input
            label="Time limit (minutes)"
            type="number"
            min={1}
            value={settings.timeLimitMinutes ?? ""}
            placeholder="No limit"
            onChange={(e) => setSettings((s) => ({ ...s, timeLimitMinutes: e.target.value ? Math.max(1, Number(e.target.value)) : null }))}
          />
          <Input
            label="Attempts allowed"
            type="number"
            min={1}
            value={settings.maxAttempts ?? ""}
            placeholder="Unlimited"
            onChange={(e) => setSettings((s) => ({ ...s, maxAttempts: e.target.value ? Math.max(1, Number(e.target.value)) : null }))}
          />
        </fieldset>
        <Toggle checked={settings.shuffle} disabled={!canEdit} onChange={(v) => setSettings((s) => ({ ...s, shuffle: v }))} label="Shuffle questions" description="Each attempt shows the questions in a different order." />
      </Card>

      <ol className="space-y-4">
        {questions.map((q, i) => (
          <QuestionEditor
            key={q.key}
            q={q}
            index={i}
            count={questions.length}
            canEdit={canEdit}
            onChange={(patch) => update(q.key, patch)}
            onMove={(d) => move(i, d)}
            onRemove={() => {
              if (q.prompt.trim() && !window.confirm(`Remove question ${i + 1}?`)) return;
              setSaved(false);
              setQuestions((qs) => qs.filter((x) => x.key !== q.key));
            }}
          />
        ))}
      </ol>

      {questions.length === 0 && <p className="rounded-panel border border-dashed border-slate-300 px-4 py-8 text-center text-sm text-muted">No questions yet. Add the first one below.</p>}

      {canEdit && (
        <div className="flex flex-wrap gap-2">
          {TYPES.map((t) => (
            <Button
              key={t.value}
              variant="secondary"
              onClick={() => {
                setSaved(false);
                setQuestions((qs) => [...qs, blankQuestion(t.value)]);
              }}
            >
              <PlusIcon className="h-4 w-4" /> {t.label}
            </Button>
          ))}
        </div>
      )}

      {issues.length > 0 && (
        <div role="alert" className="rounded-control border-l-[3px] border-brick bg-brick-wash px-3 py-2.5 text-sm text-brick-dark">
          <p className="font-semibold">Fix these before saving:</p>
          <ul className="mt-1 list-disc pl-5">
            {issues.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </div>
      )}
      {error && <ErrorBox error={error} />}
      {saved && <Notice tone="success">Quiz saved.</Notice>}
      {canEdit && (
        <div className="sticky bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-10 flex items-center justify-between gap-3 rounded-panel border border-line bg-surface/95 p-3 shadow-bar backdrop-blur lg:bottom-4">
          <span className="tnum text-sm text-muted">
            {questions.length} question{questions.length === 1 ? "" : "s"}, {total} point{total === 1 ? "" : "s"}
          </span>
          <Button onClick={save} loading={busy}>
            Save quiz
          </Button>
        </div>
      )}
    </div>
  );
}

function QuestionEditor({
  q,
  index,
  count,
  canEdit,
  onChange,
  onMove,
  onRemove,
}: {
  q: Q;
  index: number;
  count: number;
  canEdit: boolean;
  onChange: (patch: Partial<Q>) => void;
  onMove: (d: number) => void;
  onRemove: () => void;
}) {
  const id = useId();
  const multi = q.type === "MultipleChoice";
  const setOption = (key: string, patch: Partial<Opt>) => onChange({ options: q.options.map((o) => (o.key === key ? { ...o, ...patch } : o)) });
  const markCorrect = (key: string) =>
    onChange({
      options: q.options.map((o) => (multi ? (o.key === key ? { ...o, isCorrect: !o.isCorrect } : o) : { ...o, isCorrect: o.key === key })),
    });

  return (
    <li className="rounded-panel border border-line bg-surface">
      <div className="flex items-center gap-2 border-b border-line-soft bg-slate-50 px-3 py-2">
        <span className="tnum flex-1 text-sm font-semibold text-ink">Question {index + 1}</span>
        {canEdit && (
          <>
            <IconButton label={`Move question ${index + 1} up`} disabled={index === 0} onClick={() => onMove(-1)}>
              <ArrowUpIcon className="h-4 w-4" />
            </IconButton>
            <IconButton label={`Move question ${index + 1} down`} disabled={index === count - 1} onClick={() => onMove(1)}>
              <ArrowDownIcon className="h-4 w-4" />
            </IconButton>
            <IconButton label={`Remove question ${index + 1}`} danger onClick={onRemove}>
              <TrashIcon className="h-4 w-4" />
            </IconButton>
          </>
        )}
      </div>
      <fieldset disabled={!canEdit} className="space-y-4 p-4">
        <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_7rem]">
          <div>
            <label htmlFor={`${id}-type`} className="text-sm font-medium text-slate-700">
              Answer type
            </label>
            <select
              id={`${id}-type`}
              className={cx(inputClass, "mt-1.5")}
              value={q.type}
              onChange={(e) => {
                const type = e.target.value as QuestionType;
                onChange({ type, options: optionsFor(type, q.options) });
              }}
            >
              {TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </div>
          <Input label="Points" type="number" min={0} value={q.points} onChange={(e) => onChange({ points: Number(e.target.value) })} />
        </div>
        <div>
          <label htmlFor={`${id}-prompt`} className="text-sm font-medium text-slate-700">
            Question
          </label>
          <textarea id={`${id}-prompt`} rows={2} className={cx(inputClass, "mt-1.5 leading-normal")} value={q.prompt} onChange={(e) => onChange({ prompt: e.target.value })} />
        </div>

        {q.type === "ShortAnswer" ? (
          <ListField
            label="Accepted answers"
            items={q.acceptedAnswers}
            onChange={(v) => onChange({ acceptedAnswers: v })}
            placeholder="e.g. 9.8 m/s²"
            hint="Matched without caring about capitals or extra spaces. Add every spelling you'd accept."
          />
        ) : (
          <fieldset>
            <legend className="text-sm font-medium text-slate-700">{multi ? "Answers (tick every correct one)" : "Answers (choose the correct one)"}</legend>
            <ul className="mt-2 space-y-2">
              {q.options.map((o, oi) => (
                <li key={o.key} className="flex items-center gap-2">
                  <label className="flex shrink-0 items-center gap-1.5 text-xs text-muted">
                    <input
                      type={multi ? "checkbox" : "radio"}
                      name={`${id}-correct`}
                      checked={o.isCorrect}
                      onChange={() => markCorrect(o.key)}
                      className="h-4 w-4 accent-[var(--lagoon)]"
                    />
                    <span className="sr-only">Mark answer {oi + 1} correct</span>
                  </label>
                  {q.type === "TrueFalse" ? (
                    <span className={cx("flex-1 rounded-control border px-3 py-2 text-sm", o.isCorrect ? "border-lime bg-lime-wash font-semibold text-lime-dark" : "border-line text-ink")}>
                      {o.text}
                      {o.isCorrect && <span className="ml-2 text-xs font-medium">Correct</span>}
                    </span>
                  ) : (
                    <>
                      <label htmlFor={`${id}-o-${o.key}`} className="sr-only">
                        Answer {oi + 1}
                      </label>
                      <input
                        id={`${id}-o-${o.key}`}
                        className={cx(inputClass, "py-2", o.isCorrect && "border-lime")}
                        value={o.text}
                        onChange={(e) => setOption(o.key, { text: e.target.value })}
                        placeholder={`Answer ${oi + 1}`}
                      />
                      <IconButton label={`Remove answer ${oi + 1}`} danger disabled={q.options.length <= 2} onClick={() => onChange({ options: q.options.filter((x) => x.key !== o.key) })}>
                        <CloseIcon className="h-4 w-4" />
                      </IconButton>
                    </>
                  )}
                </li>
              ))}
            </ul>
            {q.type !== "TrueFalse" && canEdit && (
              <button
                type="button"
                className="mt-2 flex items-center gap-1.5 text-sm font-semibold text-lagoon-text hover:underline"
                onClick={() => onChange({ options: [...q.options, { key: newKey(), id: null, text: "", isCorrect: false }] })}
              >
                <PlusIcon className="h-4 w-4" /> Add an answer
              </button>
            )}
          </fieldset>
        )}

        <div>
          <label htmlFor={`${id}-exp`} className="text-sm font-medium text-slate-700">
            Explanation <span className="font-normal text-muted">(shown after the learner submits)</span>
          </label>
          <textarea id={`${id}-exp`} rows={2} className={cx(inputClass, "mt-1.5 leading-normal")} value={q.explanation ?? ""} onChange={(e) => onChange({ explanation: e.target.value })} />
        </div>
      </fieldset>
    </li>
  );
}
