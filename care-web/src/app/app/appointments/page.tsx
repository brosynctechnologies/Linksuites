"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatDay, lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { PatientSummaryDto } from "@/lib/types";
import type { AppointmentDto, AppointmentStatus, DoctorDto, DoctorScheduleDto, ScheduleBlockDto, SlotDto, Weekday } from "@/lib/types/clinical";
import { CheckInDialog, lagosTime } from "@/components/check-in";
import { ChevronLeftIcon, ChevronRightIcon, PlusIcon } from "@/components/icons";
import { PatientPicker } from "@/components/patient-picker";
import {
  ApiErrorView,
  Badge,
  Button,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  Select,
  Wristband,
  buttonClass,
  cx,
  inputClass,
  type BadgeTone,
} from "@/components/ui";

/** The API's label for the booking reason (AppointmentService.ReasonLabel). */
const REASON_LABEL = "Reason for visit (no medical details)";

const STATUS: Record<AppointmentStatus, { label: string; tone: BadgeTone }> = {
  Booked: { label: "Booked", tone: "scrub" },
  CheckedIn: { label: "Checked in", tone: "pass" },
  Done: { label: "Done", tone: "neutral" },
  Cancelled: { label: "Cancelled", tone: "neutral" },
  NoShow: { label: "No-show", tone: "iodine" },
};

function shiftDay(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

const weekdayFmt = new Intl.DateTimeFormat("en-GB", { weekday: "long", timeZone: "UTC" });

/**
 * Appointments: a day's calendar by doctor (each doctor a column of booked appointments and open slots), booking from an open slot,
 * moving, cancelling and checking in. Everyone but the cashier (the API's desk rule). The booking reason is non-clinical.
 * Weekly hours: leadership for any doctor, a doctor for their own.
 */
export default function AppointmentsPage() {
  const { me, access } = useCare();
  const today = lagosToday();
  const [date, setDate] = useState(today);
  const [doctorId, setDoctorId] = useState<string>("all");
  const [hoursFor, setHoursFor] = useState<DoctorDto | null>(null);
  const doctors = useApi<DoctorDto[]>(access.isCashier ? null : "/api/care/doctors");
  const appointments = useApi<AppointmentDto[]>(
    access.isCashier ? null : `/api/care/appointments${qs({ date, doctorUserId: doctorId === "all" ? undefined : doctorId })}`,
  );
  // Slots and appointments change together: one counter refreshes every column's slots after any change.
  const [version, setVersion] = useState(0);
  const refresh = () => {
    appointments.reload();
    setVersion((v) => v + 1);
  };

  if (access.isCashier) return <NoAccess text="Appointments are for the front desk, clinical staff and leadership." />;

  const list = doctors.data ?? [];
  const shown = doctorId === "all" ? list : list.filter((d) => d.userId === doctorId);
  const own = list.find((d) => d.userId === me.userId);
  const canEditHours = !access.readOnly && (access.isLeadership || !!own);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Appointments"
        subtitle={`${weekdayFmt.format(new Date(`${date}T00:00:00Z`))}, ${formatDay(date)}. Times are Lagos time.`}
        action={
          canEditHours && (
            <Button
              variant="secondary"
              onClick={() => setHoursFor(access.isLeadership ? (shown.length === 1 ? shown[0] : (own ?? list[0] ?? null)) : (own ?? null))}
              disabled={list.length === 0}
            >
              Weekly hours
            </Button>
          )
        }
      />

      <div className="flex flex-wrap items-end gap-3">
        <div className="flex items-center gap-1">
          <button type="button" className={buttonClass("ghost", "px-2.5")} aria-label="Previous day" onClick={() => setDate((d) => shiftDay(d, -1))}>
            <ChevronLeftIcon className="h-4 w-4" />
          </button>
          <label htmlFor="appt-date" className="sr-only">
            Day
          </label>
          <input id="appt-date" type="date" className={cx(inputClass, "w-auto")} value={date} onChange={(e) => e.target.value && setDate(e.target.value)} />
          <button type="button" className={buttonClass("ghost", "px-2.5")} aria-label="Next day" onClick={() => setDate((d) => shiftDay(d, 1))}>
            <ChevronRightIcon className="h-4 w-4" />
          </button>
          {date !== today && (
            <button type="button" className={buttonClass("ghost")} onClick={() => setDate(today)}>
              Today
            </button>
          )}
        </div>
        <Select label="Doctor" srLabel className="w-auto min-w-48" value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
          <option value="all">All doctors</option>
          {list.map((d) => (
            <option key={d.userId} value={d.userId}>
              {d.name}
            </option>
          ))}
        </Select>
      </div>

      {doctors.error && <ApiErrorView error={doctors.error} onRetry={doctors.reload} />}
      {appointments.error && <ApiErrorView error={appointments.error} onRetry={appointments.reload} />}
      {(!doctors.data || !appointments.data) && !doctors.error && !appointments.error && <PageLoader />}
      {doctors.data && list.length === 0 && (
        <EmptyState title="No doctors yet" text="Appointments are booked with doctors. Invite a doctor from the staff page, then set their weekly hours." />
      )}
      {doctors.data && appointments.data && shown.length > 0 && (
        <div className="no-scrollbar -mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
          <div className="grid auto-cols-[minmax(16rem,1fr)] grid-flow-col gap-3">
            {shown.map((d) => (
              <DoctorColumn
                key={d.userId}
                doctor={d}
                date={date}
                today={today}
                version={version}
                appointments={appointments.data!.filter((a) => a.doctorUserId === d.userId)}
                doctors={list}
                onChanged={refresh}
              />
            ))}
          </div>
        </div>
      )}

      {hoursFor && (
        <WeeklyHours
          doctors={access.isLeadership ? list : list.filter((d) => d.userId === me.userId)}
          initial={hoursFor}
          onClose={() => setHoursFor(null)}
          onSaved={() => setVersion((v) => v + 1)}
        />
      )}
    </div>
  );
}

