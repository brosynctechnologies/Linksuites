"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { TraitCriteriaDto, TraitKind } from "@/lib/types";
import { ApiErrorView, Button, ErrorBox, Input, Notice, PageLoader, Tabs } from "@/components/ui";
import { LevelScope } from "../level-scope";

interface Row {
  key: number;
  id: string | null;
  name: string;
}

let nextKey = 1;
const toRows = (data: TraitCriteriaDto): Row[] => data.criteria.map((c) => ({ key: nextKey++, id: c.id, name: c.name }));

const KINDS: TraitKind[] = ["Psychomotor", "Affective"];

export default function TraitsPage() {
  const [levelId, setLevelId] = useState("");
  const [kind, setKind] = useState<TraitKind>("Psychomotor");
  const [notice, setNotice] = useState<string | null>(null);
  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        Psychomotor and affective items the class teacher rates from 1 to 5. A level can have its own list; otherwise it uses the school&apos;s.
      </p>
      <LevelScope
        value={levelId}
        onChange={(v) => {
          setNotice(null);
          setLevelId(v);
        }}
      />
      <Tabs
        label="Trait kind"
        items={[
          { value: "Psychomotor", label: "Psychomotor" },
          { value: "Affective", label: "Affective" },
        ]}
        value={kind}
        onChange={setKind}
      />
      {notice && <Notice tone="success">{notice}</Notice>}
      {KINDS.map((k) => (
        <div key={k} className={k === kind ? undefined : "hidden"}>
          <TraitList kind={k} levelId={levelId} onSaved={setNotice} />
        </div>
      ))}
    </div>
  );
}

function TraitList({ kind, levelId, onSaved }: { kind: TraitKind; levelId: string; onSaved: (message: string) => void }) {
  const { data, error, reload } = useApi<TraitCriteriaDto>(`/api/school/traits${qs({ kind, levelId })}`);
  return (
    <>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <Editor
          key={`${kind}:${levelId}:${data.levelId ?? "school"}:${data.criteria.map((c) => c.id).join()}`}
          kind={kind}
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

function Editor({
  kind,
  levelId,
  data,
  onSaved,
}: {
  kind: TraitKind;
  levelId: string;
  data: TraitCriteriaDto;
  onSaved: (message: string) => void;
}) {
  const canManage = useCan("school.manage");
  const [rows, setRows] = useState<Row[]>(() => toRows(data));
  const { busy, error, run } = useAction();
  const inherits = !!levelId && data.levelId === null;
  const initial = data.criteria.map((c) => `${c.id}:${c.name}`).join("|");
  const dirty = rows.map((r) => `${r.id ?? ""}:${r.name}`).join("|") !== initial;
  const ready = rows.length > 0 && rows.length <= 20 && rows.every((r) => r.name.trim());

  async function save() {
    const body = { criteria: rows.map((r) => ({ id: r.id, name: r.name.trim() })) };
    if ((await run(() => api(`/api/school/traits${qs({ kind, levelId })}`, { method: "PUT", body }))).ok) {
      onSaved(inherits ? "This level now has its own list." : "Traits saved.");
    }
  }

  return (
    <div className="space-y-4">
      {inherits && <Notice>This level uses the school&apos;s list. Change it below to give this level its own.</Notice>}
      <div className="ruled-sheet">
        <ul className="ruled-rows">
          {rows.map((r, i) => (
            <li key={r.key} className="ruled-pad flex flex-wrap items-end gap-2 py-3">
              <div className="min-w-0 flex-1">
                <Input
                  label={`Item ${i + 1}`}
                  maxLength={40}
                  value={r.name}
                  disabled={!canManage}
                  onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, name: e.target.value } : x)))}
                />
              </div>
              {canManage && (
                <Button
                  variant="ghost"
                  className="text-brick"
                  onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                  aria-label={`Remove ${r.name || `item ${i + 1}`}`}
                >
                  Remove
                </Button>
              )}
            </li>
          ))}
        </ul>
      </div>
      {rows.length === 0 && <p className="text-sm text-brick">Add at least one item.</p>}
      {rows.length >= 20 && <p className="text-sm text-muted">A list can have at most 20 items.</p>}
      <ErrorBox error={error} />
      {canManage && (
        <div className="flex flex-wrap gap-2">
          <Button onClick={save} loading={busy} disabled={!dirty || !ready}>
            Save traits
          </Button>
          <Button
            variant="secondary"
            disabled={rows.length >= 20}
            onClick={() => setRows((rs) => [...rs, { key: nextKey++, id: null, name: "" }])}
          >
            Add an item
          </Button>
        </div>
      )}
    </div>
  );
}
