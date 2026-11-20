"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatDay, lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { SubjectAttendanceSettingsDto, SubjectLessonsDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, EmptyState, ErrorBox, Input, Notice, PageHeader, PageLoader, RuledSheet, Toggle, cx, panelClass, ruledRowClass } from "@/components/ui";
import { AcademicNoAccess, useLeadership, useReadOnly } from "../../scores/academic-shared";
import { AttendanceNav } from "../attendance-nav";

export default function LessonsPage() {
  const role = useRole();
  const leadership = useLeadership();
  const [date, setDate] = useState(lagosToday);
  // Bumped when the principal turns subject attendance on or off, so the day's lessons load again.
  const [version, setVersion] = useState(0);
  if (role === "Accountant") return <AcademicNoAccess what="Attendance" />;
  return (
    <div className="space-y-4">
      <PageHeader title="Attendance" subtitle="Take a register for each lesson on the timetable." />
      <AttendanceNav />
      {leadership && <SubjectAttendanceSetting onChanged={() => setVersion((v) => v + 1)} />}
      <div className="w-48">
        <Input label="Date" type="date" required max={lagosToday()} value={date} onChange={(e) => e.target.value && setDate(e.target.value)} />
      </div>
      <Lessons date={date} version={version} leadership={leadership} />
    </div>
  );
}

function SubjectAttendanceSetting({ onChanged }: { onChanged: () => void }) {
  const setting = useApi<SubjectAttendanceSettingsDto>("/api/school/settings/subject-attendance");
  const readOnly = useReadOnly();
  const save = useAction();

  async function set(enabled: boolean) {
    const res = await save.run(() => api<SubjectAttendanceSettingsDto>("/api/school/settings/subject-attendance", { method: "PUT", body: { enabled } }));
    if (res.ok) {
      setting.setData(res.data);
      onChanged();
    }
  }

  return (
    <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="subject-attendance-setting">
      <h2 id="subject-attendance-setting" className="font-semibold text-ink">
        Registers for each lesson
      </h2>
      {setting.error && <ApiErrorView error={setting.error} onRetry={setting.reload} />}
      {!setting.data && !setting.error && <PageLoader />}
      {setting.data && (
        <Toggle
          checked={setting.data.enabled}
          disabled={save.busy || readOnly}
          onChange={set}
          label={setting.data.enabled ? "Subject attendance is on" : "Subject attendance is off"}
          description="When it is on, subject teachers take a register for each lesson on the timetable. The daily register stays as it is."
        />
      )}
      <ErrorBox error={save.error} />
    </section>
  );
}

function Lessons({ date, version, leadership }: { date: string; version: number; leadership: boolean }) {
  const lessons = useApi<SubjectLessonsDto>(`/api/school/subject-attendance/lessons${qs({ date })}`, { nonce: version });
  if (lessons.error) return <ApiErrorView error={lessons.error} onRetry={lessons.reload} />;
  if (!lessons.data) return <PageLoader />;
  const data = lessons.data;
  if (!data.enabled) {
    return (
      <EmptyState
        title="Subject attendance is off"
        text={leadership ? "Turn it on above to take a register for each lesson." : "The principal can turn it on. Until then, take the daily register."}
      />
    );
  }
  if (data.lessons.length === 0) {
    return (
      <EmptyState
        title="No lessons on this day"
        text={leadership ? "No class has a lesson on the timetable for this day." : "You have no lesson on the timetable for this day."}
      />
    );
  }
  const taken = data.lessons.filter((l) => l.taken).length;
  return (
    <>
      <p className="tnum text-sm text-muted" aria-live="polite">
        {taken} of {data.lessons.length} lesson registers taken on {formatDay(date)}
      </p>
      {date > lagosToday() && <Notice>Registers open on the day of the lesson.</Notice>}
      <RuledSheet
        label="Lessons"
        busy={lessons.loading}
        template="4.5rem minmax(0,1fr) minmax(0,1fr) 6rem 7rem"
        head={[{ label: "Period" }, { label: "Subject" }, { label: "Class" }, { label: "Register" }, { label: "Absent, late", align: "right" }]}
      >
        {data.lessons.map((l) => (
          <li key={`${l.classSubjectId}:${l.period}`}>
            <Link href={`/app/attendance/lessons/${l.classSubjectId}/${date}/${l.period}`} className={`${ruledRowClass} hover:bg-slate-50`}>
              <span className="tnum text-sm text-muted">
                <span className="font-bold text-ink">{l.period}</span> · {l.start}
              </span>
              <span className="min-w-0 font-bold break-words text-ink">{l.subject}</span>
              <span className="min-w-0 text-sm break-words text-slate-700">{l.className}</span>
              <span>{l.taken ? <Badge tone="pass">Taken</Badge> : <Badge tone="crest">Not taken</Badge>}</span>
              <span className="tnum text-sm text-muted md:text-right md:text-ink">{l.taken ? `${l.absent} absent, ${l.late} late` : ""}</span>
            </Link>
          </li>
        ))}
      </RuledSheet>
    </>
  );
}
