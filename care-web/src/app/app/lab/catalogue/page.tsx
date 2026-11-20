"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { money } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { PatientSex, PriceItemDto } from "@/lib/types";
import type { LabRangeDto, LabTestDto } from "@/lib/types/lab-pharmacy";
import {
  ApiErrorView,
  Badge,
  Button,
  CaseSheet,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  PageHeader,
  PageLoader,
  Select,
  Toggle,
  caseRowClass,
} from "@/components/ui";

/**
 * Wave 4: the lab test catalogue (`GET /api/care/lab/tests`, every member reads it). Leadership and lab scientists add and edit tests:
 * the sample, the turnaround target, the price-list item charged when a doctor orders it, and each value it reports with its unit and
 * reference ranges by sex and age band (with optional critical limits).
 */
export default function LabCataloguePage() {
  const { access } = useCare();
  const list = useApi<LabTestDto[]>("/api/care/lab/tests");
  const [editing, setEditing] = useState<LabTestDto | "new" | null>(null);
  const canEdit = access.canEditLabCatalogue;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Lab test catalogue"
        subtitle="The tests doctors order from. Results are judged against these ranges for the patient's sex and age."
        action={canEdit && <Button onClick={() => setEditing("new")}>Add test</Button>}
      />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && (
        <EmptyState
          title="No lab tests yet"
          text={canEdit ? "Add your common tests, such as a full blood count or a malaria smear." : "Lab scientists or leadership add them."}
          action={canEdit && <Button onClick={() => setEditing("new")}>Add the first test</Button>}
        />
      )}
      {list.data && list.data.length > 0 && (
        <CaseSheet
          label="Lab tests"
          template={canEdit ? "minmax(0,1fr) 10rem 7rem 8rem 5rem" : "minmax(0,1fr) 10rem 7rem 8rem"}
          head={[{ label: "Test" }, { label: "Sample" }, { label: "Target" }, { label: "Price", align: "right" }, ...(canEdit ? [{ label: "" }] : [])]}
        >
          {list.data.map((t) => (
            <li key={t.id} className={caseRowClass}>
              <span className="min-w-0">
                <span className="block font-bold text-ink">{t.name}</span>
                <span className="block text-xs text-muted">{t.analytes.map((a) => a.name).join(", ")}</span>
                {!t.active && <Badge className="mt-0.5">Not in use</Badge>}
              </span>
              <span className="text-sm text-slate-700">{t.sampleType}</span>
              <span className="tnum text-sm text-slate-700">{t.turnaroundHours} h</span>
              <span className="tnum text-right text-sm font-bold text-ink">{t.priceMinor !== null ? money(t.priceMinor) : "Not charged"}</span>
              {canEdit && (
                <span className="md:text-right">
                  <Button variant="ghost" className="-mr-2 px-2.5" onClick={() => setEditing(t)}>
                    Edit<span className="sr-only"> {t.name}</span>
                  </Button>
                </span>
              )}
            </li>
          ))}
        </CaseSheet>
      )}
      {editing && (
        <LabTestForm
          test={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            list.reload();
          }}
        />
      )}
    </div>
  );
}

interface RangeRow {
  sex: PatientSex | "";
  minAgeYears: string;
  maxAgeYears: string;
  low: string;
  high: string;
  criticalLow: string;
  criticalHigh: string;
}
interface AnalyteRow {
  id: string | null;
  name: string;
  unit: string;
  ranges: RangeRow[];
}

const str = (n: number | null) => (n === null ? "" : String(n));
const num = (s: string) => (s.trim() === "" ? null : Number(s));
const toRow = (r: LabRangeDto): RangeRow => ({
  sex: r.sex ?? "",
  minAgeYears: str(r.minAgeYears),
  maxAgeYears: str(r.maxAgeYears),
  low: str(r.low),
  high: str(r.high),
  criticalLow: str(r.criticalLow),
  criticalHigh: str(r.criticalHigh),
});
const emptyRange: RangeRow = { sex: "", minAgeYears: "", maxAgeYears: "", low: "", high: "", criticalLow: "", criticalHigh: "" };

