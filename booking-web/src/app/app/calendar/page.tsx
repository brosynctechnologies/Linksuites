"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState, type CSSProperties } from "react";
import { staffColor, withAlpha } from "@/lib/color";
import { useCan } from "@/lib/permissions";
import { isLive, STATUS_LABEL } from "@/lib/status";
import {
  addDays,
  cityOf,
  clockToMinutes,
  dateKey,
  formatPlainDate,
  formatTime,
  minutesOfDay,
  minutesToClock,
  startOfWeek,
  todayIn,
  WEEKDAYS_SHORT,
  weekdayOf,
} from "@/lib/time";
import type { BookingDto, BookingSummaryDto, CalendarDto, OpeningHours, StaffDto, TimeOffDto } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useBusiness, useStaff } from "@/lib/use-business";
import { useNow } from "@/lib/use-now";
import { BookingDrawer } from "@/components/booking-drawer";
import { useNewBooking } from "@/components/new-booking";
import { ApiErrorView, BookingStatusBadge, Button, PageLoader, Segmented, cx, inputClass } from "@/components/ui";
import { ChevronLeftIcon, ChevronRightIcon, PlusIcon } from "@/components/icons";

type View = "day" | "week";
type Range = { start: number; end: number };

const isDate = (s: string | null): s is string => !!s && /^\d{4}-\d{2}-\d{2}$/.test(s);

export default function CalendarPage() {
  const sp = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const { business, tz: bizTz, currency } = useBusiness();
  const { staff: staffList } = useStaff();
  const { open, version } = useNewBooking();
  const canCreate = useCan("bookings.edit");
  const [drawerId, setDrawerId] = useState<string | null>(null);

  const view: View = sp.get("view") === "week" ? "week" : "day";
  const today = todayIn(bizTz);
  const rawDate = sp.get("date");
  const date = isDate(rawDate) ? rawDate : today;
  const staffFilter = sp.get("staff") ?? "";

  function setParams(patch: Record<string, string | null>) {
    const next = new URLSearchParams(sp.toString());
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    const s = next.toString();
    router.replace(s ? `${pathname}?${s}` : pathname, { scroll: false });
  }

  const from = view === "week" ? startOfWeek(date) : date;
  const to = view === "week" ? addDays(from, 6) : date;
  const cal = useApi<CalendarDto>(`/api/calendar${qs({ from, to })}`, { nonce: version });
  const data = cal.data;
  const tz = data?.timezone || bizTz;
  const interval = Math.min(60, Math.max(5, business?.slotIntervalMinutes || 15));

  const step = view === "week" ? 7 : 1;
  const title =
    view === "week" ? `${formatPlainDate(from)} to ${formatPlainDate(to)}` : date === today ? `Today, ${formatPlainDate(date)}` : formatPlainDate(date, "long");

  function replaceBooking(b: BookingDto) {
    cal.setData((d) => (d ? { ...d, bookings: d.bookings.map((x) => (x.id === b.id ? { ...x, ...b } : x)) } : d!));
  }

  const calStaff = data?.staff ?? [];
  const staffById = new Map(staffList.map((s) => [s.id, s]));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-0 items-center gap-1">
          <button
            type="button"
            onClick={() => setParams({ date: addDays(date, -step) })}
            className="flex h-10 w-10 items-center justify-center rounded-control text-slate-700 hover:bg-aubergine/[0.06]"
            aria-label={view === "week" ? "Previous week" : "Previous day"}
          >
            <ChevronLeftIcon />
          </button>
          <button
            type="button"
            onClick={() => setParams({ date: addDays(date, step) })}
            className="flex h-10 w-10 items-center justify-center rounded-control text-slate-700 hover:bg-aubergine/[0.06]"
            aria-label={view === "week" ? "Next week" : "Next day"}
          >
            <ChevronRightIcon />
          </button>
          <h1 className="ml-1 truncate font-expanded text-xl font-extrabold sm:text-2xl" aria-live="polite">
            {title}
          </h1>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {date !== today && (
            <Button variant="secondary" onClick={() => setParams({ date: null })}>
              Today
            </Button>
          )}
          <label className="sr-only" htmlFor="cal-date">
            Go to date
          </label>
          <input
            id="cal-date"
            type="date"
            className={cx(inputClass, "w-auto py-2")}
            value={date}
            onChange={(e) => isDate(e.target.value) && setParams({ date: e.target.value })}
          />
          <Segmented<View>
            label="Calendar view"
            className="hidden w-40 md:grid"
            items={[
              { value: "day", label: "Day" },
              { value: "week", label: "Week" },
            ]}
            value={view}
            onChange={(v) => setParams({ view: v === "week" ? "week" : null })}
          />
          {canCreate && (
            <Button className="hidden md:inline-flex" onClick={() => open({ date })}>
              <PlusIcon className="h-4 w-4" /> New booking
            </Button>
          )}
        </div>
      </div>

      {/* Staff filter: chips on phones, and on the week view */}
      {calStaff.length > 1 && (
        <div className={cx("no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:px-0", view === "day" && "md:hidden")} role="group" aria-label="Show staff">
          <Chip on={!staffFilter} onClick={() => setParams({ staff: null })}>
            Everyone
          </Chip>
          {calStaff.map((s) => (
            <Chip key={s.id} on={staffFilter === s.id} onClick={() => setParams({ staff: s.id })} color={staffColor(s.color, s.id)}>
              {s.name}
            </Chip>
          ))}
        </div>
      )}

      {cal.error && <ApiErrorView error={cal.error} onRetry={cal.reload} />}
      {!data && !cal.error && <PageLoader />}
      {data && (
        <>
          <div className="hidden md:block">
            {view === "day" ? (
              <DayGrid
                cal={data}
                date={date}
                tz={tz}
                interval={interval}
                staffById={staffById}
                today={today}
                busy={cal.loading}
                onOpen={setDrawerId}
                onEmpty={canCreate ? (staffId, time) => open({ date, time, staffId }) : undefined}
              />
            ) : (
              <WeekGrid
                cal={data}
                from={from}
                tz={tz}
                interval={interval}
                staffFilter={staffFilter}
                staff={staffFilter ? staffById.get(staffFilter) : undefined}
                today={today}
                busy={cal.loading}
                onOpen={setDrawerId}
                onDay={(d) => setParams({ view: null, date: d })}
                onEmpty={canCreate ? (d, time) => open({ date: d, time, staffId: staffFilter || undefined }) : undefined}
              />
            )}
          </div>
          <div className="md:hidden">
            <DayAgenda cal={data} date={date} tz={tz} staffFilter={staffFilter} onOpen={setDrawerId} />
          </div>
          <p className="text-xs text-muted">Times are in {cityOf(tz)} time.</p>
        </>
      )}

      <BookingDrawer id={drawerId} tz={tz} currency={currency} onClose={() => setDrawerId(null)} onChanged={replaceBooking} />
    </div>
  );
}

