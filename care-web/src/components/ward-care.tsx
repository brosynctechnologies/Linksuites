"use client";

// Ward care on an admission (Task 19): the medication chart with its dose buttons, nursing notes and the fluid chart. Clinical only:
// every read is a logged open through `run` (which carries an accepted emergency reason); writes need the care team, which the API
// checks (403 "This patient isn't on your ward."). Nothing is kept in the address or in storage.

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { FREQUENCIES } from "@/lib/clinical";
import { formatDay, formatStamp, lagosToday } from "@/lib/format";
import { addDays, DOSE_STATUS, FLUID_KINDS, fluidKindLabel } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import { qs } from "@/lib/use-api";
import type {
  DoseRecordedDto,
  DoseStatus,
  FluidChartDto,
  FluidDirection,
  FluidKind,
  MedicationChartDto,
  MedicationDoseDto,
  NursingNoteDto,
} from "@/lib/types/inpatient";
import type { PrescriptionItemDto, VitalSignsDto, VitalsInput } from "@/lib/types/clinical";
import { lagosTime } from "./check-in";
import { VitalsLine, type ClinicalRun } from "./clinical-parts";
import { VitalsChart } from "./vitals-chart";
import { ChevronLeftIcon, ChevronRightIcon } from "./icons";
import { ApiErrorView, Badge, Button, ErrorBox, Input, PageLoader, SectionTitle, Segmented, Select, Textarea, cx } from "./ui";

/** One clinical read on the admission, re-run when `key` changes. */
function useWardRead<T>(run: ClinicalRun, path: string) {
  const [state, setState] = useState<{ path: string; data?: T; error?: ApiError } | null>(null);
  const [nonce, setNonce] = useState(0);
  useEffect(() => {
    let live = true;
    run((headers) => api<T>(path, { headers })).then(
      (data) => live && setState({ path, data }),
      (e) => live && setState({ path, error: toApiError(e) }),
    );
    return () => {
      live = false;
    };
  }, [run, path, nonce]);
  const reload = useCallback(() => setNonce((n) => n + 1), []);
  const current = state?.path === path ? state : null;
  return { data: current?.data ?? (state?.data as T | undefined), error: current?.error, loading: !current, reload, setState };
}

/** Previous and next Lagos day, never past today. */
function DayNav({ day, onChange, label }: { day: string; onChange: (d: string) => void; label: string }) {
  const today = lagosToday();
  return (
    <div className="flex items-center gap-1" role="group" aria-label={label}>
      <Button variant="ghost" className="min-h-10 px-2" aria-label="Previous day" onClick={() => onChange(addDays(day, -1))}>
        <ChevronLeftIcon className="h-4 w-4" />
      </Button>
      <span className="tnum min-w-28 text-center text-sm font-bold text-ink">{day === today ? "Today" : formatDay(day)}</span>
      <Button variant="ghost" className="min-h-10 px-2" aria-label="Next day" disabled={day >= today} onClick={() => onChange(addDays(day, 1))}>
        <ChevronRightIcon className="h-4 w-4" />
      </Button>
    </div>
  );
}

const freqShort = (f: string) => FREQUENCIES.find((x) => x.value === f)?.value ?? f;

// ---------- Medication chart ----------

/**
 * One Lagos day of the medication chart. Due doses (and Missed ones, which may still be given late) get Given, Held and Refused buttons
 * with an optional note; Stopped doses (the course ended, or the patient left) have none. As-needed (PRN) drugs are listed with their
 * own buttons: each records a dose given now (Wave 1), which then shows on today's chart.
 */
