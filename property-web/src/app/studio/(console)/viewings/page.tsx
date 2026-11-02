"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { WEEKDAYS } from "@/lib/config";
import { addDays, formatShortDay, formatTime, lagosDay, lagosToday, phoneDigits } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { INSPECTION_STATUS, statusOf } from "@/lib/status";
import { qs, useApi } from "@/lib/use-api";
import type { InspectionDto, InspectionStatus, SlotDto, Weekday } from "@/lib/types";
import { CalendarIcon, PhoneIcon, PlusIcon, TrashIcon } from "@/components/icons";
import {
  ApiErrorView,
  Button,
  Card,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Segmented,
  StatusBadge,
  buttonClass,
  cx,
  inputClass,
} from "@/components/ui";

export default function ViewingsPage() {
  const [range, setRange] = useState<"upcoming" | "past">("upcoming");
  const [today] = useState(() => lagosToday());
  const from = range === "upcoming" ? today : addDays(today, -14);
  const to = range === "upcoming" ? addDays(today, 15) : today;
  const { data, error, reload } = useApi<InspectionDto[]>(`/api/property/inspections${qs({ from, to })}`);
  const canManage = useCan("leads.manage");
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [moving, setMoving] = useState<InspectionDto | null>(null);

  const list = [...asList(data)].sort((a, b) => new Date(a.scheduledAt).getTime() - new Date(b.scheduledAt).getTime());
  if (range === "past") list.reverse();
  const groups = new Map<string, InspectionDto[]>();
  for (const v of list) {
    const d = lagosDay(v.scheduledAt);
    groups.set(d, [...(groups.get(d) ?? []), v]);
  }

  async function setStatus(v: InspectionDto, status: InspectionStatus, proposedAt?: string) {
    setBusy(v.id);
    setActionError(null);
    try {
      await api<InspectionDto>(`/api/property/inspections/${v.id}/status`, { method: "POST", body: proposedAt ? { status, proposedAt } : { status } });
      reload();
      return true;
    } catch (e) {
      setActionError(toApiError(e));
      return false;
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Viewings" subtitle="Seekers book into your weekly windows. Confirm each request, then mark it done or no-show." />

      <section aria-labelledby="agenda-h" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="agenda-h" className="font-semibold text-asphalt">
            Agenda
          </h2>
          <Segmented
            label="Range"
            className="w-64"
            value={range}
            onChange={setRange}
            items={[
              { value: "upcoming", label: "Next 14 days" },
              { value: "past", label: "Past 14 days" },
            ]}
          />
        </div>
        {actionError && <ApiErrorView error={actionError} />}
        {!data && error && <ApiErrorView error={error} onRetry={reload} />}
        {!data && !error && <PageLoader />}
        {data && list.length === 0 && (
          <EmptyState icon={<CalendarIcon />} title={range === "upcoming" ? "No viewings booked" : "No viewings in the last 14 days"} text="Set your weekly windows below so seekers can book." />
        )}
        {[...groups.entries()].map(([day, items]) => (
          <div key={day}>
            <h3 className="mb-1.5 text-sm font-semibold text-slate-700">{day === today ? `Today, ${formatShortDay(day)}` : formatShortDay(day)}</h3>
            <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
              {items.map((v) => {
                const st = statusOf(INSPECTION_STATUS, v.status);
                const open = v.status === "Requested" || v.status === "Confirmed";
                return (
                  <li key={v.id} className="flex flex-col gap-2 p-3 sm:flex-row sm:items-center sm:p-4">
                    <span className="tnum w-14 shrink-0 text-lg font-semibold text-asphalt">{formatTime(v.scheduledAt)}</span>
                    <div className="min-w-0 flex-1">
                      <p className="font-semibold text-asphalt">{v.seekerName}</p>
                      <p className="text-sm text-muted">
                        {v.listingTitle}, {v.areaName}. Agent: {v.agentName}
                      </p>
                      {v.notes && <p className="mt-0.5 text-sm text-slate-700">“{v.notes}”</p>}
                      <div className="mt-1">
                        <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      </div>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {v.seekerPhone && (
                        <a href={`tel:+${phoneDigits(v.seekerPhone)}`} className={buttonClass("secondary")}>
                          <PhoneIcon className="h-4 w-4" /> Call
                        </a>
                      )}
                      {canManage && v.status === "Requested" && (
                        <Button loading={busy === v.id} onClick={() => setStatus(v, "Confirmed")}>
                          Confirm
                        </Button>
                      )}
                      {canManage && open && (
                        <>
                          <Button variant="secondary" onClick={() => setMoving(v)}>
                            Move
                          </Button>
                          {range === "past" || new Date(v.scheduledAt).getTime() < new Date(`${today}T23:59:59+01:00`).getTime() ? (
                            <>
                              <Button variant="secondary" loading={busy === v.id} onClick={() => setStatus(v, "Done")}>
                                Done
                              </Button>
                              <Button variant="ghost" onClick={() => setStatus(v, "NoShow")}>
                                No-show
                              </Button>
                            </>
                          ) : null}
                          <Button variant="danger" onClick={() => window.confirm("Cancel this viewing? The seeker is told by email.") && setStatus(v, "Cancelled")}>
                            Cancel
                          </Button>
                        </>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </section>

      <WeeklyWindows canManage={canManage} />

      <Modal open={!!moving} title="Move this viewing" onClose={() => setMoving(null)}>
        {moving && (
          <MoveForm
            viewing={moving}
            onSubmit={async (iso) => {
              if (await setStatus(moving, moving.status, iso)) setMoving(null);
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function MoveForm({ viewing, onSubmit }: { viewing: InspectionDto; onSubmit: (iso: string) => Promise<void> }) {
  const [value, setValue] = useState(`${lagosDay(viewing.scheduledAt)}T${formatTime(viewing.scheduledAt)}`);
  const [busy, setBusy] = useState(false);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    // The picker is in Lagos time (UTC+1, no daylight saving).
    await onSubmit(`${value}:00+01:00`);
    setBusy(false);
  }
  return (
    <form onSubmit={submit} className="space-y-4">
      <p className="text-sm text-slate-700">
        {viewing.seekerName} booked {formatShortDay(viewing.scheduledAt)} at {formatTime(viewing.scheduledAt)}. Pick the new time you agreed with them.
      </p>
      <Input label="New date and time (Lagos)" type="datetime-local" required value={value} onChange={(e) => setValue(e.target.value)} />
      <Button type="submit" className="w-full" loading={busy}>
        Move viewing
      </Button>
    </form>
  );
}

/** My weekly windows: seekers see slots inside these for the next 14 days (at least 2 hours ahead). */
function WeeklyWindows({ canManage }: { canManage: boolean }) {
  const { data, error, reload } = useApi<SlotDto[]>("/api/property/inspection-slots");
  const [draft, setDraft] = useState<SlotDto[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [saveError, setSaveError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const slots = draft ?? asList(data);
  const edit = (next: SlotDto[]) => {
    setDraft(next);
    setSaved(false);
  };

  async function save() {
    setBusy(true);
    setSaveError(null);
    try {
      await api("/api/property/inspection-slots", { method: "PUT", body: { slots } });
      setSaved(true);
      setDraft(null);
      reload();
    } catch (e) {
      setSaveError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <SectionTitle
        action={
          canManage && (
            <Button variant="secondary" onClick={() => edit([...slots, { weekday: "Saturday", start: "10:00", end: "14:00", slotMinutes: 30 }])}>
              <PlusIcon className="h-4 w-4" /> Add window
            </Button>
          )
        }
      >
        My weekly viewing windows
      </SectionTitle>
      {!data && error && <ErrorBox error={error} onRetry={reload} />}
      {data && slots.length === 0 && <p className="text-sm text-muted">No windows yet, so seekers can&apos;t book viewings with you. Add the times you can show homes.</p>}
      {slots.length > 0 && (
        <ul className="space-y-2">
          {slots.map((s, i) => (
            <li key={i} className="grid grid-cols-2 items-end gap-2 rounded-control border border-line-soft p-2 sm:grid-cols-[minmax(0,1fr)_7rem_7rem_8rem_auto]">
              <label className="col-span-2 text-xs text-muted sm:col-span-1">
                Day
                <select
                  className={cx(inputClass, "mt-1")}
                  disabled={!canManage}
                  value={s.weekday}
                  onChange={(e) => edit(slots.map((x, j) => (j === i ? { ...x, weekday: e.target.value as Weekday } : x)))}
                >
                  {WEEKDAYS.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
              </label>
              <label className="text-xs text-muted">
                From
                <input type="time" className={cx(inputClass, "mt-1")} disabled={!canManage} value={s.start} onChange={(e) => edit(slots.map((x, j) => (j === i ? { ...x, start: e.target.value } : x)))} />
              </label>
              <label className="text-xs text-muted">
                To
                <input type="time" className={cx(inputClass, "mt-1")} disabled={!canManage} value={s.end} onChange={(e) => edit(slots.map((x, j) => (j === i ? { ...x, end: e.target.value } : x)))} />
              </label>
              <label className="text-xs text-muted">
                Each viewing
                <select
                  className={cx(inputClass, "mt-1")}
                  disabled={!canManage}
                  value={s.slotMinutes}
                  onChange={(e) => edit(slots.map((x, j) => (j === i ? { ...x, slotMinutes: Number(e.target.value) } : x)))}
                >
                  {[15, 20, 30, 45, 60, 90].map((m) => (
                    <option key={m} value={m}>
                      {m} min
                    </option>
                  ))}
                </select>
              </label>
              {canManage && (
                <button type="button" onClick={() => edit(slots.filter((_, j) => j !== i))} className="flex h-11 w-11 items-center justify-center rounded-control text-brick hover:bg-brick-wash" aria-label={`Remove ${s.weekday} window`}>
                  <TrashIcon className="h-4 w-4" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {saveError && <div className="mt-3"><ErrorBox error={saveError} /></div>}
      {saved && <Notice tone="success" className="mt-3">Windows saved.</Notice>}
      {canManage && draft && (
        <div className="mt-3 flex gap-2">
          <Button loading={busy} onClick={save}>
            Save windows
          </Button>
          <Button variant="ghost" onClick={() => setDraft(null)}>
            Discard changes
          </Button>
        </div>
      )}
    </Card>
  );
}