function Chip({ on, onClick, color, children }: { on: boolean; onClick: () => void; color?: string; children: React.ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={on}
      onClick={onClick}
      className={cx(
        "flex min-h-9 shrink-0 items-center gap-1.5 rounded-full border px-3 text-sm font-medium whitespace-nowrap",
        on ? "border-aubergine bg-aubergine text-white" : "border-line bg-surface text-slate-700 hover:bg-mist-2",
      )}
    >
      {color && <span className="h-2.5 w-2.5 rounded-full" style={{ background: color }} aria-hidden="true" />}
      {children}
    </button>
  );
}

// ---------- Geometry ----------

function hoursFor(hours: OpeningHours[], date: string): Range[] {
  const h = hours.find((x) => x.weekday === weekdayOf(date));
  if (!h || h.closed) return [];
  return [{ start: clockToMinutes(h.open), end: clockToMinutes(h.close) || 1440 }];
}

function scheduleFor(staff: StaffDto | undefined, date: string): Range[] | null {
  if (!staff) return null;
  const wd = weekdayOf(date);
  return staff.schedule.filter((i) => i.weekday === wd).map((i) => ({ start: clockToMinutes(i.start), end: clockToMinutes(i.end) || 1440 }));
}

/** Start/end minutes of an instant range clipped to one date. Null when it doesn't touch the date. */
function clipToDate(startsAt: string, endsAt: string, date: string, tz: string): Range | null {
  const sd = dateKey(startsAt, tz);
  const ed = dateKey(endsAt, tz);
  if (sd > date || ed < date) return null;
  const start = sd === date ? minutesOfDay(startsAt, tz) : 0;
  const end = ed === date ? minutesOfDay(endsAt, tz) : 1440;
  return end > start ? { start, end } : null;
}

