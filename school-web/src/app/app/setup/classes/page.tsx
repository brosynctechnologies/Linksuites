"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api, type ApiError } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ClassDto, ClassSubjectDto, LevelDto, StaffDto, SubjectDto } from "@/lib/types";
import { ApiErrorView, Button, EmptyState, ErrorBox, Input, Modal, Notice, PageLoader, RuledSheet, Select, linkClass, ruledRowClass } from "@/components/ui";

function staffName(staff: StaffDto[] | undefined, id: string | null) {
  if (!id) return null;
  return staff?.find((s) => s.staffId === id)?.fullName ?? "A former member of staff";
}

export default function ClassesPage() {
  const canManage = useCan("school.manage");
  const classes = useApi<ClassDto[]>("/api/school/classes");
  const levels = useApi<LevelDto[]>("/api/school/levels");
  // Only leadership can list staff; teachers and bursars see classes without teacher names.
  const staff = useApi<StaffDto[]>(canManage ? "/api/school/staff" : null);
  const [adding, setAdding] = useState(false);
  const [subjectsFor, setSubjectsFor] = useState<ClassDto | null>(null);
  const [teacherFor, setTeacherFor] = useState<ClassDto | null>(null);
  const del = useAction();

  async function remove(c: ClassDto) {
    if (!window.confirm(`Delete ${c.name}? Classes with students can't be deleted.`)) return;
    if ((await del.run(() => api(`/api/school/classes/${c.id}`, { method: "DELETE" }))).ok) classes.reload();
  }

  const noLevels = levels.data?.length === 0;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-prose text-sm text-slate-700">Classes for the current session. A class is a level and an arm, such as JSS 1 and A.</p>
        {canManage && !noLevels && (
          <div className="flex flex-wrap items-start gap-2">
            <ImportDialog kind="classes" title="Import classes" label="Import classes" onDone={classes.reload} />
            {/* Which subjects each class takes and who teaches them, for every class at once (the Subjects button edits one class). */}
            <ImportDialog kind="class-subjects" title="Import class subjects" label="Import class subjects" />
            <Button onClick={() => setAdding(true)}>Add classes</Button>
          </div>
        )}
      </div>
      <ErrorBox error={del.error} />
      {(classes.error || levels.error) && <ApiErrorView error={classes.error ?? levels.error} onRetry={classes.reload} />}
      {(!classes.data || !levels.data) && !classes.error && !levels.error && <PageLoader />}
      {noLevels && (
        <EmptyState
          title="Add your levels first"
          text="Classes belong to a level. Apply the Nigerian template to add Creche to SSS 3 in one go."
          action={
            <Link href="/app/setup" className={linkClass}>
              Go to the template
            </Link>
          }
        />
      )}
      {classes.data && levels.data && !noLevels && classes.data.length === 0 && (
        <EmptyState title="No classes yet" text="Add the arms you run at each level, such as A and B." />
      )}
      {classes.data && classes.data.length > 0 && (
        <RuledSheet label="Classes" template="8rem minmax(0,1fr) 15rem" head={[{ label: "Class" }, { label: "Class teacher" }, { label: "" }]}>
          {classes.data.map((c) => (
            <li key={c.id} className={ruledRowClass}>
              <span className="slab text-lg font-bold text-ink">{c.name}</span>
              <span className="col-start-1 text-sm text-muted md:col-start-auto">
                {canManage ? (staffName(staff.data, c.classTeacherStaffId) ?? "No class teacher") : c.levelName}
              </span>
              {canManage ? (
                <span className="col-start-2 row-span-2 row-start-1 flex flex-wrap justify-end gap-1 md:col-start-auto md:row-span-1 md:row-start-auto">
                  <Button variant="ghost" className="px-2.5" onClick={() => setSubjectsFor(c)}>
                    Subjects
                  </Button>
                  <Button variant="ghost" className="px-2.5" onClick={() => setTeacherFor(c)}>
                    Teacher
                  </Button>
                  <Button variant="ghost" className="px-2.5 text-brick" onClick={() => remove(c)}>
                    Delete
                  </Button>
                </span>
              ) : (
                <span className="col-start-2 row-start-1 md:col-start-auto md:row-start-auto">
                  <Button variant="ghost" className="px-2.5" onClick={() => setSubjectsFor(c)}>
                    Subjects
                  </Button>
                </span>
              )}
            </li>
          ))}
        </RuledSheet>
      )}
      {adding && levels.data && (
        <AddClasses
          levels={levels.data}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false);
            classes.reload();
          }}
        />
      )}
      {teacherFor && (
        <ClassTeacher
          cls={teacherFor}
          staff={staff.data ?? []}
          onClose={() => setTeacherFor(null)}
          onSaved={() => {
            setTeacherFor(null);
            classes.reload();
          }}
        />
      )}
      {subjectsFor && <ClassSubjects cls={subjectsFor} staff={staff.data} canManage={canManage} onClose={() => setSubjectsFor(null)} />}
    </div>
  );
}

