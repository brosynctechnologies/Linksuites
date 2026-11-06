"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { api } from "@/lib/api";
import { lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { AppointmentDto, CheckInRequest, VisitDto } from "@/lib/types/clinical";
import { Button, ErrorBox, Modal, Notice, PageLoader, Wristband, buttonClass, cx } from "./ui";

export interface CheckInPatient {
  id: string;
  number: string;
  fullName: string;
}

export const timeFmt = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "Africa/Lagos" });

/** "09:30" in Lagos time. */
export function lagosTime(iso: string): string {
  return timeFmt.format(new Date(iso));
}

/**
 * Check a patient in (`POST /api/care/visits`): against one of today's booked appointments, or as a walk-in. Front desk and above (the
 * cashier gets 403). Nothing clinical is shown or sent.
 */
export function CheckInDialog({ patient, onClose, onDone }: { patient: CheckInPatient; onClose: () => void; onDone?: (v: VisitDto) => void }) {
  const today = lagosToday();
  const booked = useApi<AppointmentDto[]>(`/api/care/appointments${qs({ patientId: patient.id, date: today, status: "Booked" })}`);
  const [choice, setChoice] = useState<string | null>(null);
  const [visit, setVisit] = useState<VisitDto | null>(null);
  const { busy, error, run } = useAction();
  const appointments = booked.data ?? [];
  // Preselect today's one booked appointment once it has loaded (a walk-in stays possible).
  const selected = choice ?? (appointments.length === 1 ? appointments[0].id : "walk-in");

  async function checkIn() {
    const body: CheckInRequest = { patientId: patient.id, appointmentId: selected === "walk-in" ? null : selected };
    const res = await run(() => api<VisitDto>("/api/care/visits", { method: "POST", body }));
    if (res.ok) {
      setVisit(res.data);
      onDone?.(res.data);
    }
  }

  return (
    <Modal open title="Check in" onClose={onClose}>
      <div className="space-y-4">
        <p className="flex flex-wrap items-center gap-2 font-bold text-ink">
          <Wristband number={patient.number} /> {patient.fullName}
        </p>
        {visit ? (
          <>
            <Notice tone="success">Checked in. The patient is waiting for vitals.</Notice>
            <div className="flex flex-wrap justify-end gap-2">
              <Button variant="secondary" onClick={onClose}>
                Close
              </Button>
              <Link href="/app/queue" className={buttonClass("primary")}>
                See the queue
              </Link>
            </div>
          </>
        ) : (
          <>
            {booked.error && <ErrorBox error={booked.error} onRetry={booked.reload} />}
            {booked.loading && !booked.data && <PageLoader label="Looking for today's appointments…" />}
            {booked.data && (
              <fieldset className="space-y-2">
                <legend className="mb-1 text-sm font-medium text-slate-700">Which visit is this?</legend>
                {appointments.map((a) => (
                  <Choice key={a.id} checked={selected === a.id} onChange={() => setChoice(a.id)}>
                    Appointment at <span className="tnum">{lagosTime(a.start)}</span> with {a.doctorName}
                    {a.reason && <span className="block text-xs text-muted">{a.reason}</span>}
                  </Choice>
                ))}
                <Choice checked={selected === "walk-in"} onChange={() => setChoice("walk-in")}>
                  Walk-in, no appointment
                </Choice>
              </fieldset>
            )}
            <p className="text-xs text-muted">Check-in opens the visit bill, with the registration charge on a first visit and the consultation charge.</p>
            <ErrorBox error={error} />
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={onClose}>
                Cancel
              </Button>
              <Button loading={busy} disabled={!booked.data} onClick={checkIn}>
                Check in
              </Button>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
}

function Choice({ checked, onChange, children }: { checked: boolean; onChange: () => void; children: ReactNode }) {
  return (
    <label
      className={cx(
        "flex min-h-11 cursor-pointer items-start gap-2.5 rounded-control border px-3 py-2.5 text-sm",
        checked ? "border-scrub bg-scrub-wash" : "border-rule bg-surface hover:border-slate-300",
      )}
    >
      <input type="radio" className="mt-0.5 h-4 w-4 accent-[#0f766e]" checked={checked} onChange={onChange} />
      <span className="min-w-0">{children}</span>
    </label>
  );
}
