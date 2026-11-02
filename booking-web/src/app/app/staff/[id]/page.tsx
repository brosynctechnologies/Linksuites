"use client";

import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, asList, toApiError, upload, type ApiError } from "@/lib/api";
import { useCan } from "@/lib/permissions";
import { addDays, clockToMinutes, formatStamp, todayIn, WEEK_ORDER, WEEKDAYS, zonedInstant } from "@/lib/time";
import type { OpeningHours, SaveStaffRequest, ScheduleInterval, StaffDto, TeamDto, TimeOffDto, Weekday } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useBusiness, useServices } from "@/lib/use-business";
import { CopyButton } from "@/components/copy-button";
import { StaffAvatar } from "@/components/staff-avatar";
import {
  ApiErrorView,
  Button,
  Card,
  Checkbox,
  ErrorBox,
  Input,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  Textarea,
  Toggle,
  cx,
  inputClass,
} from "@/components/ui";
import { PlusIcon, TrashIcon } from "@/components/icons";
import { ColorSwatches } from "../color-swatches";

function toRequest(s: StaffDto): SaveStaffRequest {
  return {
    displayName: s.displayName,
    bio: s.bio,
    email: s.email,
    color: s.color,
    isBookable: s.isBookable,
    position: s.position,
    serviceIds: s.serviceIds,
    userId: s.userId,
  };
}

export default function StaffDetailPage() {
  const { id } = useParams<{ id: string }>();
  const isNew = useSearchParams().get("new") === "1";
  const canEdit = useCan("catalog.edit");
  // The contract has no GET /api/staff/{id}: load the list and pick this person.
  const { data: list, error, reload, setData: setList } = useApi<StaffDto[]>("/api/staff");
  const data = asList(list).find((s) => s.id === id);
  const setData = (s: StaffDto) => setList((l) => asList(l).map((x) => (x.id === s.id ? s : x)));
  const [banner, setBanner] = useState<string | null>(null);

  if (!data) {
    return (
      <div>
        <PageHeader title="Staff member" back="/app/staff" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : list ? <Notice tone="warning">This staff member doesn&apos;t exist any more.</Notice> : <PageLoader />}
      </div>
    );
  }

  const saved = (s: StaffDto, msg: string) => {
    setData(s);
    setBanner(msg);
  };

  return (
    <div className="space-y-4">
      <PageHeader title={data.displayName} back="/app/staff" subtitle={data.isBookable ? "Customers can book them online." : "Not bookable online."} />
      {isNew && !banner && <Notice tone="success">Added. Now set their working hours and the services they do.</Notice>}
      {banner && <Notice tone="success">{banner}</Notice>}

      <div className="grid gap-4 lg:grid-cols-2 lg:items-start">
        <div className="space-y-4">
          <ProfileCard key={`p-${data.id}`} staff={data} canEdit={canEdit} onSaved={(s) => saved(s, "Profile saved.")} />
          <FeedCard staff={data} />
        </div>
        <div className="space-y-4">
          <ScheduleCard key={`s-${data.id}`} staff={data} canEdit={canEdit} onSaved={(s) => saved(s, "Working hours saved.")} />
          <TimeOffCard staffId={data.id} canEdit={canEdit} />
          {canEdit && <DeleteCard staff={data} />}
        </div>
      </div>
    </div>
  );
}

// ---------- Profile ----------

