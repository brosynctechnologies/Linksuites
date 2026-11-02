"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { LevelDto, SubjectDto } from "@/lib/types";
import type { BankQuestionDto, SaveBankQuestionRequest } from "@/lib/types/academic";
import { ApiErrorView, Button, EmptyState, ErrorBox, Input, PageHeader, PageLoader, Select, Textarea, cx, panelClass } from "@/components/ui";
import { PlusIcon } from "@/components/icons";
import { ImportDialog } from "@/components/import-dialog";
import { AcademicNoAccess, useAcademicClasses, useReadOnly } from "../scores/academic-shared";
import { BankList } from "./bank-shared";

const MAX_STEM = 2000;
const MAX_OPTION = 500;
const MAX_OPTIONS = 8;

/** Subjects the member can write questions for: every subject for leadership, else the subjects they teach in any class. */
function useBankSubjects() {
  const academic = useAcademicClasses();
  const all = useApi<SubjectDto[]>(academic.leadership ? "/api/school/subjects" : null);
  let subjects: { id: string; name: string }[] = [];
  if (academic.leadership) subjects = (all.data ?? []).map((s) => ({ id: s.id, name: s.name }));
  else {
    const seen = new Map<string, string>();
    for (const s of academic.subjects) if (!seen.has(s.subjectId)) seen.set(s.subjectId, s.subjectName);
    subjects = [...seen].map(([id, name]) => ({ id, name }));
  }
  subjects.sort((a, b) => a.name.localeCompare(b.name));
  const loaded = academic.leadership ? !!all.data : academic.loaded;
  const error = academic.error ?? (academic.leadership ? all.error : undefined);
  return {
    subjects,
    loaded,
    error,
    leadership: academic.leadership,
    reload: () => {
      academic.reload();
      all.reload();
    },
  };
}

export default function QuestionsPage() {
  const role = useRole();
  const readOnly = useReadOnly();
  const bank = useBankSubjects();
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const [subjectPick, setSubjectPick] = useState("");
  const [levelId, setLevelId] = useState("");
  const [adding, setAdding] = useState(false);
  const [saved, setSaved] = useState(0);
  // Bumped after an import: refreshes the list without showing the form's "Question saved." line.
  const [imported, setImported] = useState(0);

  if (role === "Accountant") return <AcademicNoAccess what="The question bank" />;

  const subjectId = bank.subjects.some((s) => s.id === subjectPick) ? subjectPick : (bank.subjects[0]?.id ?? "");
  const numericLevels = (levels.data ?? []).filter((l) => l.assessmentMode === "Numeric").sort((a, b) => a.order - b.order);

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Question bank"
        subtitle="Questions for a subject you teach. A computer-based test picks from these, or draws them at random."
        action={
          !readOnly &&
          subjectId && (
            <div className="flex flex-wrap gap-2">
              <ImportDialog kind="questions" title="Import questions" onDone={() => setImported((n) => n + 1)} />
              <Button onClick={() => setAdding(true)}>
                <PlusIcon className="h-4 w-4" /> Add a question
              </Button>
            </div>
          )
        }
      />
      {bank.error && <ApiErrorView error={bank.error} onRetry={bank.reload} />}
      {levels.error && <ApiErrorView error={levels.error} onRetry={levels.reload} />}
      {!bank.loaded && !bank.error && <PageLoader />}
      {bank.loaded && bank.subjects.length === 0 && (
        <EmptyState
          title="No subjects to write for"
          text={bank.leadership ? "Add subjects in Setup first." : "You don't teach a subject this session. Ask the principal to assign you."}
        />
      )}
      {bank.loaded && bank.subjects.length > 0 && (
        <>
          <div className="flex w-full min-w-0 flex-wrap items-end gap-2">
            <div className="w-full min-w-0 sm:w-52">
              <Select label="Subject" value={subjectId} onChange={(e) => setSubjectPick(e.target.value)}>
                {bank.subjects.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </Select>
            </div>
            <div className="w-full min-w-0 sm:w-52">
              <Select label="Level" value={levelId} onChange={(e) => setLevelId(e.target.value)}>
                <option value="">Every level</option>
                {numericLevels.map((l) => (
                  <option key={l.id} value={l.id}>
                    Tagged {l.name}
                  </option>
                ))}
              </Select>
            </div>
          </div>
          {adding && (
            <QuestionForm
              key={subjectId}
              subjectId={subjectId}
              levels={numericLevels}
              defaultLevelId={levelId}
              onClose={() => setAdding(false)}
              onSaved={() => setSaved((n) => n + 1)}
            />
          )}
          {saved > 0 && adding && <p className="text-sm text-muted" role="status">Question saved. Add another, or press Done.</p>}
          {subjectId && (
            <QuestionList key={`${subjectId}:${levelId}`} subjectId={subjectId} levelId={levelId} levels={levels.data ?? []} nonce={saved + imported} canArchive={!readOnly} />
          )}
        </>
      )}
    </div>
  );
}

