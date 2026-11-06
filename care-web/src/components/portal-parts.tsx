"use client";

// Small pieces shared by the patient portal pages (Task 20). Plain language for patients and families: no clinical shorthand where a
// word will do, dates and times in Lagos time, money in naira.

import { useEffect, type ReactNode } from "react";
import type { ApiError } from "@/lib/api";
import { isPortalUnavailable, usePortalMaybe } from "@/lib/portal";
import type { AppointmentStatus, BillStatus, DischargeType, DoseFrequency } from "@/lib/types/clinical";
import type { CarePaymentStatus } from "@/lib/types/billing";
import type { PayMethod } from "@/lib/types/inpatient";
import { Badge, ErrorBox, cx, panelClass, type BadgeTone } from "./ui";

const dateFmt = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", year: "numeric", timeZone: "Africa/Lagos" });
const timeFmt = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "Africa/Lagos" });
const shortFmt = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "Africa/Lagos" });

/** "Tue 3 Nov 2026" in Lagos time. */
export function lagosDate(iso: string): string {
  return dateFmt.format(new Date(iso));
}
/** "09:30" in Lagos time. */
export function lagosClock(iso: string): string {
  return timeFmt.format(new Date(iso));
}
/** "3 Nov 2026" in Lagos time. */
export function lagosShort(iso: string | null | undefined): string {
  return iso ? shortFmt.format(new Date(iso)) : "—";
}

export const APPOINTMENT_STATUS: Record<AppointmentStatus, { label: string; tone: BadgeTone }> = {
  Booked: { label: "Booked", tone: "scrub" },
  CheckedIn: { label: "Checked in", tone: "pass" },
  Done: { label: "Seen", tone: "neutral" },
  Cancelled: { label: "Cancelled", tone: "neutral" },
  NoShow: { label: "Missed", tone: "iodine" },
};

export const BILL_STATUS: Record<BillStatus, { label: string; tone: BadgeTone }> = {
  Unpaid: { label: "To pay", tone: "iodine" },
  PartPaid: { label: "Part paid", tone: "iodine" },
  Paid: { label: "Paid", tone: "pass" },
  Waived: { label: "Waived", tone: "neutral" },
  InCredit: { label: "In credit", tone: "pass" },
};

export const PAYMENT_STATUS: Partial<Record<CarePaymentStatus, { label: string; tone: BadgeTone }>> = {
  Succeeded: { label: "Paid", tone: "pass" },
  Reversed: { label: "Reversed", tone: "neutral" },
};

export const PAY_METHOD: Record<PayMethod, string> = { Cash: "Cash", Pos: "Card (POS)", Transfer: "Bank transfer", Online: "Online" };

export const DISCHARGE: Record<DischargeType, string> = {
  Home: "Went home",
  AgainstAdvice: "Left before discharge",
  TransferredOut: "Moved to another hospital",
  Died: "Passed away",
};

/** How often, in plain words. */
export const FREQUENCY_WORDS: Record<DoseFrequency, string> = {
  OD: "once a day",
  BD: "twice a day",
  TDS: "three times a day",
  QDS: "four times a day",
  Q4H: "every 4 hours",
  Q6H: "every 6 hours",
  Q8H: "every 8 hours",
  Nocte: "at night",
  Stat: "once",
  PRN: "when needed",
};

export function StatusBadge({ map, value }: { map: Partial<Record<string, { label: string; tone: BadgeTone }>>; value: string }) {
  const s = map[value];
  return <Badge tone={s?.tone ?? "neutral"}>{s?.label ?? value}</Badge>;
}

/** A page section: a heading and a white panel. */
export function Section({ title, id, children, className }: { title: string; id?: string; children: ReactNode; className?: string }) {
  return (
    <section aria-labelledby={id} className={cx("space-y-3", className)}>
      <h2 id={id} className="display text-lg font-bold text-ink">
        {title}
      </h2>
      {children}
    </section>
  );
}

/** A quiet "nothing here yet" line inside a section. */
export function Nothing({ children }: { children: ReactNode }) {
  return <p className={cx(panelClass, "px-4 py-4 text-sm text-muted")}>{children}</p>;
}

/** A label and a value, as on a paper form. Empty values are left out. */
export function Field({ label, value }: { label: string; value: ReactNode }) {
  if (value === null || value === undefined || value === "") return null;
  return (
    <div>
      <dt className="text-xs font-bold text-muted">{label}</dt>
      <dd className="mt-0.5 text-sm whitespace-pre-line text-ink">{value}</dd>
    </div>
  );
}

/** An API error on a portal page: the suspended-clinic sentence as a heading, anything else as the API's own sentence. */
export function PortalError({ error, onRetry }: { error: ApiError | null | undefined; onRetry?: () => void }) {
  // Final fix pass (Task 20 carry): a 404 from a patient route means the link was revoked mid-session. The portal drops the patient and
  // reloads its list, so the switcher and the pages stop showing it.
  const portal = usePortalMaybe();
  const gone = error?.status === 404;
  const patientId = portal?.patient?.id;
  const lost = portal?.lost;
  useEffect(() => {
    if (gone && patientId && lost) lost(patientId);
  }, [gone, patientId, lost]);
  if (!error) return null;
  if (isPortalUnavailable(error)) {
    return (
      <div className={cx(panelClass, "p-5")} role="alert">
        <h2 className="display text-xl font-bold text-ink">{error.message}</h2>
        <p className="mt-2 max-w-prose text-sm text-slate-700">The clinic can tell you when it will open again.</p>
      </div>
    );
  }
  return <ErrorBox error={error} onRetry={onRetry} />;
}
