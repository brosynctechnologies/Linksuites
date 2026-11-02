"use client";

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, apiUrl, toApiError, type ApiError } from "@/lib/api";
import { STORAGE_PREFIX } from "@/lib/config";
import { formatMoney } from "@/lib/money";
import { addDays, cityOf, formatCountdown, formatDateTime, todayIn } from "@/lib/time";
import type { PublicBookingDto, PublicBookingPageDto, SlotDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useNow } from "@/lib/use-now";
import { AppointmentCard } from "@/components/appointment-card";
import { DateSlotPicker } from "@/components/slot-picker";
import { ContactButtons, PublicFrame } from "@/components/public-parts";
import { Button, ErrorBox, Input, Modal, Notice, PageLoader, Textarea, buttonClass, cx } from "@/components/ui";
import { CalendarIcon, MapPinIcon, StarIcon } from "@/components/icons";

function storedEmail(): string {
  if (typeof window === "undefined") return "";
  try {
    const raw = window.localStorage.getItem(`${STORAGE_PREFIX}customer`);
    return raw ? ((JSON.parse(raw) as { email?: string }).email ?? "") : "";
  } catch {
    return "";
  }
}

export function ManageBooking() {
  const { token } = useParams<{ token: string }>();
  const search = useSearchParams();
  const router = useRouter();
  const reference = search.get("reference") || search.get("trxref");
  const justBooked = search.get("booked") === "1" || !!reference;
  const base = `/api/public/bookings/${encodeURIComponent(token)}`;

  // Coming back from Paystack: verify once, then drop the reference from the URL.
  const verified = useRef(false);
  const [verifying, setVerifying] = useState(!!reference);
  const [verifyError, setVerifyError] = useState<ApiError | null>(null);
  const { data, error, reload, setData } = useApi<PublicBookingDto>(verifying ? null : base, { auth: false });

  useEffect(() => {
    if (!reference || verified.current) return;
    verified.current = true;
    api<PublicBookingDto>(`${base}/verify`, { method: "POST", body: { reference }, auth: false })
      .then(
        (b) => setData(b),
        (e) => setVerifyError(toApiError(e)),
      )
      .finally(() => {
        setVerifying(false);
        router.replace(`/b/${encodeURIComponent(token)}?booked=1`, { scroll: false });
      });
  }, [reference, base, token, router, setData]);

  if (verifying) return <PageLoader label="Checking your payment…" />;
  if (error && !data) {
    return (
      <PublicFrame brand={null}>
        <div className="px-4 py-16 text-center">
          <h1 className="font-expanded text-2xl font-extrabold">{error.status === 404 ? "Booking not found" : "This page didn't load"}</h1>
          <p className="mx-auto mt-2 max-w-sm text-sm text-muted">
            {error.status === 404 ? "Check the link in your confirmation message. It may have been mistyped." : error.message}
          </p>
          {error.status !== 404 && (
            <Button className="mt-6" onClick={reload}>
              Try again
            </Button>
          )}
        </div>
      </PublicFrame>
    );
  }
  if (!data) return <PageLoader />;

  return <BookingView token={token} booking={data} setBooking={setData} justBooked={justBooked} verifyError={verifyError} />;
}