/** Visible window for the grid: opening hours plus anything booked outside them, whole hours. */
function windowFor(cal: CalendarDto, dates: string[], tz: string): Range {
  let start = Infinity;
  let end = -Infinity;
  for (const d of dates) {
    for (const r of hoursFor(cal.hours, d)) {
      start = Math.min(start, r.start);
      end = Math.max(end, r.end);
    }
    for (const b of cal.bookings) {
      if (!isLive(b.status) && b.status !== "Completed" && b.status !== "NoShow") continue;
      const r = clipToDate(b.startsAt, b.endsAt, d, tz);
      if (r) {
        start = Math.min(start, r.start);
        end = Math.max(end, r.end);
      }
    }
  }
  if (!Number.isFinite(start)) {
    start = 8 * 60;
    end = 20 * 60;
  }
  return { start: Math.floor(start / 60) * 60, end: Math.min(1440, Math.ceil(end / 60) * 60) };
}

interface Placed<T> {
  item: T;
  range: Range;
  lane: number;
}

/** Side-by-side lanes for overlapping items. */
function placeInLanes<T>(items: { item: T; range: Range }[]): { placed: Placed<T>[]; lanes: number } {
  const sorted = [...items].sort((a, b) => a.range.start - b.range.start || b.range.end - a.range.end);
  const laneEnds: number[] = [];
  const placed = sorted.map(({ item, range }) => {
    let lane = laneEnds.findIndex((e) => e <= range.start);
    if (lane === -1) {
      lane = laneEnds.length;
      laneEnds.push(range.end);
    } else laneEnds[lane] = range.end;
    return { item, range, lane };
  });
  return { placed, lanes: Math.max(1, laneEnds.length) };
}

/** Bookings that occupy the diary (cancelled and expired ones free the time). */
function shown(b: BookingSummaryDto) {
  return b.status !== "Cancelled" && b.status !== "Expired";
}

function rowHeight(interval: number) {
  return Math.max(8, Math.round((26 * interval) / 15));
}

// ---------- Grid pieces ----------

function TimeGutter({ win, interval }: { win: Range; interval: number }) {
  const rows = Math.ceil((win.end - win.start) / interval);
  const labels = [];
  for (let m = win.start; m < win.end; m += 60) {
    labels.push(
      <span
        key={m}
        className="-mt-2 pr-2 text-right text-xs text-muted tnum"
        style={{ gridRow: `${Math.floor((m - win.start) / interval) + 1} / span ${Math.max(1, Math.floor(60 / interval))}` }}
      >
        {minutesToClock(m)}
      </span>,
    );
  }
  return (
    <div className="grid" style={{ gridTemplateRows: `repeat(${rows}, var(--row))` }} aria-hidden="true">
      {labels}
    </div>
  );
}

