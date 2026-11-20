"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import {
  SUBJECT_ATTENDANCE_STATUSES,
  type SaveSubjectRegisterRequest,
  type SubjectAttendanceStatus,
  type SubjectRegisterDto,
} from "@/lib/types/academic";
import { ApiErrorView, Button, ErrorBox, Notice, PageHeader, PageLoader, cx } from "@/components/ui";
import { AcademicNoAccess, useReadOnly } from "../../../../../scores/academic-shared";

const STATUS_TONE: Record<SubjectAttendanceStatus, string> = {
  Present: "border-pass bg-pass text-white",
  Absent: "border-brick bg-brick text-white",
  Late: "border-crest bg-crest text-white",
};

export default function LessonRegisterPage() {
  const { classSubjectId, date, period } = useParams<{ classSubjectId: string; date: string; period: string }>();
  const role = useRole();
  const register = useApi<SubjectRegisterDto>(`/api/school/subject-attendance/${classSubjectId}/${date}/${period}`);
  // Kept here: a save replaces the sheet with the stored copy, and the confirmation should stay until the next change.
  const [justSaved, setJustSaved] = useState(false);
  if (role === "Accountant") return <AcademicNoAccess what="Attendance" />;
  const data = register.data;
  return (
    <div className="space-y-4">
      <PageHeader
        title={data ? `${data.subject} · ${data.className}` : "Lesson register"}
        back="/app/attendance/lessons"
        subtitle={data ? `${formatDay(data.date)}, period ${data.period} (${data.start}–${data.end})` : undefined}
      />
      {register.error && <ApiErrorView error={register.error} onRetry={register.reload} />}
      {!data && !register.error && <PageLoader label="Opening the register…" />}
      {data && (
        <Sheet
          key={`${data.clientId ?? ""}:${data.taken}`}
          register={data}
          justSaved={justSaved}
          onEdit={() => setJustSaved(false)}
          onSaved={(saved) => {
            setJustSaved(true);
            register.setData(saved);
          }}
          onReload={register.reload}
        />
      )}
    </div>
  );
}

function Sheet({
  register,
  justSaved,
  onEdit,
  onSaved,
  onReload,
}: {
  register: SubjectRegisterDto;
  justSaved: boolean;
  onEdit: () => void;
  onSaved: (saved: SubjectRegisterDto) => void;
  onReload: () => void;
}) {
  const readOnly = useReadOnly() || register.locked;
  const [marks, setMarks] = useState<Record<string, SubjectAttendanceStatus>>(() =>
    Object.fromEntries(register.marks.map((m) => [m.enrolmentId, m.status])),
  );
  const [dirty, setDirty] = useState(false);
  const save = useAction();

  function set(enrolmentId: string, status: SubjectAttendanceStatus) {
    setMarks((m) => ({ ...m, [enrolmentId]: status }));
    setDirty(true);
    onEdit();
  }

  async function submit() {
    const body: SaveSubjectRegisterRequest = {
      clientId: crypto.randomUUID(),
      marks: register.marks.map((m) => ({ enrolmentId: m.enrolmentId, status: marks[m.enrolmentId] ?? "Present" })),
    };
    const res = await save.run(() =>
      api<SubjectRegisterDto>(`/api/school/subject-attendance/${register.classSubjectId}/${register.date}/${register.period}`, { method: "PUT", body }),
    );
    if (res.ok) onSaved(res.data);
  }

  if (register.marks.length === 0) {
    return <Notice>No active students are in this class yet. Add students to the class, then take the register.</Notice>;
  }
  const counts = SUBJECT_ATTENDANCE_STATUSES.map((s) => ({ status: s, n: register.marks.filter((m) => marks[m.enrolmentId] === s).length }));

  return (
    <div className="space-y-4">
      {register.locked && <Notice tone="warning">This term is locked for the class, so this register can&apos;t change. Ask the principal to unlock it.</Notice>}
      {!register.locked && !dirty && !justSaved && register.taken && <Notice tone="success">This register has been taken. Changes you save replace it.</Notice>}

      <div className="flex flex-wrap items-center gap-2 text-sm" aria-live="polite">
        {counts.map((c, i) => (
          <span key={c.status} className="tnum">
            <span className="font-semibold text-ink">{c.n}</span> <span className="text-muted">{c.status.toLowerCase()}</span>
            {i < counts.length - 1 && <span className="text-faint">,</span>}
          </span>
        ))}
      </div>

      {!readOnly && (
        <Button
          variant="secondary"
          onClick={() => {
            setMarks(Object.fromEntries(register.marks.map((m) => [m.enrolmentId, "Present" as SubjectAttendanceStatus])));
            setDirty(true);
            onEdit();
          }}
        >
          Mark everyone present
        </Button>
      )}

      <div className="ruled-sheet">
        <ul className="ruled-rows" aria-label="Students">
          {register.marks.map((m) => (
            <li key={m.enrolmentId} className="ruled-pad flex flex-wrap items-center justify-between gap-x-3 gap-y-2 py-3">
              <div className="min-w-0">
                <p className="font-bold break-words text-ink">{m.studentName}</p>
                <p className="tnum text-xs text-muted">{m.admissionNo}</p>
              </div>
              <div className="flex flex-wrap gap-1" role="group" aria-label={`${m.studentName}'s mark`}>
                {SUBJECT_ATTENDANCE_STATUSES.map((s) => (
                  <button
                    key={s}
                    type="button"
                    disabled={readOnly}
                    aria-pressed={marks[m.enrolmentId] === s}
                    onClick={() => set(m.enrolmentId, s)}
                    className={cx(
                      "min-h-11 min-w-[4.5rem] rounded-control border px-2.5 text-sm font-semibold disabled:cursor-not-allowed",
                      marks[m.enrolmentId] === s ? STATUS_TONE[s] : "border-rule bg-surface text-slate-700 hover:border-slate-300",
                    )}
                  >
                    {s}
                  </button>
                ))}
              </div>
            </li>
          ))}
        </ul>
      </div>

      <ErrorBox error={save.error} />
      {save.error?.status === 409 && (
        <Button variant="secondary" onClick={onReload}>
          Open the latest register
        </Button>
      )}
      {justSaved && !dirty && <Notice tone="success">Register saved.</Notice>}
      {!readOnly && (
        <Button onClick={submit} loading={save.busy}>
          Save register
        </Button>
      )}
    </div>
  );
}