function BookingView({
  token,
  booking: b,
  setBooking,
  justBooked,
  verifyError,
}: {
  token: string;
  booking: PublicBookingDto;
  setBooking: (b: PublicBookingDto) => void;
  justBooked: boolean;
  verifyError: ApiError | null;
}) {
  const base = `/api/public/bookings/${encodeURIComponent(token)}`;
  const tz = b.timezone;
  const now = useNow();
  const holdLeft = b.status === "Pending" && b.holdExpiresAt && now ? new Date(b.holdExpiresAt).getTime() - now : null;
  const holdOver = holdLeft !== null && holdLeft <= 0;
  const live = b.status === "Pending" || b.status === "Confirmed";

  const [panel, setPanel] = useState<"none" | "reschedule" | "cancel">("none");
  const [notice, setNotice] = useState<string | null>(null);

  let heading: string | null = null;
  if (justBooked && b.status === "Confirmed") heading = "You're booked";
  else if (b.status === "Pending" && !holdOver) heading = "Pay the deposit to keep this time";

  return (
    <PublicFrame brand={b.business.brandColor}>
      <div className="space-y-5 px-4 pt-6 pb-16 sm:px-0 sm:pt-10">
        {heading && <h1 className="text-xl font-bold">{heading}</h1>}
        {verifyError && (
          <Notice tone="warning">
            We couldn&apos;t confirm your payment yet ({verifyError.message}). If you paid, refresh in a minute; the business will see it either way.
          </Notice>
        )}
        {notice && <Notice tone="success">{notice}</Notice>}

        <AppointmentCard
          animate={justBooked}
          businessName={b.business.name}
          logoUrl={b.business.logoUrl}
          startsAt={b.startsAt}
          timezone={tz}
          serviceName={b.service.name}
          durationMinutes={b.service.durationMinutes}
          staffName={b.staff?.displayName}
          staffPhotoUrl={b.staff?.photoUrl}
          status={b.status}
          code={b.code}
          statusSlot={
            b.status === "Pending" ? (
              holdOver ? (
                <p className="flex items-center gap-2 text-sm font-semibold text-muted">
                  <span className="h-2 w-2 rounded-full border border-faint" aria-hidden="true" />
                  Hold ran out
                </p>
              ) : (
                <p className="flex items-center gap-2 text-sm font-semibold text-amber-text" role="timer" aria-live="off">
                  <span className="h-2 w-2 rounded-full bg-amber" aria-hidden="true" />
                  Pending deposit. Held for <span className="tnum">{holdLeft !== null ? formatCountdown(holdLeft) : "…"}</span>
                </p>
              )
            ) : undefined
          }
        >
          {b.status === "Pending" && !holdOver && b.depositDueMinor > 0 && <PayDeposit base={base} booking={b} />}
          {b.status === "Pending" && !holdOver && b.depositDueMinor <= 0 && (
            <p className="text-sm text-slate-700">Your deposit is already covered. Contact {b.business.name} to confirm your booking.</p>
          )}
          {b.status === "Pending" && holdOver && (
            <Link href={`/${b.business.slug}`} className={buttonClass("secondary", "w-full")}>
              Book again
            </Link>
          )}
        </AppointmentCard>
        <p className="text-center text-xs text-muted">Times are in {cityOf(tz)} time.</p>

        {/* Money */}
        <section className="rounded-panel border border-line bg-surface p-4 sm:p-5" aria-label="Payment">
          <dl className="space-y-2 text-sm">
            <Row label="Price" value={formatMoney(b.priceMinor, b.currency)} />
            {b.depositMinor > 0 && <Row label="Deposit" value={formatMoney(b.depositMinor, b.currency)} />}
            <Row label="Paid so far" value={formatMoney(b.paidMinor, b.currency)} />
            {live && (
              <div className="border-t border-line-soft pt-2">
                <Row label={`Balance to pay at ${b.business.name}`} value={formatMoney(b.balanceMinor, b.currency)} strong />
              </div>
            )}
          </dl>
          {(b.status === "Cancelled" || b.status === "Expired") && b.paidMinor > 0 && (
            <p className="mt-3 border-t border-line-soft pt-3 text-sm text-slate-700">
              You paid {formatMoney(b.paidMinor, b.currency)}. Ask {b.business.name} about your deposit.
            </p>
          )}
        </section>

        {/* Where */}
        <section className="space-y-3 rounded-panel border border-line bg-surface p-4 sm:p-5">
          <h2 className="font-bold">Getting there</h2>
          {b.business.address && (
            <p className="flex gap-2 text-sm text-slate-700">
              <MapPinIcon className="mt-0.5 h-4 w-4 shrink-0 text-muted" />
              <a
                href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(b.business.address)}`}
                target="_blank"
                rel="noopener noreferrer"
                className="underline-offset-2 hover:underline"
              >
                {b.business.address}
              </a>
            </p>
          )}
          <ContactButtons
            phone={b.business.phone}
            whatsapp={b.business.whatsappNumber}
            message={`Hello ${b.business.name}, about my booking ${b.code} on ${formatDateTime(b.startsAt, tz)}.`}
          />
          {live && (
            <a href={apiUrl(`${base}/ics`)} className={buttonClass("secondary")}>
              <CalendarIcon className="h-4 w-4" /> Add to calendar
            </a>
          )}
        </section>

        {/* Change or cancel */}
        {live && (
          <section className="space-y-3 rounded-panel border border-line bg-surface p-4 sm:p-5">
            <h2 className="font-bold">Need to change it?</h2>
            {b.canReschedule || b.canCancel ? (
              <>
                {b.cancelDeadline && (
                  <p className="text-sm text-slate-700">You can move or cancel this booking until {formatDateTime(b.cancelDeadline, tz)}.</p>
                )}
                <div className="flex flex-wrap gap-2">
                  {b.canReschedule && (
                    <Button variant={panel === "reschedule" ? "primary" : "secondary"} onClick={() => setPanel(panel === "reschedule" ? "none" : "reschedule")}>
                      Choose a new time
                    </Button>
                  )}
                  {b.canCancel && (
                    <Button variant="danger" onClick={() => setPanel("cancel")}>
                      Cancel booking
                    </Button>
                  )}
                </div>
                {panel === "reschedule" && (
                  <Reschedule
                    base={base}
                    booking={b}
                    onDone={(nb) => {
                      setBooking(nb);
                      setPanel("none");
                      setNotice(`Moved to ${formatDateTime(nb.startsAt, nb.timezone)}.`);
                    }}
                  />
                )}
              </>
            ) : (
              <p className="text-sm text-slate-700">
                {b.cancelDeadline ? `Changes closed at ${formatDateTime(b.cancelDeadline, tz)}. ` : ""}
                Call or message {b.business.name} if your plans change.
              </p>
            )}
          </section>
        )}

        {/* Review */}
        {b.review ? (
          <section className="rounded-panel border border-line bg-surface p-4 sm:p-5">
            <h2 className="font-bold">Your review</h2>
            <Stars value={b.review.rating} />
            {b.review.comment && <p className="mt-2 text-sm whitespace-pre-line text-slate-700">{b.review.comment}</p>}
          </section>
        ) : (
          b.canReview && <ReviewForm base={base} businessName={b.business.name} onDone={(nb) => setBooking(nb)} />
        )}

        {b.business.policies && (
          <section className="px-1">
            <h2 className="mb-1.5 font-bold">Booking policies</h2>
            <p className="text-sm whitespace-pre-line text-slate-700">{b.business.policies}</p>
          </section>
        )}

        <p className="text-center text-sm">
          <Link href={`/${b.business.slug}`} className="font-semibold text-brand underline underline-offset-2">
            Book another appointment
          </Link>
        </p>
      </div>

      <CancelModal
        open={panel === "cancel"}
        base={base}
        booking={b}
        onClose={() => setPanel("none")}
        onDone={(nb) => {
          setBooking(nb);
          setPanel("none");
          setNotice("Your booking is cancelled.");
        }}
      />
    </PublicFrame>
  );
}

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className={strong ? "font-semibold" : "text-slate-700"}>{label}</dt>
      <dd className={cx("tnum", strong ? "text-lg font-bold" : "font-medium")}>{value}</dd>
    </div>
  );
}

function PayDeposit({ base, booking }: { base: string; booking: PublicBookingDto }) {
  const [email, setEmail] = useState(storedEmail);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function pay(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await api<{ authorizationUrl: string }>(`${base}/pay`, { method: "POST", body: { email: email.trim() }, auth: false });
      window.location.assign(res.authorizationUrl);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={pay} className="space-y-3 pt-1">
      <Input
        label="Email for the receipt"
        type="email"
        autoComplete="email"
        required
        value={email}
        onChange={(e) => setEmail(e.target.value)}
      />
      <ErrorBox error={error} />
      <button
        type="submit"
        disabled={busy || !email.trim()}
        className="flex min-h-12 w-full items-center justify-center rounded-control bg-brand px-4 font-semibold text-on-brand disabled:opacity-60"
      >
        {busy ? "Opening payment…" : `Pay ${formatMoney(booking.depositDueMinor, booking.currency)} deposit`}
      </button>
    </form>
  );
}

function Reschedule({ base, booking, onDone }: { base: string; booking: PublicBookingDto; onDone: (b: PublicBookingDto) => void }) {
  const tz = booking.timezone;
  const { data: page, error: pageError, reload } = useApi<PublicBookingPageDto>(`/api/public/book/${encodeURIComponent(booking.business.slug)}`, {
    auth: false,
  });
  const [slot, setSlot] = useState<SlotDto | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  if (pageError) return <ErrorBox error={pageError} onRetry={reload} />;
  if (!page) return <PageLoader label="Loading times…" />;

  const service = page.services.find((s) => s.id === booking.service.id);
  const staff = page.staff.find((s) => s.id === booking.staff?.id) ?? null;
  if (!service) {
    return (
      <p className="text-sm text-slate-700">
        This service isn&apos;t bookable online any more. Call or message {booking.business.name} to move your booking.
      </p>
    );
  }

  const today = todayIn(tz);

  async function save() {
    if (!slot) return;
    setBusy(true);
    setError(null);
    try {
      const nb = await api<PublicBookingDto>(`${base}/reschedule`, {
        method: "POST",
        auth: false,
        body: { startsAt: slot.startsAt, staffId: staff?.id ?? slot.staffIds[0] ?? null },
      });
      onDone(nb);
    } catch (err) {
      const e = toApiError(err);
      if (e.status === 409) {
        setSlot(null);
        setRefreshKey((k) => k + 1);
      }
      setError(e.status === 409 ? toApiError(new Error("That time was just taken. Pick another from the updated list.")) : e);
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4 border-t border-line-soft pt-4">
      <DateSlotPicker
        source={{ kind: "public", slug: booking.business.slug, serviceId: service.id, staffId: staff?.id ?? null }}
        timezone={tz}
        today={today}
        maxDate={addDays(today, page.maxDaysAhead)}
        value={slot}
        onChange={setSlot}
        refreshKey={refreshKey}
        excludeStartsAt={booking.startsAt}
        showZoneNote
      />
      <ErrorBox error={error} />
      <Button className="w-full" disabled={!slot} loading={busy} onClick={save}>
        {slot ? `Move to ${formatDateTime(slot.startsAt, tz)}` : "Pick a new time"}
      </Button>
    </div>
  );
}

function CancelModal({
  open,
  base,
  booking,
  onClose,
  onDone,
}: {
  open: boolean;
  base: string;
  booking: PublicBookingDto;
  onClose: () => void;
  onDone: (b: PublicBookingDto) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function cancel() {
    setBusy(true);
    setError(null);
    try {
      onDone(await api<PublicBookingDto>(`${base}/cancel`, { method: "POST", auth: false }));
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open={open}
      title="Cancel this booking?"
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Keep booking
          </Button>
          <Button variant="danger" loading={busy} onClick={cancel}>
            Cancel booking
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm text-slate-700">
        <p>
          {booking.service.name} on {formatDateTime(booking.startsAt, booking.timezone)} will be released for someone else.
        </p>
        {booking.paidMinor > 0 && (
          <p>
            Deposits aren&apos;t refunded automatically. {booking.business.policies ? "The business's policy below explains what happens to yours." : `Ask ${booking.business.name} about yours.`}
          </p>
        )}
        {booking.paidMinor > 0 && booking.business.policies && (
          <p className="rounded-control bg-mist px-3 py-2 whitespace-pre-line">{booking.business.policies}</p>
        )}
        <ErrorBox error={error} />
      </div>
    </Modal>
  );
}

function Stars({ value }: { value: number }) {
  return (
    <p className="mt-1 flex gap-0.5 text-amber" aria-label={`${value} out of 5`}>
      {[1, 2, 3, 4, 5].map((i) => (
        <StarIcon key={i} filled={i <= value} className="h-5 w-5" />
      ))}
    </p>
  );
}

function ReviewForm({ base, businessName, onDone }: { base: string; businessName: string; onDone: (b: PublicBookingDto) => void }) {
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!rating) return;
    setBusy(true);
    setError(null);
    try {
      onDone(await api<PublicBookingDto>(`${base}/review`, { method: "POST", auth: false, body: { rating, comment: comment.trim() || null } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-panel border border-line bg-surface p-4 sm:p-5">
      <h2 className="font-bold">How was your visit to {businessName}?</h2>
      <fieldset>
        <legend className="sr-only">Rating out of 5</legend>
        <div className="flex gap-1">
          {[1, 2, 3, 4, 5].map((i) => (
            <label key={i} className="cursor-pointer">
              <input type="radio" name="rating" value={i} checked={rating === i} onChange={() => setRating(i)} className="peer sr-only" />
              <span className="flex h-11 w-11 items-center justify-center rounded-control text-amber peer-focus-visible:outline-3 peer-focus-visible:outline-rose">
                <StarIcon filled={i <= rating} className="h-7 w-7" />
                <span className="sr-only">
                  {i} star{i === 1 ? "" : "s"}
                </span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      <Textarea label="Tell others about it (optional)" value={comment} onChange={(e) => setComment(e.target.value)} rows={3} />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} disabled={!rating}>
        Post review
      </Button>
    </form>
  );
}

