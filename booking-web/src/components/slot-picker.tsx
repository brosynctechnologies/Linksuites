"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { qs, useApi } from "@/lib/use-api";
import {
  addDays,
  addMonths,
  cityOf,
  daysInMonth,
  formatMonth,
  formatPlainDate,
  formatTime,
  groupByDayPart,
  monthOf,
  WEEK_ORDER,
  WEEKDAYS_SHORT,
  weekdayOf,
} from "@/lib/time";
import type { AvailabilityDto, DayAvailability, SlotDto } from "@/lib/types";
import { ChevronLeftIcon, ChevronRightIcon } from "./icons";
import { cx } from "./styles";
import { ErrorBox, Skeleton } from "./ui";

/** Where availability comes from: the public booking page, or the signed-in business (/api/availability). */
export type SlotSource =
  | { kind: "public"; slug: string; serviceId: string; staffId: string | null }
  | { kind: "app"; serviceId: string; staffId: string | null };

const pad = (n: number) => String(n).padStart(2, "0");

export interface DateSlotPickerProps {
  source: SlotSource;
  timezone: string;
  /** Today in the business zone (YYYY-MM-DD). */
  today: string;
  /** Last bookable date (inclusive). */
  maxDate: string;
  /** Date to open on (defaults to the first available day). */
  initialDate?: string;
  value: SlotDto | null;
  onChange: (slot: SlotDto | null) => void;
  /** Bump to refetch slots (e.g. after a 409 "slot taken"). */
  refreshKey?: number;
  /** Say "Times are in Lagos time" under the slots. */
  showZoneNote?: boolean;
  /** Exclude the slot the booking already has (rescheduling). */
  excludeStartsAt?: string;
}

/**
 * Month calendar (with availability dots from /days on the public page) and, on phones, a week strip,
 * then slot chips grouped Morning / Afternoon / Evening. Selected states use the page's --brand colour.
 */
