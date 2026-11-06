"use client";

import { useState, type FormEvent } from "react";
import { CalendarIcon, PlusIcon } from "@/components/icons";
import { APPOINTMENT_STATUS, PortalError, StatusBadge, lagosClock, lagosDate } from "@/components/portal-parts";
import { Button, Card, EmptyState, ErrorBox, Input, Modal, PageHeader, PageLoader, Select, cx } from "@/components/ui";
import { api } from "@/lib/api";
import { lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { LATE_CANCEL, usePatientApi, usePortal } from "@/lib/portal";
import { qs, useApi } from "@/lib/use-api";
import type { DoctorDto, SlotDto } from "@/lib/types/clinical";
import type { PortalAppointmentDto, PortalAppointmentsDto } from "@/lib/types/portal";

/** The API's label for the booking reason, used until the list answers with its own (`reasonLabel`). */
const REASON_LABEL = "Reason for visit (no medical details)";

/**
 * Appointments: book from a doctor's open slots, and cancel at least 2 hours before (the API says why when it is later). There is no
 * portal reschedule route (API gap): to change a time, cancel and book again, or call the clinic.
 */
export default function PortalAppointmentsPage() {
  const { patient } = usePortal();
  const list = useApi<PortalAppointmentsDto>(usePatientApi("/appointments"));
  const [booking, setBooking] = useState(false);
  const [cancelling, setCancelling] = useState<PortalAppointmentDto | null>(null);
  const reasonLabel = list.data?.reasonLabel ?? REASON_LABEL;
  const [now] = useState(Date.now);
  const all = list.data?.appointments ?? [];
  const upcoming = all.filter((a) => new Date(a.end).getTime() >= now && (a.status === "Booked" || a.status === "CheckedIn")).sort((a, b) => a.start.localeCompare(b.start));
  const past = all.filter((a) => !upcoming.includes(a)).sort((a, b) => b.start.localeCompare(a.start));
  const canBook = patient?.status !== "Deceased";

  return (
    <div>
      <PageHeader
        title="Appointments"
        subtitle={patient ? `At ${patient.clinicName}.` : undefined}
        action={
          canBook ? (
            <Button onClick={() => setBooking(true)}>
              <PlusIcon className="h-4 w-4" /> Book
            </Button>
          ) : undefined
        }
      />
      {list.error && !list.data && <PortalError error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && (
        <div className="space-y-6">
          <section aria-labelledby="upcoming" className="space-y-3">
            <h2 id="upcoming" className="display text-lg font-bold text-ink">
              Coming up
            </h2>
            {upcoming.length === 0 ? (
              <EmptyState
                icon={<CalendarIcon />}
                title="Nothing booked"
                text="Pick a doctor and a time that suits you."
                action={canBook ? <Button onClick={() => setBooking(true)}>Book an appointment</Button> : undefined}
              />
            ) : (
              upcoming.map((a) => <AppointmentCard key={a.id} a={a} reasonLabel={reasonLabel} onCancel={() => setCancelling(a)} />)
            )}
            {upcoming.length > 0 && (
              <p className="text-sm text-muted">To change a time, cancel it and book again at least 2 hours before, or call the clinic.</p>
            )}
          </section>
          {past.length > 0 && (
            <section aria-labelledby="past" className="space-y-3">
              <h2 id="past" className="display text-lg font-bold text-ink">
                Earlier and cancelled
              </h2>
              <div className="case-sheet">
                <ul className="case-rows">
                  {past.map((a) => (
                    <li key={a.id} className="case-pad flex flex-wrap items-center justify-between gap-2 py-3 text-sm">
                      <span>
                        <span className="tnum font-bold text-ink">
                          {lagosDate(a.start)}, {lagosClock(a.start)}
                        </span>{" "}
                        <span className="text-slate-700">with {a.doctorName}</span>
                      </span>
                      <StatusBadge map={APPOINTMENT_STATUS} value={a.status} />
                    </li>
                  ))}
                </ul>
              </div>
            </section>
          )}
        </div>
      )}
      {booking && patient && (
        <BookDialog
          patientId={patient.id}
          reasonLabel={reasonLabel}
          onClose={() => setBooking(false)}
          onBooked={() => {
            setBooking(false);
            list.reload();
          }}
        />
      )}
      {cancelling && patient && (
        <CancelDialog
          patientId={patient.id}
          appointment={cancelling}
          onClose={() => setCancelling(null)}
          onDone={() => {
            setCancelling(null);
            list.reload();
          }}
        />
      )}
    </div>
  );
}

function AppointmentCard({ a, reasonLabel, onCancel }: { a: PortalAppointmentDto; reasonLabel: string; onCancel: () => void }) {
  const late = a.status === "Booked" && !a.canCancel;
  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="display text-lg font-bold text-ink">{lagosDate(a.start)}</p>
          <p className="tnum text-sm text-slate-700">
            {lagosClock(a.start)} to {lagosClock(a.end)} with {a.doctorName}
          </p>
        </div>
        <StatusBadge map={APPOINTMENT_STATUS} value={a.status} />
      </div>
      {a.reason && (
        <dl className="mt-3">
          <dt className="text-xs font-bold text-muted">{reasonLabel}</dt>
          <dd className="mt-0.5 text-sm text-ink">{a.reason}</dd>
        </dl>
      )}
      {a.canCancel && (
        <Button variant="secondary" className="mt-4" onClick={onCancel}>
          Cancel this appointment
        </Button>
      )}
      {late && <p className="mt-3 text-sm text-slate-700">{LATE_CANCEL}</p>}
    </Card>
  );
}