export function MedicationChart({ admissionId, run, canRecord }: { admissionId: string; run: ClinicalRun; canRecord: boolean }) {
  const [day, setDay] = useState(lagosToday());
  const chart = useWardRead<MedicationChartDto>(run, `/api/care/admissions/${admissionId}/medication-chart${qs({ date: day })}`);
  // The clock for "Overdue", ticking each minute (read outside render).
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(t);
  }, []);
  const data = chart.data;

  function recorded(r: DoseRecordedDto) {
    chart.setState((s) =>
      s?.data
        ? {
            ...s,
            data: {
              ...s.data,
              doses: s.data.doses.map((d) => (d.id === r.id ? { ...d, status: r.status, recordedAt: r.recordedAt, recordedByUserId: r.recordedByUserId, recordedByName: "you", note: r.note } : d)),
            },
          }
        : s,
    );
  }

  return (
    <div className="space-y-3">
      <SectionTitle action={<DayNav day={day} onChange={setDay} label="Chart day" />}>Medication chart</SectionTitle>
      {chart.error && <ApiErrorView error={chart.error} onRetry={chart.reload} />}
      {chart.loading && !data && <PageLoader label="Loading the chart…" />}
      {data && data.doses.length === 0 && <p className="text-sm text-muted">No doses due on this day.</p>}
      {data && data.doses.length > 0 && (
        <ul className={cx("divide-y divide-rule-soft rounded-panel border border-rule bg-surface", chart.loading && "opacity-60")}>
          {data.doses.map((d) => (
            <DoseRow key={d.id} dose={d} now={now} canRecord={canRecord} onRecorded={recorded} onConflict={chart.reload} />
          ))}
        </ul>
      )}
      {data && data.asNeeded.length > 0 && (
        <div className="rounded-control border border-rule p-3 text-sm">
          <p className="font-bold text-ink">As needed (PRN)</p>
          <ul className="mt-1 divide-y divide-rule-soft">
            {data.asNeeded.map((i) => (
              <AsNeededRow
                key={i.id}
                admissionId={admissionId}
                item={i}
                canRecord={canRecord}
                onRecorded={() => (day === lagosToday() ? chart.reload() : setDay(lagosToday()))}
              />
            ))}
          </ul>
          <p className="mt-2 text-xs text-muted">As-needed doses aren&apos;t scheduled. Record each one when you give it: it shows on today&apos;s chart.</p>
        </div>
      )}
    </div>
  );
}

/** Wave 1: one as-needed drug, with Given, Held and Refused buttons that record a dose now (`POST /api/care/admissions/{id}/prn-doses`). */
function AsNeededRow({ admissionId, item: i, canRecord, onRecorded }: { admissionId: string; item: PrescriptionItemDto; canRecord: boolean; onRecorded: () => void }) {
  const [note, setNote] = useState("");
  const [noting, setNoting] = useState(false);
  const { busy, error, run } = useAction();

  async function record(status: DoseStatus) {
    const res = await run(() =>
      api<DoseRecordedDto>(`/api/care/admissions/${admissionId}/prn-doses`, { method: "POST", body: { prescriptionItemId: i.id, status, note: note.trim() || null } }),
    );
    if (res.ok) {
      setNote("");
      setNoting(false);
      onRecorded();
    }
  }

  return (
    <li className="space-y-2 py-2">
      <p>
        <span className="font-bold text-ink">
          {i.drug}
          {i.strength && ` ${i.strength}`}
        </span>{" "}
        <span className="text-slate-700">
          {i.dose}
          {i.route ? `, ${i.route}` : ""}
        </span>
        {i.instructions && <span className="block text-xs text-muted">{i.instructions}</span>}
      </p>
      {canRecord && (
        <div className="flex flex-wrap items-center gap-2">
          <Button className="min-h-10" loading={busy} onClick={() => record("Given")}>
            Given now
          </Button>
          <Button variant="secondary" className="min-h-10" disabled={busy} onClick={() => record("Held")}>
            Held
          </Button>
          <Button variant="secondary" className="min-h-10" disabled={busy} onClick={() => record("Refused")}>
            Refused
          </Button>
          {!noting && (
            <Button variant="ghost" className="min-h-10 px-2.5" onClick={() => setNoting(true)}>
              Add a note
            </Button>
          )}
          {noting && (
            <div className="w-full max-w-md">
              <Input label={`Note for ${i.drug}`} srLabel placeholder="Why it was given, held or refused" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} error={error?.field("note")} />
            </div>
          )}
        </div>
      )}
      <ErrorBox error={error && !error.field("note") ? error : null} />
    </li>
  );
}

