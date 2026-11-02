"use client";

import { addDays } from "@/lib/format";
import { cx } from "./styles";

// A plain month grid (Monday first) shared by the short-let date picker and the studio's stays calendar.

export interface DayLook {
  /** Can't be picked. */
  disabled?: boolean;
  /** Booked or closed night: struck through. */
  struck?: boolean;
  /** Check-in / check-out ends. */
  selected?: boolean;
  /** Nights between the ends. */
  inRange?: boolean;
  /** Extra marker colour class for the dot under the number (studio: stays, closed). */
  dot?: string;
  /** Screen-reader description added after the date. */
  note?: string;
}

const monthFmt = new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric", timeZone: "UTC" });
const fullFmt = new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" });

/** "2026-10" → first day "2026-10-01". */
export function monthStart(month: string): string {
  return `${month}-01`;
}

export function nextMonth(month: string, n = 1): string {
  const [y, m] = month.split("-").map(Number);
  const d = new Date(Date.UTC(y, m - 1 + n, 1));
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}`;
}

export function MonthCalendar({ month, look, onPick, className }: { month: string; look: (day: string) => DayLook; onPick?: (day: string) => void; className?: string }) {
  const [y, m] = month.split("-").map(Number);
  const first = new Date(Date.UTC(y, m - 1, 1));
  const daysInMonth = new Date(Date.UTC(y, m, 0)).getUTCDate();
  const lead = (first.getUTCDay() + 6) % 7;
  const start = monthStart(month);
  const cells: (string | null)[] = [...Array(lead).fill(null), ...Array.from({ length: daysInMonth }, (_, i) => addDays(start, i))];

  return (
    <div className={cx("min-w-0", className)}>
      <p className="mb-2 text-center text-sm font-semibold text-asphalt">{monthFmt.format(first)}</p>
      <div className="grid grid-cols-7 text-center text-xs text-muted" aria-hidden="true">
        {["M", "T", "W", "T", "F", "S", "S"].map((d, i) => (
          <span key={i} className="py-1">
            {d}
          </span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-y-0.5">
        {cells.map((day, i) => {
          if (!day) return <span key={`e${i}`} />;
          const l = look(day);
          const [yy, mm, dd] = day.split("-").map(Number);
          const label = `${fullFmt.format(new Date(Date.UTC(yy, mm - 1, dd)))}${l.struck ? ", unavailable" : ""}${l.note ? `, ${l.note}` : ""}`;
          return (
            <button
              key={day}
              type="button"
              disabled={l.disabled || !onPick}
              onClick={() => onPick?.(day)}
              aria-label={label}
              aria-pressed={l.selected || undefined}
              className={cx(
                "relative flex h-11 items-center justify-center text-sm tabular-nums",
                l.inRange && "bg-adire-wash",
                l.selected && "rounded-control bg-adire font-semibold text-white",
                !l.selected && !l.disabled && onPick && "rounded-control hover:bg-plaster-2",
                l.disabled && !l.selected && "cursor-not-allowed text-faint",
                l.struck && !l.selected && "line-through decoration-2",
              )}
            >
              {dd}
              {l.dot && <span className={cx("absolute bottom-1 h-1 w-1 rounded-full", l.dot)} aria-hidden="true" />}
            </button>
          );
        })}
      </div>
    </div>
  );
}
