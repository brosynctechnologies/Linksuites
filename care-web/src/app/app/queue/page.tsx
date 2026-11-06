"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { canWriteNotes, STAGES, stageLabel, urgencyTone } from "@/lib/clinical";
import { formatDay, lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { QueueDto, VisitDto, VisitStage } from "@/lib/types/clinical";
import { lagosTime } from "@/components/check-in";
import { ChevronLeftIcon, ChevronRightIcon, PlusIcon } from "@/components/icons";
import {
  ApiErrorView,
  Badge,
  Button,
  CaseSheet,
  ErrorBox,
  NoAccess,
  PageHeader,
  PageLoader,
  Segmented,
  Wristband,
  buttonClass,
  caseRowClass,
  cx,
  inputClass,
} from "@/components/ui";

const POLL_MS = 30_000;

function shiftDay(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

function waited(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? `${h} h ${m} min` : `${h} h`;
}

/**
 * The day's queue by stage (`GET /api/care/queue`), polled every 30 seconds while it shows today. Everyone but the cashier. The rows are
 * demographics, stage, urgency and wait only; triage and the consultation open from here for the roles that may use them.
 */
export default function QueuePage() {
  const { me, access } = useCare();
  const today = lagosToday();
  // null = follow today: the request leaves the date to the API (Lagos today), so the queue rolls over at Lagos midnight while it polls.
  const [picked, setPicked] = useState<string | null>(null);
  const following = picked === null;
  const pick = (d: string) => setPicked(d >= today ? null : d);
  // Doctors (not the director) get their own patients by default; they can switch to everyone.
  const isDoctor = me.clinicalRole === "Doctor" && !access.isDirector;
  const [scope, setScope] = useState<"mine" | "all">("mine");
  const path = access.isCashier ? null : `/api/care/queue${qs({ date: picked ?? undefined, mine: isDoctor ? scope === "mine" : undefined })}`;
  const queue = useApi<QueueDto>(path);
  const { reload } = queue;
  // The day on screen: the chosen one, or the day the API answered for (today in Lagos).
  const date = picked ?? (following && queue.data ? queue.data.date : today);

  useEffect(() => {
    if (!path || !following) return;
    const t = setInterval(reload, POLL_MS);
    return () => clearInterval(t);
  }, [path, following, reload]);

  if (access.isCashier) return <NoAccess text="The queue is for the front desk and clinical staff." />;

  const stages = queue.data?.stages ?? [];
  const waiting = stages.filter((s) => s.stage !== "Done").reduce((n, s) => n + s.visits.length, 0);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Queue"
        subtitle={following ? `Today, ${formatDay(date)}. ${waiting} waiting. Updates every 30 seconds.` : formatDay(date)}
        action={
          !access.readOnly && (
            <Link href="/app/check-in" className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> Check in a patient
            </Link>
          )
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-1">
          <button type="button" className={buttonClass("ghost", "px-2.5")} aria-label="Previous day" onClick={() => pick(shiftDay(date, -1))}>
            <ChevronLeftIcon className="h-4 w-4" />
          </button>
          <label htmlFor="queue-date" className="sr-only">
            Day
          </label>
          <input id="queue-date" type="date" className={cx(inputClass, "w-auto")} value={date} max={today} onChange={(e) => e.target.value && pick(e.target.value)} />
          <button
            type="button"
            className={buttonClass("ghost", "px-2.5")}
            aria-label="Next day"
            disabled={following}
            onClick={() => pick(shiftDay(date, 1))}
          >
            <ChevronRightIcon className="h-4 w-4" />
          </button>
          {!following && (
            <button type="button" className={buttonClass("ghost")} onClick={() => setPicked(null)}>
              Today
            </button>
          )}
        </div>
        {isDoctor && (
          <Segmented
            label="Whose patients"
            className="w-64"
            value={scope}
            onChange={setScope}
            items={[
              { value: "mine", label: "My patients" },
              { value: "all", label: "Everyone" },
            ]}
          />
        )}
      </div>

      {queue.error && <ApiErrorView error={queue.error} onRetry={queue.reload} />}
      {!queue.data && !queue.error && <PageLoader />}
      {queue.data && (
        <div className="space-y-6">
          {STAGES.map(({ value }) => {
            const visits = stages.find((s) => s.stage === value)?.visits ?? [];
            return <StageSection key={value} stage={value} visits={visits} onChanged={queue.reload} myId={me.userId} />;
          })}
        </div>
      )}
    </div>
  );
}

function StageSection({ stage, visits, onChanged, myId }: { stage: VisitStage; visits: VisitDto[]; onChanged: () => void; myId: string }) {
  const titleId = `stage-${stage}`;
  const body =
    visits.length === 0 ? (
      <p className="rounded-panel border border-dashed border-slate-300 px-4 py-3 text-sm text-muted">Nobody here.</p>
    ) : (
      <CaseSheet label={stageLabel(stage)} template="minmax(0,1.6fr) minmax(0,1fr) 7rem minmax(0,1.4fr)" head={[{ label: "Patient" }, { label: "Doctor" }, { label: "Waited" }, { label: "", align: "right" }]}>
        {visits.map((v) => (
          <VisitRow key={v.id} visit={v} onChanged={onChanged} myId={myId} />
        ))}
      </CaseSheet>
    );
  if (stage === "Done")
    return (
      <details>
        <summary className="mb-2 cursor-pointer font-semibold text-ink">
          Done <span className="tnum font-normal text-muted">({visits.length})</span>
        </summary>
        {body}
      </details>
    );
  return (
    <section aria-labelledby={titleId}>
      <h2 id={titleId} className="mb-2 font-semibold text-ink">
        {stageLabel(stage)} <span className="tnum font-normal text-muted">({visits.length})</span>
      </h2>
      {body}
    </section>
  );
}

function VisitRow({ visit: v, onChanged, myId }: { visit: VisitDto; onChanged: () => void; myId: string }) {
  const { access } = useCare();
  const move = useAction();
  const clinical = access.isClinical;
  const triage = clinical && (v.stage === "WaitingVitals" || v.stage === "WaitingDoctor");
  // The API lets the visit's doctor open the note, and the director any visit (a read view on another doctor's visit).
  const consult = canWriteNotes(access) && v.stage !== "WaitingVitals" && (v.doctorUserId === myId || access.isDirector);
  const canMove = !access.readOnly && !access.isCashier && v.stage !== "Done";

  const [picked, setTarget] = useState<VisitStage | "">("");
  // A poll may have moved the visit onto the picked stage meanwhile.
  const target = picked === v.stage ? "" : picked;

  /** Moves happen only on the Move button; moves with consequences for the patient are confirmed first. */
  async function moveTo(stage: VisitStage) {
    if (stage === "Done" && !window.confirm(`Close ${v.patientName}'s visit? A closed visit can't be reopened.`)) return;
    if (
      stage === "WaitingPayment" &&
      !window.confirm(`Send ${v.patientName} to payment? If the bill has a balance, the patient's portal users may be told the bill is due. That can't be taken back.`)
    )
      return;
    const res = await move.run(() => api<VisitDto>(`/api/care/visits/${v.id}/stage`, { method: "POST", body: { stage } }));
    if (res.ok) {
      setTarget("");
      onChanged();
    }
  }

  return (
    <li className={caseRowClass}>
      <span className="min-w-0">
        <span className="flex flex-wrap items-center gap-2">
          <Wristband number={v.patientNumber} />
          <Link href={`/app/patients/${v.patientId}`} className="font-bold text-ink hover:underline">
            {v.patientName}
          </Link>
          {v.urgency !== "Routine" && <Badge tone={urgencyTone(v.urgency)}>{v.urgency}</Badge>}
        </span>
        <span className="tnum mt-0.5 block text-xs text-muted">
          {v.sex}, {v.ageYears}. Arrived {lagosTime(v.arrivedAt)}
          {v.appointmentId ? ", booked" : ", walk-in"}
        </span>
      </span>
      <span className="tnum text-right text-sm text-slate-700 md:hidden">{waited(v.minutesWaited)}</span>
      <span className="col-span-2 text-sm text-slate-700 md:col-span-1">{v.doctorName ?? <span className="text-muted">No doctor yet</span>}</span>
      <span className="tnum hidden text-sm text-slate-700 md:block">{waited(v.minutesWaited)}</span>
      <span className="col-span-2 flex flex-wrap items-center justify-start gap-2 md:col-span-1 md:justify-end">
        {triage && (
          <Link href={`/app/visits/${v.id}/triage`} className={buttonClass(v.stage === "WaitingVitals" ? "primary" : "secondary", "min-h-10 px-3")}>
            {v.stage === "WaitingVitals" ? "Take vitals" : "Re-triage"}
          </Link>
        )}
        {consult && (
          <Link href={`/app/visits/${v.id}/consultation`} className={buttonClass(v.stage === "WaitingDoctor" ? "primary" : "secondary", "min-h-10 px-3")}>
            {v.stage === "WaitingDoctor" ? "See patient" : "Consultation"}
          </Link>
        )}
        {canMove && (
          <>
            <label htmlFor={`move-${v.id}`} className="sr-only">
              Move {v.patientName} to
            </label>
            <select
              id={`move-${v.id}`}
              className={cx(inputClass, "min-h-10 w-auto py-1.5 pr-8")}
              value={target}
              disabled={move.busy}
              onChange={(e) => setTarget(e.target.value as VisitStage | "")}
            >
              <option value="">Move to…</option>
              {STAGES.filter((s) => s.value !== v.stage).map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
            <Button variant="secondary" className="min-h-10 px-3" disabled={!target} loading={move.busy} onClick={() => target && moveTo(target)}>
              Move
            </Button>
          </>
        )}
      </span>
      {move.error && (
        <span className="col-span-full">
          <ErrorBox error={move.error} />
        </span>
      )}
    </li>
  );
}
