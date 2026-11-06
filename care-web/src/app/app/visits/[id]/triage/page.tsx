"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { bp, flagTone, isCriticalFlag, previewFlags, stageLabel, URGENCIES, vitalFlag, VITAL_RANGES } from "@/lib/clinical";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { DoctorDto, TriageRequest, TriageResultDto, Urgency, VisitDto, VitalsInput } from "@/lib/types/clinical";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  Input,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  Segmented,
  Select,
  Wristband,
  buttonClass,
} from "@/components/ui";

type Field = keyof VitalsInput;
const FIELDS: { key: Field; label: string; unit: string; step: string; inputMode: "decimal" | "numeric" }[] = [
  { key: "temperatureC", label: "Temperature", unit: "°C", step: "0.1", inputMode: "decimal" },
  { key: "systolic", label: "Systolic", unit: "mmHg", step: "1", inputMode: "numeric" },
  { key: "diastolic", label: "Diastolic", unit: "mmHg", step: "1", inputMode: "numeric" },
  { key: "pulse", label: "Pulse", unit: "per minute", step: "1", inputMode: "numeric" },
  { key: "respRate", label: "Respiratory rate", unit: "per minute", step: "1", inputMode: "numeric" },
  { key: "spO2", label: "SpO2", unit: "%", step: "1", inputMode: "numeric" },
  { key: "weightKg", label: "Weight", unit: "kg", step: "0.1", inputMode: "decimal" },
  { key: "heightCm", label: "Height", unit: "cm", step: "0.1", inputMode: "decimal" },
];
const EMPTY: Record<Field, string> = { temperatureC: "", systolic: "", diastolic: "", pulse: "", respRate: "", spO2: "", weightKg: "", heightCm: "" };

function num(s: string): number | null {
  const t = s.trim();
  if (!t) return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

/**
 * Triage: vital signs (flagged as they are typed, with the API's own flags shown after saving), urgency and the doctor. Doctors, nurses
 * and the medical director (the API refuses everyone else). The page reads only the visit's demographics: earlier readings are not
 * loaded here, because reading vitals before triage would ask a nurse with no care relationship yet for an emergency reason.
 */
export default function TriagePage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const visit = useApi<VisitDto>(access.isClinical ? `/api/care/visits/${encodeURIComponent(id)}` : null);
  const doctors = useApi<DoctorDto[]>(access.isClinical ? "/api/care/doctors" : null);
  const [result, setResult] = useState<TriageResultDto | null>(null);

  if (!access.isClinical) return <NoAccess text="Triage is for doctors and nurses." />;
  if (visit.error) return <ApiErrorView error={visit.error} onRetry={visit.reload} />;
  if (!visit.data || !doctors.data) return doctors.error ? <ApiErrorView error={doctors.error} onRetry={doctors.reload} /> : <PageLoader />;
  const v = result?.visit ?? visit.data;

  return (
    <div className="space-y-5">
      <PageHeader
        back="/app/queue"
        title="Triage"
        subtitle={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Wristband number={v.patientNumber} size="lg" />
            <span className="font-bold text-ink">{v.patientName}</span>
            <span className="tnum">
              {v.sex}, {v.ageYears}
            </span>
            <span>{stageLabel(v.stage)}</span>
          </span>
        }
      />
      {result ? (
        <TriageDone result={result} />
      ) : v.stage === "WaitingVitals" || v.stage === "WaitingDoctor" ? (
        <TriageForm visit={v} doctors={doctors.data} onDone={setResult} />
      ) : (
        <Notice tone="warning">Triage is only for patients waiting to be seen.</Notice>
      )}
    </div>
  );
}

