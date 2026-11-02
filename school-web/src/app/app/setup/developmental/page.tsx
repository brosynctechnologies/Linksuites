"use client";

import { useState, type ReactNode } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { DevelopmentalAreasDto, DevelopmentalScaleDto } from "@/lib/types";
import { ApiErrorView, Button, ErrorBox, Input, Notice, PageLoader, Tabs } from "@/components/ui";
import { LevelScope } from "../level-scope";

type Tab = "areas" | "scale";

export default function DevelopmentalPage() {
  const [levelId, setLevelId] = useState("");
  const [tab, setTab] = useState<Tab>("areas");
  const [notice, setNotice] = useState<string | null>(null);
  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        Areas a creche or nursery class is rated on, and the scale those ratings use. A level can have its own list; otherwise it uses the school&apos;s.
      </p>
      <LevelScope
        value={levelId}
        onChange={(v) => {
          setNotice(null);
          setLevelId(v);
        }}
      />
      <Tabs
        label="Developmental setup"
        items={[
          { value: "areas", label: "Areas" },
          { value: "scale", label: "Scale" },
        ]}
        value={tab}
        onChange={(v) => {
          setNotice(null);
          setTab(v);
        }}
      />
      {notice && <Notice tone="success">{notice}</Notice>}
      <div className={tab === "areas" ? undefined : "hidden"}>
        <Areas levelId={levelId} onSaved={setNotice} />
      </div>
      <div className={tab === "scale" ? undefined : "hidden"}>
        <Scale levelId={levelId} onSaved={setNotice} />
      </div>
    </div>
  );
}

interface AreaRow {
  key: number;
  id: string | null;
  name: string;
}
interface ScaleRow {
  key: number;
  id: string | null;
  code: string;
  label: string;
}

let nextKey = 1;

function Areas({ levelId, onSaved }: { levelId: string; onSaved: (message: string) => void }) {
  const { data, error, reload } = useApi<DevelopmentalAreasDto>(`/api/school/developmental/areas${qs({ levelId })}`);
  return (
    <>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <AreasEditor
          key={`${levelId}:${data.levelId ?? "school"}:${data.areas.map((a) => a.id).join()}`}
          levelId={levelId}
          data={data}
          onSaved={(message) => {
            onSaved(message);
            reload();
          }}
        />
      )}
    </>
  );
}

function AreasEditor({
  levelId,
  data,
  onSaved,
}: {
  levelId: string;
  data: DevelopmentalAreasDto;
  onSaved: (message: string) => void;
}) {
  const canManage = useCan("school.manage");
  const [rows, setRows] = useState<AreaRow[]>(() => data.areas.map((a) => ({ key: nextKey++, id: a.id, name: a.name })));
  const { busy, error, run } = useAction();
  const inherits = !!levelId && data.levelId === null;
  const dirty = rows.map((r) => `${r.id ?? ""}:${r.name}`).join("|") !== data.areas.map((a) => `${a.id}:${a.name}`).join("|");
  const ready = rows.length > 0 && rows.length <= 20 && rows.every((r) => r.name.trim());

  async function save() {
    const body = { areas: rows.map((r) => ({ id: r.id, name: r.name.trim() })) };
    if ((await run(() => api(`/api/school/developmental/areas${qs({ levelId })}`, { method: "PUT", body }))).ok) {
      onSaved(inherits ? "This level now has its own areas." : "Areas saved.");
    }
  }

  return (
    <ListShell
      inherits={inherits}
      inheritText="This level uses the school's areas. Change them below to give this level its own."
      empty={rows.length === 0}
      full={rows.length >= 20}
      error={error}
      canManage={canManage}
      busy={busy}
      ready={ready && dirty}
      saveLabel="Save areas"
      addLabel="Add an area"
      onSave={save}
      onAdd={() => setRows((rs) => [...rs, { key: nextKey++, id: null, name: "" }])}
    >
      {rows.map((r, i) => (
        <li key={r.key} className="ruled-pad flex flex-wrap items-end gap-2 py-3">
          <div className="min-w-0 flex-1">
            <Input
              label={`Area ${i + 1}`}
              maxLength={40}
              value={r.name}
              disabled={!canManage}
              onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, name: e.target.value } : x)))}
            />
          </div>
          {canManage && (
            <Button variant="ghost" className="text-brick" onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))} aria-label={`Remove ${r.name || `area ${i + 1}`}`}>
              Remove
            </Button>
          )}
        </li>
      ))}
    </ListShell>
  );
}

