"use client";

import { useParams, useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { api, upload } from "@/lib/api";
import { formatDay, lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { RELATIONSHIPS } from "@/lib/school";
import { useApi } from "@/lib/use-api";
import type { ClassDto, LevelDto, StudentDto, StudentGuardianDto, StudentStatus } from "@/lib/types";
import { GuardianInvite } from "@/components/guardian-invite";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  Checkbox,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  RuledSheet,
  SectionTitle,
  Select,
  Textarea,
} from "@/components/ui";
import { STATUS_TONE, StudentForm } from "../student-form";

export default function StudentPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const canManage = useCan("school.manage");
  const { data: s, error, reload, setData } = useApi<StudentDto>(`/api/school/students/${id}`);
  const classes = useApi<ClassDto[]>("/api/school/classes");
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const [dialog, setDialog] = useState<"edit" | "transfer" | "status" | null>(null);
  const del = useAction();

  if (!s) {
    return (
      <div>
        <PageHeader title="Student" back="/app/students" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const cls = classes.data?.find((c) => c.id === s.classId);
  const level = levels.data?.find((l) => l.id === cls?.levelId);

  async function remove() {
    if (!s || !window.confirm(`Delete ${s.fullName}? Only do this for a record added by mistake.`)) return;
    if ((await del.run(() => api(`/api/school/students/${s.id}`, { method: "DELETE" }))).ok) router.replace("/app/students");
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title={s.fullName}
        back="/app/students"
        subtitle={
          <span className="flex flex-wrap items-center gap-2">
            <span className="tnum">{s.admissionNo}</span>
            <span>{s.className ?? "No class"}</span>
            <Badge tone={STATUS_TONE[s.status]}>{s.status}</Badge>
          </span>
        }
        action={
          canManage ? (
            <>
              <Button variant="secondary" onClick={() => setDialog("edit")}>
                Edit
              </Button>
              {s.status === "Active" && (
                <Button variant="secondary" onClick={() => setDialog("transfer")}>
                  Change class
                </Button>
              )}
              <Button variant="secondary" onClick={() => setDialog("status")}>
                {s.status === "Active" ? "Withdraw or graduate" : "Change status"}
              </Button>
            </>
          ) : undefined
        }
      />
      <ErrorBox error={del.error} />

      <div className="grid gap-5 md:grid-cols-[14rem_minmax(0,1fr)]">
        <Photo student={s} canManage={canManage} onChange={setData} />
        <Card>
          <SectionTitle>Details</SectionTitle>
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-6 gap-y-2 text-sm">
            <dt className="text-muted">Gender</dt>
            <dd>{s.gender}</dd>
            <dt className="text-muted">Date of birth</dt>
            <dd className="tnum">{formatDay(s.dateOfBirth)}</dd>
            <dt className="text-muted">Admitted</dt>
            <dd className="tnum">{formatDay(s.admittedAt)}</dd>
            {s.status !== "Active" && (
              <>
                <dt className="text-muted">{s.status}</dt>
                <dd>
                  <span className="tnum">{formatDay(s.statusDate)}</span>
                  {s.statusReason && <span className="block text-slate-700">{s.statusReason}</span>}
                </dd>
              </>
            )}
          </dl>
        </Card>
      </div>

      <Guardians student={s} canManage={canManage} onChange={reload} />
      {canManage && level?.hasStudentLogins && s.status === "Active" && <StudentLogin student={s} />}

      {canManage && (
        <div className="border-t border-rule pt-4">
          <Button variant="danger" onClick={remove} loading={del.busy}>
            Delete this record
          </Button>
          <p className="mt-1 text-xs text-muted">For mistakes only. Students with attendance or other records are withdrawn or transferred instead.</p>
        </div>
      )}

      {dialog === "edit" && classes.data && (
        <StudentForm
          student={s}
          classes={classes.data}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null);
            reload();
          }}
        />
      )}
      {dialog === "transfer" && classes.data && (
        <Transfer
          student={s}
          classes={classes.data}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null);
            reload();
          }}
        />
      )}
      {dialog === "status" && (
        <StatusChange
          student={s}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");
}

/** The private photo: shown through a short-lived link; JPEG, PNG or WebP. */
function Photo({ student, canManage, onChange }: { student: StudentDto; canManage: boolean; onChange: (s: StudentDto) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const { busy, error, run } = useAction();
  return (
    <div className="space-y-2">
      <div className="flex aspect-[4/5] w-full max-w-56 items-center justify-center overflow-hidden rounded-panel border border-rule bg-ruled-2">
        {student.photoUrl ? (
          <img src={student.photoUrl} alt={`Photo of ${student.fullName}`} className="h-full w-full object-cover" />
        ) : (
          <span className="slab text-4xl font-bold text-faint" aria-hidden="true">
            {initials(student.fullName)}
          </span>
        )}
      </div>
      {canManage && (
        <>
          <input
            ref={input}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="sr-only"
            aria-label="Choose a photo"
            onChange={async (e) => {
              const file = e.target.files?.[0];
              e.target.value = "";
              if (!file) return;
              const res = await run(() => upload<StudentDto>(`/api/school/students/${student.id}/photo`, file));
              if (res.ok && res.data) onChange(res.data);
            }}
          />
          <Button variant="secondary" loading={busy} onClick={() => input.current?.click()}>
            {student.photoUrl ? "Change photo" : "Add a photo"}
          </Button>
          <ErrorBox error={error} />
        </>
      )}
    </div>
  );
}

function Transfer({ student, classes, onClose, onSaved }: { student: StudentDto; classes: ClassDto[]; onClose: () => void; onSaved: () => void }) {
  const others = classes.filter((c) => c.id !== student.classId);
  const [classId, setClassId] = useState(others[0]?.id ?? "");
  const { busy, error, run } = useAction();
  return (
    <Modal open title={`Change ${student.firstName}'s class`} onClose={onClose}>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          if ((await run(() => api(`/api/school/students/${student.id}/transfer`, { method: "POST", body: { classId } }))).ok) onSaved();
        }}
        className="space-y-4"
      >
        <p className="text-sm text-slate-700">Moves {student.firstName} to another class in this session. Earlier records stay with the old class.</p>
        <Select label="New class" value={classId} onChange={(e) => setClassId(e.target.value)} error={error?.field("classId")}>
          {others.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
        <ErrorBox error={error && !error.field("classId") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!classId}>
            Change class
          </Button>
        </div>
      </form>
    </Modal>
  );
}

const STATUS_TEXT: Record<StudentStatus, string> = {
  Active: "Back in school. Choose a class afterwards if needed.",
  Graduated: "Finished at this school.",
  Withdrawn: "Left the school.",
  Transferred: "Moved to another school.",
};

function StatusChange({ student, onClose, onSaved }: { student: StudentDto; onClose: () => void; onSaved: () => void }) {
  const choices = (["Withdrawn", "Transferred", "Graduated", "Active"] as StudentStatus[]).filter((x) => x !== student.status);
  const [status, setStatus] = useState<StudentStatus>(choices[0]);
  const [date, setDate] = useState(lagosToday());
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = { status, date: status === "Active" ? null : date || null, reason: reason.trim() || null };
    if ((await run(() => api(`/api/school/students/${student.id}/status`, { method: "POST", body }))).ok) onSaved();
  }

  return (
    <Modal open title={`Change ${student.firstName}'s status`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <fieldset>
          <legend className="text-sm font-medium text-slate-700">New status</legend>
          <div className="mt-1.5 grid gap-2">
            {choices.map((c) => (
              <label key={c} className="flex min-h-11 cursor-pointer items-start gap-3 rounded-control border border-rule p-3 has-[:checked]:border-blazer has-[:checked]:bg-blazer-wash">
                <input type="radio" name="status" checked={status === c} onChange={() => setStatus(c)} className="mt-1 accent-[#6b1d33]" />
                <span>
                  <span className="block text-sm font-bold text-ink">{c}</span>
                  <span className="block text-xs text-slate-600">{STATUS_TEXT[c]}</span>
                </span>
              </label>
            ))}
          </div>
        </fieldset>
        {status !== "Active" && <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} error={error?.field("date")} />}
        <Textarea label="Reason" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} error={error?.field("reason")} hint="Optional. Staff see this on the record." />
        {status !== "Active" && <Notice tone="warning">The record is kept. Parent and student access to it ends.</Notice>}
        <ErrorBox error={error && !error.field("date") && !error.field("reason") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Save status
          </Button>
        </div>
      </form>
    </Modal>
  );
}