function Column({
  date,
  win,
  interval,
  tz,
  bookings,
  timeOff,
  working,
  nowMin,
  onOpen,
  onEmpty,
  emptyLabel,
}: {
  date: string;
  win: Range;
  interval: number;
  tz: string;
  bookings: BookingSummaryDto[];
  timeOff: TimeOffDto[];
  working: Range[];
  nowMin: number | null;
  onOpen: (id: string) => void;
  onEmpty?: (time: string) => void;
  emptyLabel: string;
}) {
  const rows = Math.ceil((win.end - win.start) / interval);
  const rowOf = (m: number) => Math.max(0, Math.min(rows, Math.floor((m - win.start) / interval)));
  const span = (r: Range) => Math.max(1, Math.ceil((Math.min(r.end, win.end) - win.start) / interval) - rowOf(r.start));

  const items = bookings
    .filter(shown)
    .map((b) => ({ item: b, range: clipToDate(b.startsAt, b.endsAt, date, tz) }))
    .filter((x): x is { item: BookingSummaryDto; range: Range } => !!x.range && x.range.end > win.start && x.range.start < win.end);
  const { placed, lanes } = placeInLanes(items);
  const offs = timeOff
    .map((t) => ({ t, range: clipToDate(t.startsAt, t.endsAt, date, tz) }))
    .filter((x): x is { t: TimeOffDto; range: Range } => !!x.range && x.range.end > win.start && x.range.start < win.end);

  const isWorking = (m: number) => working.some((r) => m >= r.start && m < r.end);

  return (
    <div
      className="relative grid border-l border-line"
      style={{ gridTemplateRows: `repeat(${rows}, var(--row))`, gridTemplateColumns: `repeat(${lanes}, minmax(0, 1fr))` }}
    >
      {Array.from({ length: rows }, (_, i) => {
        const m = win.start + i * interval;
        const onHour = m % 60 === 0;
        const off = !isWorking(m);
        const label = `${emptyLabel} at ${minutesToClock(m)}`;
        return onEmpty ? (
          <button
            key={i}
            type="button"
            onClick={() => onEmpty(minutesToClock(m))}
            aria-label={`New booking: ${label}`}
            className={cx(
              "border-t text-left transition-colors hover:bg-rose-wash focus-visible:z-20",
              onHour ? "border-line" : "border-line-soft",
              off && "bg-mist-2/70",
            )}
            style={{ gridRow: i + 1, gridColumn: "1 / -1" }}
          />
        ) : (
          <div
            key={i}
            className={cx("border-t", onHour ? "border-line" : "border-line-soft", off && "bg-mist-2/70")}
            style={{ gridRow: i + 1, gridColumn: "1 / -1" }}
          />
        );
      })}

      {offs.map(({ t, range }) => (
        <div
          key={t.id}
          className="pointer-events-none z-[5] m-0.5 overflow-hidden rounded-[6px] border border-dashed border-slate-300 px-2 py-1 text-xs text-muted"
          style={{
            gridRow: `${rowOf(range.start) + 1} / span ${span(range)}`,
            gridColumn: "1 / -1",
            backgroundImage: "repeating-linear-gradient(135deg, rgb(43 24 56 / 0.06) 0 6px, transparent 6px 12px)",
          }}
        >
          Time off{t.reason ? `: ${t.reason}` : ""}
        </div>
      ))}

      {placed.map(({ item: b, range, lane }) => {
        const color = staffColor(b.staffColor, b.staffId);
        const pending = b.status === "Pending";
        const done = b.status === "Completed" || b.status === "NoShow";
        const style: CSSProperties = {
          gridRow: `${rowOf(range.start) + 1} / span ${span(range)}`,
          gridColumn: lane + 1,
          background: done ? "#F5F3F7" : withAlpha(color, pending ? 0.08 : 0.16),
          borderLeftColor: color,
        };
        return (
          <button
            key={b.id}
            type="button"
            onClick={() => onOpen(b.id)}
            className={cx(
              "z-10 m-0.5 min-h-0 overflow-hidden rounded-[6px] border-l-[3px] px-2 py-1 text-left text-xs leading-tight text-aubergine hover:brightness-95",
              pending && "border border-l-[3px] border-dashed border-amber",
              done && "text-muted",
              b.status === "NoShow" && "line-through",
            )}
            style={style}
            aria-label={`${formatTime(b.startsAt, tz)} ${b.customerName}, ${b.serviceName}, with ${b.staffName}. ${STATUS_LABEL[b.status]}`}
          >
            <span className="block font-semibold tnum">{formatTime(b.startsAt, tz)}</span>
            <span className="block truncate font-semibold">{b.customerName}</span>
            <span className="block truncate">{b.serviceName}</span>
            {pending && <span className="block truncate text-amber-text">Pending deposit</span>}
          </button>
        );
      })}

      {nowMin !== null && nowMin >= win.start && nowMin < win.end && (
        <div
          className="pointer-events-none absolute inset-x-0 z-20 h-0.5 bg-rose"
          style={{ top: `calc(${(nowMin - win.start) / interval} * var(--row))` }}
          aria-hidden="true"
        >
          <span className="absolute -top-1 -left-1 h-2.5 w-2.5 rounded-full bg-rose" />
        </div>
      )}
    </div>
  );
}