/** Add one or more arms of a level at once: "A, B, C" makes JSS 1A, JSS 1B and JSS 1C. */
function AddClasses({ levels, onClose, onSaved }: { levels: LevelDto[]; onClose: () => void; onSaved: () => void }) {
  const sorted = [...levels].sort((a, b) => a.order - b.order);
  const [levelId, setLevelId] = useState(sorted[0]?.id ?? "");
  const [arms, setArms] = useState("A");
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<{ arm: string; error: ApiError }[]>([]);
  const action = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const list = [...new Set(arms.split(/[,\s]+/).map((a) => a.trim()).filter(Boolean))];
    if (list.length === 0) list.push("");
    setBusy(true);
    const failed: { arm: string; error: ApiError }[] = [];
    for (const arm of list) {
      const res = await action.run(() => api("/api/school/classes", { method: "POST", body: { levelId, arm } }));
      if (!res.ok) failed.push({ arm, error: res.error });
    }
    setBusy(false);
    if (failed.length === 0) onSaved();
    else {
      setErrors(failed);
      setArms(failed.map((f) => f.arm).join(", "));
    }
  }

  return (
    <Modal open title="Add classes" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Select label="Level" value={levelId} onChange={(e) => setLevelId(e.target.value)}>
          {sorted.map((l) => (
            <option key={l.id} value={l.id}>
              {l.name}
            </option>
          ))}
        </Select>
        <Input label="Arms" value={arms} onChange={(e) => setArms(e.target.value)} hint="Separate arms with commas, for example A, B, C. Each one becomes a class." />
        {errors.length > 0 && (
          <Notice tone="error">
            <ul className="space-y-0.5">
              {errors.map((f) => (
                <li key={f.arm}>
                  {f.arm ? `${f.arm}: ` : ""}
                  {f.error.message}
                </li>
              ))}
            </ul>
          </Notice>
        )}
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Add classes
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function StaffOptions({ staff }: { staff: StaffDto[] }) {
  return (
    <>
      <option value="">No one yet</option>
      {staff.map((s) => (
        <option key={s.staffId} value={s.staffId}>
          {s.fullName}
        </option>
      ))}
    </>
  );
}

function ClassTeacher({ cls, staff, onClose, onSaved }: { cls: ClassDto; staff: StaffDto[]; onClose: () => void; onSaved: () => void }) {
  const [staffId, setStaffId] = useState(cls.classTeacherStaffId ?? "");
  const { busy, error, run } = useAction();
  return (
    <Modal open title={`Class teacher for ${cls.name}`} onClose={onClose}>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          if ((await run(() => api(`/api/school/classes/${cls.id}/class-teacher`, { method: "PUT", body: { staffId: staffId || null } }))).ok) onSaved();
        }}
        className="space-y-4"
      >
        <Select label="Class teacher" value={staffId} onChange={(e) => setStaffId(e.target.value)} hint="The class teacher takes the register and writes the term remark.">
          <StaffOptions staff={staff} />
        </Select>
        <ErrorBox error={error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Save
          </Button>
        </div>
      </form>
    </Modal>
  );
}

