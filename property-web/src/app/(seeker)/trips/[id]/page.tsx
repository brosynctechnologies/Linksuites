"use client";

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatDay, formatStamp, lagosToday, localPath, phoneDigits } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { STAY_STATUS, statusOf } from "@/lib/status";
import type { MyStayDto } from "@/lib/types";
import { Ledger } from "@/components/ledger";
import { PhoneIcon } from "@/components/icons";
import { ApiErrorView, Button, Card, ErrorBox, Notice, PageHeader, PageLoader, StatusBadge, buttonClass } from "@/components/ui";

/**
 * One trip, and the Paystack return page: with `?reference=` (or `?trxref=`) it verifies the payment first.
 */
export default function TripPage() {
  const { id } = useParams<{ id: string }>();
  const sp = useSearchParams();
  const reference = sp.get("reference") ?? sp.get("trxref");
  const [stay, setStay] = useState<MyStayDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [verifyError, setVerifyError] = useState<ApiError | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const started = useRef<string | null>(null);

  useEffect(() => {
    const key = `${id}:${reference}:${attempt}`;
    if (started.current === key) return;
    started.current = key;
    const load = async () => {
      if (reference) {
        try {
          setStay(await api<MyStayDto>(`/api/stays/${encodeURIComponent(id)}/verify`, { method: "POST", body: { reference } }));
          setVerifyError(null);
          return;
        } catch (e) {
          setVerifyError(toApiError(e));
        }
      }
      try {
        const list = await api<MyStayDto[]>("/api/me/stays");
        const hit = asList(list).find((s) => s.id === id);
        if (hit) setStay(hit);
        else setError(toApiError(new Error("We couldn't find this trip on your account.")));
      } catch (e) {
        setError(toApiError(e));
      }
    };
    void load();
  }, [id, reference, attempt]);

  async function cancel() {
    if (!stay) return;
    if (!window.confirm("Cancel this stay? Your refund depends on how close it is to check-in.")) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await api<MyStayDto>(`/api/stays/${stay.id}/cancel`, { method: "POST" });
      setStay(updated);
      setNotice(updated.refundMinor > 0 ? `Cancelled. ${formatMoney(updated.refundMinor, updated.currency)} is on its way back to you.` : "Cancelled.");
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  if (!stay && error) {
    return (
      <div>
        <PageHeader title="Trip" back="/trips" />
        <ApiErrorView error={error} onRetry={() => setAttempt((a) => a + 1)} />
      </div>
    );
  }
  if (!stay) return <PageLoader label={reference ? "Confirming your payment…" : "Loading your trip…"} />;

  const st = statusOf(STAY_STATUS, stay.status);
  const listingHref = localPath(stay.listingUrl);
  // The API's rule: an unpaid hold can always be released; a confirmed stay only before check-in day (it has "started" on the day).
  const canCancel = stay.status === "Pending" || (stay.status === "Confirmed" && stay.checkIn > lagosToday());
  const nightsLabel = `${stay.nights} night${stay.nights === 1 ? "" : "s"} × ${formatMoney(stay.nightlyMinor, stay.currency)}`;
  const priceLines = [
    { key: "nights", label: nightsLabel, amountMinor: stay.nightlyMinor * stay.nights },
    ...(stay.cleaningMinor > 0 ? [{ key: "cleaning", label: "Cleaning fee", amountMinor: stay.cleaningMinor }] : []),
  ];

  return (
    <div className="space-y-5">
      <PageHeader title={stay.listingTitle} back="/trips" subtitle={stay.areaName} />
      {reference && stay.status === "Confirmed" && !verifyError && <Notice tone="success">Payment confirmed. Your stay is booked.</Notice>}
      {verifyError && (
        <Notice tone="warning">
          We couldn&apos;t confirm the payment yet ({verifyError.message}). If money left your account, don&apos;t pay again; payments can take a minute to
          clear.{" "}
          <button type="button" className="font-semibold underline" onClick={() => setAttempt((a) => a + 1)}>
            Check again
          </button>
        </Notice>
      )}
      {stay.status === "Pending" && stay.holdExpiresAt && (
        <Notice tone="warning">These dates are held until {formatStamp(stay.holdExpiresAt)} while payment completes. After that they are released.</Notice>
      )}
      {notice && <Notice tone="success">{notice}</Notice>}
      {error && <ErrorBox error={error} />}

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <Card className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
            {listingHref && (
              <Link href={listingHref} className="text-sm font-semibold text-adire hover:underline">
                View listing
              </Link>
            )}
          </div>
          <dl className="grid gap-3 sm:grid-cols-2">
            <div>
              <dt className="text-sm text-muted">Check-in</dt>
              <dd className="font-semibold text-asphalt">
                {formatDay(stay.checkIn)}
                {stay.checkInTime ? `, from ${stay.checkInTime}` : ""}
              </dd>
            </div>
            <div>
              <dt className="text-sm text-muted">Check-out</dt>
              <dd className="font-semibold text-asphalt">
                {formatDay(stay.checkOut)}
                {stay.checkOutTime ? `, by ${stay.checkOutTime}` : ""}
              </dd>
            </div>
            <div>
              <dt className="text-sm text-muted">Guests</dt>
              <dd className="font-semibold text-asphalt">{stay.guests}</dd>
            </div>
            <div>
              <dt className="text-sm text-muted">Host</dt>
              <dd className="font-semibold text-asphalt">{stay.agentName}</dd>
            </div>
            {stay.address && (
              <div className="sm:col-span-2">
                <dt className="text-sm text-muted">Address</dt>
                <dd className="font-semibold text-asphalt">{stay.address}</dd>
              </div>
            )}
          </dl>
          {!stay.address && stay.status !== "Cancelled" && stay.status !== "Expired" && (
            <p className="text-sm text-muted">The host&apos;s number and check-in details appear here once the booking is confirmed. The address is on the listing.</p>
          )}
          {stay.houseRules && (
            <div>
              <h2 className="text-sm font-semibold text-asphalt">House rules</h2>
              <p className="prose-listing mt-1 text-sm">{stay.houseRules}</p>
            </div>
          )}
          <div className="flex flex-wrap gap-2">
            {stay.agentPhone && (
              <a href={`tel:+${phoneDigits(stay.agentPhone)}`} className={buttonClass("secondary")}>
                <PhoneIcon className="h-4 w-4" /> Call the host
              </a>
            )}
            {canCancel && (
              <Button variant="danger" loading={busy} onClick={cancel}>
                Cancel stay
              </Button>
            )}
          </div>
          {canCancel && (
            <p className="text-xs text-muted">
              Cancel more than 48 hours before 14:00 on check-in day for a full refund. Later than that, you get everything back except one night.
            </p>
          )}
        </Card>
        {stay.paid ? (
          <Ledger
            title="What you paid"
            currency={stay.currency}
            lines={[...priceLines, ...(stay.refundMinor > 0 ? [{ key: "refund", label: "Refunded", amountMinor: -stay.refundMinor }] : [])]}
            totalLabel={stay.refundMinor > 0 ? "Net paid" : "Total"}
            totalMinor={stay.totalMinor - stay.refundMinor}
          />
        ) : (
          // Nothing was paid (still waiting for payment, or cancelled before paying), so this is the price, not a receipt.
          <Ledger title="Price" currency={stay.currency} lines={priceLines} totalLabel="Total" totalMinor={stay.totalMinor} />
        )}
      </div>
    </div>
  );
}
