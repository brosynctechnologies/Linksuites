"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import { STAGES } from "@/lib/school";
import type { AssessmentMode, LevelDto, Stage } from "@/lib/types";
import { ApiErrorView, Badge, Button, Checkbox, EmptyState, ErrorBox, Input, Modal, PageLoader, RuledSheet, Select, ruledRowClass } from "@/components/ui";


type Editing = { level: LevelDto | null } | null;

export default function LevelsPage() {
  const canManage = useCan("school.manage");
  const { data, error, reload } = useApi<LevelDto[]>("/api/school/levels");
  const [editing, setEditing] = useState<Editing>(null);
  const del = useAction();

  async function remove(level: LevelDto) {
    if (!window.confirm(`Delete ${level.name}? Levels with classes can't be deleted.`)) return;
    if ((await del.run(() => api(`/api/school/levels/${level.id}`, { method: "DELETE" }))).ok) reload();
  }

  const levels = [...(data ?? [])].sort((a, b) => a.order - b.order);
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-prose text-sm text-slate-700">Levels run in order, lowest first. Promotion moves each class up to the next level.</p>
        {canManage && <Button onClick={() => setEditing({ level: null })}>Add a level</Button>}
      </div>
      <ErrorBox error={del.error} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && levels.length === 0 && (
        <EmptyState title="No levels yet" text="Apply the Nigerian template on the setup overview, or add your levels one by one." />
      )}
      {levels.length > 0 && (
        <RuledSheet
          label="Levels"
          template="3rem minmax(0,1fr) 8rem 8.5rem 7.5rem 7rem"
          head={[{ label: "Order" }, { label: "Level" }, { label: "Stage" }, { label: "Assessment" }, { label: "Student logins" }, { label: "" }]}
        >
          {levels.map((l) => (
            <li key={l.id} className={ruledRowClass}>
              <span className="tnum hidden text-muted md:block">{l.order}</span>
              <span className="font-bold text-ink">{l.name}</span>
              <span className="col-start-1 text-sm text-muted md:col-start-auto">{STAGES.find((s) => s.value === l.stage)?.label ?? l.stage}</span>
              <span className="col-start-1 text-sm text-muted md:col-start-auto">{l.assessmentMode}</span>
              <span className="col-start-1 md:col-start-auto">{l.hasStudentLogins ? <Badge tone="pass">On</Badge> : <span className="text-sm text-muted">Off</span>}</span>
              {canManage ? (
                <span className="col-start-2 row-span-4 row-start-1 flex justify-end gap-1 md:col-start-auto md:row-span-1 md:row-start-auto">
                  <Button variant="ghost" className="px-2.5" onClick={() => setEditing({ level: l })}>
                    Edit
                  </Button>
                  <Button variant="ghost" className="px-2.5 text-brick" onClick={() => remove(l)}>
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
        <LevelForm
          level={editing.level}
          nextOrder={(levels.at(-1)?.order ?? 0) + 1}
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

/** The mode the API stores when the request leaves assessmentMode out. */
function modeFor(stage: Stage): AssessmentMode {
  return stage === "Creche" || stage === "Nursery" ? "Developmental" : "Numeric";
}

function LevelForm({ level, nextOrder, onClose, onSaved }: { level: LevelDto | null; nextOrder: number; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(level?.name ?? "");
  const [stage, setStage] = useState<Stage>(level?.stage ?? "Primary");
  const [mode, setMode] = useState<AssessmentMode>(level?.assessmentMode ?? modeFor(level?.stage ?? "Primary"));
  const [order, setOrder] = useState(String(level?.order ?? nextOrder));
  const [logins, setLogins] = useState(level?.hasStudentLogins ?? false);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = { name: name.trim(), stage, order: Number(order) || 0, hasStudentLogins: logins, assessmentMode: mode };
    const ok = await run(() =>
      level ? api(`/api/school/levels/${level.id}`, { method: "PUT", body }) : api("/api/school/levels", { method: "POST", body }),
    );
    if (ok.ok) onSaved();
  }

  return (
    <Modal open title={level ? `Edit ${level.name}` : "Add a level"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Name" required placeholder="e.g. JSS 1" value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} />
        <Select
          label="Stage"
          value={stage}
          onChange={(e) => {
            const s = e.target.value as Stage;
            setStage(s);
            if (!level) {
              setLogins(s === "Jss" || s === "Sss");
              setMode(modeFor(s));
            }
          }}
        >
          {STAGES.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </Select>
        <Select label="Assessment mode" value={mode} onChange={(e) => setMode(e.target.value as AssessmentMode)} error={error?.field("assessmentMode")}>
          <option value="Numeric">Numeric</option>
          <option value="Developmental">Developmental</option>
        </Select>
        <Input label="Order" type="number" inputMode="numeric" min={0} value={order} onChange={(e) => setOrder(e.target.value)} error={error?.field("order")} hint="Lower numbers come first." />
        <Checkbox checked={logins} onChange={setLogins} label="Students at this level can sign in (admission number and password)" />
        <ErrorBox error={error && !error.field("name") && !error.field("order") && !error.field("assessmentMode") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {level ? "Save level" : "Add level"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
