"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { GradingDto } from "@/lib/types";
import { ApiErrorView, Button, ErrorBox, Input, Notice, PageLoader } from "@/components/ui";
import { LevelScope } from "../level-scope";

interface Row {
  key: number;
  min: string;
  max: string;
  grade: string;
  remark: string;
}

let nextKey = 1;
const toRows = (g: GradingDto): Row[] =>
  g.bands.map((b) => ({ key: nextKey++, min: String(b.min), max: String(b.max), grade: b.grade, remark: b.remark }));

export default function GradingPage() {
  const [levelId, setLevelId] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const { data, error, reload } = useApi<GradingDto>(`/api/school/grading${qs({ levelId })}`);
  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        Grades for a subject&apos;s total out of 100. The bands must cover 0 to 100 with no gaps or overlaps. A level can have its own bands.
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
          key={`${levelId}:${data.levelId ?? "school"}:${JSON.stringify(data.bands)}`}
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

function Editor({ levelId, data, onSaved }: { levelId: string; data: GradingDto; onSaved: (message: string) => void }) {
  const canManage = useCan("school.manage");
  const [rows, setRows] = useState<Row[]>(() => toRows(data));
  const { busy, error, run } = useAction();
  const inherits = !!levelId && data.levelId === null;
  const set = (key: number, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  async function save() {
    const bands = rows
      .map((r) => ({ min: Number(r.min) || 0, max: Number(r.max) || 0, grade: r.grade.trim(), remark: r.remark.trim() }))
      .sort((a, b) => b.min - a.min);
    if ((await run(() => api("/api/school/grading", { method: "PUT", body: { levelId: levelId || null, bands } }))).ok) onSaved("Grading saved.");
  }

  async function reset() {
    if ((await run(() => api(`/api/school/grading/${levelId}`, { method: "DELETE" }))).ok) onSaved("This level now uses the school's grading.");
  }

  return (
    <div className="space-y-4">
      {inherits && <Notice>This level uses the school&apos;s grading. Change it below to give this level its own.</Notice>}
      <div className="ruled-sheet">
        <ul className="ruled-rows">
          {rows.map((r) => (
            <li key={r.key} className="ruled-pad grid grid-cols-[4.5rem_4.5rem_4.5rem_minmax(0,1fr)] items-end gap-2 py-3 sm:grid-cols-[5rem_5rem_5rem_minmax(0,1fr)_auto]">
              <Input label="Grade" value={r.grade} disabled={!canManage} onChange={(e) => set(r.key, { grade: e.target.value.toUpperCase() })} className="slab font-bold" />
              <Input label="From" type="number" inputMode="decimal" value={r.min} disabled={!canManage} onChange={(e) => set(r.key, { min: e.target.value })} />
              <Input label="To" type="number" inputMode="decimal" value={r.max} disabled={!canManage} onChange={(e) => set(r.key, { max: e.target.value })} />
              <Input label="Remark" value={r.remark} disabled={!canManage} onChange={(e) => set(r.key, { remark: e.target.value })} />
              {canManage && (
                <Button
                  variant="ghost"
                  className="col-span-4 justify-self-start px-2 text-brick sm:col-span-1"
                  onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                  aria-label={`Remove ${r.grade || "band"}`}
                >
                  Remove
                </Button>
              )}
            </li>
          ))}
        </ul>
      </div>
      <ErrorBox error={error} />
      {canManage && (
        <div className="flex flex-wrap gap-2">
          <Button onClick={save} loading={busy} disabled={rows.length === 0 || rows.some((r) => !r.grade.trim())}>
            Save grading
          </Button>
          <Button variant="secondary" onClick={() => setRows((rs) => [...rs, { key: nextKey++, min: "", max: "", grade: "", remark: "" }])}>
            Add a band
          </Button>
          {levelId && !inherits && (
            <Button variant="ghost" onClick={reset} disabled={busy}>
              Use the school&apos;s grading
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
