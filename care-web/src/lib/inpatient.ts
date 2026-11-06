// Shared words for the inpatient and money screens (Task 19). The API is the authority; these are labels and tones only.

import type { BadgeTone } from "@/components/ui";
import type { BillStatus, DischargeType } from "./types/clinical";
import type { AdjustmentKind, CarePaymentStatus } from "./types/billing";
import type { BedStatus, DoseStatus, FluidKind, PayMethod, WardKind } from "./types/inpatient";

export const WARD_KINDS: { value: WardKind; label: string }[] = [
  { value: "General", label: "General" },
  { value: "Male", label: "Male" },
  { value: "Female", label: "Female" },
  { value: "Maternity", label: "Maternity" },
  { value: "Paediatric", label: "Paediatric" },
  { value: "Surgical", label: "Surgical" },
  { value: "Intensive", label: "Intensive care" },
  { value: "Private", label: "Private" },
  { value: "Other", label: "Other" },
];
export const wardKindLabel = (k: WardKind) => WARD_KINDS.find((x) => x.value === k)?.label ?? k;

export const BED_STATUS: Record<BedStatus, { label: string; tone: BadgeTone }> = {
  Free: { label: "Free", tone: "pass" },
  Occupied: { label: "Occupied", tone: "scrub" },
  Cleaning: { label: "Cleaning", tone: "iodine" },
};

/** Every dose status in words, Stopped included (Task 9 ruling: the course ended, at discharge or its last day). */
export const DOSE_STATUS: Record<DoseStatus, { label: string; tone: BadgeTone }> = {
  Due: { label: "Due", tone: "neutral" },
  Given: { label: "Given", tone: "pass" },
  Held: { label: "Held", tone: "iodine" },
  Refused: { label: "Refused", tone: "iodine" },
  Missed: { label: "Missed", tone: "arterial" },
  Stopped: { label: "Stopped", tone: "neutral" },
};

export const FLUID_KINDS: { value: FluidKind; label: string; direction: "In" | "Out" }[] = [
  { value: "Oral", label: "Oral", direction: "In" },
  { value: "IV", label: "IV", direction: "In" },
  { value: "Blood", label: "Blood", direction: "In" },
  { value: "NG", label: "NG tube", direction: "In" },
  { value: "Urine", label: "Urine", direction: "Out" },
  { value: "Vomit", label: "Vomit", direction: "Out" },
  { value: "Drain", label: "Drain", direction: "Out" },
  { value: "Stool", label: "Stool", direction: "Out" },
  { value: "Other", label: "Other", direction: "In" },
];
export const fluidKindLabel = (k: FluidKind) => FLUID_KINDS.find((x) => x.value === k)?.label ?? k;

export const DISCHARGE_TYPES: { value: DischargeType; label: string; text: string }[] = [
  { value: "Home", label: "Home", text: "Discharged home. Needs a signed discharge summary." },
  { value: "AgainstAdvice", label: "Against medical advice", text: "The patient is leaving against advice. Write why." },
  { value: "TransferredOut", label: "Transfer out", text: "Transferred to another facility. Needs a signed discharge summary." },
  { value: "Died", label: "Died", text: "Record the time and cause of death. The patient's booked appointments are cancelled without a message." },
];
export const dischargeLabel = (t: DischargeType | null | undefined) => (t ? (DISCHARGE_TYPES.find((x) => x.value === t)?.label ?? t) : "");

export const BILL_STATUS: Record<BillStatus, { label: string; tone: BadgeTone }> = {
  Unpaid: { label: "Unpaid", tone: "arterial" },
  PartPaid: { label: "Part paid", tone: "iodine" },
  Paid: { label: "Paid", tone: "pass" },
  Waived: { label: "Waived", tone: "neutral" },
  InCredit: { label: "In credit", tone: "scrub" },
};

export const PAYMENT_STATUS: Record<CarePaymentStatus, { label: string; tone: BadgeTone }> = {
  Succeeded: { label: "Received", tone: "pass" },
  Pending: { label: "Pending", tone: "neutral" },
  Mismatch: { label: "Amount mismatch", tone: "arterial" },
  Reversed: { label: "Reversed", tone: "iodine" },
  Abandoned: { label: "Abandoned", tone: "neutral" },
};

/** Desk methods (online payments come from the portal). */
export const DESK_METHODS: { value: PayMethod; label: string }[] = [
  { value: "Cash", label: "Cash" },
  { value: "Pos", label: "POS" },
  { value: "Transfer", label: "Bank transfer" },
];
export const methodLabel = (m: PayMethod) => (m === "Online" ? "Online (Paystack)" : (DESK_METHODS.find((x) => x.value === m)?.label ?? m));

export const ADJUSTMENT_KINDS: { value: AdjustmentKind; label: string }[] = [
  { value: "Discount", label: "Discount" },
  { value: "Waiver", label: "Waiver" },
];

/** A `datetime-local` value ("2026-11-05T14:30") read as Lagos time, as an ISO instant. */
export function lagosLocalToIso(value: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)) return null;
  return `${value}:00+01:00`;
}

/** Now in Lagos as a `datetime-local` value. */
export function lagosNowLocal(): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Africa/Lagos",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).formatToParts(new Date());
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? "00";
  return `${get("year")}-${get("month")}-${get("day")}T${get("hour") === "24" ? "00" : get("hour")}:${get("minute")}`;
}

/** A Lagos day ("YYYY-MM-DD") moved by n days. */
export function addDays(day: string, n: number): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

/** A fresh key for one desk payment, so a retry after a lost answer records it once. */
export function newIdempotencyKey(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) return `desk-${crypto.randomUUID()}`;
  return `desk-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