// ---------- Guardians ----------

function Guardians({ student, canManage, onChange }: { student: StudentDto; canManage: boolean; onChange: () => void }) {
  const [editing, setEditing] = useState<{ guardian: StudentGuardianDto | null } | null>(null);
  const action = useAction();
  const guardians = student.guardians ?? [];

  return (
    <section aria-labelledby="guardians" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="guardians" className="slab text-xl font-bold text-ink">
          Parents and guardians
        </h2>
        {canManage && (
          <Button variant="secondary" onClick={() => setEditing({ guardian: null })}>
            Add a guardian
          </Button>
        )}
      </div>
      <ErrorBox error={action.error} />
      {guardians.length === 0 ? (
        <p className="text-sm text-muted">No parent or guardian yet. Add one so the school can invite them.</p>
      ) : (
        <RuledSheet label="Parents and guardians">
          {guardians.map((g) => (
            <li key={g.guardianId} className="ruled-pad space-y-2 py-3">
              <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-bold text-ink">
                    {g.fullName}
                    <span className="text-sm font-normal text-muted">{g.relationship}</span>
                    {g.isPrimary && <Badge tone="blazer">Main contact</Badge>}
                    {g.hasAccount && <Badge tone="pass">Has a parent account</Badge>}
                  </p>
                  <p className="tnum text-sm text-slate-700">{g.phone ?? "No phone"}</p>
                  <p className="text-sm break-all text-slate-700">{g.email ?? "No email"}</p>
                </div>
                {canManage && (
                  <div className="flex flex-wrap gap-1">
                    <Button variant="ghost" className="px-2.5" onClick={() => setEditing({ guardian: g })}>
                      Edit
                    </Button>
                    {!g.isPrimary && (
                      <Button
                        variant="ghost"
                        className="px-2.5"
                        onClick={async () => {
                          if ((await action.run(() => api(`/api/school/students/${student.id}/guardians/${g.guardianId}/primary`, { method: "POST" }))).ok) onChange();
                        }}
                      >
                        Make main contact
                      </Button>
                    )}
                    <Button
                      variant="ghost"
                      className="px-2.5 text-brick"
                      onClick={async () => {
                        if (!window.confirm(`Remove ${g.fullName} from ${student.firstName}'s record?`)) return;
                        if ((await action.run(() => api(`/api/school/students/${student.id}/guardians/${g.guardianId}`, { method: "DELETE" }))).ok) onChange();
                      }}
                    >
                      Remove
                    </Button>
                  </div>
                )}
              </div>
              {canManage && (g.email || g.phone) && <GuardianInvite guardianId={g.guardianId} hasEmail={!!g.email} hasAccount={g.hasAccount} />}
            </li>
          ))}
        </RuledSheet>
      )}
      {editing && (
        <GuardianForm
          studentId={student.id}
          guardian={editing.guardian}
          first={guardians.length === 0}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            onChange();
          }}
        />
      )}
    </section>
  );
}