function BookDialog({ patientId, reasonLabel, onClose, onBooked }: { patientId: string; reasonLabel: string; onClose: () => void; onBooked: () => void }) {
  const today = lagosToday();
  const doctors = useApi<DoctorDto[]>(`/api/care-portal/patients/${patientId}/doctors`);
  const [doctorId, setDoctorId] = useState("");
  const [date, setDate] = useState(today);
  const [start, setStart] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const chosenDoctor = doctorId || doctors.data?.[0]?.userId || "";
  const slots = useApi<SlotDto[]>(chosenDoctor && date ? `/api/care-portal/patients/${patientId}/doctors/${chosenDoctor}/slots${qs({ date })}` : null);
  const { busy, error, run } = useAction();
  const [openedAt] = useState(Date.now);
  const open = (slots.data ?? []).filter((s) => new Date(s.start).getTime() > openedAt);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!start || !chosenDoctor) return;
    const res = await run(() =>
      api(`/api/care-portal/patients/${patientId}/appointments`, {
        method: "POST",
        body: { doctorUserId: chosenDoctor, start, reason: reason.trim() || null },
      }),
    );
    if (res.ok) onBooked();
    else if (res.error.code === "slot_taken") {
      setStart(null);
      slots.reload();
    }
  }

  return (
    <Modal open title="Book an appointment" onClose={onClose}>
      {doctors.error && !doctors.data ? (
        <PortalError error={doctors.error} onRetry={doctors.reload} />
      ) : !doctors.data ? (
        <PageLoader />
      ) : doctors.data.length === 0 ? (
        <p className="text-sm text-slate-700">The clinic has no doctors taking bookings online yet. Please call the clinic.</p>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Doctor"
              value={chosenDoctor}
              onChange={(e) => {
                setDoctorId(e.target.value);
                setStart(null);
              }}
            >
              {doctors.data.map((d) => (
                <option key={d.userId} value={d.userId}>
                  {d.name}
                </option>
              ))}
            </Select>
            <Input
              label="Day"
              type="date"
              min={today}
              value={date}
              onChange={(e) => {
                setDate(e.target.value);
                setStart(null);
              }}
            />
          </div>
          {slots.error && <PortalError error={slots.error} onRetry={slots.reload} />}
          {slots.loading && <p className="text-sm text-muted">Loading open times…</p>}
          {slots.data && !slots.loading && open.length === 0 && <p className="text-sm text-slate-700">No open times that day. Try another day.</p>}
          {open.length > 0 && !slots.loading && (
            <fieldset>
              <legend className="mb-2 text-sm font-medium text-slate-700">Open times</legend>
              <div className="flex flex-wrap gap-2">
                {open.map((s) => (
                  <button
                    key={s.start}
                    type="button"
                    aria-pressed={start === s.start}
                    onClick={() => setStart(s.start)}
                    className={cx(
                      "tnum min-h-11 rounded-control border px-3 text-sm font-bold",
                      start === s.start ? "border-scrub bg-scrub text-white" : "border-rule bg-surface text-ink hover:border-scrub-line",
                    )}
                  >
                    {lagosClock(s.start)}
                  </button>
                ))}
              </div>
            </fieldset>
          )}
          <Input
            label={reasonLabel}
            maxLength={120}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            error={error?.field("reason")}
            hint="For example: check-up, follow-up or new patient. The front desk can read this, so leave out medical details."
          />
          <ErrorBox error={error && !error.field("reason") ? error : null} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={busy} disabled={!start}>
              Book {start ? `for ${lagosClock(start)}` : ""}
            </Button>
          </div>
        </form>
      )}
    </Modal>
  );
}

function CancelDialog({ patientId, appointment: a, onClose, onDone }: { patientId: string; appointment: PortalAppointmentDto; onClose: () => void; onDone: () => void }) {
  const { busy, error, run } = useAction();
  async function confirm() {
    const res = await run(() => api(`/api/care-portal/patients/${patientId}/appointments/${a.id}/cancel`, { method: "POST" }));
    if (res.ok) onDone();
  }
  return (
    <Modal
      open
      title="Cancel this appointment?"
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Keep it
          </Button>
          <Button variant="danger" loading={busy} onClick={confirm}>
            Cancel the appointment
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-700">
          <span className="tnum font-bold text-ink">
            {lagosDate(a.start)} at {lagosClock(a.start)}
          </span>{" "}
          with {a.doctorName}.
        </p>
        <ErrorBox error={error} />
      </div>
    </Modal>
  );
}