function LabTestForm({ test, onClose, onSaved }: { test: LabTestDto | null; onClose: () => void; onSaved: () => void }) {
  const prices = useApi<PriceItemDto[]>("/api/care/price-list?active=true&category=Test");
  const [name, setName] = useState(test?.name ?? "");
  const [sampleType, setSampleType] = useState(test?.sampleType ?? "");
  const [hours, setHours] = useState(String(test?.turnaroundHours ?? 24));
  const [priceItemId, setPriceItemId] = useState(test?.priceItemId ?? "");
  const [active, setActive] = useState(test?.active ?? true);
  const [analytes, setAnalytes] = useState<AnalyteRow[]>(
    test?.analytes.map((a) => ({ id: a.id, name: a.name, unit: a.unit ?? "", ranges: a.ranges.map(toRow) })) ?? [{ id: null, name: "", unit: "", ranges: [] }],
  );
  const { busy, error, run } = useAction();

  function setAnalyte(i: number, patch: Partial<AnalyteRow>) {
    setAnalytes((list) => list.map((a, j) => (j === i ? { ...a, ...patch } : a)));
  }
  function setRange(i: number, k: number, patch: Partial<RangeRow>) {
    setAnalyte(i, { ranges: analytes[i].ranges.map((r, j) => (j === k ? { ...r, ...patch } : r)) });
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = {
      name: name.trim(),
      sampleType: sampleType.trim(),
      turnaroundHours: Number(hours) || null,
      priceItemId: priceItemId || null,
      active,
      analytes: analytes.map((a) => ({
        id: a.id,
        name: a.name.trim(),
        unit: a.unit.trim() || null,
        ranges: a.ranges.map((r) => ({
          sex: r.sex || null,
          minAgeYears: num(r.minAgeYears),
          maxAgeYears: num(r.maxAgeYears),
          low: num(r.low),
          high: num(r.high),
          criticalLow: num(r.criticalLow),
          criticalHigh: num(r.criticalHigh),
        })),
      })),
    };
    const res = await run(() => (test ? api(`/api/care/lab/tests/${test.id}`, { method: "PUT", body }) : api("/api/care/lab/tests", { method: "POST", body })));
    if (res.ok) onSaved();
  }

  const rangeError = (i: number, k: number) =>
    ["sex", "minAgeYears", "maxAgeYears", "low", "high", "criticalLow", "criticalHigh"].map((f) => error?.field(`analytes[${i}].ranges[${k}].${f}`)).find(Boolean);

  return (
    <Modal open title={test ? `Edit ${test.name}` : "Add a lab test"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} placeholder="e.g. Full blood count" />
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Sample" required value={sampleType} onChange={(e) => setSampleType(e.target.value)} error={error?.field("sampleType")} placeholder="e.g. Blood (EDTA)" />
          <Input label="Turnaround target (hours)" inputMode="numeric" value={hours} onChange={(e) => setHours(e.target.value)} error={error?.field("turnaroundHours")} className="tnum" />
        </div>
        <Select label="Charged as" value={priceItemId} onChange={(e) => setPriceItemId(e.target.value)} error={error?.field("priceItemId")}>
          <option value="">Not charged</option>
          {(prices.data ?? []).map((p) => (
            <option key={p.id} value={p.id}>
              {p.name} ({money(p.priceMinor)})
            </option>
          ))}
        </Select>
        <Toggle checked={active} onChange={setActive} label="In use" description="Tests not in use can't be ordered; past orders keep their results." />

        <fieldset className="space-y-4">
          <legend className="text-sm font-bold text-ink">Values the test reports</legend>
          {error?.field("analytes") && <p className="text-sm text-arterial">{error.field("analytes")}</p>}
          {analytes.map((a, i) => (
            <div key={i} className="space-y-3 rounded-control border border-rule p-3">
              <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_8rem_auto] sm:items-end">
                <Input label="Value" value={a.name} onChange={(e) => setAnalyte(i, { name: e.target.value })} error={error?.field(`analytes[${i}].name`)} placeholder="e.g. Haemoglobin" />
                <Input label="Unit" value={a.unit} onChange={(e) => setAnalyte(i, { unit: e.target.value })} error={error?.field(`analytes[${i}].unit`)} placeholder="g/dL" />
                <Button variant="ghost" disabled={analytes.length === 1} onClick={() => setAnalytes((list) => list.filter((_, j) => j !== i))}>
                  Remove
                </Button>
              </div>
              {a.ranges.length === 0 && <p className="text-xs text-muted">No range: the result is written as text (for example &quot;Not seen&quot;).</p>}
              {a.ranges.map((r, k) => (
                <div key={k} className="space-y-1">
                  <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-8 lg:items-end">
                    <Select label="Sex" value={r.sex} onChange={(e) => setRange(i, k, { sex: e.target.value as PatientSex | "" })}>
                      <option value="">Both</option>
                      <option value="Female">Female</option>
                      <option value="Male">Male</option>
                    </Select>
                    <Input label="From age" inputMode="numeric" value={r.minAgeYears} onChange={(e) => setRange(i, k, { minAgeYears: e.target.value })} />
                    <Input label="Under age" inputMode="numeric" value={r.maxAgeYears} onChange={(e) => setRange(i, k, { maxAgeYears: e.target.value })} />
                    <Input label="Low" inputMode="decimal" value={r.low} onChange={(e) => setRange(i, k, { low: e.target.value })} />
                    <Input label="High" inputMode="decimal" value={r.high} onChange={(e) => setRange(i, k, { high: e.target.value })} />
                    <Input label="Critical low" inputMode="decimal" value={r.criticalLow} onChange={(e) => setRange(i, k, { criticalLow: e.target.value })} />
                    <Input label="Critical high" inputMode="decimal" value={r.criticalHigh} onChange={(e) => setRange(i, k, { criticalHigh: e.target.value })} />
                    <Button variant="ghost" onClick={() => setAnalyte(i, { ranges: a.ranges.filter((_, j) => j !== k) })}>
                      Remove range
                    </Button>
                  </div>
                  {rangeError(i, k) && <p className="text-sm text-arterial">{rangeError(i, k)}</p>}
                </div>
              ))}
              <Button variant="secondary" onClick={() => setAnalyte(i, { ranges: [...a.ranges, { ...emptyRange }] })}>
                Add a range
              </Button>
            </div>
          ))}
          <Button variant="secondary" onClick={() => setAnalytes((list) => [...list, { id: null, name: "", unit: "", ranges: [] }])}>
            Add a value
          </Button>
        </fieldset>
        <ErrorBox error={error && !error.field("name") && !error.field("sampleType") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {test ? "Save" : "Add test"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
