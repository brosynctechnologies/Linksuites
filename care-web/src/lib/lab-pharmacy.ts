// Wave 4: labels shared by the lab and pharmacy screens.

import type { LabFlag, LabStage, StockMovementKind } from "./types/lab-pharmacy";

export const LAB_STAGES: { value: LabStage; label: string }[] = [
  { value: "AwaitingSample", label: "Waiting for sample" },
  { value: "AwaitingResult", label: "Waiting for result" },
  { value: "AwaitingReview", label: "Waiting for review" },
  { value: "Done", label: "Reviewed" },
];

export function labStageLabel(stage: LabStage): string {
  return LAB_STAGES.find((s) => s.value === stage)?.label ?? stage;
}

/** A flag's word and badge tone. Critical is red, low and high amber, normal green; no range shows nothing. */
export function flagView(flag: LabFlag): { label: string; tone: "arterial" | "iodine" | "pass" } | null {
  switch (flag) {
    case "CriticalLow":
      return { label: "Critically low", tone: "arterial" };
    case "CriticalHigh":
      return { label: "Critically high", tone: "arterial" };
    case "Low":
      return { label: "Low", tone: "iodine" };
    case "High":
      return { label: "High", tone: "iodine" };
    case "Normal":
      return { label: "Normal", tone: "pass" };
    default:
      return null;
  }
}

/**
 * The flag a typed value would get against the patient's range, so the form shows it before saving (the server decides on save).
 * Mirrors LabRules.Flag: at or beyond a critical limit is critical; outside low/high is low/high.
 */
export function previewFlag(value: string, r: { low: number | null; high: number | null; criticalLow: number | null; criticalHigh: number | null }): LabFlag {
  const v = Number(value.trim());
  if (value.trim() === "" || !Number.isFinite(v)) return "None";
  if (r.low === null && r.high === null && r.criticalLow === null && r.criticalHigh === null) return "None";
  if (r.criticalLow !== null && v <= r.criticalLow) return "CriticalLow";
  if (r.criticalHigh !== null && v >= r.criticalHigh) return "CriticalHigh";
  if (r.low !== null && v < r.low) return "Low";
  if (r.high !== null && v > r.high) return "High";
  return "Normal";
}

/** "45 min", "3 h 10 min", "2 d 4 h"; a dash when there is nothing to average. */
export function minutesText(m: number | null): string {
  if (m === null) return "–";
  const total = Math.round(m);
  if (total < 60) return `${total} min`;
  if (total < 60 * 24) return `${Math.floor(total / 60)} h ${total % 60} min`;
  return `${Math.floor(total / 1440)} d ${Math.floor((total % 1440) / 60)} h`;
}

export const MOVEMENT_KINDS: Record<StockMovementKind, string> = { Receipt: "Received", Dispense: "Dispensed", Adjustment: "Count adjustment" };

export const LAB_NAV = [
  { href: "/app/lab", label: "Worklist", exact: true },
  { href: "/app/lab/catalogue", label: "Test catalogue" },
  { href: "/app/lab/turnaround", label: "Turnaround" },
];

export const PHARMACY_NAV = [
  { href: "/app/pharmacy", label: "Stock", exact: true },
  { href: "/app/pharmacy/dispense", label: "Dispense" },
  { href: "/app/pharmacy/receive", label: "Receive stock" },
  { href: "/app/pharmacy/counts", label: "Stock count" },
  { href: "/app/pharmacy/alerts", label: "Alerts" },
  { href: "/app/pharmacy/reports", label: "Reports" },
];
