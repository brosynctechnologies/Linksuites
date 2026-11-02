"use client";

import Link from "next/link";
import { useState } from "react";
import { formatDay, lagosToday } from "@/lib/format";
import { discardQueued, OFFLINE_BANNER, useQueuedRegisters } from "@/lib/offline-register";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassAttendanceDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, Input, Notice, PageHeader, PageLoader, RuledSheet, linkClass, ruledRowClass } from "@/components/ui";
import { AcademicNoAccess, useAcademicClasses, useLeadership } from "../scores/academic-shared";

export default function AttendancePage() {
  const role = useRole();
  const [date, setDate] = useState(lagosToday);
  if (role === "Accountant") return <AcademicNoAccess what="Attendance" />;
  return (
    <div className="space-y-4">
      <PageHeader title="Attendance" subtitle="Take the register for a class. It saves on this phone when there is no connection." />
      <Queued />
      <div className="w-48">
        <Input label="Date" type="date" required max={lagosToday()} value={date} onChange={(e) => e.target.value && setDate(e.target.value)} />
      </div>
      <Classes date={date} />
    </div>
  );
}

/** Registers waiting on this phone, and any the school refused. */
function Queued() {
  const queued = useQueuedRegisters();
  const waiting = queued.filter((q) => !q.failed);
  const refused = queued.filter((q) => q.failed);
  if (queued.length === 0) return null;
  return (
    <div className="space-y-2">
      {waiting.length > 0 && (
        <Notice tone="warning">
          <p className="font-semibold">{OFFLINE_BANNER}</p>
          <ul className="mt-1 list-disc pl-5">
            {waiting.map((q) => (
              <li key={q.key}>
                {q.className}, {formatDay(q.date)}
              </li>
            ))}
          </ul>
        </Notice>
      )}
      {refused.map((q) => (
        <Notice key={q.key} tone="error">
          <p>
            The register for {q.className} on {formatDay(q.date)} was not saved: {q.failed}
          </p>
          <div className="mt-2 flex flex-wrap gap-2">
            <Link href={`/app/attendance/${q.classId}/${q.date}`} className={linkClass}>
              Open the register
            </Link>
            <Button variant="ghost" className="min-h-0 px-2 py-0.5" onClick={() => void discardQueued(q.key)}>
              Discard it
            </Button>
          </div>
        </Notice>
      ))}
    </div>
  );
}

function Classes({ date }: { date: string }) {
  const leadership = useLeadership();
  const mine = useAcademicClasses();
  const summary = useApi<ClassAttendanceDto[]>(leadership ? `/api/school/attendance/summary${qs({ date })}` : null);

  if (leadership) {
    if (summary.error) return <ApiErrorView error={summary.error} onRetry={summary.reload} />;
    if (!summary.data) return <PageLoader />;
    if (summary.data.length === 0) return <EmptyState title="No classes yet" text="Add classes for the current session in Setup, then take their registers here." />;
    const taken = summary.data.filter((c) => c.taken).length;
    return (
      <>
        <p className="tnum text-sm text-muted" aria-live="polite">
          {taken} of {summary.data.length} registers taken on {formatDay(date)}
        </p>
        <RuledSheet
          label="Classes"
          busy={summary.loading}
          template="minmax(0,1fr) 6rem 5rem 5rem 5rem"
          head={[{ label: "Class" }, { label: "Register" }, { label: "Present", align: "right" }, { label: "Absent", align: "right" }, { label: "Share", align: "right" }]}
        >
          {summary.data.map((c) => (
            <li key={c.classId}>
              <Link href={`/app/attendance/${c.classId}/${date}`} className={`${ruledRowClass} hover:bg-slate-50`}>
                <span className="font-bold text-ink">{c.className}</span>
                <span>{c.taken ? <Badge tone="pass">Taken</Badge> : <Badge tone="crest">Not taken</Badge>}</span>
                <span className="tnum hidden text-right text-sm md:block">{c.taken ? c.present + c.late : "—"}</span>
                <span className="tnum hidden text-right text-sm md:block">{c.taken ? c.absent : "—"}</span>
                <span className="tnum col-span-2 text-sm text-muted md:col-span-1 md:text-right md:text-ink">
                  {c.percent === null ? "" : `${c.percent}%`}
                  <span className="md:hidden">{c.taken ? ` present, ${c.absent} absent` : ""}</span>
                </span>
              </Link>
            </li>
          ))}
        </RuledSheet>
      </>
    );
  }

  if (mine.error) return <ApiErrorView error={mine.error} onRetry={mine.reload} />;
  if (!mine.loaded) return <PageLoader />;
  if (mine.classes.length === 0) {
    return <EmptyState title="No classes to take" text="You are not a class teacher or subject teacher of any class this session. Ask the principal to assign you." />;
  }
  return (
    <RuledSheet label="Your classes">
      {mine.classes.map((c) => (
        <li key={c.id}>
          <Link href={`/app/attendance/${c.id}/${date}`} className="ruled-pad flex min-h-12 items-center justify-between gap-3 py-3 hover:bg-slate-50">
            <span className="font-bold text-ink">{c.name}</span>
            <span className="text-sm text-muted">{mine.classTeacherOf.has(c.id) ? "Your class" : "You teach here"}</span>
          </Link>
        </li>
      ))}
    </RuledSheet>
  );
}
