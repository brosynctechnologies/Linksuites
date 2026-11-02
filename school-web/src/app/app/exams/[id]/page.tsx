"use client";

import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { formatDay, formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ClassSubjectDto, StaffDto } from "@/lib/types";
import type { ExamPeriodDto, ExamPeriodViewDto, ExamSittingDto, ExamWarningDto, SaveExamSittingRequest } from "@/lib/types/academic";
import { ImportDialog } from "@/components/import-dialog";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, Input, Notice, PageHeader, PageLoader, Select, Textarea, panelClass } from "@/components/ui";
import { AcademicNoAccess, useAcademicClasses, useReadOnly } from "../../scores/academic-shared";

export default function ExamPeriodPage() {
  const role = useRole();
  const { id } = useParams<{ id: string }>();
  if (role === "Accountant") return <AcademicNoAccess what="The exam timetable" />;
  return <Period id={id} />;
}

type Action = ReturnType<typeof useAction>;

function Period({ id }: { id: string }) {
  const canManage = useCan("school.manage");
  const readOnly = useReadOnly();
  const view = useApi<ExamPeriodViewDto>(`/api/school/exam-periods/${id}`);
  const publish = useAction();
  const add = useAction();
  const remove = useAction();
  const [warnings, setWarnings] = useState<ExamWarningDto[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);

  if (view.error) {
    return (
      <div className="min-w-0 space-y-4">
        <PageHeader title="Exam period" back="/app/exams" />
        <ApiErrorView error={view.error} onRetry={view.reload} />
      </div>
    );
  }
  if (!view.data) return <PageLoader label="Opening the exam period…" />;
  const period = view.data;

  /** A later action that worked replaces whatever an earlier one reported. */
  function clearMessages() {
    publish.setError(null);
    add.setError(null);
    remove.setError(null);
    setWarnings([]);
  }

  function saved(sitting: ExamSittingDto) {
    clearMessages();
    setWarnings(sitting.warnings);
    view.setData((current) => {
      const base = current ?? period;
      return { ...base, sittings: [...base.sittings.filter((s) => s.id !== sitting.id), sitting].sort(bySitting) };
    });
  }

  async function publishPeriod() {
    if (!window.confirm("Publish this timetable? Teachers will see the sittings for subjects they teach. Publishing keeps the first published time.")) return;
    const res = await publish.run(() => api<ExamPeriodDto>(`/api/school/exam-periods/${period.id}/publish`, { method: "POST" }));
    if (res.ok) {
      clearMessages();
      view.setData((current) => ({ ...(current ?? period), publishedAt: res.data.publishedAt }));
    }
  }

  async function removeSitting(sitting: ExamSittingDto) {
    if (!window.confirm(`Remove the ${sitting.subject} sitting for ${sitting.className}? Students and parents stop seeing it.`)) return;
    setRemovingId(sitting.id);
    const res = await remove.run(() => api<void>(`/api/school/exam-sittings/${sitting.id}`, { method: "DELETE" }));
    setRemovingId(null);
    if (res.ok) {
      clearMessages();
      if (editingId === sitting.id) setEditingId(null);
      view.setData((current) => {
        const base = current ?? period;
        return { ...base, sittings: base.sittings.filter((s) => s.id !== sitting.id) };
      });
    }
  }

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title={period.name}
        subtitle={`${period.kind === "Test" ? "Test" : "Examination"} · ${formatDay(period.start)} – ${formatDay(period.end)}`}
        back="/app/exams"
        action={<Badge tone={period.publishedAt ? "pass" : "neutral"}>{period.publishedAt ? "Published" : "Not published"}</Badge>}
      />
      {period.publishedAt && <p className="text-sm text-muted">Published {formatStamp(period.publishedAt)}</p>}
      {!canManage && <p className="text-sm text-muted">Sittings for subjects you teach. A sitting is added by the principal.</p>}
      <ErrorBox error={publish.error ?? remove.error} />
      {warnings.length > 0 && (
        <Notice tone="warning">
          <ul className="list-disc pl-5">
            {warnings.map((w) => (
              <li key={w.sittingId}>{w.detail}</li>
            ))}
          </ul>
        </Notice>
      )}
      {period.sittings.length === 0 && <EmptyState title="No sittings yet" text={canManage ? "Add the first subject, date and venue." : "Nothing is published for a subject you teach."} />}
      {period.sittings.length > 0 && (
        <ul className="min-w-0 space-y-3" aria-label="Sittings">
          {period.sittings.map((sitting) =>
            editingId === sitting.id ? (
              <li key={sitting.id} className="w-full min-w-0">
                <EditSitting
                  period={period}
                  sitting={sitting}
                  onSaved={(s) => {
                    setEditingId(null);
                    saved(s);
                  }}
                  onCancel={() => setEditingId(null)}
                />
              </li>
            ) : (
              <li key={sitting.id} className={`${panelClass} w-full min-w-0 space-y-1 p-4`}>
                <p className="font-bold break-words text-ink">
                  {sitting.subject} <span className="font-medium text-muted">· {sitting.className}</span>
                </p>
                <p className="tnum text-sm text-slate-700">
                  {formatDay(sitting.date)} · {sitting.start}–{sitting.end} · {sitting.durationMinutes} min
                </p>
                <p className="text-sm break-words text-slate-700">{sitting.venue}</p>
                <p className="text-sm break-words whitespace-pre-line text-slate-800">{sitting.instructions}</p>
                {sitting.invigilator && <p className="text-xs text-muted">Invigilator: {sitting.invigilator}</p>}
                {canManage && (
                  <div className="flex flex-wrap gap-2 pt-2">
                    <Button variant="secondary" onClick={() => setEditingId(sitting.id)} disabled={!!removingId}>
                      Edit
                    </Button>
                    <Button variant="secondary" loading={removingId === sitting.id} disabled={!!removingId && removingId !== sitting.id} onClick={() => removeSitting(sitting)}>
                      Remove
                    </Button>
                  </div>
                )}
              </li>
            ),
          )}
        </ul>
      )}
      {canManage && (
        <div className="flex w-full min-w-0 flex-col gap-4">
          <SittingForm period={period} action={add} onSaved={saved} />
          {/* Imported sittings are only added; the reload keeps the forms above, so anything typed there stays. */}
          {!readOnly && (
            <ImportDialog
              kind="exam-sittings"
              scope={{ periodId: period.id }}
              title={`Import sittings for ${period.name}`}
              onDone={() => {
                clearMessages();
                view.reload();
              }}
            />
          )}
          {!period.publishedAt && (
            <Button variant="secondary" loading={publish.busy} onClick={publishPeriod}>
              Publish timetable
            </Button>
          )}
        </div>
      )}
    </div>
  );
}