type Entry = { kind: "appointment"; at: string; appointment: AppointmentDto } | { kind: "slot"; at: string; slot: SlotDto };

function DoctorColumn({
  doctor,
  date,
  today,
  version,
  appointments,
  doctors,
  onChanged,
}: {
  doctor: DoctorDto;
  date: string;
  today: string;
  version: number;
  appointments: AppointmentDto[];
  doctors: DoctorDto[];
  onChanged: () => void;
}) {
  const { access } = useCare();
  const slots = useApi<SlotDto[]>(`/api/care/doctors/${doctor.userId}/slots${qs({ date })}`, { nonce: version });
  const [booking, setBooking] = useState<SlotDto | null>(null);
  const canBook = !access.readOnly && date >= today;
  const entries: Entry[] = [
    ...appointments.map((a) => ({ kind: "appointment" as const, at: a.start, appointment: a })),
    ...(canBook ? (slots.data ?? []) : []).map((s) => ({ kind: "slot" as const, at: s.start, slot: s })),
  ].sort((a, b) => a.at.localeCompare(b.at));

  return (
    <section aria-label={doctor.name} className="min-w-0 rounded-panel border border-rule bg-surface">
      <h2 className="border-b border-rule bg-slate-50 px-4 py-2.5 font-semibold text-ink">{doctor.name}</h2>
      <div className="space-y-2 p-3">
        {slots.error && <ErrorBox error={slots.error} onRetry={slots.reload} />}
        {slots.loading && !slots.data && <p className="text-sm text-muted">Loading slots…</p>}
        {entries.length === 0 && slots.data && (
          <p className="text-sm text-muted">{canBook ? "No open times this day. Set the doctor's weekly hours to open slots." : "No appointments."}</p>
        )}
        {entries.map((e) =>
          e.kind === "slot" ? (
            <button
              key={`s-${e.at}`}
              type="button"
              onClick={() => setBooking(e.slot)}
              className="flex min-h-11 w-full items-center gap-2 rounded-control border border-dashed border-scrub-line px-3 text-left text-sm text-scrub hover:bg-scrub-wash"
            >
              <PlusIcon className="h-4 w-4" />
              <span className="tnum font-bold">{lagosTime(e.slot.start)}</span> Open
            </button>
          ) : (
            <AppointmentCard key={e.appointment.id} appointment={e.appointment} isToday={date === today} doctors={doctors} onChanged={onChanged} />
          ),
        )}
      </div>
      {booking && <BookDialog doctor={doctor} slot={booking} onClose={() => setBooking(null)} onBooked={onChanged} />}
    </section>
  );
}