/** The subjects taught in one class, each with its subject teacher. */
function ClassSubjects({ cls, staff, canManage, onClose }: { cls: ClassDto; staff: StaffDto[] | undefined; canManage: boolean; onClose: () => void }) {
  const links = useApi<ClassSubjectDto[]>(`/api/school/classes/${cls.id}/subjects`);
  const subjects = useApi<SubjectDto[]>("/api/school/subjects");
  const [adding, setAdding] = useState("");
  const action = useAction();

  const taken = new Set((links.data ?? []).map((l) => l.subjectId));
  const available = (subjects.data ?? []).filter((s) => !taken.has(s.id));

  async function add(subjectIds: string[]) {
    for (const id of subjectIds) {
      const res = await action.run(() => api(`/api/school/classes/${cls.id}/subjects`, { method: "POST", body: { subjectId: id } }));
      if (!res.ok) break;
    }
    setAdding("");
    links.reload();
  }

  return (
    <Modal open wide title={`Subjects in ${cls.name}`} onClose={onClose}>
      <div className="space-y-4">
        <ErrorBox error={action.error ?? links.error ?? subjects.error} />
        {!links.data && !links.error && <PageLoader />}
        {links.data && links.data.length === 0 && <p className="text-sm text-muted">No subjects in this class yet.</p>}
        {links.data && links.data.length > 0 && (
          <RuledSheet label={`Subjects in ${cls.name}`}>
            {links.data.map((l) => (
              <li key={l.id} className="ruled-pad flex flex-wrap items-center justify-between gap-x-3 gap-y-2 py-2.5">
                <span className="min-w-0">
                  <span className="font-bold text-ink">{l.subjectName}</span> <span className="text-xs text-muted">{l.subjectCode}</span>
                </span>
                {canManage && staff ? (
                  <span className="flex items-center gap-1">
                    <Select
                      label={`Teacher for ${l.subjectName}`}
                      srLabel
                      value={l.teacherStaffId ?? ""}
                      onChange={async (e) => {
                        const staffId = e.target.value || null;
                        if ((await action.run(() => api(`/api/school/class-subjects/${l.id}/teacher`, { method: "PUT", body: { staffId } }))).ok) links.reload();
                      }}
                      className="w-48"
                    >
                      <StaffOptions staff={staff} />
                    </Select>
                    <Button
                      variant="ghost"
                      className="px-2.5 text-brick"
                      aria-label={`Remove ${l.subjectName}`}
                      onClick={async () => {
                        if ((await action.run(() => api(`/api/school/classes/${cls.id}/subjects/${l.subjectId}`, { method: "DELETE" }))).ok) links.reload();
                      }}
                    >
                      Remove
                    </Button>
                  </span>
                ) : (
                  <span className="text-sm text-muted">{staffName(staff, l.teacherStaffId) ?? ""}</span>
                )}
              </li>
            ))}
          </RuledSheet>
        )}
        {canManage && subjects.data && available.length > 0 && (
          <div className="flex flex-wrap items-end gap-2">
            <div className="min-w-48 flex-1">
              <Select label="Add a subject" value={adding} onChange={(e) => setAdding(e.target.value)}>
                <option value="">Choose a subject</option>
                {available.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </Select>
            </div>
            <Button variant="secondary" disabled={!adding} loading={action.busy} onClick={() => add([adding])}>
              Add
            </Button>
            {links.data?.length === 0 && (
              <Button variant="secondary" loading={action.busy} onClick={() => add(available.map((s) => s.id))}>
                Add all {available.length}
              </Button>
            )}
          </div>
        )}
        {canManage && subjects.data?.length === 0 && (
          <p className="text-sm text-muted">
            No subjects yet.{" "}
            <Link href="/app/setup/subjects" className={linkClass}>
              Add subjects
            </Link>
          </p>
        )}
      </div>
    </Modal>
  );
}