function DoseRow({
  dose: d,
  now,
  canRecord,
  onRecorded,
  onConflict,
}: {
  dose: MedicationDoseDto;
  now: number;
  canRecord: boolean;
  onRecorded: (r: DoseRecordedDto) => void;
  onConflict: () => void;
}) {
  const [note, setNote] = useState("");
  const [noting, setNoting] = useState(false);
  const { busy, error, run } = useAction();
  const open = d.status === "Due" || d.status === "Missed";
  const overdue = d.status === "Due" && new Date(d.dueAt).getTime() < now;

  async function record(status: DoseStatus) {
    const res = await run(() => api<DoseRecordedDto>(`/api/care/doses/${d.id}`, { method: "POST", body: { status, note: note.trim() || null } }));
    if (res.ok) {
      onRecorded(res.data);
      setNote("");
      setNoting(false);
    } else if (res.error.code === "dose_recorded" || res.error.code === "dose_stopped") onConflict();
  }

  const s = DOSE_STATUS[d.status];
  return (
    <li className="space-y-2 px-3 py-3 sm:px-4">
      <div className="flex flex-wrap items-start gap-x-3 gap-y-1">
        <span className="tnum display w-14 shrink-0 text-lg font-bold text-ink">{lagosTime(d.dueAt)}</span>
        <div className="min-w-0 flex-1 text-sm">
          <p>
            <span className="font-bold text-ink">
              {d.drug}
              {d.strength && ` ${d.strength}`}
            </span>{" "}
            <span className="text-slate-700">
              {d.dose}, {freqShort(d.frequency)}
              {d.route ? `, ${d.route}` : ""}
            </span>
          </p>
          {d.instructions && <p className="text-xs text-muted">{d.instructions}</p>}
          {d.recordedAt && (
            <p className="text-xs text-muted">
              {s.label} by {d.recordedByName ?? "staff"} at {formatStamp(d.recordedAt)}
              {d.note && `: ${d.note}`}
            </p>
          )}
        </div>
        <span className="flex items-center gap-1.5">
          {overdue && <Badge tone="arterial">Overdue</Badge>}
          <Badge tone={s.tone}>{s.label}</Badge>
        </span>
      </div>
      {open && canRecord && (
        <div className="flex flex-wrap items-center gap-2 sm:pl-[4.25rem]">
          <Button className="min-h-10" loading={busy} onClick={() => record("Given")}>
            Given
          </Button>
          <Button variant="secondary" className="min-h-10" disabled={busy} onClick={() => record("Held")}>
            Held
          </Button>
          <Button variant="secondary" className="min-h-10" disabled={busy} onClick={() => record("Refused")}>
            Refused
          </Button>
          {!noting && (
            <Button variant="ghost" className="min-h-10 px-2.5" onClick={() => setNoting(true)}>
              Add a note
            </Button>
          )}
          {noting && (
            <div className="w-full max-w-md">
              <Input label="Dose note" srLabel placeholder="Why held, refused or late" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} error={error?.field("note")} />
            </div>
          )}
          {d.status === "Missed" && <p className="w-full text-xs text-muted">Missed: it can still be recorded if it was given late.</p>}
        </div>
      )}
      {d.status === "Stopped" && <p className="text-xs text-muted sm:pl-[4.25rem]">Stopped: the course ended or the patient left. It can&apos;t be recorded.</p>}
      <ErrorBox error={error && !error.field("note") ? error : null} />
    </li>
  );
}

// ---------- Nursing notes ----------

