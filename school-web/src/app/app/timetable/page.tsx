"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ClassSubjectDto } from "@/lib/types";
import { MAX_PERIOD, SCHOOL_DAYS, type SchoolDay, type TimetableDto, type TimetableSlotInput } from "@/lib/types/academic";
import { ImportDialog } from "@/components/import-dialog";
import { ApiErrorView, Button, EmptyState, ErrorBox, Input, Notice, PageHeader, PageLoader, SectionTitle, Select, cx, panelClass } from "@/components/ui";
import { AcademicNoAccess, ClassPicker, useAcademicClasses, useReadOnly } from "../scores/academic-shared";

const dayLabel = (day: string) => SCHOOL_DAYS.find((d) => d.value === day)?.label ?? day;

export default function TimetablePage() {
  const role = useRole();
  const academic = useAcademicClasses();
  const [classPick, setClassPick] = useState("");
  const classId = academic.classes.some((c) => c.id === classPick) ? classPick : academic.classes.length === 1 ? academic.classes[0].id : "";

  if (role === "Accountant") return <AcademicNoAccess what="The timetable" />;
  return (
    <div className="space-y-4">
      <PageHeader title="Timetable" subtitle="Each class's lessons for the week, period by period." />
      {academic.error && <ApiErrorView error={academic.error} onRetry={academic.reload} />}
      {!academic.loaded && !academic.error && <PageLoader />}
      {academic.loaded && academic.classes.length === 0 && (
        <EmptyState title="No timetables for you" text="You don't teach in any class this session. Ask the principal to assign you." />
      )}
      {academic.loaded && academic.classes.length > 0 && (
        <>
          <ClassPicker classes={academic.classes} value={classId} onChange={setClassPick} />
          {!classId && <p className="text-sm text-muted">Choose a class to see its timetable.</p>}
          {classId && <ClassTimetable key={classId} classId={classId} leadership={academic.leadership} />}
        </>
      )}
    </div>
  );
}

