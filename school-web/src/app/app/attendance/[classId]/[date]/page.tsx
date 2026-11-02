"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDay, lagosToday } from "@/lib/format";
import {
  cacheRegister,
  cachedRegister,
  flushQueue,
  isConnectionError,
  OFFLINE_BANNER,
  onRegisterSent,
  queuedRegister,
  registerBase,
  registerKey,
  saveRegister,
  unsentRegisterCount,
  useQueuedRegisters,
  type QueuedRegister,
} from "@/lib/offline-register";
import { ATTENDANCE_STATUSES, type AttendanceStatus, type MessageParentDto, type RegisterDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, ErrorBox, Input, Notice, PageHeader, PageLoader, WhatsAppLink, cx, linkClass } from "@/components/ui";
import { useRole } from "@/lib/roles";
import { useAcademicClasses, useReadOnly, useSessionTerms } from "../../../scores/academic-shared";

const DATE = /^\d{4}-\d{2}-\d{2}$/;

type Loaded = { register: RegisterDto; queued: QueuedRegister | null; offline: boolean; loadedAt: number };

export default function RegisterPage() {
  const { classId, date } = useParams<{ classId: string; date: string }>();
  const router = useRouter();
  const tenantId = useAuth()?.tenant?.id ?? null;
  const [state, setState] = useState<{ key: string; loaded?: Loaded; error?: ApiError } | null>(null);
  const [nonce, setNonce] = useState(0);
  const key = tenantId ? `${registerKey(tenantId, classId, date)}#${nonce}` : null;
  const validDate = DATE.test(date);
  const readOnly = useReadOnly();
  const role = useRole();
  // Typed marks on the sheet below that are not saved yet (reported by the sheet), so an import never lands under them.
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!tenantId || !key || !validDate) return;
    const ctrl = new AbortController();
    api<RegisterDto>(`/api/school/attendance/${classId}/${date}`, { signal: ctrl.signal }).then(
      async (register) => {
        await cacheRegister(tenantId, register);
        const queued = await queuedRegister(tenantId, classId, date);
        if (ctrl.signal.aborted) return;
        setState({ key, loaded: { register, queued, offline: false, loadedAt: Date.now() } });
      },
      async (e) => {
        if (ctrl.signal.aborted) return;
        const error = toApiError(e);
        const cached = isConnectionError(error) ? await cachedRegister(tenantId, classId, date) : null;
        const queued = cached ? await queuedRegister(tenantId, classId, date) : null;
        if (ctrl.signal.aborted) return;
        setState(cached ? { key, loaded: { register: cached, queued, offline: true, loadedAt: Date.now() } } : { key, error });
      },
    );
    return () => ctrl.abort();
  }, [tenantId, key, classId, date, validDate]);

  // A queued copy of this register reached the server: show the stored copy.
  useEffect(() => {
    if (!tenantId) return;
    const mine = registerKey(tenantId, classId, date);
    return onRegisterSent((sentKey, register) => {
      if (sentKey === mine) setState((s) => (s ? { key: s.key, loaded: { register, queued: null, offline: false, loadedAt: Date.now() } } : s));
    });
  }, [tenantId, classId, date]);

  const current = state && state.key === key ? state : null;
  const title = current?.loaded?.register.className ?? "Register";
  return (
    <div className="space-y-4">
      <PageHeader title={title} back="/app/attendance" subtitle={validDate ? formatDay(date) : undefined} />
      <div className="w-48">
        <Input
          label="Date"
          type="date"
          required
          max={lagosToday()}
          value={validDate ? date : ""}
          onChange={(e) => e.target.value && router.replace(`/app/attendance/${classId}/${e.target.value}`)}
        />
      </div>
      {!validDate && <ErrorBox error="This link has no valid date. Choose a date above." />}
      {current?.error && <ApiErrorView error={current.error} onRetry={() => setNonce((n) => n + 1)} />}
      {validDate && !current && <PageLoader label="Opening the register…" />}
      {current?.loaded && tenantId && (
        <Sheet
          key={`${key}:${current.loaded.loadedAt}`}
          tenantId={tenantId}
          loaded={current.loaded}
          onReload={() => setNonce((n) => n + 1)}
          onDirty={setDirty}
        />
      )}
      {/* Outside the sheet, so the reload after an import keeps the dialog and its result open. Never for the bursar, who has no registers. */}
      {validDate && tenantId && !readOnly && role !== "Accountant" && (
        <AttendanceImportGate
          classId={classId}
          date={date}
          dirty={dirty}
          offline={!!current?.loaded?.offline}
          onDone={() => setNonce((n) => n + 1)}
        />
      )}
    </div>
  );
}