export function NursingNotes({ admissionId, run, canWrite }: { admissionId: string; run: ClinicalRun; canWrite: boolean }) {
  const notes = useWardRead<NursingNoteDto[]>(run, `/api/care/admissions/${admissionId}/nursing-notes`);
  const [text, setText] = useState("");
  const { busy, error, run: act } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await act(() => api<NursingNoteDto>(`/api/care/admissions/${admissionId}/nursing-notes`, { method: "POST", body: { note: text } }));
    if (res.ok) {
      setText("");
      notes.setState((s) => (s?.data ? { ...s, data: [res.data, ...s.data] } : s));
    }
  }

  return (
    <div className="space-y-3">
      <SectionTitle>Nursing notes</SectionTitle>
      {canWrite && (
        <form onSubmit={submit} className="space-y-2">
          <Textarea label="New nursing note" rows={3} maxLength={4000} value={text} onChange={(e) => setText(e.target.value)} error={error?.field("note")} />
          <ErrorBox error={error && !error.field("note") ? error : null} />
          <Button type="submit" loading={busy} disabled={!text.trim()}>
            Add the note
          </Button>
        </form>
      )}
      {notes.error && <ApiErrorView error={notes.error} onRetry={notes.reload} />}
      {notes.loading && !notes.data && <PageLoader label="Loading notes…" />}
      {notes.data && notes.data.length === 0 && <p className="text-sm text-muted">No nursing notes yet.</p>}
      {notes.data && notes.data.length > 0 && (
        <ul className="space-y-2">
          {notes.data.map((n) => (
            <li key={n.id} className="rounded-control border border-rule bg-surface px-3 py-2 text-sm">
              <p className="text-xs text-muted">
                {n.authorName}, {formatStamp(n.at)}
              </p>
              <p className="mt-0.5 whitespace-pre-wrap text-ink">{n.note}</p>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ---------- Ward vital signs (Wave 1) ----------

type VitalField = keyof VitalsInput;
const VITAL_FIELDS: { key: VitalField; label: string; step: string; inputMode: "decimal" | "numeric" }[] = [
  { key: "temperatureC", label: "Temperature (°C)", step: "0.1", inputMode: "decimal" },
  { key: "systolic", label: "Systolic (mmHg)", step: "1", inputMode: "numeric" },
  { key: "diastolic", label: "Diastolic (mmHg)", step: "1", inputMode: "numeric" },
  { key: "pulse", label: "Pulse (per minute)", step: "1", inputMode: "numeric" },
  { key: "respRate", label: "Respiratory rate (per minute)", step: "1", inputMode: "numeric" },
  { key: "spO2", label: "SpO2 (%)", step: "1", inputMode: "numeric" },
  { key: "weightKg", label: "Weight (kg)", step: "0.1", inputMode: "decimal" },
];
const NO_VITALS: Record<VitalField, string> = { temperatureC: "", systolic: "", diastolic: "", pulse: "", respRate: "", spO2: "", weightKg: "", heightCm: "" };

function vitalNumber(s: string): number | null {
  const t = s.trim();
  if (!t) return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

/**
 * The ward's vital signs chart (`GET /api/care/admissions/{id}/vitals`, a logged open): the trend and every set, newest first. Ward staff
 * add a set with the same checks and flags as triage; a critical reading tells the admitting doctor (no value in the notice).
 */
export function WardVitals({ admissionId, run, canWrite }: { admissionId: string; run: ClinicalRun; canWrite: boolean }) {
  const chart = useWardRead<VitalSignsDto[]>(run, `/api/care/admissions/${admissionId}/vitals`);
  const [values, setValues] = useState(NO_VITALS);
  const { busy, error, run: act } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const vitals: VitalsInput = {
      temperatureC: vitalNumber(values.temperatureC),
      systolic: vitalNumber(values.systolic),
      diastolic: vitalNumber(values.diastolic),
      pulse: vitalNumber(values.pulse),
      respRate: vitalNumber(values.respRate),
      spO2: vitalNumber(values.spO2),
      weightKg: vitalNumber(values.weightKg),
      heightCm: null,
    };
    const res = await act(() => api<VitalSignsDto>(`/api/care/admissions/${admissionId}/vitals`, { method: "POST", body: { vitals } }));
    if (res.ok) {
      setValues(NO_VITALS);
      chart.setState((s) => (s?.data ? { ...s, data: [...s.data, res.data] } : s));
    }
  }

  const rows = chart.data ?? [];
  // A reading's own error shows under its input; anything else (such as "Record at least one vital sign.") shows below the form.
  const inputFields = VITAL_FIELDS.map((f) => `vitals.${f.key}`);
  return (
    <div className="space-y-3">
      <SectionTitle>Vital signs</SectionTitle>
      {chart.error && <ApiErrorView error={chart.error} onRetry={chart.reload} />}
      {chart.loading && !chart.data && <PageLoader label="Loading vital signs…" />}
      {chart.data && rows.length > 1 && <VitalsChart vitals={rows} />}
      {chart.data && rows.length === 0 && <p className="text-sm text-muted">No vital signs taken on the ward yet.</p>}
      {rows.length > 0 && (
        <ul className="divide-y divide-rule-soft rounded-control border border-rule bg-surface text-sm">
          {[...rows].reverse().map((v) => (
            <li key={v.id} className="px-3 py-2">
              <VitalsLine vitals={v} />
            </li>
          ))}
        </ul>
      )}
      {canWrite && (
        <form onSubmit={submit} className="space-y-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3">
          <p className="text-sm font-bold text-ink">Take a set of vital signs</p>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {VITAL_FIELDS.map((f) => (
              <Input
                key={f.key}
                label={f.label}
                type="number"
                step={f.step}
                inputMode={f.inputMode}
                value={values[f.key]}
                onChange={(e) => setValues((s) => ({ ...s, [f.key]: e.target.value }))}
                error={error?.field(`vitals.${f.key}`)}
                className="tnum"
              />
            ))}
          </div>
          <ErrorBox error={error && !inputFields.some((f) => error.field(f)) ? error : null} />
          <Button type="submit" loading={busy} disabled={Object.values(values).every((v) => !v.trim())}>
            Save the readings
          </Button>
        </form>
      )}
    </div>
  );
}

// ---------- Fluid chart ----------

export function FluidChart({ admissionId, run, canWrite }: { admissionId: string; run: ClinicalRun; canWrite: boolean }) {
  const [day, setDay] = useState(lagosToday());
  const chart = useWardRead<FluidChartDto>(run, `/api/care/admissions/${admissionId}/fluids${qs({ date: day })}`);
  const [direction, setDirection] = useState<FluidDirection>("In");
  const [kind, setKind] = useState<FluidKind>("Oral");
  const [ml, setMl] = useState("");
  const { busy, error, run: act } = useAction();
  const kinds = FLUID_KINDS.filter((k) => k.direction === direction || k.value === "Other");

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await act(() =>
      api(`/api/care/admissions/${admissionId}/fluids`, { method: "POST", body: { direction, kind, ml: ml.trim() ? Number(ml) : null } }),
    );
    if (res.ok) {
      setMl("");
      if (day === lagosToday()) chart.reload();
      else setDay(lagosToday());
    }
  }

  const data = chart.data;
  return (
    <div className="space-y-3">
      <SectionTitle action={<DayNav day={day} onChange={setDay} label="Fluid chart day" />}>Fluid chart</SectionTitle>
      {chart.error && <ApiErrorView error={chart.error} onRetry={chart.reload} />}
      {data && (
        <dl className="tnum grid grid-cols-3 gap-2 text-center">
          {[
            ["In", `${data.inMl.toLocaleString("en-NG")} ml`],
            ["Out", `${data.outMl.toLocaleString("en-NG")} ml`],
            ["Balance", `${data.balanceMl > 0 ? "+" : ""}${data.balanceMl.toLocaleString("en-NG")} ml`],
          ].map(([label, value]) => (
            <div key={label} className="rounded-control border border-rule bg-surface px-2 py-2">
              <dt className="text-xs text-muted">{label}, Lagos day</dt>
              <dd className="display text-lg font-bold text-ink">{value}</dd>
            </div>
          ))}
        </dl>
      )}
      {chart.loading && !data && <PageLoader label="Loading the fluid chart…" />}
      {data && data.entries.length === 0 && <p className="text-sm text-muted">Nothing charted on this day.</p>}
      {data && data.entries.length > 0 && (
        <ul className={cx("divide-y divide-rule-soft rounded-control border border-rule bg-surface text-sm", chart.loading && "opacity-60")}>
          {data.entries.map((f) => (
            <li key={f.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2">
              <span className="tnum w-12 font-bold text-ink">{lagosTime(f.at)}</span>
              <Badge tone={f.direction === "In" ? "scrub" : "iodine"}>{f.direction}</Badge>
              <span className="text-ink">{fluidKindLabel(f.kind)}</span>
              <span className="tnum font-bold text-ink">{f.ml.toLocaleString("en-NG")} ml</span>
              <span className="ml-auto text-xs text-muted">{f.recordedByName}</span>
            </li>
          ))}
        </ul>
      )}
      {canWrite && (
        <form onSubmit={submit} className="grid gap-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3 sm:grid-cols-[12rem_minmax(0,1fr)_8rem_auto] sm:items-end">
          <Segmented
            label="In or out"
            value={direction}
            onChange={(v) => {
              setDirection(v);
              setKind(v === "In" ? "Oral" : "Urine");
            }}
            items={[
              { value: "In", label: "In" },
              { value: "Out", label: "Out" },
            ]}
          />
          <Select label="Kind" value={kind} onChange={(e) => setKind(e.target.value as FluidKind)} error={error?.field("kind")}>
            {kinds.map((k) => (
              <option key={k.value} value={k.value}>
                {k.label}
              </option>
            ))}
          </Select>
          <Input label="Millilitres" type="number" min={1} max={10000} value={ml} onChange={(e) => setMl(e.target.value)} error={error?.field("ml")} />
          <Button type="submit" loading={busy} disabled={!ml.trim()}>
            Chart it
          </Button>
          <div className="sm:col-span-4">
            <ErrorBox error={error && !["kind", "ml", "direction"].some((f) => error.field(f)) ? error : null} />
          </div>
        </form>
      )}
    </div>
  );
}