function bySitting(a: ExamSittingDto, b: ExamSittingDto): number {
  return a.date.localeCompare(b.date) || a.start.localeCompare(b.start) || a.subject.localeCompare(b.subject);
}

function minutesBetween(start: string, end: string): number | null {
  const [sh, sm] = start.split(":").map(Number);
  const [eh, em] = end.split(":").map(Number);
  if (![sh, sm, eh, em].every((n) => Number.isFinite(n))) return null;
  return eh * 60 + em - (sh * 60 + sm);
}

function EditSitting({
  period,
  sitting,
  onSaved,
  onCancel,
}: {
  period: ExamPeriodViewDto;
  sitting: ExamSittingDto;
  onSaved: (sitting: ExamSittingDto) => void;
  onCancel: () => void;
}) {
  const action = useAction();
  return <SittingForm period={period} sitting={sitting} action={action} onSaved={onSaved} onCancel={onCancel} />;
}

/** Adds a sitting, or with `sitting` edits that one in place. */
function SittingForm({
  period,
  sitting,
  action,
  onSaved,
  onCancel,
}: {
  period: ExamPeriodViewDto;
  sitting?: ExamSittingDto;
  action: Action;
  onSaved: (sitting: ExamSittingDto) => void;
  onCancel?: () => void;
}) {
  const academic = useAcademicClasses();
  const staff = useApi<StaffDto[]>("/api/school/staff");
  const [classPick, setClassPick] = useState<string | null>(null);
  const classId = classPick ?? (sitting ? (academic.classes.find((c) => c.name === sitting.className)?.id ?? "") : "");
  const [classSubjectId, setClassSubjectId] = useState(sitting?.classSubjectId ?? "");
  const subjects = useApi<ClassSubjectDto[]>(classId ? `/api/school/classes/${classId}/subjects` : null);
  const [date, setDate] = useState(sitting?.date ?? period.start);
  const [start, setStart] = useState(sitting?.start ?? "09:00");
  const [end, setEnd] = useState(sitting?.end ?? "11:00");
  const [venue, setVenue] = useState(sitting?.venue ?? "");
  const [instructions, setInstructions] = useState(sitting?.instructions ?? "");
  const [invigilatorStaffId, setInvigilatorStaffId] = useState(sitting?.invigilatorStaffId ?? "");
  const [localError, setLocalError] = useState<string | null>(null);
  const duration = minutesBetween(start, end);
  const subjectId = (subjects.data ?? []).some((s) => s.id === classSubjectId) ? classSubjectId : (subjects.data?.[0]?.id ?? "");

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (duration === null || duration <= 0) {
      setLocalError("The exam must end after it starts.");
      return;
    }
    setLocalError(null);
    const body: SaveExamSittingRequest = {
      classSubjectId: subjectId,
      date,
      start,
      end,
      durationMinutes: duration,
      venue: venue.trim(),
      instructions: instructions.trim(),
      invigilatorStaffId: invigilatorStaffId || null,
    };
    const res = await action.run(() =>
      sitting
        ? api<ExamSittingDto>(`/api/school/exam-sittings/${sitting.id}`, { method: "PUT", body })
        : api<ExamSittingDto>(`/api/school/exam-periods/${period.id}/sittings`, { method: "POST", body }),
    );
    if (res.ok) {
      onSaved(res.data);
      if (!sitting) {
        setVenue("");
        setInstructions("");
      }
    }
  }

  return (
    <form className="w-full min-w-0 space-y-3 rounded-panel border border-rule bg-surface p-4" onSubmit={submit}>
      <h2 className="font-semibold text-ink">{sitting ? `Edit the ${sitting.subject} sitting` : "Add a sitting"}</h2>
      <ErrorBox error={localError ?? action.error} />
      <ApiErrorView error={academic.error} onRetry={academic.reload} />
      <ApiErrorView error={subjects.error} onRetry={subjects.reload} />
      <ApiErrorView error={staff.error} onRetry={staff.reload} />
      <div className="grid w-full min-w-0 gap-3 sm:grid-cols-2">
        <Select
          label="Class"
          value={classId}
          onChange={(e) => {
            setClassPick(e.target.value);
            setClassSubjectId("");
          }}
        >
          <option value="">Choose a class</option>
          {academic.classes.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
        <Select label="Subject" value={subjectId} onChange={(e) => setClassSubjectId(e.target.value)} disabled={!classId}>
          {(subjects.data ?? []).length === 0 && <option value="">{subjects.loading ? "Loading…" : "No subjects yet"}</option>}
          {(subjects.data ?? []).map((s) => (
            <option key={s.id} value={s.id}>
              {s.subjectName}
            </option>
          ))}
        </Select>
        <Input label="Date" type="date" required min={period.start} max={period.end} value={date} onChange={(e) => setDate(e.target.value)} />
        <Input label="Venue" required maxLength={200} value={venue} onChange={(e) => setVenue(e.target.value)} />
        <Input label="Starts" type="time" required value={start} onChange={(e) => setStart(e.target.value)} />
        <Input label="Ends" type="time" required value={end} onChange={(e) => setEnd(e.target.value)} hint={duration !== null && duration > 0 ? `${duration} minutes` : "End after the start."} />
      </div>
      <Textarea label="Instructions" required maxLength={4000} value={instructions} onChange={(e) => setInstructions(e.target.value)} />
      <Select label="Invigilator" value={invigilatorStaffId} onChange={(e) => setInvigilatorStaffId(e.target.value)} hint="Optional. A clash is saved and shown as a warning.">
        <option value="">No invigilator</option>
        {(staff.data ?? []).map((member) => (
          <option key={member.staffId} value={member.staffId}>
            {member.fullName}
          </option>
        ))}
      </Select>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" loading={action.busy} disabled={!subjectId}>
          {sitting ? "Save sitting" : "Add sitting"}
        </Button>
        {onCancel && (
          <Button variant="secondary" onClick={onCancel} disabled={action.busy}>
            Cancel
          </Button>
        )}
      </div>
    </form>
  );
}