function AppointmentCard({ appointment: a, isToday, doctors, onChanged }: { appointment: AppointmentDto; isToday: boolean; doctors: DoctorDto[]; onChanged: () => void }) {
  const { access } = useCare();
  const cancel = useAction();
  const [checkingIn, setCheckingIn] = useState(false);
  const [moving, setMoving] = useState(false);
  const status = STATUS[a.status];
  const live = a.status === "Booked" && !access.readOnly;

  async function doCancel() {
    if (!window.confirm(`Cancel ${a.patientName}'s appointment at ${lagosTime(a.start)}?`)) return;
    const res = await cancel.run(() => api<AppointmentDto>(`/api/care/appointments/${a.id}/cancel`, { method: "POST" }));
    if (res.ok) onChanged();
  }

  return (
    <div className={cx("rounded-control border border-rule p-3 text-sm", a.status === "Cancelled" && "opacity-60")}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="tnum font-bold text-ink">
          {lagosTime(a.start)}–{lagosTime(a.end)}
        </span>
        <Badge tone={status.tone}>{status.label}</Badge>
      </div>
      <p className="mt-1.5 flex flex-wrap items-center gap-2">
        <Wristband number={a.patientNumber} />
        <span className="font-bold text-ink">{a.patientName}</span>
      </p>
      {a.reason && <p className="mt-1 text-muted">{a.reason}</p>}
      {a.source === "Portal" && <p className="mt-1 text-xs text-muted">Booked by the patient online.</p>}
      {live && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {isToday && (
            <Button className="min-h-10 px-3" onClick={() => setCheckingIn(true)}>
              Check in
            </Button>
          )}
          <Button variant="secondary" className="min-h-10 px-3" onClick={() => setMoving(true)}>
            Move
          </Button>
          <Button variant="ghost" className="min-h-10 px-3" loading={cancel.busy} onClick={doCancel}>
            Cancel
          </Button>
        </div>
      )}
      {cancel.error && (
        <div className="mt-2">
          <ErrorBox error={cancel.error} />
        </div>
      )}
      {checkingIn && (
        <CheckInDialog
          patient={{ id: a.patientId, number: a.patientNumber, fullName: a.patientName }}
          onClose={() => setCheckingIn(false)}
          onDone={onChanged}
        />
      )}
      {moving && <MoveDialog appointment={a} doctors={doctors} onClose={() => setMoving(false)} onMoved={onChanged} />}
    </div>
  );
}