function DayGrid({
  cal,
  date,
  tz,
  interval,
  staffById,
  today,
  busy,
  onOpen,
  onEmpty,
}: {
  cal: CalendarDto;
  date: string;
  tz: string;
  interval: number;
  staffById: Map<string, StaffDto>;
  today: string;
  busy: boolean;
  onOpen: (id: string) => void;
  onEmpty?: (staffId: string, time: string) => void;
}) {
  const now = useNow();
  const win = useMemo(() => windowFor(cal, [date], tz), [cal, date, tz]);
  const nowMin = date === today && now ? minutesOfDay(new Date(now), tz) : null;
  const business = hoursFor(cal.hours, date);
  const cols = cal.staff;
  const hidden = cal.bookings.filter((b) => !shown(b)).length;

  if (cols.length === 0) {
    return <p className="rounded-panel border border-line bg-surface p-5 text-sm text-muted">Add a staff member to see their day here.</p>;
  }

  return (
    <div className={cx("overflow-hidden rounded-panel border border-line bg-surface", busy && "opacity-60")}>
      <div className="overflow-x-auto">
        <div style={{ "--row": `${rowHeight(interval)}px`, minWidth: `${4 + cols.length * 10}rem` } as CSSProperties}>
          <div className="sticky top-0 z-30 grid border-b border-line bg-aubergine text-white" style={{ gridTemplateColumns: `4rem repeat(${cols.length}, minmax(10rem, 1fr))` }}>
            <span />
            {cols.map((s) => {
              const count = cal.bookings.filter((b) => b.staffId === s.id && shown(b)).length;
              return (
                <div key={s.id} className="flex items-center gap-2 border-l border-white/10 px-3 py-2.5">
                  <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: staffColor(s.color, s.id) }} aria-hidden="true" />
                  <span className="truncate text-sm font-semibold">{s.name}</span>
                  <span className="ml-auto text-xs text-on-aubergine tnum">{count}</span>
                </div>
              );
            })}
          </div>
          <div className="grid py-2" style={{ gridTemplateColumns: `4rem repeat(${cols.length}, minmax(10rem, 1fr))` }}>
            <TimeGutter win={win} interval={interval} />
            {cols.map((s) => (
              <Column
                key={s.id}
                date={date}
                win={win}
                interval={interval}
                tz={tz}
                bookings={cal.bookings.filter((b) => b.staffId === s.id)}
                timeOff={cal.timeOff.filter((t) => t.staffId === s.id)}
                working={scheduleFor(staffById.get(s.id), date) ?? business}
                nowMin={nowMin}
                onOpen={onOpen}
                onEmpty={onEmpty ? (t) => onEmpty(s.id, t) : undefined}
                emptyLabel={`${s.name}, ${formatPlainDate(date)}`}
              />
            ))}
          </div>
        </div>
      </div>
      {hidden > 0 && (
        <p className="border-t border-line-soft px-4 py-2 text-xs text-muted">
          {hidden} cancelled or expired booking{hidden === 1 ? " is" : "s are"} hidden from the grid.
        </p>
      )}
    </div>
  );
}

