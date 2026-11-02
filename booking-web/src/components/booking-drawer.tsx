"use client";

import Link from "next/link";
import { formatMoney } from "@/lib/money";
import { staffColor } from "@/lib/color";
import { SOURCE_LABEL } from "@/lib/status";
import { formatDateLong, formatTimeRange } from "@/lib/time";
import type { BookingDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useNow } from "@/lib/use-now";
import { QuickActions } from "./booking-actions";
import { PhoneIcon, WhatsAppIcon } from "./icons";
import { telHref, whatsappHref } from "./public-parts";
import { ApiErrorView, BookingStatusBadge, Drawer, PageLoader, buttonClass } from "./ui";

/** Quick look at a booking from the calendar, with the status actions and a link to the full page. */
export function BookingDrawer({
  id,
  tz,
  currency,
  onClose,
  onChanged,
}: {
  id: string | null;
  tz: string;
  currency: string;
  onClose: () => void;
  onChanged: (b: BookingDto) => void;
}) {
  const { data: b, error, reload, setData } = useApi<BookingDto>(id ? `/api/bookings/${id}` : null);
  const now = useNow();
  const shown = b && b.id === id ? b : null;

  return (
    <Drawer open={!!id} title={shown ? shown.customerName : "Booking"} onClose={onClose}>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!shown && !error && <PageLoader />}
      {shown && (
        <div className="space-y-5">
          <div>
            <p className="font-expanded text-[2.5rem] leading-none font-extrabold tnum">{formatTimeRange(shown.startsAt, shown.endsAt, tz).split("–")[0]}</p>
            <p className="mt-1 font-expanded font-semibold">{formatDateLong(shown.startsAt, tz)}</p>
            <p className="text-sm text-muted tnum">{formatTimeRange(shown.startsAt, shown.endsAt, tz)}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <BookingStatusBadge status={shown.status} />
            <span className="text-xs text-muted">
              {SOURCE_LABEL[shown.source]}, ref {shown.code}
            </span>
          </div>
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between gap-4">
              <dt className="text-muted">Service</dt>
              <dd className="text-right font-medium">{shown.serviceName}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-muted">With</dt>
              <dd className="flex items-center gap-1.5 font-medium">
                <span className="h-2.5 w-2.5 rounded-full" style={{ background: staffColor(shown.staffColor, shown.staffId) }} aria-hidden="true" />
                {shown.staffName}
              </dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-muted">Price</dt>
              <dd className="font-medium tnum">{formatMoney(shown.priceMinor, currency)}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-muted">Paid</dt>
              <dd className="font-medium tnum">{formatMoney(shown.paidMinor, currency)}</dd>
            </div>
          </dl>
          {shown.customer.noShowCount > 0 && (
            <p className="rounded-control bg-brick-wash px-3 py-2 text-sm text-brick-dark">
              {shown.customer.noShowCount} earlier no-show{shown.customer.noShowCount === 1 ? "" : "s"}.
            </p>
          )}
          {shown.notes && <p className="rounded-control bg-mist px-3 py-2 text-sm whitespace-pre-line">{shown.notes}</p>}
          {shown.customer.phone && (
            <div className="flex flex-wrap gap-2">
              <a href={telHref(shown.customer.phone)} className={buttonClass("secondary", "min-h-9 px-3 py-1.5")}>
                <PhoneIcon className="h-4 w-4" /> Call
              </a>
              <a
                href={shown.whatsappReminderUrl || whatsappHref(shown.customer.phone)}
                target="_blank"
                rel="noopener noreferrer"
                className={buttonClass("secondary", "min-h-9 px-3 py-1.5")}
              >
                <WhatsAppIcon className="h-4 w-4 text-mint" /> WhatsApp
              </a>
            </div>
          )}
          <QuickActions
            booking={shown}
            startsInPast={!!now && new Date(shown.startsAt).getTime() <= now}
            onChanged={(nb) => {
              setData(nb);
              onChanged(nb);
            }}
          />
          <Link href={`/app/bookings/${shown.id}`} className={buttonClass("primary", "w-full")}>
            Open booking
          </Link>
        </div>
      )}
    </Drawer>
  );
}