function ProfileCard({ staff, canEdit, onSaved }: { staff: StaffDto; canEdit: boolean; onSaved: (s: StaffDto) => void }) {
  const { services } = useServices();
  const canSeeTeam = useCan("team.manage");
  const { data: team } = useApi<TeamDto>(canSeeTeam ? "/api/team" : null);
  const [form, setForm] = useState<SaveStaffRequest>(() => toRequest(staff));
  const [busy, setBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [photo, setPhoto] = useState(staff.photoUrl);
  const [error, setError] = useState<ApiError | null>(null);
  const set = <K extends keyof SaveStaffRequest>(k: K, v: SaveStaffRequest[K]) => setForm((f) => ({ ...f, [k]: v }));

  // A service counts as theirs when either side links them.
  const has = (serviceId: string) => form.serviceIds.includes(serviceId);
  const linkedFromService = (serviceId: string) => services.find((s) => s.id === serviceId)?.staffIds.includes(staff.id) ?? false;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.displayName.trim()) return setError(toApiError(new Error("Add the name customers see.")));
    setBusy(true);
    setError(null);
    try {
      onSaved(await api<StaffDto>(`/api/staff/${staff.id}`, { method: "PUT", body: { ...form, displayName: form.displayName.trim() } }));
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function uploadPhoto(file: File | undefined) {
    if (!file) return;
    setUploading(true);
    setError(null);
    try {
      const s = await upload<StaffDto>(`/api/staff/${staff.id}/photo`, file);
      setPhoto(s.photoUrl);
      onSaved(s);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setUploading(false);
    }
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <SectionTitle>Profile</SectionTitle>
        <div className="flex items-center gap-4">
          <StaffAvatar staff={{ ...staff, photoUrl: photo, color: form.color, displayName: form.displayName || staff.displayName }} size="lg" />
          {canEdit && (
            <label className={cx("cursor-pointer text-sm font-semibold text-rose underline-offset-2 hover:underline", uploading && "opacity-60")}>
              {uploading ? "Uploading…" : photo ? "Change photo" : "Add a photo"}
              <input type="file" accept="image/*" className="sr-only" disabled={uploading} onChange={(e) => uploadPhoto(e.target.files?.[0])} />
            </label>
          )}
        </div>
        <fieldset disabled={!canEdit} className="space-y-4">
          <Input label="Name customers see" value={form.displayName} onChange={(e) => set("displayName", e.target.value)} required />
          <Textarea label="Short bio (optional)" rows={2} value={form.bio ?? ""} onChange={(e) => set("bio", e.target.value || null)} placeholder="e.g. Braids and locs, 8 years" />
          <Input label="Email (optional)" type="email" value={form.email ?? ""} onChange={(e) => set("email", e.target.value || null)} />
          <ColorSwatches value={form.color} onChange={(c) => set("color", c)} disabled={!canEdit} />
          <Toggle checked={form.isBookable} onChange={(v) => set("isBookable", v)} label="Bookable online" description="Turn off for someone who only takes walk-ins." disabled={!canEdit} />
          <fieldset>
            <legend className="mb-2 text-sm font-medium text-slate-700">Services they do</legend>
            {services.length === 0 ? (
              <p className="text-sm text-muted">No services yet. Add them on the Services page.</p>
            ) : (
              <div className="grid gap-2 sm:grid-cols-2">
                {services.map((s) => (
                  <Checkbox
                    key={s.id}
                    checked={has(s.id) || linkedFromService(s.id)}
                    onChange={(on) => set("serviceIds", on ? [...new Set([...form.serviceIds, s.id])] : form.serviceIds.filter((x) => x !== s.id))}
                    label={s.name}
                  />
                ))}
              </div>
            )}
            {services.some((s) => linkedFromService(s.id) && !has(s.id)) && (
              <p className="mt-2 text-xs text-muted">Some services list them already; untick on the Services page to remove those.</p>
            )}
          </fieldset>
          {canSeeTeam && (
            <Select
              label="Their login (optional)"
              value={form.userId ?? ""}
              onChange={(e) => set("userId", e.target.value || null)}
              hint="Link a team member so their own login shows their bookings."
            >
              <option value="">Not linked</option>
              {asList(team?.members).map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.fullName || m.email} ({m.role})
                </option>
              ))}
            </Select>
          )}
        </fieldset>
        <ErrorBox error={error} />
        {canEdit && (
          <Button type="submit" loading={busy}>
            Save profile
          </Button>
        )}
      </form>
    </Card>
  );
}

// ---------- Weekly schedule ----------

type DayRows = Record<Weekday, { start: string; end: string }[]>;

function toRows(schedule: ScheduleInterval[]): DayRows {
  const rows = { 0: [], 1: [], 2: [], 3: [], 4: [], 5: [], 6: [] } as DayRows;
  for (const i of schedule) rows[i.weekday]?.push({ start: i.start, end: i.end });
  for (const w of WEEK_ORDER) rows[w].sort((a, b) => a.start.localeCompare(b.start));
  return rows;
}