const MAX_IMPORT_DAYS = 31;

function shiftDay(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000) + 1;
}

type ImportProps = { classId: string; date: string; dirty: boolean; offline: boolean; onDone: () => void };

/**
 * Shows the import only to someone who can take this class's register: leadership, or a teacher of the class (as the register's own check).
 * It waits for the classes and the current term, so the default range can start no earlier than the first date they may change.
 */
function AttendanceImportGate(props: ImportProps) {
  const academic = useAcademicClasses();
  const t = useSessionTerms();
  if (!academic.loaded || (!t.loaded && !t.error)) return null;
  if (!academic.leadership && !academic.classes.some((c) => c.id === props.classId)) return null;
  const today = lagosToday();
  const term = t.terms.find((x) => x.isCurrent);
  // The register's window: the current term, and for a teacher only the last 7 days.
  const termStart = term?.startDate ?? null;
  const teacherStart = shiftDay(today, -7);
  const windowStart = academic.leadership ? termStart : termStart && termStart > teacherStart ? termStart : teacherStart;
  return <AttendanceImport {...props} earliest={windowStart} />;
}

/**
 * Import several days of this class's registers from Excel or CSV: one column per date with P, A, L or E. It is refused while the sheet
 * has typed marks not saved yet, or while this phone holds registers of this class that haven't sent (waiting or refused), so the import
 * never lands under marks the teacher still means to send. Clicking first tries to send the waiting registers.
 */
function AttendanceImport({ classId, date, dirty, offline, onDone, earliest }: ImportProps & { earliest: string | null }) {
  const today = lagosToday();
  const [to, setTo] = useState(() => (date <= today ? date : today));
  const [from, setFrom] = useState(() => {
    const week = shiftDay(date <= today ? date : today, -6);
    return earliest && earliest > week && earliest <= today ? earliest : week;
  });
  const unsent = useQueuedRegisters().filter((q) => q.classId === classId).length;
  const dirtyRef = useRef(dirty);
  useEffect(() => {
    dirtyRef.current = dirty;
  }, [dirty]);

  const rangeProblem =
    !DATE.test(from) || !DATE.test(to)
      ? "Choose the first and last dates."
      : from > to
        ? "Choose a start date on or before the end date."
        : to > today
          ? "Choose today or an earlier date."
          : daysBetween(from, to) > MAX_IMPORT_DAYS
            ? "Choose at most 31 days."
            : null;
  const disabledReason = dirty
    ? "Save the register before importing."
    : unsent > 0
      ? "Registers of this class saved on this phone haven't sent yet. Send or discard them on the Attendance page before importing."
      : offline
        ? "You are offline. Import when you are back online."
        : (rangeProblem ?? undefined);

  async function beforeOpen() {
    // Send anything waiting first; open only when nothing of this class is left on the phone and nothing typed is unsaved.
    await flushQueue();
    return !dirtyRef.current && (await unsentRegisterCount(classId)) === 0;
  }

  return (
    <section className="space-y-3 border-t border-rule pt-4" aria-labelledby="attendance-import">
      <div>
        <h2 id="attendance-import" className="font-semibold text-ink">
          Import several days
        </h2>
        <p className="text-sm text-muted">
          Fill in a template for up to 31 days with P, A, L or E. Only absences on today&apos;s date message parents.
        </p>
      </div>
      <div className="flex flex-wrap items-end gap-3">
        <div className="w-44">
          <Input
            label="From"
            type="date"
            required
            min={earliest ?? undefined}
            max={to || today}
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </div>
        <div className="w-44">
          <Input label="To" type="date" required max={today} value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
        <ImportDialog
          kind="attendance"
          scope={{ classId, from, to }}
          title="Import attendance"
          disabledReason={disabledReason}
          beforeOpen={beforeOpen}
          onDone={onDone}
        />
      </div>
      {unsent > 0 && !dirty && (
        <Link href="/app/attendance" className={linkClass}>
          Open the Attendance page to send or discard them
        </Link>
      )}
    </section>
  );
}