function GuardianForm({
  studentId,
  guardian,
  first,
  onClose,
  onSaved,
}: {
  studentId: string;
  guardian: StudentGuardianDto | null;
  first: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    fullName: guardian?.fullName ?? "",
    phone: guardian?.phone ?? "",
    email: guardian?.email ?? "",
    relationship: guardian?.relationship ?? "Mother",
  });
  const [primary, setPrimary] = useState(guardian?.isPrimary ?? first);
  const { busy, error, run } = useAction();
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = {
      fullName: form.fullName.trim(),
      phone: form.phone.trim() || null,
      email: form.email.trim() || null,
      relationship: form.relationship.trim() || null,
      isPrimary: primary,
    };
    const res = await run(() =>
      guardian
        ? api(`/api/school/students/${studentId}/guardians/${guardian.guardianId}`, { method: "PUT", body })
        : api(`/api/school/students/${studentId}/guardians`, { method: "POST", body }),
    );
    if (res.ok) onSaved();
  }

  const fields = ["fullName", "phone", "email", "relationship"];
  return (
    <Modal open title={guardian ? `Edit ${guardian.fullName}` : "Add a parent or guardian"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Full name" required autoComplete="off" value={form.fullName} onChange={set("fullName")} error={error?.field("fullName")} />
        <Select label="Relationship" value={form.relationship} onChange={set("relationship")} error={error?.field("relationship")}>
          {[...new Set([...RELATIONSHIPS, form.relationship])].map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </Select>
        <Input
          label="Phone number"
          type="tel"
          autoComplete="off"
          placeholder="0803 123 4567"
          value={form.phone}
          onChange={set("phone")}
          error={error?.field("phone")}
          hint="Used to send the invitation on WhatsApp. A parent with the same number is shared between siblings."
        />
        <Input label="Email" type="email" autoComplete="off" value={form.email} onChange={set("email")} error={error?.field("email")} hint="The invitation and sign-in codes go here." />
        <Checkbox checked={primary} onChange={setPrimary} label="Main contact for this student" />
        <ErrorBox error={error && !fields.some((f) => error.field(f)) ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {guardian ? "Save guardian" : "Add guardian"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}

// ---------- Student login (levels with student logins) ----------

function StudentLogin({ student }: { student: StudentDto }) {
  const [password, setPassword] = useState("");
  const [done, setDone] = useState(false);
  const { busy, error, run } = useAction();
  return (
    <Card>
      <SectionTitle>Student sign-in</SectionTitle>
      <p className="-mt-1 mb-4 max-w-prose text-sm text-slate-700">
        {student.firstName} signs in on the school&apos;s page with admission number <strong className="tnum">{student.admissionNo}</strong> and this password.
        Setting a new one replaces the old one.
      </p>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setDone(false);
          if ((await run(() => api(`/api/school/students/${student.id}/login`, { method: "POST", body: { password } }))).ok) {
            setDone(true);
            setPassword("");
          }
        }}
        className="flex max-w-md flex-wrap items-end gap-2"
      >
        <div className="min-w-48 flex-1">
          <Input
            label="New password"
            type="text"
            autoComplete="off"
            spellCheck={false}
            minLength={8}
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            hint="At least 8 characters. Write it down for the student."
            error={error?.field("password")}
          />
        </div>
        <Button type="submit" variant="secondary" loading={busy} className="mb-6">
          Set password
        </Button>
      </form>
      <ErrorBox error={error && !error.field("password") ? error : null} />
      {done && <Notice tone="success">Password set. {student.firstName} can sign in now.</Notice>}
    </Card>
  );
}