function BookDialog({ doctor, slot, onClose, onBooked }: { doctor: DoctorDto; slot: SlotDto; onClose: () => void; onBooked: () => void }) {
  const [patient, setPatient] = useState<PatientSummaryDto | null>(null);
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!patient) return;
    const res = await run(() =>
      api<AppointmentDto>("/api/care/appointments", {
        method: "POST",
        body: { patientId: patient.id, doctorUserId: doctor.userId, start: slot.start, reason: reason.trim() || null },
      }),
    );
    if (res.ok) {
      onBooked();
      onClose();
    } else if (res.error.code === "slot_taken") onBooked();
  }

  return (
    <Modal open title="Book an appointment" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">
          With <span className="font-bold text-ink">{doctor.name}</span> at <span className="tnum font-bold text-ink">{lagosTime(slot.start)}</span>.
        </p>
        {patient ? (
          <div className="flex flex-wrap items-center gap-2 rounded-control border border-scrub-line bg-scrub-wash px-3 py-2.5 text-sm">
            <Wristband number={patient.number} />
            <span className="font-bold text-ink">{patient.fullName}</span>
            <Button variant="ghost" className="ml-auto min-h-9 px-2.5" onClick={() => setPatient(null)}>
              Change
            </Button>
          </div>
        ) : (
          <PatientPicker onPick={setPatient} />
        )}
        <Input
          label={REASON_LABEL}
          maxLength={120}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          error={error?.field("reason")}
          hint="Such as follow-up or new patient. Front desk can read this."
        />
        <ErrorBox error={error && !error.field("reason") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!patient}>
            Book
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function MoveDialog({ appointment: a, doctors, onClose, onMoved }: { appointment: AppointmentDto; doctors: DoctorDto[]; onClose: () => void; onMoved: () => void }) {
  const today = lagosToday();
  const [doctorId, setDoctorId] = useState(a.doctorUserId);
  const [date, setDate] = useState(() => new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Lagos" }).format(new Date(a.start)));
  const [start, setStart] = useState<string | null>(null);
  const slots = useApi<SlotDto[]>(date ? `/api/care/doctors/${doctorId}/slots${qs({ date })}` : null);
  const { busy, error, run } = useAction();

  async function submit() {
    if (!start) return;
    const res = await run(() => api<AppointmentDto>(`/api/care/appointments/${a.id}`, { method: "PUT", body: { doctorUserId: doctorId, start, reason: null } }));
    if (res.ok) {
      onMoved();
      onClose();
    } else if (res.error.code === "slot_taken") slots.reload();
  }

  return (
    <Modal
      open
      title="Move the appointment"
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={busy} disabled={!start} onClick={submit}>
            Move
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="flex flex-wrap items-center gap-2 text-sm">
          <Wristband number={a.patientNumber} /> <span className="font-bold text-ink">{a.patientName}</span>
        </p>
        <div className="grid gap-3 sm:grid-cols-2">
          <Select
            label="Doctor"
            value={doctorId}
            onChange={(e) => {
              setDoctorId(e.target.value);
              setStart(null);
            }}
          >
            {doctors.map((d) => (
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
        {slots.error && <ErrorBox error={slots.error} onRetry={slots.reload} />}
        {slots.loading && <p className="text-sm text-muted">Loading open times…</p>}
        {slots.data && slots.data.length === 0 && <p className="text-sm text-muted">No open times that day.</p>}
        {slots.data && slots.data.length > 0 && (
          <fieldset>
            <legend className="mb-2 text-sm font-medium text-slate-700">Open times</legend>
            <div className="flex flex-wrap gap-2">
              {slots.data.map((s) => (
                <button
                  key={s.start}
                  type="button"
                  aria-pressed={start === s.start}
                  onClick={() => setStart(s.start)}
                  className={cx(
                    "tnum min-h-10 rounded-control border px-3 text-sm font-bold",
                    start === s.start ? "border-scrub bg-scrub text-white" : "border-rule bg-surface text-ink hover:border-scrub-line",
                  )}
                >
                  {lagosTime(s.start)}
                </button>
              ))}
            </div>
          </fieldset>
        )}
        <ErrorBox error={error} />
      </div>
    </Modal>
  );
}

// ---------- Weekly hours ----------

const WEEKDAYS: Weekday[] = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

interface BlockRow {
  key: number;
  weekday: Weekday;
  start: string;
  end: string;
  slotMinutes: string;
}

function toRows(blocks: ScheduleBlockDto[]): BlockRow[] {
  return blocks.map((b, i) => ({ key: i, weekday: b.weekday, start: b.start.slice(0, 5), end: b.end.slice(0, 5), slotMinutes: String(b.slotMinutes) }));
}

/** A doctor's weekly hours (`GET`/`PUT /api/care/doctors/{id}/schedule`): Lagos wall-clock blocks. Existing appointments are kept. */
function WeeklyHours({ doctors, initial, onClose, onSaved }: { doctors: DoctorDto[]; initial: DoctorDto; onClose: () => void; onSaved: () => void }) {
  const [doctorId, setDoctorId] = useState(initial.userId);
  return (
    <Modal open wide title="Weekly hours" onClose={onClose}>
      <div className="space-y-4">
        {doctors.length > 1 && (
          <Select label="Doctor" value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
            {doctors.map((d) => (
              <option key={d.userId} value={d.userId}>
                {d.name}
              </option>
            ))}
          </Select>
        )}
        <HoursEditor key={doctorId} doctorId={doctorId} onClose={onClose} onSaved={onSaved} />
      </div>
    </Modal>
  );
}

function HoursEditor({ doctorId, onClose, onSaved }: { doctorId: string; onClose: () => void; onSaved: () => void }) {
  const schedule = useApi<DoctorScheduleDto>(`/api/care/doctors/${doctorId}/schedule`);
  if (schedule.error) return <ApiErrorView error={schedule.error} onRetry={schedule.reload} />;
  if (!schedule.data) return <PageLoader />;
  return <HoursForm doctorId={doctorId} initial={toRows(schedule.data.blocks)} onClose={onClose} onSaved={onSaved} />;
}

function HoursForm({ doctorId, initial, onClose, onSaved }: { doctorId: string; initial: BlockRow[]; onClose: () => void; onSaved: () => void }) {
  const [rows, setRows] = useState<BlockRow[]>(initial);
  const [nextKey, setNextKey] = useState(initial.length);
  const [saved, setSaved] = useState(false);
  const { busy, error, run } = useAction();

  function update(key: number, patch: Partial<BlockRow>) {
    setSaved(false);
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }
  function add() {
    setSaved(false);
    const last = rows[rows.length - 1];
    const weekday = last ? WEEKDAYS[(WEEKDAYS.indexOf(last.weekday) + 1) % 7] : "Monday";
    setRows((rs) => [...rs, { key: nextKey, weekday, start: last?.start ?? "09:00", end: last?.end ?? "13:00", slotMinutes: last?.slotMinutes ?? "15" }]);
    setNextKey((k) => k + 1);
  }

  async function save() {
    const blocks = rows.map((r) => ({ weekday: r.weekday, start: r.start, end: r.end, slotMinutes: Number(r.slotMinutes) || null }));
    const res = await run(() => api<DoctorScheduleDto>(`/api/care/doctors/${doctorId}/schedule`, { method: "PUT", body: { blocks } }));
    if (res.ok) {
      setRows(toRows(res.data.blocks));
      setSaved(true);
      onSaved();
    }
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-slate-700">Open slots are made from these hours, minus booked appointments. Changing them leaves existing appointments as they are.</p>
      {rows.length === 0 && <p className="text-sm text-muted">No hours set, so nobody can book this doctor.</p>}
      <ul className="space-y-2">
        {rows.map((r) => (
          <li key={r.key} className="grid grid-cols-2 items-end gap-2 rounded-control border border-rule p-3 sm:grid-cols-[1.4fr_1fr_1fr_1fr_auto]">
            <Select label="Day" value={r.weekday} onChange={(e) => update(r.key, { weekday: e.target.value as Weekday })}>
              {WEEKDAYS.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </Select>
            <Input label="From" type="time" value={r.start} onChange={(e) => update(r.key, { start: e.target.value })} />
            <Input label="To" type="time" value={r.end} onChange={(e) => update(r.key, { end: e.target.value })} />
            <Input label="Slot (minutes)" type="number" min={5} max={120} value={r.slotMinutes} onChange={(e) => update(r.key, { slotMinutes: e.target.value })} />
            <Button
              variant="ghost"
              onClick={() => {
                setSaved(false);
                setRows((rs) => rs.filter((x) => x.key !== r.key));
              }}
            >
              Remove
            </Button>
          </li>
        ))}
      </ul>
      <Button variant="secondary" onClick={add}>
        <PlusIcon className="h-4 w-4" /> Add hours
      </Button>
      <ErrorBox error={error} />
      {saved && <Notice tone="success">Weekly hours saved.</Notice>}
      <div className="flex justify-end gap-2 border-t border-rule-soft pt-3">
        <Button variant="secondary" onClick={onClose}>
          Close
        </Button>
        <Button loading={busy} onClick={save}>
          Save hours
        </Button>
      </div>
    </div>
  );
}