const STATUS_TONE: Record<AttendanceStatus, string> = {
  Present: "border-pass bg-pass text-white",
  Absent: "border-brick bg-brick text-white",
  Late: "border-crest bg-crest text-white",
  Excused: "border-blazer bg-blazer text-white",
};

interface Mark {
  status: AttendanceStatus;
  note: string;
}

function initialMarks(register: RegisterDto, queued: QueuedRegister | null): Record<string, Mark> {
  const marks: Record<string, Mark> = {};
  for (const m of register.marks) marks[m.enrolmentId] = { status: m.status, note: m.note ?? "" };
  // Marks saved on this phone and not sent yet are newer than the copy loaded. Not when someone else has saved the register since
  // (stale): the teacher then reviews the stored marks.
  if (queued?.stale) return marks;
  for (const m of queued?.marks ?? []) if (marks[m.enrolmentId]) marks[m.enrolmentId] = { status: m.status, note: m.note ?? "" };
  return marks;
}

function Sheet({ tenantId, loaded, onReload, onDirty }: { tenantId: string; loaded: Loaded; onReload: () => void; onDirty: (dirty: boolean) => void }) {
  const { register, offline } = loaded;
  const readOnly = useReadOnly();
  const queuedAll = useQueuedRegisters();
  const key = registerKey(tenantId, register.classId, register.date);
  const queued = queuedAll.find((q) => q.key === key);
  const [marks, setMarks] = useState<Record<string, Mark>>(() => initialMarks(register, loaded.queued));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [result, setResult] = useState<{ kind: "sent"; messageParents: MessageParentDto[] } | { kind: "queued" } | null>(null);
  const [dirty, setDirty] = useState(false);
  // The version of the register these marks are based on; it moves on with each save that reaches the server.
  const [base, setBase] = useState(() => registerBase(register));

  // The page's import is refused while marks typed here are unsaved. A sheet that goes away (reload, another date) has nothing unsaved.
  useEffect(() => {
    onDirty(dirty);
  }, [dirty, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);

  const set =(enrolmentId: string, patch: Partial<Mark>) => {
    setMarks((m) => ({ ...m, [enrolmentId]: { ...m[enrolmentId], ...patch } }));
    setDirty(true);
    setResult(null);
  };

  const counts = ATTENDANCE_STATUSES.map((s) => ({ status: s, n: register.marks.filter((m) => marks[m.enrolmentId]?.status === s).length }));

  async function save() {
    setBusy(true);
    setError(null);
    const outcome = await saveRegister({
      classId: register.classId,
      className: register.className,
      date: register.date,
      marks: register.marks.map((m) => ({
        enrolmentId: m.enrolmentId,
        status: marks[m.enrolmentId].status,
        note: marks[m.enrolmentId].status === "Present" ? null : marks[m.enrolmentId].note.trim() || null,
      })),
      baseClientId: base,
    });
    setBusy(false);
    if (outcome.kind === "error") {
      setError(outcome.error);
      return;
    }
    if (outcome.kind === "sent") setBase(registerBase(outcome.register));
    setDirty(false);
    setResult(outcome.kind === "sent" ? { kind: "sent", messageParents: outcome.register.messageParents } : { kind: "queued" });
  }

  if (register.marks.length === 0) {
    return <Notice>No active students are in this class yet. Add students to the class, then take the register.</Notice>;
  }

  const waiting = queued && !queued.failed;
  return (
    <div className="space-y-4">
      {offline && !waiting && (
        <Notice tone="warning">
          You are offline. This is the register as it was last opened on this phone. You can still take it.{" "}
          <button type="button" className="font-semibold underline underline-offset-2" onClick={onReload}>
            Try to reload
          </button>
        </Notice>
      )}
      {(waiting || result?.kind === "queued") && <Notice tone="warning">{OFFLINE_BANNER}</Notice>}
      {queued?.failed &&
        (queued.stale ? (
          <Notice tone="error">
            Someone else updated this register after you took it on this phone, so your marks were not sent. The marks below are the latest
            saved. Review them, change any that are wrong and save again.
          </Notice>
        ) : (
          <Notice tone="error">The register saved on this phone was not accepted: {queued.failed} Change it below and save again.</Notice>
        ))}
      {!waiting && !offline && !dirty && !result && register.taken && <Notice tone="success">This register has been taken. Changes you save replace it.</Notice>}

      <div className="flex flex-wrap items-center gap-2 text-sm" aria-live="polite">
        {counts.map((c) => (
          <span key={c.status} className="tnum">
            <span className="font-semibold text-ink">{c.n}</span> <span className="text-muted">{c.status.toLowerCase()}</span>
            {c.status !== "Excused" && <span className="text-faint">,</span>}
          </span>
        ))}
      </div>

      {!readOnly && (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="secondary"
            onClick={() => {
              setMarks((m) => Object.fromEntries(Object.entries(m).map(([id, v]) => [id, { ...v, status: "Present" as AttendanceStatus }])));
              setDirty(true);
              setResult(null);
            }}
          >
            Mark everyone present
          </Button>
        </div>
      )}

      <div className="ruled-sheet">
        <ul className="ruled-rows" aria-label="Students">
          {register.marks.map((m) => {
            const mark = marks[m.enrolmentId];
            return (
              <li key={m.enrolmentId} className="ruled-pad space-y-2 py-3">
                <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
                  <div className="min-w-0">
                    <p className="font-bold text-ink">{m.studentName}</p>
                    <p className="tnum text-xs text-muted">{m.admissionNo}</p>
                  </div>
                  <div className="flex flex-wrap gap-1" role="group" aria-label={`${m.studentName}'s mark`}>
                    {ATTENDANCE_STATUSES.map((s) => (
                      <button
                        key={s}
                        type="button"
                        disabled={readOnly}
                        aria-pressed={mark.status === s}
                        onClick={() => set(m.enrolmentId, { status: s })}
                        className={cx(
                          "min-h-11 min-w-[4.5rem] rounded-control border px-2.5 text-sm font-semibold disabled:cursor-not-allowed",
                          mark.status === s ? STATUS_TONE[s] : "border-rule bg-surface text-slate-700 hover:border-slate-300",
                        )}
                      >
                        {s}
                      </button>
                    ))}
                  </div>
                </div>
                {mark.status !== "Present" && (
                  <Input
                    label={`Note for ${m.studentName}`}
                    srLabel
                    placeholder="Note (optional), e.g. sick"
                    maxLength={200}
                    disabled={readOnly}
                    value={mark.note}
                    onChange={(e) => set(m.enrolmentId, { note: e.target.value })}
                  />
                )}
              </li>
            );
          })}
        </ul>
      </div>

      <ErrorBox error={error} />
      {error?.status === 409 && (
        <Button variant="secondary" onClick={onReload}>
          Open the latest register
        </Button>
      )}
      {result?.kind === "sent" && (
        <div className="space-y-3">
          <Notice tone="success">Register saved.</Notice>
          {result.messageParents.length > 0 && (
            <div className="space-y-2">
              <p className="text-sm font-semibold text-ink">Message parents of the students marked absent</p>
              <ul className="space-y-2">
                {result.messageParents.map((p) => (
                  <li key={p.whatsAppUrl} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                    <span>
                      {p.guardianName}, parent of {p.studentName}
                    </span>
                    <WhatsAppLink href={p.whatsAppUrl}>Message on WhatsApp</WhatsAppLink>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
      {!readOnly && (
        <div className="flex flex-wrap items-center gap-3">
          <Button onClick={save} loading={busy}>
            Save register
          </Button>
          {waiting && <Badge tone="crest">Waiting to send</Badge>}
        </div>
      )}
    </div>
  );
}
