"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { addDays, daysBetween, formatDay, lagosToday } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import type { AvailabilityDto, DateRange, ShortLetDto, StayBookingResponse, StayQuoteDto } from "@/lib/types";
import { Ledger } from "@/components/ledger";
import { MonthCalendar, monthStart, nextMonth } from "@/components/month-calendar";
import { ChevronLeftIcon, ChevronRightIcon } from "@/components/icons";
import { ApiErrorView, Button, Input, Modal, Select, buttonClass } from "@/components/ui";

const MAX_NIGHTS = 90;

function inRanges(day: string, ranges: DateRange[]) {
  return ranges.some((r) => day >= r.start.slice(0, 10) && day < r.end.slice(0, 10));
}

/** A cost the host collects in person (a caution deposit, say). Never part of the online total. */
export interface HostPaidCost {
  key: string;
  label: string;
  amountMinor: number;
}

/**
 * Short-let dates: a two-month calendar with booked and closed nights struck through, a min-nights hint, and the
 * ledger as a live quote (nights × price, cleaning, total) from /stay-quote. "Reserve and pay" holds the dates for
 * 15 minutes and sends the guest to Paystack. Other costs the host lists are shown apart, as paid to the host and not
 * charged here, because the online total is nights and cleaning only.
 */
export function StayBooker({
  listingId,
  title,
  nightlyMinor,
  currency,
  shortLet,
  hostPaid = [],
}: {
  listingId: string;
  title: string;
  nightlyMinor: number;
  currency: string;
  shortLet: ShortLetDto;
  hostPaid?: HostPaidCost[];
}) {
  const auth = useAuth();
  // Signed in, the guest's own unpaid hold (say, after going back from Paystack) doesn't count as taken for them.
  const signedIn = !!auth;
  const pathname = usePathname();
  const today = lagosToday();
  const [month, setMonth] = useState(today.slice(0, 7));
  const [avail, setAvail] = useState<AvailabilityDto | null>(null);
  const [availNonce, setAvailNonce] = useState(0);
  const [checkIn, setCheckIn] = useState<string | null>(null);
  const [checkOut, setCheckOut] = useState<string | null>(null);
  const [guests, setGuests] = useState(1);
  const [quote, setQuote] = useState<{ key: string; data?: StayQuoteDto; error?: ApiError } | null>(null);
  const [phone, setPhone] = useState("");
  const [busy, setBusy] = useState(false);
  const [bookError, setBookError] = useState<ApiError | null>(null);
  const [askLogin, setAskLogin] = useState(false);

  const from = monthStart(month);
  const to = monthStart(nextMonth(month, 2));

  useEffect(() => {
    const ctrl = new AbortController();
    api<AvailabilityDto>(`/api/listings/${listingId}/availability?from=${from}&to=${to}`, { auth: signedIn, signal: ctrl.signal }).then(
      (a) => setAvail({ booked: a?.booked ?? [], blocked: a?.blocked ?? [] }),
      () => {},
    );
    return () => ctrl.abort();
  }, [listingId, from, to, availNonce, signedIn]);

  const quoteKey = checkIn && checkOut ? `${checkIn}|${checkOut}|${guests}` : null;
  useEffect(() => {
    if (!checkIn || !checkOut || !quoteKey) return;
    const ctrl = new AbortController();
    api<StayQuoteDto>(`/api/listings/${listingId}/stay-quote`, { method: "POST", body: { listingId, checkIn, checkOut, guests }, auth: signedIn, signal: ctrl.signal }).then(
      (data) => setQuote({ key: quoteKey, data }),
      (e) => !ctrl.signal.aborted && setQuote({ key: quoteKey, error: toApiError(e) }),
    );
    return () => ctrl.abort();
  }, [listingId, checkIn, checkOut, guests, quoteKey, signedIn]);

  const taken = (day: string) => !!avail && (inRanges(day, avail.booked) || inRanges(day, avail.blocked));
  const nightsFree = (a: string, b: string) => {
    for (let d = a; d < b; d = addDays(d, 1)) if (taken(d)) return false;
    return true;
  };

  function pick(day: string) {
    setBookError(null);
    if (!checkIn || checkOut || day <= checkIn) {
      setCheckIn(day);
      setCheckOut(null);
      return;
    }
    if (!nightsFree(checkIn, day)) {
      setCheckIn(taken(day) ? null : day);
      setCheckOut(null);
      return;
    }
    setCheckOut(day);
  }

  const nights = checkIn && checkOut ? daysBetween(checkIn, checkOut) : 0;
  const current = quote && quote.key === quoteKey ? quote : null;
  const q = current?.data;
  const tooShort = nights > 0 && nights < shortLet.minNights;

  async function reserve() {
    if (!checkIn || !checkOut) return;
    if (!auth) return setAskLogin(true);
    setBusy(true);
    setBookError(null);
    try {
      const res = await api<StayBookingResponse>("/api/stays", {
        method: "POST",
        body: { listingId, checkIn, checkOut, guests, phone: phone.trim() || null },
      });
      window.location.assign(res.authorizationUrl);
    } catch (e) {
      const err = toApiError(e);
      setBookError(err);
      if (err.status === 409) {
        setAvailNonce((n) => n + 1);
        setCheckOut(null);
      }
      setBusy(false);
    }
  }

  const look = (day: string) => {
    const past = day < today;
    const isTaken = taken(day);
    const selected = day === checkIn || day === checkOut;
    const inRange = !!checkIn && !!checkOut && day > checkIn && day < checkOut;
    // While choosing check-out, a booked night can still be the morning you leave.
    const canBeCheckout = !!checkIn && !checkOut && day > checkIn && daysBetween(checkIn, day) <= MAX_NIGHTS && nightsFree(checkIn, day);
    return { disabled: past || (isTaken && !canBeCheckout), struck: isTaken, selected, inRange };
  };

  return (
    <div className="space-y-4">
      <Ledger
        title="Your stay"
        currency={q?.currency ?? currency}
        live
        busy={!!quoteKey && !current}
        lines={[
          {
            key: "nights",
            label: q ? `${formatMoney(q.nightlyMinor, q.currency)} × ${q.nights} night${q.nights === 1 ? "" : "s"}` : `${formatMoney(nightlyMinor, currency)} a night`,
            amountMinor: q ? q.subtotalMinor : null,
          },
          { key: "cleaning", label: "Cleaning", amountMinor: q ? q.cleaningMinor : shortLet.cleaningFeeMinor },
        ]}
        totalLabel="Total"
        totalMinor={q ? q.totalMinor : null}
        totalPlaceholder={checkIn && !checkOut ? "Pick your check-out day" : "Pick your dates"}
        footnote={
          q && checkIn && checkOut ? `${formatDay(checkIn)} to ${formatDay(checkOut)}, ${guests} guest${guests === 1 ? "" : "s"}. Paid securely; the host is paid after check-in.` : undefined
        }
      />

      {hostPaid.length > 0 && (
        <section aria-labelledby="host-paid-h" className="rounded-panel border border-line bg-surface p-4">
          <h2 id="host-paid-h" className="font-semibold text-asphalt">
            Paid to the host, not charged here
          </h2>
          <p className="mt-1 text-sm text-muted">The host collects these from you directly. They aren&apos;t in the total above, and PropertyLink never charges them.</p>
          <dl className="mt-3 space-y-2 text-[0.9375rem]">
            {hostPaid.map((c) => (
              <div key={c.key} className="flex items-baseline gap-2">
                <dt className="min-w-0 shrink text-slate-700">{c.label}</dt>
                <span className="leader" aria-hidden="true" />
                <dd className="price shrink-0 text-right font-semibold text-asphalt">{formatMoney(c.amountMinor, currency)}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      <section aria-labelledby="dates-h" className="rounded-panel border border-line bg-surface p-4">
        <div className="flex items-center justify-between gap-2">
          <h2 id="dates-h" className="font-semibold text-asphalt">
            Choose dates
          </h2>
          <div className="flex gap-1">
            <button
              type="button"
              onClick={() => setMonth((m) => nextMonth(m, -1))}
              disabled={month <= today.slice(0, 7)}
              className="flex h-11 w-11 items-center justify-center rounded-control hover:bg-plaster-2 disabled:opacity-40"
              aria-label="Earlier months"
            >
              <ChevronLeftIcon />
            </button>
            <button type="button" onClick={() => setMonth((m) => nextMonth(m, 1))} className="flex h-11 w-11 items-center justify-center rounded-control hover:bg-plaster-2" aria-label="Later months">
              <ChevronRightIcon />
            </button>
          </div>
        </div>
        <p className="mt-1 text-sm text-muted">
          {!checkIn ? "Tap your check-in day." : !checkOut ? "Now tap your check-out day." : `${nights} night${nights === 1 ? "" : "s"}.`} Minimum stay{" "}
          {shortLet.minNights} night{shortLet.minNights === 1 ? "" : "s"}. Crossed-out nights are taken.
        </p>
        <div className="mt-3 grid gap-5 sm:grid-cols-2">
          <MonthCalendar month={month} look={look} onPick={pick} />
          <MonthCalendar month={nextMonth(month)} look={look} onPick={pick} />
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <Select label="Guests" value={guests} onChange={(e) => setGuests(Number(e.target.value))}>
            {Array.from({ length: Math.max(1, shortLet.maxGuests) }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                {n} guest{n === 1 ? "" : "s"}
              </option>
            ))}
          </Select>
          <Input label="Phone (optional)" type="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} hint="Shared with the host once paid." />
        </div>
        {tooShort && <p className="mt-3 text-sm font-medium text-brick">This place needs at least {shortLet.minNights} nights.</p>}
        {current?.error && !tooShort && <div className="mt-3"><ApiErrorView error={current.error} /></div>}
        {bookError && <div className="mt-3"><ApiErrorView error={bookError} /></div>}
        <Button className="mt-4 w-full" disabled={!q || busy} loading={busy} onClick={reserve}>
          Reserve and pay{q ? ` ${formatMoney(q.totalMinor, q.currency)}` : ""}
        </Button>
        <p className="mt-2 text-xs text-muted">
          Your dates are held for 15 minutes while you pay. Cancel more than 48 hours before 14:00 on check-in day for a full refund; later, you get everything
          back except one night.
        </p>
      </section>

      <Modal open={askLogin} title="Log in to reserve" onClose={() => setAskLogin(false)}>
        <p className="text-sm text-slate-700">Log in or create a free account to book {title}. Your trips appear under Trips.</p>
        <div className="mt-4 grid gap-2">
          <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={buttonClass("primary")}>
            Log in
          </Link>
          <Link href={`/signup?next=${encodeURIComponent(pathname)}`} className={buttonClass("secondary")}>
            Create an account
          </Link>
        </div>
      </Modal>
    </div>
  );
}