function WeekGrid({
  cal,
  from,
  tz,
  interval,
  staffFilter,
  staff,
  today,
  busy,
  onOpen,
  onDay,
  onEmpty,
}: {
  cal: CalendarDto;
  from: string;
  tz: string;
  interval: number;
  staffFilter: string;
  staff?: StaffDto;
  today: string;
  busy: boolean;
  onOpen: (id: string) => void;
  onDay: (d: string) => void;
  onEmpty?: (date: string, time: string) => void;
}) {
  const now = useNow();
  const dates = useMemo(() => Array.from({ length: 7 }, (_, i) => addDays(from, i)), [from]);
  const win = useMemo(() => windowFor(cal, dates, tz), [cal, dates, tz]);
  const bookings = staffFilter ? cal.bookings.filter((b) => b.staffId === staffFilter) : cal.bookings;
  const timeOff = staffFilter ? cal.timeOff.filter((t) => t.staffId === staffFilter) : [];

  return (
    <div className={cx("overflow-hidden rounded-panel border border-line bg-surface", busy && "opacity-60")}>
      <div className="overflow-x-auto">
        <div style={{ "--row": `${rowHeight(interval)}px`, minWidth: "60rem" } as CSSProperties}>
          <div className="sticky top-0 z-30 grid border-b border-line bg-aubergine text-white" style={{ gridTemplateColumns: "4rem repeat(7, minmax(8rem, 1fr))" }}>
            <span />
            {dates.map((d) => (
              <button
                key={d}
                type="button"
                onClick={() => onDay(d)}
                className={cx("border-l border-white/10 px-3 py-2 text-left hover:bg-aubergine-2", d === today && "bg-aubergine-3")}
                aria-label={`Open ${formatPlainDate(d, "long")}`}
              >
                <span className="block text-xs text-on-aubergine">{WEEKDAYS_SHORT[weekdayOf(d)]}</span>
                <span className="font-expanded text-lg font-bold tnum">{Number(d.slice(8))}</span>
                {d === today && <span className="ml-2 text-xs text-rose-light">Today</span>}
              </button>
            ))}
          </div>
          <div className="grid py-2" style={{ gridTemplateColumns: "4rem repeat(7, minmax(8rem, 1fr))" }}>
            <TimeGutter win={win} interval={interval} />
            {dates.map((d) => (
              <Column
                key={d}
                date={d}
                win={win}
                interval={interval}
                tz={tz}
                bookings={bookings}
                timeOff={timeOff}
                working={(staffFilter ? scheduleFor(staff, d) : null) ?? hoursFor(cal.hours, d)}
                nowMin={d === today && now ? minutesOfDay(new Date(now), tz) : null}
                onOpen={onOpen}
                onEmpty={onEmpty ? (t) => onEmpty(d, t) : undefined}
                emptyLabel={formatPlainDate(d)}
              />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function DayAgenda({
  cal,
  date,
  tz,
  staffFilter,
  onOpen,
}: {
  cal: CalendarDto;
  date: string;
  tz: string;
  staffFilter: string;
  onOpen: (id: string) => void;
}) {
  const list = cal.bookings
    .filter((b) => dateKey(b.startsAt, tz) === date && (!staffFilter || b.staffId === staffFilter))
    .sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const offs = cal.timeOff.filter((t) => (!staffFilter || t.staffId === staffFilter) && clipToDate(t.startsAt, t.endsAt, date, tz));
  const names = new Map(cal.staff.map((s) => [s.id, s.name]));

  if (list.length === 0 && offs.length === 0) {
    return <p className="rounded-panel border border-dashed border-slate-300 px-4 py-10 text-center text-sm text-muted">Nothing booked on {formatPlainDate(date)}.</p>;
  }

  return (
    <ol className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
      {offs.map((t) => (
        <li key={t.id} className="px-4 py-3 text-sm text-muted">
          {names.get(t.staffId) ?? "Staff"} is off {formatTime(t.startsAt, tz)}–{formatTime(t.endsAt, tz)}
          {t.reason ? `: ${t.reason}` : ""}
        </li>
      ))}
      {list.map((b) => (
        <li key={b.id}>
          <button
            type="button"
            onClick={() => onOpen(b.id)}
            className={cx("grid w-full grid-cols-[4.5rem_minmax(0,1fr)] gap-x-3 px-4 py-3.5 text-left hover:bg-mist", !isLive(b.status) && "text-muted")}
          >
            <span>
              <span className="block font-expanded text-2xl leading-none font-extrabold tnum">{formatTime(b.startsAt, tz)}</span>
              <span className="mt-1 block text-xs text-muted tnum">to {formatTime(b.endsAt, tz)}</span>
            </span>
            <span className="min-w-0 border-l-[3px] pl-3" style={{ borderColor: staffColor(b.staffColor, b.staffId) }}>
              <span className="block truncate font-semibold">{b.customerName}</span>
              <span className="block truncate text-sm">{b.serviceName}</span>
              <span className="mt-1 flex flex-wrap items-center gap-2 text-sm">
                {b.staffName}
                <BookingStatusBadge status={b.status} />
              </span>
            </span>
          </button>
        </li>
      ))}
    </ol>
  );
}