export function DateSlotPicker({
  source,
  timezone,
  today,
  maxDate,
  initialDate,
  value,
  onChange,
  refreshKey = 0,
  showZoneNote,
  excludeStartsAt,
}: DateSlotPickerProps) {
  const [month, setMonth] = useState(monthOf(initialDate ?? today));
  const [picked, setPicked] = useState<string | null>(initialDate ?? null);

  const daysPath =
    source.kind === "public"
      ? `/api/public/book/${encodeURIComponent(source.slug)}/days${qs({ serviceId: source.serviceId, staffId: source.staffId, month })}`
      : null;
  const days = useApi<DayAvailability[]>(daysPath, { auth: false, nonce: refreshKey });
  const dayMap = useMemo(() => {
    const m = new Map<string, boolean>();
    if (days.data && !days.loading) for (const d of days.data) m.set(d.date, d.available);
    return m;
  }, [days.data, days.loading]);

  const monthStart = `${month}-01`;
  const monthEnd = `${month}-${pad(daysInMonth(month))}`;
  const inRange = (d: string) => d >= today && d <= maxDate;
  const isAvailable = (d: string) => inRange(d) && (source.kind === "app" || dayMap.get(d) === true);

  // Fewest taps: until the person picks a day, open on the first available day of the month.
  const firstAvailable = useMemo(() => {
    if (source.kind === "public" && (days.loading || !days.data)) return null;
    for (let d = monthStart < today ? today : monthStart; d <= monthEnd && d <= maxDate; d = addDays(d, 1)) {
      if (source.kind === "app" || dayMap.get(d) === true) return d;
    }
    return null;
  }, [source.kind, days.loading, days.data, monthStart, monthEnd, today, maxDate, dayMap]);
  const date = picked && monthOf(picked) === month ? picked : firstAvailable;

  const slotsPath = date
    ? source.kind === "public"
      ? `/api/public/book/${encodeURIComponent(source.slug)}/availability${qs({ serviceId: source.serviceId, staffId: source.staffId, date })}`
      : `/api/availability${qs({ serviceId: source.serviceId, staffId: source.staffId, date })}`
    : null;
  const slots = useApi<AvailabilityDto>(slotsPath, { auth: source.kind === "app", nonce: refreshKey });
  const tz = slots.data?.timezone || timezone;
  const list = useMemo(
    () => (slots.data && !slots.loading ? slots.data.slots.filter((s) => s.startsAt !== excludeStartsAt) : []),
    [slots.data, slots.loading, excludeStartsAt],
  );
  const groups = useMemo(() => groupByDayPart(list, tz), [list, tz]);

  const canPrev = month > monthOf(today);
  const canNext = addMonths(month, 1) <= monthOf(maxDate);

  function pickDay(d: string) {
    setPicked(d);
    if (value) onChange(null);
  }
  function goMonth(n: number) {
    setMonth((m) => addMonths(m, n));
    setPicked(null);
    if (value) onChange(null);
  }

  // Next available day after an empty one (public page only, from the month's /days list).
  const nextAvailable = useMemo(() => {
    if (!date || source.kind !== "public") return null;
    for (let d = addDays(date, 1); d <= monthEnd && d <= maxDate; d = addDays(d, 1)) if (dayMap.get(d)) return d;
    return null;
  }, [date, source.kind, monthEnd, maxDate, dayMap]);

  return (
    <div className="space-y-5">
      <div>
        <div className="mb-2 flex items-center justify-between gap-2">
          <p className="font-semibold" aria-live="polite">
            {formatMonth(month)}
          </p>
          <div className="flex gap-1">
            <button
              type="button"
              onClick={() => goMonth(-1)}
              disabled={!canPrev}
              className="flex h-10 w-10 items-center justify-center rounded-control text-slate-700 hover:bg-aubergine/[0.06] disabled:opacity-30"
              aria-label="Previous month"
            >
              <ChevronLeftIcon />
            </button>
            <button
              type="button"
              onClick={() => goMonth(1)}
              disabled={!canNext}
              className="flex h-10 w-10 items-center justify-center rounded-control text-slate-700 hover:bg-aubergine/[0.06] disabled:opacity-30"
              aria-label="Next month"
            >
              <ChevronRightIcon />
            </button>
          </div>
        </div>

        <WeekStrip
          month={month}
          today={today}
          maxDate={maxDate}
          selected={date}
          isAvailable={isAvailable}
          showDots={source.kind === "public"}
          onPick={pickDay}
        />
        <MonthGrid
          month={month}
          today={today}
          selected={date}
          inRange={inRange}
          isAvailable={isAvailable}
          showDots={source.kind === "public"}
          loading={source.kind === "public" && days.loading}
          onPick={pickDay}
        />
        {days.error && (
          <div className="mt-3">
            <ErrorBox error={days.error} onRetry={days.reload} />
          </div>
        )}
      </div>

      <div aria-live="polite">
        {!date && source.kind === "public" && !days.loading && days.data && (
          <p className="rounded-control bg-mist-2 px-4 py-3 text-sm text-slate-700">
            No free days left in {formatMonth(month)}.{" "}
            {canNext ? (
              <button type="button" className="font-semibold text-brand underline underline-offset-2" onClick={() => goMonth(1)}>
                Look at {formatMonth(addMonths(month, 1))}
              </button>
            ) : (
              "Bookings open further ahead soon."
            )}
          </p>
        )}
        {date && (
          <>
            <p className="mb-3 font-expanded text-base font-bold">{formatPlainDate(date, "long")}</p>
            {slots.error ? (
              <ErrorBox error={slots.error} onRetry={slots.reload} />
            ) : slots.loading || !slots.data ? (
              <div className="grid grid-cols-3 gap-2 sm:grid-cols-4" role="status">
                <span className="sr-only">Loading times…</span>
                {Array.from({ length: 8 }, (_, i) => (
                  <Skeleton key={i} className="h-11 rounded-control" />
                ))}
              </div>
            ) : list.length === 0 ? (
              <p className="rounded-control bg-mist-2 px-4 py-3 text-sm text-slate-700">
                No times left on {formatPlainDate(date)}.{" "}
                {nextAvailable ? (
                  <button type="button" className="font-semibold text-brand underline underline-offset-2" onClick={() => pickDay(nextAvailable)}>
                    See {formatPlainDate(nextAvailable)}
                  </button>
                ) : (
                  "Try another day."
                )}
              </p>
            ) : (
              <div className="space-y-4">
                {groups.map((g) => (
                  <fieldset key={g.part}>
                    <legend className="mb-2 text-sm font-medium text-muted">{g.part}</legend>
                    <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
                      {g.items.map((s) => {
                        const selected = value?.startsAt === s.startsAt;
                        return (
                          <button
                            key={s.startsAt}
                            type="button"
                            aria-pressed={selected}
                            onClick={() => onChange(selected ? null : s)}
                            className={cx(
                              "min-h-11 rounded-control border text-base font-semibold tnum transition-colors",
                              selected
                                ? "animate-fill border-brand bg-brand text-on-brand"
                                : "border-line bg-surface text-aubergine hover:border-brand-line hover:bg-brand-wash",
                            )}
                          >
                            {formatTime(s.startsAt, tz)}
                          </button>
                        );
                      })}
                    </div>
                  </fieldset>
                ))}
              </div>
            )}
            {showZoneNote && <p className="mt-3 text-xs text-muted">Times are in {cityOf(tz)} time.</p>}
          </>
        )}
      </div>
    </div>
  );
}