function ScheduleCard({ staff, canEdit, onSaved }: { staff: StaffDto; canEdit: boolean; onSaved: (s: StaffDto) => void }) {
  const [rows, setRows] = useState<DayRows>(() => toRows(staff.schedule));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const { data: hours } = useApi<OpeningHours[]>("/api/booking-business/hours");

  const update = (w: Weekday, fn: (list: { start: string; end: string }[]) => { start: string; end: string }[]) =>
    setRows((r) => ({ ...r, [w]: fn(r[w]) }));

  function addInterval(w: Weekday) {
    update(w, (list) => {
      const last = list[list.length - 1];
      if (!last) return [{ start: "09:00", end: "18:00" }];
      // Adding a second interval leaves a break: split the last one around its middle.
      const mid = Math.round((clockToMinutes(last.start) + clockToMinutes(last.end)) / 2 / 30) * 30;
      const hh = (m: number) => `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`;
      return [...list.slice(0, -1), { start: last.start, end: hh(mid) }, { start: hh(Math.min(mid + 60, clockToMinutes(last.end))), end: last.end }];
    });
  }

  function copyMonday() {
    setRows((r) => ({ ...r, 2: [...r[1]], 3: [...r[1]], 4: [...r[1]], 5: [...r[1]] }));
  }

  function applyOpeningHours() {
    if (!hours) return;
    const next = { ...rows };
    for (const h of hours) next[h.weekday] = h.closed ? [] : [{ start: h.open, end: h.close }];
    setRows(next);
  }

  async function save() {
    const body: ScheduleInterval[] = [];
    for (const w of WEEK_ORDER) {
      const sorted = [...rows[w]].sort((a, b) => a.start.localeCompare(b.start));
      for (let i = 0; i < sorted.length; i++) {
        const it = sorted[i];
        if (!it.start || !it.end || it.end <= it.start) return setError(toApiError(new Error(`${WEEKDAYS[w]}: each finish time must be after its start.`)));
        if (i > 0 && it.start < sorted[i - 1].end) return setError(toApiError(new Error(`${WEEKDAYS[w]}: the times overlap.`)));
        body.push({ weekday: w, start: it.start, end: it.end });
      }
    }
    setBusy(true);
    setError(null);
    try {
      onSaved(await api<StaffDto>(`/api/staff/${staff.id}/schedule`, { method: "PUT", body }));
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="space-y-4">
      <SectionTitle>Working hours</SectionTitle>
      <p className="text-sm text-muted">Gaps between times are breaks. A day with no times is a day off.</p>
      {canEdit && (
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" className="min-h-9 px-3 py-1.5" onClick={copyMonday}>
            Copy Monday to weekdays
          </Button>
          {hours && (
            <Button variant="ghost" className="min-h-9 px-3 py-1.5" onClick={applyOpeningHours}>
              Use opening hours
            </Button>
          )}
        </div>
      )}
      <ul className="divide-y divide-line-soft">
        {WEEK_ORDER.map((w) => (
          <li key={w} className="grid grid-cols-[6.5rem_minmax(0,1fr)] gap-3 py-3">
            <span className="pt-2 text-sm font-semibold">{WEEKDAYS[w]}</span>
            <div className="space-y-2">
              {rows[w].length === 0 && <p className="pt-2 text-sm text-muted">Day off</p>}
              {rows[w].map((it, i) => (
                <div key={i} className="flex items-center gap-2">
                  <label className="sr-only" htmlFor={`s-${w}-${i}`}>
                    {WEEKDAYS[w]} start {i + 1}
                  </label>
                  <input
                    id={`s-${w}-${i}`}
                    type="time"
                    step={300}
                    disabled={!canEdit}
                    className={cx(inputClass, "w-[7.5rem] py-2 tnum")}
                    value={it.start}
                    onChange={(e) => update(w, (l) => l.map((x, j) => (j === i ? { ...x, start: e.target.value } : x)))}
                  />
                  <span className="text-sm text-muted">to</span>
                  <label className="sr-only" htmlFor={`e-${w}-${i}`}>
                    {WEEKDAYS[w]} finish {i + 1}
                  </label>
                  <input
                    id={`e-${w}-${i}`}
                    type="time"
                    step={300}
                    disabled={!canEdit}
                    className={cx(inputClass, "w-[7.5rem] py-2 tnum")}
                    value={it.end}
                    onChange={(e) => update(w, (l) => l.map((x, j) => (j === i ? { ...x, end: e.target.value } : x)))}
                  />
                  {canEdit && (
                    <button
                      type="button"
                      onClick={() => update(w, (l) => l.filter((_, j) => j !== i))}
                      className="flex h-9 w-9 shrink-0 items-center justify-center rounded-control text-muted hover:bg-brick-wash hover:text-brick"
                      aria-label={`Remove ${WEEKDAYS[w]} ${it.start} to ${it.end}`}
                    >
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  )}
                </div>
              ))}
              {canEdit && (
                <button type="button" onClick={() => addInterval(w)} className="flex items-center gap-1 text-sm font-semibold text-rose underline-offset-2 hover:underline">
                  <PlusIcon className="h-3.5 w-3.5" /> {rows[w].length === 0 ? "Add hours" : "Add a break"}
                </button>
              )}
            </div>
          </li>
        ))}
      </ul>
      <ErrorBox error={error} />
      {canEdit && (
        <Button loading={busy} onClick={save}>
          Save working hours
        </Button>
      )}
    </Card>
  );
}

// ---------- Time off ----------

function TimeOffCard({ staffId, canEdit }: { staffId: string; canEdit: boolean }) {
  const { tz } = useBusiness();
  const { data, error, reload } = useApi<TimeOffDto[]>(`/api/staff/${staffId}/time-off`);
  const list = asList(data).sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const today = todayIn(tz);
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [allDay, setAllDay] = useState(true);
  const [startTime, setStartTime] = useState("12:00");
  const [endTime, setEndTime] = useState("14:00");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [formError, setFormError] = useState<ApiError | null>(null);

  async function add(e: FormEvent) {
    e.preventDefault();
    const startsAt = zonedInstant(from, allDay ? "00:00" : startTime, tz);
    const endsAt = allDay ? zonedInstant(addDays(to, 1), "00:00", tz) : zonedInstant(to, endTime, tz);
    if (new Date(endsAt) <= new Date(startsAt)) return setFormError(toApiError(new Error("The end must be after the start.")));
    setBusy("add");
    setFormError(null);
    try {
      await api<TimeOffDto>(`/api/staff/${staffId}/time-off`, { method: "POST", body: { startsAt, endsAt, reason: reason.trim() || null } });
      setReason("");
      reload();
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  async function remove(t: TimeOffDto) {
    if (!window.confirm("Delete this time off? Those hours become bookable again.")) return;
    setBusy(t.id);
    try {
      await api(`/api/staff/${staffId}/time-off/${t.id}`, { method: "DELETE" });
      reload();
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card className="space-y-4">
      <SectionTitle>Time off</SectionTitle>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {data && list.length === 0 && <p className="text-sm text-muted">No time off booked. Holidays and appointments added here block those hours.</p>}
      {list.length > 0 && (
        <ul className="divide-y divide-line-soft">
          {list.map((t) => (
            <li key={t.id} className="flex items-center justify-between gap-3 py-2.5">
              <span className="min-w-0 text-sm">
                <span className="block font-medium tnum">
                  {formatStamp(t.startsAt, tz)} to {formatStamp(t.endsAt, tz)}
                </span>
                {t.reason && <span className="block text-muted">{t.reason}</span>}
              </span>
              {canEdit && (
                <button
                  type="button"
                  disabled={busy === t.id}
                  onClick={() => remove(t)}
                  className="flex h-9 w-9 shrink-0 items-center justify-center rounded-control text-muted hover:bg-brick-wash hover:text-brick disabled:opacity-50"
                  aria-label="Delete this time off"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {canEdit && (
        <form onSubmit={add} className="space-y-3 border-t border-line-soft pt-4">
          <p className="text-sm font-semibold">Add time off</p>
          <Checkbox checked={allDay} onChange={setAllDay} label="Whole days" />
          <div className="grid grid-cols-2 gap-3">
            <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} required />
            {!allDay && <Input label="Start time" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} required />}
            <Input label={allDay ? "To (inclusive)" : "Until"} type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} required />
            {!allDay && <Input label="End time" type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} required />}
          </div>
          <Input label="Reason (optional)" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Annual leave" hint="Only your team sees this." />
          <ErrorBox error={formError} />
          <Button type="submit" variant="secondary" loading={busy === "add"}>
            Add time off
          </Button>
        </form>
      )}
    </Card>
  );
}

// ---------- Calendar feed ----------

function FeedCard({ staff }: { staff: StaffDto }) {
  if (!staff.calendarFeedUrl) return null;
  return (
    <Card className="space-y-3">
      <SectionTitle>Their bookings in Google or Apple Calendar</SectionTitle>
      <p className="text-sm text-slate-700">
        Subscribe to this private link and {staff.displayName}&apos;s bookings appear in their phone calendar. Anyone with the link can see them, so share it only with {staff.displayName}.
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <code className="min-w-0 flex-1 truncate rounded-control bg-mist px-3 py-2 text-xs">{staff.calendarFeedUrl}</code>
        <CopyButton text={staff.calendarFeedUrl} label="Copy link" />
      </div>
    </Card>
  );
}

function DeleteCard({ staff }: { staff: StaffDto }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function remove() {
    if (!window.confirm(`Remove ${staff.displayName}? Their past bookings stay in your records.`)) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/api/staff/${staff.id}`, { method: "DELETE" });
      router.replace("/app/staff");
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <Card className="space-y-3">
      <SectionTitle>Remove staff member</SectionTitle>
      <p className="text-sm text-muted">To stop online bookings but keep them on the calendar, turn off &ldquo;Bookable online&rdquo; instead.</p>
      <ErrorBox error={error} />
      <Button variant="danger" loading={busy} onClick={remove}>
        Remove {staff.displayName}
      </Button>
    </Card>
  );
}