function ClassTimetable({ classId, leadership }: { classId: string; leadership: boolean }) {
  const readOnly = useReadOnly();
  const { data, error, reload, setData } = useApi<TimetableDto>(`/api/school/timetable/${classId}`);
  const subjects = useApi<ClassSubjectDto[]>(leadership ? `/api/school/classes/${classId}/subjects` : null);
  const [editing, setEditing] = useState(false);
  const [saved, setSaved] = useState(false);

  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader />;
  const canEdit = leadership && !readOnly;

  return (
    <div className="space-y-4">
      {data.warnings.length > 0 && (
        <Notice tone="warning">
          <p className="font-semibold">Teacher clashes</p>
          <ul className="mt-1 list-disc pl-5">
            {data.warnings.map((w, i) => (
              <li key={i}>
                {w.teacher} also teaches {w.otherClass} on {dayLabel(w.day)}, period {w.period}.
              </li>
            ))}
          </ul>
        </Notice>
      )}
      {saved && !editing && <Notice tone="success">Timetable saved.</Notice>}
      {editing && subjects.data ? (
        <Editor
          data={data}
          subjects={subjects.data}
          onCancel={() => setEditing(false)}
          onSaved={(t) => {
            setData(t);
            setEditing(false);
            setSaved(true);
          }}
        />
      ) : (
        <>
          <Week data={data} />
          {canEdit && (
            <div className="flex flex-wrap gap-2">
              <Button
                onClick={() => {
                  setSaved(false);
                  setEditing(true);
                }}
                disabled={!subjects.data}
              >
                {data.slots.length === 0 ? "Build the timetable" : "Change the timetable"}
              </Button>
              {/* Only outside the editor, so an import never lands on lessons typed but not saved. The file replaces the week. */}
              <ImportDialog
                kind="timetable"
                scope={{ classId: data.classId }}
                title={`Import the timetable for ${data.className}`}
                onDone={() => {
                  setSaved(false);
                  reload();
                }}
              />
              <ApiErrorView error={subjects.error} onRetry={subjects.reload} />
              {subjects.data && subjects.data.length === 0 && <p className="text-sm text-muted">Add subjects to this class in Setup first.</p>}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function Week({ data }: { data: TimetableDto }) {
  if (data.slots.length === 0) return <EmptyState title="No lessons yet" text="The principal builds each class's timetable here." />;
  const days = SCHOOL_DAYS.filter((d) => d.value !== "Sat" || data.slots.some((s) => s.day === "Sat"));
  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
      {days.map((d) => {
        const slots = data.slots.filter((s) => s.day === d.value).sort((a, b) => a.period - b.period);
        return (
          <section key={d.value} className={cx(panelClass, "p-4")} aria-labelledby={`day-${d.value}`}>
            <SectionTitle id={`day-${d.value}`}>{d.label}</SectionTitle>
            {slots.length === 0 ? (
              <p className="text-sm text-muted">No lessons.</p>
            ) : (
              <ul className="space-y-2">
                {slots.map((s) => (
                  <li key={s.id} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-x-3 text-sm">
                    <span className="tnum text-muted">
                      {s.start}–{s.end}
                    </span>
                    <span className="min-w-0">
                      <span className="font-bold text-ink">{s.subject}</span>
                      <span className="block text-xs text-muted">
                        Period {s.period}
                        {s.teacher ? `, ${s.teacher}` : ", no teacher yet"}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        );
      })}
    </div>
  );
}

interface Row {
  key: number;
  day: SchoolDay;
  period: string;
  start: string;
  end: string;
  classSubjectId: string;
}

let nextKey = 1;

function addMinutes(time: string, minutes: number): string {
  const [h, m] = time.split(":").map(Number);
  if (!Number.isFinite(h) || !Number.isFinite(m)) return time;
  const total = Math.min(23 * 60 + 59, h * 60 + m + minutes);
  return `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
}

function Editor({ data, subjects, onCancel, onSaved }: { data: TimetableDto; subjects: ClassSubjectDto[]; onCancel: () => void; onSaved: (t: TimetableDto) => void }) {
  const [rows, setRows] = useState<Row[]>(() =>
    data.slots.map((s) => ({ key: nextKey++, day: s.day, period: String(s.period), start: s.start, end: s.end, classSubjectId: s.classSubjectId })),
  );
  const [showSaturday, setShowSaturday] = useState(() => data.slots.some((s) => s.day === "Sat"));
  const { busy, error, run } = useAction();
  const set = (key: number, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  function add(day: SchoolDay) {
    setRows((rs) => {
      const mine = rs.filter((r) => r.day === day);
      const last = mine.reduce<Row | null>((best, r) => (!best || Number(r.period) > Number(best.period) ? r : best), null);
      const period = Math.min(MAX_PERIOD, (last ? Number(last.period) || 0 : 0) + 1);
      // The same period on another day is the best guess for the times.
      const twin = rs.find((r) => r.day !== day && Number(r.period) === period);
      const start = twin?.start ?? last?.end ?? "08:00";
      const end = twin?.end ?? addMinutes(start, 40);
      return [...rs, { key: nextKey++, day, period: String(period), start, end, classSubjectId: subjects[0]?.id ?? "" }];
    });
  }

  async function save() {
    const body: TimetableSlotInput[] = rows.map((r) => ({ day: r.day, period: Number(r.period), start: r.start, end: r.end, classSubjectId: r.classSubjectId }));
    const res = await run(() => api<TimetableDto>(`/api/school/timetable/${data.classId}`, { method: "PUT", body }));
    if (res.ok) onSaved(res.data);
  }

  const fieldError = (i: number, field: string) => error?.field(`slots[${i}].${field}`) ?? (field === "day" ? error?.field(`slots[${i}]`) : undefined);
  const hasRowError = !!error && rows.some((_, i) => ["day", "period", "start", "end", "classSubjectId"].some((f) => fieldError(i, f)));
  const days = SCHOOL_DAYS.filter((d) => d.value !== "Sat" || showSaturday);

  return (
    <div className="space-y-4">
      {days.map((d) => {
        const mine = rows.map((r, index) => ({ r, index })).filter((x) => x.r.day === d.value);
        return (
          <section key={d.value} className={cx(panelClass, "p-4")} aria-labelledby={`edit-${d.value}`}>
            <SectionTitle id={`edit-${d.value}`}>{d.label}</SectionTitle>
            {mine.length === 0 && <p className="mb-3 text-sm text-muted">No lessons.</p>}
            <ul className="space-y-3">
              {mine.map(({ r, index }) => (
                <li key={r.key} className="flex flex-wrap items-end gap-2 border-b border-rule-soft pb-3 last:border-0">
                  <div className="w-24">
                    <Input
                      label="Period"
                      type="number"
                      inputMode="numeric"
                      min={1}
                      max={MAX_PERIOD}
                      value={r.period}
                      onChange={(e) => set(r.key, { period: e.target.value })}
                      error={fieldError(index, "period")}
                    />
                  </div>
                  <div className="w-28">
                    <Input label="Starts" type="time" value={r.start} onChange={(e) => set(r.key, { start: e.target.value })} error={fieldError(index, "start")} />
                  </div>
                  <div className="w-28">
                    <Input label="Ends" type="time" value={r.end} onChange={(e) => set(r.key, { end: e.target.value })} error={fieldError(index, "end")} />
                  </div>
                  <div className="min-w-44 flex-1">
                    <Select label="Subject" value={r.classSubjectId} onChange={(e) => set(r.key, { classSubjectId: e.target.value })} error={fieldError(index, "classSubjectId")}>
                      {subjects.map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.subjectName}
                        </option>
                      ))}
                    </Select>
                  </div>
                  <Button variant="ghost" className="text-brick" onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))} aria-label={`Remove ${d.label} period ${r.period}`}>
                    Remove
                  </Button>
                  {fieldError(index, "day") && <p className="w-full text-xs font-medium text-brick">{fieldError(index, "day")}</p>}
                </li>
              ))}
            </ul>
            <Button variant="secondary" className="mt-3" onClick={() => add(d.value)} disabled={subjects.length === 0}>
              Add a lesson
            </Button>
          </section>
        );
      })}
      {!showSaturday && (
        <Button variant="ghost" onClick={() => setShowSaturday(true)}>
          Add Saturday lessons
        </Button>
      )}
      {error && !hasRowError && <ErrorBox error={error} />}
      {hasRowError && <ErrorBox error="Some lessons need attention. Check the messages above." />}
      <div className="flex flex-wrap gap-2">
        <Button onClick={save} loading={busy}>
          Save timetable
        </Button>
        <Button variant="secondary" onClick={onCancel} disabled={busy}>
          Cancel
        </Button>
      </div>
    </div>
  );
}