function Scale({ levelId, onSaved }: { levelId: string; onSaved: (message: string) => void }) {
  const { data, error, reload } = useApi<DevelopmentalScaleDto>(`/api/school/developmental/scale${qs({ levelId })}`);
  return (
    <>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <ScaleEditor
          key={`${levelId}:${data.levelId ?? "school"}:${data.scale.map((s) => s.id).join()}`}
          levelId={levelId}
          data={data}
          onSaved={(message) => {
            onSaved(message);
            reload();
          }}
        />
      )}
    </>
  );
}

function ScaleEditor({
  levelId,
  data,
  onSaved,
}: {
  levelId: string;
  data: DevelopmentalScaleDto;
  onSaved: (message: string) => void;
}) {
  const canManage = useCan("school.manage");
  const [rows, setRows] = useState<ScaleRow[]>(() => data.scale.map((s) => ({ key: nextKey++, id: s.id, code: s.code, label: s.label })));
  const { busy, error, run } = useAction();
  const inherits = !!levelId && data.levelId === null;
  const dirty =
    rows.map((r) => `${r.id ?? ""}:${r.code}:${r.label}`).join("|") !== data.scale.map((s) => `${s.id}:${s.code}:${s.label}`).join("|");
  const ready = rows.length > 0 && rows.length <= 20 && rows.every((r) => r.code.trim() && r.label.trim());

  async function save() {
    const body = { scale: rows.map((r) => ({ id: r.id, code: r.code.trim(), label: r.label.trim() })) };
    if ((await run(() => api(`/api/school/developmental/scale${qs({ levelId })}`, { method: "PUT", body }))).ok) {
      onSaved(inherits ? "This level now has its own scale." : "Scale saved.");
    }
  }

  return (
    <ListShell
      inherits={inherits}
      inheritText="This level uses the school's scale. Change it below to give this level its own."
      empty={rows.length === 0}
      full={rows.length >= 20}
      error={error}
      canManage={canManage}
      busy={busy}
      ready={ready && dirty}
      saveLabel="Save scale"
      addLabel="Add a point"
      onSave={save}
      onAdd={() => setRows((rs) => [...rs, { key: nextKey++, id: null, code: "", label: "" }])}
    >
      {rows.map((r, i) => (
        <li key={r.key} className="ruled-pad flex flex-wrap items-end gap-2 py-3">
          <div className="w-full min-w-0 sm:w-40">
            <Input
              label="Code"
              maxLength={40}
              value={r.code}
              disabled={!canManage}
              hint={i === 0 ? "A short code, such as excellent." : undefined}
              onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, code: e.target.value } : x)))}
            />
          </div>
          <div className="min-w-0 flex-1">
            <Input
              label="Label"
              maxLength={40}
              value={r.label}
              disabled={!canManage}
              onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, label: e.target.value } : x)))}
            />
          </div>
          {canManage && (
            <Button
              variant="ghost"
              className="text-brick"
              onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
              aria-label={`Remove ${r.label || `point ${i + 1}`}`}
            >
              Remove
            </Button>
          )}
        </li>
      ))}
    </ListShell>
  );
}

function ListShell({
  inherits,
  inheritText,
  empty,
  full,
  error,
  canManage,
  busy,
  ready,
  saveLabel,
  addLabel,
  onSave,
  onAdd,
  children,
}: {
  inherits: boolean;
  inheritText: string;
  empty: boolean;
  full: boolean;
  error: ReturnType<typeof useAction>["error"];
  canManage: boolean;
  busy: boolean;
  ready: boolean;
  saveLabel: string;
  addLabel: string;
  onSave: () => void;
  onAdd: () => void;
  children: ReactNode;
}) {
  return (
    <div className="space-y-4">
      {inherits && <Notice>{inheritText}</Notice>}
      <div className="ruled-sheet">
        <ul className="ruled-rows">{children}</ul>
      </div>
      {empty && <p className="text-sm text-brick">Add at least one item.</p>}
      {full && <p className="text-sm text-muted">A list can have at most 20 items.</p>}
      <ErrorBox error={error} />
      {canManage && (
        <div className="flex flex-wrap gap-2">
          <Button onClick={onSave} loading={busy} disabled={!ready}>
            {saveLabel}
          </Button>
          <Button variant="secondary" disabled={full} onClick={onAdd}>
            {addLabel}
          </Button>
        </div>
      )}
    </div>
  );
}