function QuestionList({
  subjectId,
  levelId,
  levels,
  nonce,
  canArchive,
}: {
  subjectId: string;
  levelId: string;
  levels: LevelDto[];
  nonce: number;
  canArchive: boolean;
}) {
  const list = useApi<BankQuestionDto[]>(`/api/school/questions${qs({ subjectId, levelId })}`, { nonce });
  const archive = useAction();
  const [archivingId, setArchivingId] = useState<string | null>(null);

  async function setArchived(question: BankQuestionDto, archived: boolean) {
    setArchivingId(question.id);
    const path = `/api/school/questions/${question.id}/${archived ? "archive" : "unarchive"}`;
    const res = await archive.run(() => api<BankQuestionDto>(path, { method: "POST" }));
    setArchivingId(null);
    if (res.ok) list.reload();
  }

  if (list.error) return <ApiErrorView error={list.error} onRetry={list.reload} />;
  if (!list.data) return <PageLoader label="Opening the bank…" />;
  if (list.data.length === 0) {
    return (
      <EmptyState
        title={levelId ? "No questions tagged for this level" : "No questions yet"}
        text={levelId ? "Questions for every level are listed under Every level, and still count for this level's tests." : "Add the first question for this subject."}
      />
    );
  }
  return (
    <div className="min-w-0 space-y-2">
      {canArchive && (
        <p className="text-sm text-muted">Archive a question with a mistake: new tests stop using it, and tests already started keep it as it was.</p>
      )}
      <ErrorBox error={archive.error} />
      <BankList
        questions={list.data}
        levels={levels}
        busy={list.loading}
        onArchive={canArchive ? setArchived : undefined}
        archivingId={archivingId}
      />
    </div>
  );
}

function QuestionForm({
  subjectId,
  levels,
  defaultLevelId,
  onClose,
  onSaved,
}: {
  subjectId: string;
  levels: LevelDto[];
  defaultLevelId: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [stem, setStem] = useState("");
  const [options, setOptions] = useState<string[]>(["", "", "", ""]);
  const [correct, setCorrect] = useState(0);
  const [marks, setMarks] = useState("1");
  const [levelId, setLevelId] = useState(defaultLevelId);
  const save = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: SaveBankQuestionRequest = {
      subjectId,
      levelId: levelId || null,
      stem: stem.trim(),
      options: options.map((o) => o.trim()),
      correctIndex: correct,
      marks: Number(marks),
    };
    if ((await save.run(() => api<BankQuestionDto>("/api/school/questions", { method: "POST", body }))).ok) {
      onSaved();
      setStem("");
      setOptions(options.map(() => ""));
      setCorrect(0);
    }
  }

  return (
    <form className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} onSubmit={submit}>
      <ErrorBox error={save.error} />
      <Textarea label="Question" required maxLength={MAX_STEM} value={stem} onChange={(e) => setStem(e.target.value)} />
      <fieldset className="min-w-0 space-y-2">
        <legend className="mb-1 text-sm font-medium text-slate-800">Options</legend>
        {options.map((o, i) => (
          <div key={i} className="flex w-full min-w-0 items-end gap-2">
            <div className="min-w-0 flex-1">
              <Input
                label={`Option ${String.fromCharCode(65 + i)}`}
                required
                maxLength={MAX_OPTION}
                value={o}
                onChange={(e) => setOptions((current) => current.map((x, j) => (j === i ? e.target.value : x)))}
              />
            </div>
            {options.length > 2 && (
              <Button
                variant="secondary"
                aria-label={`Remove option ${String.fromCharCode(65 + i)}`}
                onClick={() => {
                  setOptions((current) => current.filter((_, j) => j !== i));
                  setCorrect((c) => (c === i ? 0 : c > i ? c - 1 : c));
                }}
              >
                Remove
              </Button>
            )}
          </div>
        ))}
        {options.length < MAX_OPTIONS && (
          <Button variant="secondary" onClick={() => setOptions((current) => [...current, ""])}>
            Add an option
          </Button>
        )}
      </fieldset>
      <div className="grid w-full min-w-0 gap-3 sm:grid-cols-3">
        <Select label="Correct option" value={String(correct)} onChange={(e) => setCorrect(Number(e.target.value))}>
          {options.map((_, i) => (
            <option key={i} value={i}>
              Option {String.fromCharCode(65 + i)}
            </option>
          ))}
        </Select>
        <Input label="Marks" type="number" inputMode="decimal" min="0.1" max="1000" step="0.1" required value={marks} onChange={(e) => setMarks(e.target.value)} />
        <Select label="Level" value={levelId} onChange={(e) => setLevelId(e.target.value)} hint="Every level means any class can draw it.">
          <option value="">Every level</option>
          {levels.map((l) => (
            <option key={l.id} value={l.id}>
              {l.name}
            </option>
          ))}
        </Select>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" loading={save.busy}>
          Save question
        </Button>
        <Button variant="secondary" onClick={onClose} disabled={save.busy}>
          Done
        </Button>
      </div>
    </form>
  );
}
