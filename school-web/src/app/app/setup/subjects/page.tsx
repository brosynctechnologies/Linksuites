"use client";

import { useState, type FormEvent } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { SubjectDto } from "@/lib/types";
import { ApiErrorView, Button, EmptyState, ErrorBox, Input, Modal, PageLoader, RuledSheet, ruledRowClass } from "@/components/ui";

export default function SubjectsPage() {
  const canManage = useCan("school.manage");
  const { data, error, reload } = useApi<SubjectDto[]>("/api/school/subjects");
  const [editing, setEditing] = useState<{ subject: SubjectDto | null } | null>(null);
  const del = useAction();

  async function remove(s: SubjectDto) {
    if (!window.confirm(`Delete ${s.name}? Subjects taught in a class can't be deleted.`)) return;
    if ((await del.run(() => api(`/api/school/subjects/${s.id}`, { method: "DELETE" }))).ok) reload();
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-prose text-sm text-slate-700">Every subject the school teaches. Choose which classes take each one on the classes page.</p>
        {canManage && (
          <div className="flex flex-wrap items-start gap-2">
            <ImportDialog kind="subjects" title="Import subjects" onDone={reload} />
            <Button onClick={() => setEditing({ subject: null })}>Add a subject</Button>
          </div>
        )}
      </div>
      <ErrorBox error={del.error} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && data.length === 0 && <EmptyState title="No subjects yet" text="Apply the template on the setup overview, or add your subjects one by one." />}
      {data && data.length > 0 && (
        <RuledSheet label="Subjects" template="minmax(0,1fr) 7rem 9rem" head={[{ label: "Subject" }, { label: "Code" }, { label: "" }]}>
          {data.map((s) => (
            <li key={s.id} className={ruledRowClass}>
              <span className="font-bold text-ink">{s.name}</span>
              <span className="tnum col-start-1 text-sm text-muted md:col-start-auto">{s.code}</span>
              {canManage ? (
                <span className="col-start-2 row-span-2 row-start-1 flex justify-end gap-1 md:col-start-auto md:row-span-1 md:row-start-auto">
                  <Button variant="ghost" className="px-2.5" onClick={() => setEditing({ subject: s })}>
                    Edit
                  </Button>
                  <Button variant="ghost" className="px-2.5 text-brick" onClick={() => remove(s)}>
                    Delete
                  </Button>
                </span>
              ) : (
                <span />
              )}
            </li>
          ))}
        </RuledSheet>
      )}
      {editing && (
        <SubjectForm
          subject={editing.subject}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function SubjectForm({ subject, onClose, onSaved }: { subject: SubjectDto | null; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(subject?.name ?? "");
  const [code, setCode] = useState(subject?.code ?? "");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = { name: name.trim(), code: code.trim() };
    const res = await run(() =>
      subject ? api(`/api/school/subjects/${subject.id}`, { method: "PUT", body }) : api("/api/school/subjects", { method: "POST", body }),
    );
    if (res.ok) onSaved();
  }

  return (
    <Modal open title={subject ? `Edit ${subject.name}` : "Add a subject"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Name" required placeholder="e.g. Basic Science" value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} />
        <Input
          label="Code"
          placeholder="e.g. BSC"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          error={error?.field("code")}
          hint="A short code for the report card and score sheets."
        />
        <ErrorBox error={error && !error.field("name") && !error.field("code") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {subject ? "Save subject" : "Add subject"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
