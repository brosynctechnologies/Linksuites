"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { AssessmentDto } from "@/lib/types";
import { ApiErrorView, Button, ErrorBox, Input, Notice, PageLoader, cx } from "@/components/ui";
import { LevelScope } from "../level-scope";

interface Row {
  key: number;
  /** The stored component's id; null for a part added here. The API matches parts by id, so scores stay with their column. */
  id: string | null;
  name: string;
  maxScore: string;
}

let nextKey = 1;
const toRows = (a: AssessmentDto): Row[] => a.components.map((c) => ({ key: nextKey++, id: c.id, name: c.name, maxScore: String(c.maxScore) }));

export default function AssessmentPage() {
  const [levelId, setLevelId] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const { data, error, reload } = useApi<AssessmentDto>(`/api/school/assessment${qs({ levelId })}`);
  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        How each subject&apos;s term score is made up. The parts must add up to 100. A level can have its own scheme; otherwise it uses the
        school&apos;s.
      </p>
      <LevelScope
        value={levelId}
        onChange={(v) => {
          setNotice(null);
          setLevelId(v);
        }}
      />
      {notice && <Notice tone="success">{notice}</Notice>}
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <Editor
          key={`${levelId}:${data.levelId ?? "school"}:${JSON.stringify(data.components)}`}
          levelId={levelId}
          data={data}
          onSaved={(message) => {
            setNotice(message);
            reload();
          }}
        />
      )}
    </div>
  );
}

function Editor({ levelId, data, onSaved }: { levelId: string; data: AssessmentDto; onSaved: (message: string) => void }) {
  const canManage = useCan("school.manage");
  const [rows, setRows] = useState<Row[]>(() => toRows(data));
  const { busy, error, run } = useAction();
  const inherits = !!levelId && data.levelId === null;
  const total = rows.reduce((sum, r) => sum + (Number(r.maxScore) || 0), 0);
  const set = (key: number, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  async function save() {
    const body = { levelId: levelId || null, components: rows.map((r) => ({ id: r.id, name: r.name.trim(), maxScore: Number(r.maxScore) || 0 })) };
    if ((await run(() => api("/api/school/assessment", { method: "PUT", body }))).ok) onSaved("Assessment saved.");
  }

  async function reset() {
    if ((await run(() => api(`/api/school/assessment/${levelId}`, { method: "DELETE" }))).ok) onSaved("This level now uses the school's scheme.");
  }

  return (
    <div className="space-y-4">
      {inherits && <Notice>This level uses the school&apos;s scheme. Change it below to give this level its own.</Notice>}
      <div className="ruled-sheet">
        <ul className="ruled-rows">
          {rows.map((r, i) => (
            <li key={r.key} className="ruled-pad flex flex-wrap items-end gap-2 py-3">
              <div className="min-w-36 flex-1">
                <Input label={`Part ${i + 1}`} value={r.name} disabled={!canManage} onChange={(e) => set(r.key, { name: e.target.value })} />
              </div>
              <div className="w-28">
                <Input
                  label="Out of"
                  type="number"
                  inputMode="decimal"
                  min={0}
                  max={100}
                  value={r.maxScore}
                  disabled={!canManage}
                  onChange={(e) => set(r.key, { maxScore: e.target.value })}
                />
              </div>
              {canManage && rows.length > 1 && (
                <Button
                  variant="ghost"
                  className="text-brick"
                  onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                  aria-label={`Remove ${r.name || `part ${i + 1}`}`}
                >
                  Remove
                </Button>
              )}
            </li>
          ))}
          <li className="ruled-pad flex items-center justify-between gap-3 py-3">
            <span className="font-bold text-ink">Total</span>
            <span className={cx("slab tnum text-xl font-bold", total === 100 ? "text-pass" : "text-brick")}>{total}</span>
          </li>
        </ul>
      </div>
      {total !== 100 && <p className="text-sm text-brick">The parts add up to {total}. Make them add up to 100 to save.</p>}
      <ErrorBox error={error} />
      {canManage && (
        <div className="flex flex-wrap gap-2">
          <Button onClick={save} loading={busy} disabled={total !== 100 || rows.some((r) => !r.name.trim())}>
            Save assessment
          </Button>
          <Button variant="secondary" onClick={() => setRows((rs) => [...rs, { key: nextKey++, id: null, name: "", maxScore: "0" }])}>
            Add a part
          </Button>
          {levelId && !inherits && (
            <Button variant="ghost" onClick={reset} disabled={busy}>
              Use the school&apos;s scheme
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
