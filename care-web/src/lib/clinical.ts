// Shared words and rules for the clinical screens (Task 18). The API is the authority on every flag and role; these mirror it so the
// screens can speak the same words before a save.

import type { BadgeTone } from "@/components/ui";
import type { CareAccess } from "./roles";
import type { DoseFrequency, TestKind, Urgency, VisitStage, VitalsInput } from "./types/clinical";

export const STAGES: { value: VisitStage; label: string; short: string }[] = [
  { value: "WaitingVitals", label: "Waiting for vitals", short: "Vitals" },
  { value: "WaitingDoctor", label: "Waiting for the doctor", short: "Doctor" },
  { value: "WithDoctor", label: "With the doctor", short: "With doctor" },
  { value: "WaitingPayment", label: "Waiting to pay", short: "Payment" },
  { value: "Done", label: "Done", short: "Done" },
];
export function stageLabel(s: VisitStage): string {
  return STAGES.find((x) => x.value === s)?.label ?? s;
}

export const URGENCIES: { value: Urgency; label: string }[] = [
  { value: "Routine", label: "Routine" },
  { value: "Urgent", label: "Urgent" },
  { value: "Emergency", label: "Emergency" },
];
export function urgencyTone(u: Urgency): BadgeTone {
  return u === "Emergency" ? "arterial" : u === "Urgent" ? "iodine" : "neutral";
}

export const FREQUENCIES: { value: DoseFrequency; label: string }[] = [
  { value: "OD", label: "OD (once a day)" },
  { value: "BD", label: "BD (twice a day)" },
  { value: "TDS", label: "TDS (three times a day)" },
  { value: "QDS", label: "QDS (four times a day)" },
  { value: "Q4H", label: "Q4H (every 4 hours)" },
  { value: "Q6H", label: "Q6H (every 6 hours)" },
  { value: "Q8H", label: "Q8H (every 8 hours)" },
  { value: "Nocte", label: "Nocte (at night)" },
  { value: "Stat", label: "Stat (once, now)" },
  { value: "PRN", label: "PRN (as needed)" },
];

export const TEST_KINDS: { value: TestKind; label: string }[] = [
  { value: "Lab", label: "Lab" },
  { value: "Imaging", label: "Imaging" },
  { value: "Other", label: "Other" },
];

/** Doctors and the medical director write notes, prescribe and request tests (a nurse gets 403 from the API). */
export function canWriteNotes(a: CareAccess): boolean {
  return a.isClinical && (a.isDirector || a.clinicalRole === "Doctor");
}

// ---- Vital-sign ranges: a copy of api/src/Saas.Application/Care/Vitals.cs, used only to preview flags while typing ----

type Level = "Normal" | "Low" | "High" | "Critical";
interface Range {
  label: string;
  normalMin: number | null;
  normalMax: number | null;
  criticalBelow: number | null;
  criticalHigh: number | null;
  criticalHighInclusive: boolean;
}
function classify(r: Range, v: number): Level {
  if (r.criticalBelow !== null && v < r.criticalBelow) return "Critical";
  if (r.criticalHigh !== null && (r.criticalHighInclusive ? v >= r.criticalHigh : v > r.criticalHigh)) return "Critical";
  if (r.normalMin !== null && v < r.normalMin) return "Low";
  if (r.normalMax !== null && v > r.normalMax) return "High";
  return "Normal";
}
export const VITAL_RANGES: Record<"temperatureC" | "systolic" | "diastolic" | "pulse" | "respRate" | "spO2", Range> = {
  temperatureC: { label: "temperature", normalMin: 36.1, normalMax: 37.8, criticalBelow: 35, criticalHigh: 40, criticalHighInclusive: true },
  systolic: { label: "systolic pressure", normalMin: 90, normalMax: 139, criticalBelow: 80, criticalHigh: 180, criticalHighInclusive: true },
  diastolic: { label: "diastolic pressure", normalMin: 60, normalMax: 89, criticalBelow: null, criticalHigh: 120, criticalHighInclusive: true },
  pulse: { label: "pulse", normalMin: 60, normalMax: 100, criticalBelow: 40, criticalHigh: 140, criticalHighInclusive: false },
  respRate: { label: "respiratory rate", normalMin: 12, normalMax: 20, criticalBelow: 8, criticalHigh: 30, criticalHighInclusive: false },
  spO2: { label: "SpO2", normalMin: 95, normalMax: null, criticalBelow: 90, criticalHigh: null, criticalHighInclusive: false },
};

/** The flag the API would give one reading ("High pulse"), or null when it is normal or missing. */
export function vitalFlag(key: keyof typeof VITAL_RANGES, value: number | null | undefined): string | null {
  if (value === null || value === undefined || Number.isNaN(value)) return null;
  const range = VITAL_RANGES[key];
  const level = classify(range, value);
  return level === "Normal" ? null : `${level} ${range.label}`;
}

/** Every flag for a set of readings, in the API's order. */
export function previewFlags(v: Partial<VitalsInput>): string[] {
  const keys = ["temperatureC", "systolic", "diastolic", "pulse", "respRate", "spO2"] as const;
  return keys.map((k) => vitalFlag(k, v[k])).filter((f): f is string => !!f);
}

export function isCriticalFlag(flag: string): boolean {
  return flag.startsWith("Critical ");
}

/** "Critical SpO2" → arterial; "High pulse" → iodine. */
export function flagTone(flag: string): BadgeTone {
  return isCriticalFlag(flag) ? "arterial" : "iodine";
}

/** Blood pressure as "120/80", or what is known of it. */
export function bp(systolic: number | null, diastolic: number | null): string | null {
  if (systolic === null && diastolic === null) return null;
  return `${systolic ?? "–"}/${diastolic ?? "–"}`;
}
