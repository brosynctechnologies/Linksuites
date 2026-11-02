import type { ReactNode } from "react";
import { initials } from "@/lib/color";
import { STATUS_LABEL } from "@/lib/status";
import { formatDateLong, formatDuration, formatTime } from "@/lib/time";
import type { BookingStatus } from "@/lib/types";
import { cx } from "./styles";

const statusText: Record<BookingStatus, string> = {
  Pending: "text-amber-text",
  Confirmed: "text-mint-dark",
  Completed: "text-slate-600",
  Cancelled: "text-brick",
  NoShow: "text-brick",
  Expired: "text-muted",
};
const statusDot: Record<BookingStatus, string> = {
  Pending: "bg-amber",
  Confirmed: "bg-mint",
  Completed: "bg-faint",
  Cancelled: "bg-brick",
  NoShow: "bg-brick",
  Expired: "border border-faint",
};

export interface AppointmentCardProps {
  businessName: string;
  logoUrl?: string | null;
  startsAt: string;
  timezone: string;
  serviceName: string;
  durationMinutes?: number;
  staffName?: string | null;
  staffPhotoUrl?: string | null;
  status?: BookingStatus;
  code?: string;
  /** Overrides the date line (the landing page example shows no year). */
  dateText?: string;
  /** Replaces the status word, e.g. the "Held for 9:41" countdown. */
  statusSlot?: ReactNode;
  /** Business brand colour for the top rule (defaults to the page's --brand). */
  accent?: string;
  /** Plays the arrival motion (on confirmation). */
  animate?: boolean;
  className?: string;
  children?: ReactNode;
}

/**
 * The appointment card: the card a salon hands you at the counter. Business name, the time huge in
 * Archivo Expanded, the date beneath, then the service and who is doing it. The one memorable thing.
 */
export function AppointmentCard({
  businessName,
  logoUrl,
  startsAt,
  timezone,
  serviceName,
  durationMinutes,
  staffName,
  staffPhotoUrl,
  status,
  code,
  dateText,
  statusSlot,
  accent,
  animate,
  className,
  children,
}: AppointmentCardProps) {
  const cancelled = status === "Cancelled" || status === "Expired" || status === "NoShow";
  return (
    <article
      className={cx("relative overflow-hidden rounded-card bg-surface text-aubergine shadow-card", animate && "animate-card-in", className)}
      aria-label={`Appointment at ${businessName}`}
    >
      <span className="absolute inset-x-0 top-0 h-1.5" style={{ background: accent ?? "var(--brand)" }} aria-hidden="true" />
      <div className="px-6 pt-7 pb-5 sm:px-7">
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2.5">
            {logoUrl && <img src={logoUrl} alt="" className="h-8 w-8 shrink-0 rounded-full border border-line object-cover" />}
            <p className="truncate font-expanded text-sm font-bold">{businessName}</p>
          </div>
          {code && <p className="shrink-0 text-xs text-muted tnum">Ref {code}</p>}
        </div>

        <p
          className={cx(
            "mt-5 font-expanded text-[clamp(3.5rem,17vw,5rem)] leading-[0.95] font-extrabold tnum",
            cancelled && "text-faint line-through decoration-[0.06em]",
          )}
        >
          <time dateTime={startsAt}>{formatTime(startsAt, timezone)}</time>
        </p>
        <p className={cx("mt-2 font-expanded text-lg font-semibold", cancelled && "text-muted")}>{dateText ?? formatDateLong(startsAt, timezone)}</p>
      </div>

      <div className="mx-6 border-t-2 border-dashed border-line sm:mx-7" aria-hidden="true" />

      <div className="space-y-3 px-6 pt-4 pb-6 sm:px-7">
        <div>
          <p className="font-semibold">{serviceName}</p>
          {durationMinutes ? <p className="text-sm text-muted">{formatDuration(durationMinutes)}</p> : null}
        </div>
        {staffName && (
          <div className="flex items-center gap-2.5">
            {staffPhotoUrl ? (
              <img src={staffPhotoUrl} alt="" className="h-8 w-8 rounded-full object-cover" />
            ) : (
              <span className="flex h-8 w-8 items-center justify-center rounded-full bg-mist-2 text-xs font-semibold text-slate-700" aria-hidden="true">
                {initials(staffName)}
              </span>
            )}
            <p className="text-sm">
              with <span className="font-semibold">{staffName}</span>
            </p>
          </div>
        )}
        {(statusSlot || status) && (
          <div className="pt-1">
            {statusSlot ?? (
              status && (
                <p className={cx("flex items-center gap-2 text-sm font-semibold", statusText[status])}>
                  <span className={cx("h-2 w-2 rounded-full", statusDot[status])} aria-hidden="true" />
                  {STATUS_LABEL[status]}
                </p>
              )
            )}
          </div>
        )}
        {children}
      </div>
    </article>
  );
}
