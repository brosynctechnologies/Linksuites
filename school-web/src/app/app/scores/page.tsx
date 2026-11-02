"use client";

import { useEffect, useRef, useState } from "react";
import { api, toApiError } from "@/lib/api";
import { isConnectionError } from "@/lib/offline-register";
import { useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ClassSubjectDto } from "@/lib/types";
import type { SaveScoresRequest, ScoreEntryInput, ScoreGridDto } from "@/lib/types/academic";
import { ImportDialog } from "@/components/import-dialog";
import { ApiErrorView, EmptyState, Notice, PageHeader, PageLoader, Select, cx, panelClass } from "@/components/ui";
import { AcademicNoAccess, ClassPicker, TermPicker, score, useAcademicClasses, useReadOnly, useSessionTerms } from "./academic-shared";

export default function ScoresPage() {
  const role = useRole();
  const academic = useAcademicClasses();
  const t = useSessionTerms();
  const [classId, setClassId] = useState("");
  const [subjectPick, setSubjectPick] = useState("");
  const [termPick, setTermPick] = useState("");
  const termId = termPick || t.current?.id || "";

  // Leadership and the class teacher see every subject of the class; a subject teacher sees their own.
  const seesAll = academic.leadership || academic.classTeacherOf.has(classId);
  const classSubjects = useApi<ClassSubjectDto[]>(classId && seesAll ? `/api/school/classes/${classId}/subjects` : null);
  const mySubjects = academic.subjects.filter((s) => s.classId === classId);
  const subjects = seesAll
    ? (classSubjects.data ?? []).map((s) => ({ id: s.id, name: s.subjectName }))
    : mySubjects.map((s) => ({ id: s.classSubjectId, name: s.subjectName }));
  const classSubjectId = subjects.some((s) => s.id === subjectPick) ? subjectPick : (subjects[0]?.id ?? "");
  const canWrite = academic.leadership || academic.subjects.some((s) => s.classSubjectId === classSubjectId);
  const readOnly = useReadOnly();
  // Bumped after an import so the sheet reopens with the saved scores.
  const [imported, setImported] = useState(0);
  // What the open sheet reports: locked, and whether typed scores need fixing. Keyed by scope, so a reload after an import keeps the dialog open.
  const scopeKey = `${classSubjectId}:${termId}`;
  const [sheet, setSheet] = useState<SheetStatus | null>(null);
  const settleRef = useRef<(() => Promise<boolean>) | null>(null);
  const sheetStatus = sheet?.key === scopeKey ? sheet : null;

  if (role === "Accountant") return <AcademicNoAccess what="Score entry" />;
  const error = academic.error ?? t.error;
  return (
    <div className="space-y-4">
      <PageHeader title="Scores" subtitle="Enter each part of the term score. Scores save as you type." />
      {error && (
        <ApiErrorView
          error={error}
          onRetry={() => {
            academic.reload();
            t.reload();
          }}
        />
      )}
      {(!academic.loaded || !t.loaded) && !error && <PageLoader />}
      {academic.loaded && t.loaded && academic.classes.length === 0 && (
        <EmptyState title="No classes to score" text="You don't teach a subject in any class this session. Ask the principal to assign you." />
      )}
      {academic.loaded && t.loaded && academic.classes.length > 0 && (
        <>
          <div className="flex flex-wrap items-end gap-2">
            <ClassPicker
              classes={academic.classes}
              value={classId}
              onChange={(id) => {
                setClassId(id);
                setSubjectPick("");
              }}
            />
            {classId && (
              <div className="w-52">
                <Select label="Subject" value={classSubjectId} onChange={(e) => setSubjectPick(e.target.value)}>
                  {subjects.length === 0 && <option value="">{classSubjects.loading ? "Loading…" : "No subjects yet"}</option>}
                  {subjects.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </Select>
              </div>
            )}
            <TermPicker terms={t.terms} value={termId} onChange={setTermPick} />
            {classSubjectId && termId && canWrite && !readOnly && sheetStatus && !sheetStatus.locked && (
              <div className="sm:ml-auto">
                <ImportDialog
                  kind="scores"
                  scope={{ classSubjectId, termId }}
                  title="Import scores"
                  disabledReason={sheetStatus.blocked ? "Save or fix your typed scores before importing." : undefined}
                  beforeOpen={async () => (settleRef.current ? settleRef.current() : true)}
                  onDone={() => setImported((n) => n + 1)}
                />
              </div>
            )}
          </div>
          <ApiErrorView error={classSubjects.error} onRetry={classSubjects.reload} />
          {!classId && <p className="text-sm text-muted">Choose a class to open its score sheet.</p>}
          {classId && classSubjects.data && subjects.length === 0 && <Notice>This class has no subjects yet. Add them in Setup, under Classes.</Notice>}
          {classSubjectId && termId && (
            <Grid
              key={`${scopeKey}:${imported}`}
              classSubjectId={classSubjectId}
              termId={termId}
              canWrite={canWrite}
              onStatus={(locked, blocked) => setSheet({ key: scopeKey, locked, blocked })}
              settleRef={settleRef}
            />
          )}
        </>
      )}
    </div>
  );
}

interface SheetStatus {
  key: string;
  locked: boolean;
  /** Some typed scores are invalid or failed to save; an import would reload the sheet and lose them. */
  blocked: boolean;
}

interface SheetHooks {
  onStatus: (locked: boolean, blocked: boolean) => void;
  /** Set by the sheet: saves any typed score now and resolves true when every typed score is saved. */
  settleRef: React.MutableRefObject<(() => Promise<boolean>) | null>;
}

function Grid({ classSubjectId, termId, canWrite, onStatus, settleRef }: { classSubjectId: string; termId: string; canWrite: boolean } & SheetHooks) {
  const { data, error, reload } = useApi<ScoreGridDto>(`/api/school/scores?classSubjectId=${classSubjectId}&termId=${termId}`);
  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader label="Opening the score sheet…" />;
  return <Sheet grid={data} canWrite={canWrite} onStatus={onStatus} settleRef={settleRef} />;
}

interface Cell {
  /** What the teacher typed. */
  text: string;
  /** What the server holds, as text. */
  saved: string;
  saving: boolean;
  error: string | null;
}

const cellKey = (enrolmentId: string, componentId: string) => `${enrolmentId}|${componentId}`;
const AUTOSAVE_MS = 800;

/** "" is a cleared score; otherwise a number from 0 to the maximum with at most one decimal place. */
function parseCell(text: string, max: number, name: string): { value: number | null } | { error: string } {
  const clean = text.trim();
  if (clean === "") return { value: null };
  if (!/^\d+(\.\d)?$/.test(clean)) return { error: `Enter a number such as 12 or 12.5.` };
  const value = Number(clean);
  if (value > max) return { error: `${name} is out of ${score(max)}.` };
  return { value };
}

function Sheet({ grid, canWrite, onStatus, settleRef }: { grid: ScoreGridDto; canWrite: boolean } & SheetHooks) {
  const readOnly = useReadOnly();
  const [locked, setLocked] = useState(grid.locked);
  const [cells, setCells] = useState<Record<string, Cell>>(() => {
    const out: Record<string, Cell> = {};
    for (const s of grid.students)
      for (const c of grid.components) {
        const v = s.values[c.id];
        const text = v === null || v === undefined ? "" : score(v);
        out[cellKey(s.enrolmentId, c.id)] = { text, saved: text, saving: false, error: null };
      }
    return out;
  });
  const [sheetError, setSheetError] = useState<string | null>(null);
  const cellsRef = useRef(cells);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inFlight = useRef(false);
  // The save in flight, so the import can wait for it.
  const pending = useRef<Promise<void> | null>(null);
  const editable = canWrite && !locked && !readOnly;

  /** Applies a change to the cells and to cellsRef at once (cellsRef is only written here), so code awaiting a save reads the result straight away. */
  function update(fn: (cs: Record<string, Cell>) => Record<string, Cell>) {
    const next = fn(cellsRef.current);
    cellsRef.current = next;
    setCells(next);
  }

  const unsaved = Object.values(cells).some((c) => c.text.trim() !== c.saved || c.saving);
  useEffect(() => {
    if (!unsaved) return;
    const warn = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [unsaved]);

  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
    },
    [],
  );

  const components = new Map(grid.components.map((c) => [c.id, c]));

  async function flush() {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
    if (inFlight.current) {
      schedule();
      return;
    }
    const current = cellsRef.current;
    const entries: ScoreEntryInput[] = [];
    const sent: Record<string, string> = {};
    const invalid: Record<string, string> = {};
    for (const [key, cell] of Object.entries(current)) {
      const text = cell.text.trim();
      if (text === cell.saved || cell.saving) continue;
      const [enrolmentId, componentId] = key.split("|");
      const component = components.get(componentId)!;
      const parsed = parseCell(text, component.maxScore, component.name);
      if ("error" in parsed) {
        invalid[key] = parsed.error;
        continue;
      }
      entries.push({ enrolmentId, componentId, value: parsed.value });
      sent[key] = text;
    }
    update((cs) => {
      const next = { ...cs };
      for (const [k, message] of Object.entries(invalid)) next[k] = { ...next[k], error: message };
      for (const k of Object.keys(sent)) next[k] = { ...next[k], saving: true, error: null };
      return next;
    });
    if (entries.length === 0) return;

    inFlight.current = true;
    const body: SaveScoresRequest = { classSubjectId: grid.classSubjectId, termId: grid.termId, entries };
    const run = send(body, sent);
    pending.current = run;
    await run;
    pending.current = null;
  }

  async function send(body: SaveScoresRequest, sent: Record<string, string>) {
    try {
      const saved = await api<ScoreGridDto>("/api/school/scores", { method: "PUT", body });
      setLocked(saved.locked);
      setSheetError(null);
      update((cs) => {
        const next = { ...cs };
        for (const [k, text] of Object.entries(sent)) {
          const normalised = text === "" ? "" : score(Number(text));
          next[k] = { ...next[k], saved: normalised, text: next[k].text.trim() === text ? normalised : next[k].text, saving: false, error: null };
        }
        return next;
      });
    } catch (e) {
      const err = toApiError(e);
      const message = isConnectionError(err) ? "Not saved: no connection. Edit the score again to retry." : err.message;
      if (err.code === "term_locked") setLocked(true);
      setSheetError(isConnectionError(err) ? "Some scores are not saved because there is no connection. They are still on the sheet; edit one to try again." : null);
      update((cs) => {
        const next = { ...cs };
        for (const k of Object.keys(sent)) next[k] = { ...next[k], saving: false, error: message };
        return next;
      });
    } finally {
      inFlight.current = false;
    }
  }

  /** Saves typed scores now (after any save in flight) and tells whether every typed score is saved, for the import. */
  async function settle(): Promise<boolean> {
    if (pending.current) await pending.current;
    await flush();
    if (pending.current) await pending.current;
    return !Object.values(cellsRef.current).some((c) => c.error || c.saving || c.text.trim() !== c.saved);
  }

  useEffect(() => {
    settleRef.current = settle;
    return () => {
      settleRef.current = null;
    };
  });

  // Tell the page whether the import can run: not on a locked term, and not while typed scores need fixing.
  const blocked = Object.values(cells).some((c) => c.error);
  const onStatusRef = useRef(onStatus);
  useEffect(() => {
    onStatusRef.current = onStatus;
  });
  useEffect(() => {
    onStatusRef.current(locked, blocked);
  }, [locked, blocked]);

  function schedule() {
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => void flush(), AUTOSAVE_MS);
  }

  function change(key: string, text: string) {
    update((cs) => ({ ...cs, [key]: { ...cs[key], text, error: null } }));
    schedule();
  }

  if (grid.students.length === 0) return <Notice>No active students are in this class yet.</Notice>;

  const savingCount = Object.values(cells).filter((c) => c.saving).length;
  const pendingCount = Object.values(cells).filter((c) => !c.saving && !c.error && c.text.trim() !== c.saved).length;
  const errorCount = Object.values(cells).filter((c) => c.error).length;
  const maxTotal = grid.components.reduce((sum, c) => sum + c.maxScore, 0);

  return (
    <div className="space-y-3">
      {locked && <Notice tone="warning">Results are locked for this class this term, so scores can&apos;t change. Ask the principal to unlock them.</Notice>}
      {!locked && !canWrite && <Notice>You can read these scores. Only the subject teacher and the principal can change them.</Notice>}
      {sheetError && <Notice tone="error">{sheetError}</Notice>}
      {editable && (
        <p className="text-sm text-muted" aria-live="polite">
          {savingCount > 0
            ? "Saving…"
            : errorCount > 0
              ? `${errorCount} ${errorCount === 1 ? "score needs" : "scores need"} attention.`
              : pendingCount > 0
                ? "Waiting to save…"
                : "All scores saved."}
        </p>
      )}
      <div className={cx(panelClass, "overflow-x-auto")}>
        <table className="w-full min-w-max text-sm">
          <thead>
            <tr className="border-b border-rule bg-slate-50 text-left text-xs font-bold text-muted">
              <th scope="col" className="sticky left-0 bg-slate-50 px-3 py-2.5">
                Student
              </th>
              {grid.components.map((c) => (
                <th key={c.id} scope="col" className="px-2 py-2.5 text-right">
                  {c.name} <span className="font-normal">({score(c.maxScore)})</span>
                </th>
              ))}
              <th scope="col" className="px-3 py-2.5 text-right">
                Total ({score(maxTotal)})
              </th>
            </tr>
          </thead>
          <tbody>
            {grid.students.map((s) => {
              const total = grid.components.reduce((sum, c) => {
                const n = Number(cells[cellKey(s.enrolmentId, c.id)].text.trim());
                return sum + (Number.isFinite(n) ? n : 0);
              }, 0);
              return (
                <tr key={s.enrolmentId} className="border-b border-rule-soft align-top last:border-0">
                  <th scope="row" className="sticky left-0 bg-surface px-3 py-2 text-left font-normal">
                    <span className="block font-bold text-ink">{s.name}</span>
                    <span className="tnum block text-xs text-muted">{s.admissionNo}</span>
                  </th>
                  {grid.components.map((c) => {
                    const key = cellKey(s.enrolmentId, c.id);
                    const cell = cells[key];
                    const errorId = `err-${key}`;
                    return (
                      <td key={c.id} className="px-2 py-2 text-right">
                        <input
                          aria-label={`${s.name}, ${c.name} out of ${score(c.maxScore)}`}
                          inputMode="decimal"
                          autoComplete="off"
                          className={cx(
                            "tnum block min-h-11 w-20 rounded-control border border-rule bg-surface px-2 py-2 text-right text-base text-ink hover:border-slate-300 focus:border-blazer disabled:bg-slate-50 disabled:text-muted aria-[invalid=true]:border-brick sm:text-sm",
                            "ml-auto",
                          )}
                          value={cell.text}
                          disabled={!editable}
                          aria-invalid={!!cell.error || undefined}
                          aria-describedby={cell.error ? errorId : undefined}
                          onChange={(e) => change(key, e.target.value)}
                          onBlur={() => {
                            if (cellsRef.current[key]?.text.trim() !== cellsRef.current[key]?.saved) void flush();
                          }}
                        />
                        {cell.error && (
                          <span id={errorId} className="mt-1 block max-w-32 text-left text-xs font-medium text-brick" role="alert">
                            {cell.error}
                          </span>
                        )}
                        {cell.saving && <span className="mt-1 block text-xs text-muted">Saving…</span>}
                      </td>
                    );
                  })}
                  <td className="slab tnum px-3 py-2 text-right text-lg font-bold text-ink">{score(Math.round(total * 10) / 10)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