function TriageForm({ visit, doctors, onDone }: { visit: VisitDto; doctors: DoctorDto[]; onDone: (r: TriageResultDto) => void }) {
  const [values, setValues] = useState(EMPTY);
  const [urgency, setUrgency] = useState<Urgency>(visit.urgency);
  const [doctorId, setDoctorId] = useState(visit.doctorUserId ?? "");
  const { busy, error, run } = useAction();

  const vitals: VitalsInput = {
    temperatureC: num(values.temperatureC),
    systolic: num(values.systolic),
    diastolic: num(values.diastolic),
    pulse: num(values.pulse),
    respRate: num(values.respRate),
    spO2: num(values.spO2),
    weightKg: num(values.weightKg),
    heightCm: num(values.heightCm),
  };
  const flags = previewFlags(vitals);
  const bmi = vitals.weightKg && vitals.heightCm ? Math.round((vitals.weightKg / (vitals.heightCm / 100) ** 2) * 10) / 10 : null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: TriageRequest = { vitals, urgency, doctorUserId: doctorId || null };
    const res = await run(() => api<TriageResultDto>(`/api/care/visits/${visit.id}/triage`, { method: "POST", body }));
    if (res.ok) onDone(res.data);
  }

  const fieldError = (k: Field) => error?.field(`vitals.${k}`);
  const known = ["vitals", "urgency", "doctorUserId", ...FIELDS.map((f) => `vitals.${f.key}`)];
  return (
    <form onSubmit={submit} className="space-y-5">
      <Card>
        <h2 className="mb-3 font-semibold text-ink">Vital signs</h2>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {FIELDS.map((f) => {
            const flag = f.key in VITAL_RANGES ? vitalFlag(f.key as keyof typeof VITAL_RANGES, vitals[f.key]) : null;
            return (
              <div key={f.key} className="space-y-1">
                <Input
                  label={`${f.label} (${f.unit})`}
                  type="number"
                  step={f.step}
                  inputMode={f.inputMode}
                  value={values[f.key]}
                  onChange={(e) => setValues((s) => ({ ...s, [f.key]: e.target.value }))}
                  error={fieldError(f.key)}
                  className="tnum"
                />
                {flag && <Badge tone={flagTone(flag)}>{flag}</Badge>}
              </div>
            );
          })}
        </div>
        <p className="mt-3 text-xs text-muted">
          Record at least one reading. Flags follow the clinic&apos;s ranges and are checked again when you save.
          {bmi !== null && <span className="tnum"> BMI {bmi}.</span>}
        </p>
        {error?.field("vitals") && <p className="mt-2 text-sm font-medium text-arterial">{error.field("vitals")}</p>}
      </Card>

      {flags.length > 0 && (
        <Notice tone={flags.some(isCriticalFlag) ? "error" : "warning"}>
          {flags.join(", ")}.
          {flags.some(isCriticalFlag) && " A critical reading tells the doctor in-app when you save."}
        </Notice>
      )}

      <Card className="space-y-4">
        <div className="space-y-1.5">
          <p className="text-sm font-medium text-slate-700" id="urgency-label">
            Urgency
          </p>
          <Segmented label="Urgency" className="max-w-md" value={urgency} onChange={setUrgency} items={URGENCIES} />
          {error?.field("urgency") && <p className="text-xs font-medium text-arterial">{error.field("urgency")}</p>}
        </div>
        <Select label="Doctor" value={doctorId} onChange={(e) => setDoctorId(e.target.value)} error={error?.field("doctorUserId")}>
          <option value="">Choose a doctor</option>
          {doctors.map((d) => (
            <option key={d.userId} value={d.userId}>
              {d.name}
            </option>
          ))}
        </Select>
      </Card>

      <ErrorBox error={error && !known.some((k) => error.field(k)) ? error : null} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" loading={busy}>
          Save triage
        </Button>
        <Link href="/app/queue" className={buttonClass("ghost")}>
          Cancel
        </Link>
      </div>
    </form>
  );
}

function TriageDone({ result }: { result: TriageResultDto }) {
  const v = result.vitals;
  const rows: [string, string | null][] = [
    ["Temperature", v.temperatureC !== null ? `${v.temperatureC} °C` : null],
    ["Blood pressure", bp(v.systolic, v.diastolic) && `${bp(v.systolic, v.diastolic)} mmHg`],
    ["Pulse", v.pulse !== null ? `${v.pulse} per minute` : null],
    ["Respiratory rate", v.respRate !== null ? `${v.respRate} per minute` : null],
    ["SpO2", v.spO2 !== null ? `${v.spO2}%` : null],
    ["Weight", v.weightKg !== null ? `${v.weightKg} kg` : null],
    ["Height", v.heightCm !== null ? `${v.heightCm} cm` : null],
    ["BMI", v.bmi !== null ? String(v.bmi) : null],
  ];
  return (
    <div className="space-y-4">
      <Notice tone={v.critical ? "error" : "success"}>
        Triage saved. {result.visit.patientName} is waiting for {result.visit.doctorName ?? "the doctor"}.
        {v.critical && " The reading is critical: the doctor has been told in-app."}
      </Notice>
      <Card>
        <h2 className="mb-3 font-semibold text-ink">Readings</h2>
        {v.flags.length > 0 && (
          <div className="mb-3 flex flex-wrap gap-1.5">
            {v.flags.map((f) => (
              <Badge key={f} tone={flagTone(f)}>
                {f}
              </Badge>
            ))}
          </div>
        )}
        <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[10rem_minmax(0,1fr)]">
          {rows
            .filter(([, value]) => value)
            .map(([label, value]) => (
              <div key={label} className="contents">
                <dt className="text-muted">{label}</dt>
                <dd className="tnum text-ink">{value}</dd>
              </div>
            ))}
        </dl>
      </Card>
      <Link href="/app/queue" className={buttonClass("primary")}>
        Back to the queue
      </Link>
    </div>
  );
}