function DayDot({ on }: { on: boolean }) {
  return <span className={cx("mx-auto mt-0.5 block h-1 w-1 rounded-full", on ? "bg-current" : "bg-transparent")} aria-hidden="true" />;
}

function MonthGrid({
  month,
  today,
  selected,
  inRange,
  isAvailable,
  showDots,
  loading,
  onPick,
}: {
  month: string;
  today: string;
  selected: string | null;
  inRange: (d: string) => boolean;
  isAvailable: (d: string) => boolean;
  showDots: boolean;
  loading: boolean;
  onPick: (d: string) => void;
}) {
  const first = `${month}-01`;
  const lead = (weekdayOf(first) + 6) % 7; // Monday-first
  const count = daysInMonth(month);
  const cells: (string | null)[] = [...Array.from({ length: lead }, () => null), ...Array.from({ length: count }, (_, i) => `${month}-${pad(i + 1)}`)];

  return (
    <div className={cx("hidden sm:block", loading && "opacity-60")}>
      <div className="grid grid-cols-7 gap-1 text-center text-xs text-muted" aria-hidden="true">
        {WEEK_ORDER.map((w) => (
          <span key={w} className="py-1">
            {WEEKDAYS_SHORT[w]}
          </span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-1" role="group" aria-label="Choose a day">
        {cells.map((d, i) =>
          d ? (
            <button
              key={d}
              type="button"
              disabled={!isAvailable(d)}
              aria-pressed={selected === d}
              aria-label={`${formatPlainDate(d, "long")}${isAvailable(d) ? "" : ", unavailable"}`}
              onClick={() => onPick(d)}
              className={cx(
                "h-11 rounded-control text-sm font-semibold tnum transition-colors",
                selected === d
                  ? "bg-brand text-on-brand"
                  : isAvailable(d)
                    ? "text-aubergine hover:bg-brand-wash"
                    : inRange(d)
                      ? "text-faint line-through decoration-faint/50"
                      : "text-slate-300",
                d === today && selected !== d && "ring-1 ring-line ring-inset",
              )}
            >
              {Number(d.slice(8))}
              {showDots && <DayDot on={isAvailable(d) && selected !== d} />}
            </button>
          ) : (
            <span key={`b${i}`} />
          ),
        )}
      </div>
    </div>
  );
}

function WeekStrip({
  month,
  today,
  maxDate,
  selected,
  isAvailable,
  showDots,
  onPick,
}: {
  month: string;
  today: string;
  maxDate: string;
  selected: string | null;
  isAvailable: (d: string) => boolean;
  showDots: boolean;
  onPick: (d: string) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const start = `${month}-01` < today ? today : `${month}-01`;
  const dates: string[] = [];
  for (let d = start; monthOf(d) === month && d <= maxDate; d = addDays(d, 1)) dates.push(d);

  useEffect(() => {
    if (!selected) return;
    const el = ref.current?.querySelector<HTMLElement>(`[data-date="${selected}"]`);
    el?.scrollIntoView({ block: "nearest", inline: "center", behavior: "smooth" });
  }, [selected]);

  return (
    <div ref={ref} className="no-scrollbar -mx-4 flex gap-1.5 overflow-x-auto px-4 pb-1 sm:hidden" role="group" aria-label="Choose a day">
      {dates.map((d) => {
        const on = selected === d;
        const avail = isAvailable(d);
        return (
          <button
            key={d}
            data-date={d}
            type="button"
            disabled={!avail}
            aria-pressed={on}
            aria-label={`${formatPlainDate(d, "long")}${avail ? "" : ", unavailable"}`}
            onClick={() => onPick(d)}
            className={cx(
              "flex w-[3.25rem] shrink-0 flex-col items-center rounded-control border py-2 transition-colors",
              on ? "border-brand bg-brand text-on-brand" : avail ? "border-line bg-surface text-aubergine" : "border-transparent text-faint",
            )}
          >
            <span className="text-xs">{WEEKDAYS_SHORT[weekdayOf(d)]}</span>
            <span className="font-expanded text-lg leading-tight font-bold tnum">{Number(d.slice(8))}</span>
            {showDots && <DayDot on={avail && !on} />}
          </button>
        );
      })}
    </div>
  );
}
