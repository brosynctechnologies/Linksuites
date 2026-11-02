"use client";

import { useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useCan } from "@/lib/permissions";
import type { BookingDto, BookingStatus, BookingSummaryDto } from "@/lib/types";
import { WhatsAppIcon } from "./icons";
import { whatsappHref } from "./public-parts";
import { Button, cx } from "./ui";

/** POST /api/bookings/{id}/status */
export function setBookingStatus(id: string, status: BookingStatus, note?: string | null) {
  return api<BookingDto>(`/api/bookings/${id}/status`, { method: "POST", body: { status, note: note ?? null } });
}

/**
 * Open the ready-made WhatsApp reminder for a booking. The summary rows don't carry the link, so this
 * opens a tab straight away (keeping the click's permission to open it) and points it at the
 * booking's whatsappReminderUrl once loaded.
 */
export async function openWhatsAppReminder(b: BookingSummaryDto | BookingDto) {
  const direct = "whatsappReminderUrl" in b ? b.whatsappReminderUrl : null;
  if (direct) {
    window.open(direct, "_blank", "noopener");
    return;
  }
  const w = window.open("", "_blank");
  try {
    const full = await api<BookingDto>(`/api/bookings/${b.id}`);
    const url = full.whatsappReminderUrl || (full.customer.phone ? whatsappHref(full.customer.phone) : null);
    if (url && w) w.location.href = url;
    else w?.close();
  } catch {
    w?.close();
  }
}

/** Confirm / Completed / No-show / Remind buttons for one booking, as fits its status. */
export function QuickActions({
  booking,
  onChanged,
  compact,
  startsInPast,
}: {
  booking: BookingSummaryDto;
  onChanged: (b: BookingDto) => void;
  compact?: boolean;
  /** Completed and No-show only make sense once the appointment has started. */
  startsInPast: boolean;
}) {
  const canEdit = useCan("bookings.edit");
  const [busy, setBusy] = useState<BookingStatus | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  if (!canEdit) return null;

  async function to(status: BookingStatus) {
    setBusy(status);
    setError(null);
    try {
      onChanged(await setBookingStatus(booking.id, status));
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const btn = compact ? "min-h-9 px-3 py-1.5" : undefined;
  const s = booking.status;
  return (
    <div className="flex flex-wrap items-center gap-2">
      {s === "Pending" && (
        <Button variant="secondary" className={btn} loading={busy === "Confirmed"} onClick={() => to("Confirmed")}>
          Confirm
        </Button>
      )}
      {s === "Confirmed" && startsInPast && (
        <>
          <Button variant="secondary" className={btn} loading={busy === "Completed"} onClick={() => to("Completed")}>
            Completed
          </Button>
          <Button variant="danger" className={btn} loading={busy === "NoShow"} onClick={() => to("NoShow")}>
            No-show
          </Button>
        </>
      )}
      {(s === "Confirmed" || s === "Pending") && !startsInPast && (booking.customerPhone || "whatsappReminderUrl" in booking) && (
        <Button variant="ghost" className={cx(btn, "text-mint-dark")} onClick={() => openWhatsAppReminder(booking)}>
          <WhatsAppIcon className="h-4 w-4" /> Remind
        </Button>
      )}
      {error && (
        <p className="w-full text-xs text-brick" role="alert">
          {error.message}
        </p>
      )}
    </div>
  );
}
