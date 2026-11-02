"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { PAYMENT_METHOD_LABEL, PAYMENT_TYPE_LABEL, SOURCE_LABEL, STATUS_LABEL, isLive } from "@/lib/status";
import { addDays, cityOf, dateKey, formatDateTime, formatStamp, todayIn } from "@/lib/time";
import type { BookingDto, BookingStatus, PaymentMethod, PaymentType, SlotDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { staffForService, useBusiness, useServices, useStaff } from "@/lib/use-business";
import { useNow } from "@/lib/use-now";
import { AppointmentCard } from "@/components/appointment-card";
import { openWhatsAppReminder, setBookingStatus } from "@/components/booking-actions";
import { CopyButton } from "@/components/copy-button";
import { telHref, whatsappHref } from "@/components/public-parts";
import { DateSlotPicker } from "@/components/slot-picker";
import {
  ApiErrorView,
  Button,
  Card,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  StatusBadge,
  Textarea,
  buttonClass,
  cx,
} from "@/components/ui";
import { PhoneIcon, StarIcon, WhatsAppIcon } from "@/components/icons";

export default function BookingDetailPage() {
  const { id } = useParams<{ id: string }>();
  const auth = useAuth();
  const { tz, currency } = useBusiness();
  const { data: b, error, reload, setData } = useApi<BookingDto>(`/api/bookings/${id}`);
  const canEdit = useCan("bookings.edit");
  const canRefund = useCan("bookings.refund");
  const now = useNow();
  const [panel, setPanel] = useState<"none" | "reschedule" | "payment" | "refund" | "cancel">("none");
  const [busy, setBusy] = useState<BookingStatus | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

  if (!b) {
    return (
      <div>
        <PageHeader title="Booking" back="/app/bookings" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const started = !!now && new Date(b.startsAt).getTime() <= now;
  const live = isLive(b.status);
  // The server works out what Paystack can still send back (across every online payment).
  const refundable = b.refundableMinor;
  // The server rejects payments on cancelled or expired bookings.
  const canTakePayment = live || b.status === "Completed" || b.status === "NoShow";
  const balance = Math.max(0, b.priceMinor - b.paidMinor);

  function updated(nb: BookingDto, message?: string) {
    setData(nb);
    setPanel("none");
    if (message) setBanner(message);
  }

  async function changeStatus(status: BookingStatus, note?: string) {
    setBusy(status);
    setActionError(null);
    setBanner(null);
    try {
      updated(await setBookingStatus(b!.id, status, note), `Marked ${STATUS_LABEL[status].toLowerCase()}.`);
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="space-y-5">
      <PageHeader title={b.customerName} back="/app/bookings" subtitle={`Ref ${b.code}, ${SOURCE_LABEL[b.source].toLowerCase()} booking`} />
      {banner && <Notice tone="success">{banner}</Notice>}
      {actionError && <ApiErrorView error={actionError} />}

      <div className="grid gap-5 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)] lg:items-start">
        {/* The appointment card and what to do next */}
        <div className="space-y-4">
          <AppointmentCard
            businessName={auth?.tenant.name ?? "Your business"}
            startsAt={b.startsAt}
            timezone={tz}
            serviceName={b.serviceName}
            durationMinutes={Math.round((new Date(b.endsAt).getTime() - new Date(b.startsAt).getTime()) / 60000)}
            staffName={b.staffName}
            status={b.status}
            accent="#C2255C"
            statusSlot={
              b.status === "Pending" && b.holdExpiresAt ? (
                <p className="flex items-center gap-2 text-sm font-semibold text-amber-text">
                  <span className="h-2 w-2 rounded-full bg-amber" aria-hidden="true" />
                  Pending deposit, held until {formatDateTime(b.holdExpiresAt, tz).split(", ")[1]}
                </p>
              ) : undefined
            }
          />
          <p className="text-center text-xs text-muted">Times are in {cityOf(tz)} time.</p>

          {canEdit && (
            <Card className="space-y-3">
              <SectionTitle>Status</SectionTitle>
              <div className="flex flex-wrap gap-2">
                {b.status === "Pending" && (
                  <Button loading={busy === "Confirmed"} onClick={() => changeStatus("Confirmed")}>
                    Confirm without deposit
                  </Button>
                )}
                {b.status === "Confirmed" && (
                  <>
                    <Button loading={busy === "Completed"} onClick={() => changeStatus("Completed")} disabled={!started}>
                      Completed
                    </Button>
                    <Button variant="danger" loading={busy === "NoShow"} onClick={() => changeStatus("NoShow")} disabled={!started}>
                      No-show
                    </Button>
                  </>
                )}
                {(b.status === "NoShow" || b.status === "Completed") && (
                  <Button variant="secondary" loading={busy === "Confirmed"} onClick={() => changeStatus("Confirmed")}>
                    Undo, back to confirmed
                  </Button>
                )}
                {live && (
                  <Button variant="danger" onClick={() => setPanel("cancel")}>
                    Cancel booking
                  </Button>
                )}
              </div>
              {b.status === "Confirmed" && !started && <p className="text-xs text-muted">Completed and no-show open once the appointment starts.</p>}
              {live && (
                <div className="flex flex-wrap gap-2 border-t border-line-soft pt-3">
                  <Button variant="secondary" onClick={() => setPanel(panel === "reschedule" ? "none" : "reschedule")}>
                    Reschedule
                  </Button>
                  {(b.customer.phone || b.whatsappReminderUrl) && (
                    <Button variant="secondary" onClick={() => openWhatsAppReminder(b)}>
                      <WhatsAppIcon className="h-4 w-4 text-mint" /> Remind on WhatsApp
                    </Button>
                  )}
                </div>
              )}
            </Card>
          )}
        </div>

        <div className="space-y-4">
          {panel === "reschedule" && <ReschedulePanel booking={b} tz={tz} onClose={() => setPanel("none")} onDone={(nb) => updated(nb, `Moved to ${formatDateTime(nb.startsAt, tz)}.`)} />}

          {/* Customer */}
          <Card>
            <SectionTitle
              action={
                <Link href={`/app/customers/${b.customer.id}`} className="text-sm font-semibold text-rose underline-offset-2 hover:underline">
                  Customer history
                </Link>
              }
            >
              Customer
            </SectionTitle>
            <p className="font-semibold">{b.customer.name}</p>
            <p className="text-sm text-muted">{[b.customer.phone, b.customer.email].filter(Boolean).join(", ")}</p>
            {b.customer.noShowCount > 0 && (
              <p className="mt-2 inline-block rounded-control bg-brick-wash px-2.5 py-1 text-sm text-brick-dark">
                {b.customer.noShowCount} no-show{b.customer.noShowCount === 1 ? "" : "s"} before
              </p>
            )}
            {b.customer.phone && (
              <div className="mt-3 flex flex-wrap gap-2">
                <a href={telHref(b.customer.phone)} className={buttonClass("secondary", "min-h-9 px-3 py-1.5")}>
                  <PhoneIcon className="h-4 w-4" /> Call
                </a>
                <a href={whatsappHref(b.customer.phone)} target="_blank" rel="noopener noreferrer" className={buttonClass("secondary", "min-h-9 px-3 py-1.5")}>
                  <WhatsAppIcon className="h-4 w-4 text-mint" /> WhatsApp
                </a>
              </div>
            )}
            <NotesEditor booking={b} canEdit={canEdit} onSaved={(nb) => updated(nb, "Notes saved.")} />
          </Card>

          {/* Money */}
          <Card>
            <SectionTitle
              action={
                canEdit && (
                  <div className="flex gap-2">
                    {canRefund && refundable > 0 && (
                      <Button variant="ghost" className="min-h-9 px-3 py-1.5" onClick={() => setPanel("refund")}>
                        Refund deposit
                      </Button>
                    )}
                    {canTakePayment && (
                      <Button variant="secondary" className="min-h-9 px-3 py-1.5" onClick={() => setPanel("payment")}>
                        Record payment
                      </Button>
                    )}
                  </div>
                )
              }
            >
              Payments
            </SectionTitle>
            <dl className="grid grid-cols-3 gap-3 text-sm">
              <div>
                <dt className="text-muted">Price</dt>
                <dd className="font-semibold tnum">{formatMoney(b.priceMinor, currency)}</dd>
              </div>
              <div>
                <dt className="text-muted">Paid</dt>
                <dd className="font-semibold text-mint-dark tnum">{formatMoney(b.paidMinor, currency)}</dd>
              </div>
              <div>
                <dt className="text-muted">Balance</dt>
                <dd className="font-semibold tnum">{formatMoney(balance, currency)}</dd>
              </div>
            </dl>
            {b.depositMinor > 0 && <p className="mt-2 text-xs text-muted">Deposit for this service: {formatMoney(b.depositMinor, currency)}.</p>}
            {b.payments.length > 0 && (
              <ul className="mt-4 divide-y divide-line-soft border-t border-line-soft">
                {b.payments.map((p) => (
                  <li key={p.id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
                    <span className="min-w-0">
                      <span className="block font-medium">
                        {PAYMENT_TYPE_LABEL[p.type]}, {PAYMENT_METHOD_LABEL[p.method].toLowerCase()}
                      </span>
                      <span className="block text-xs text-muted">
                        {formatStamp(p.createdAt, tz)}
                        {p.reference ? `, ref ${p.reference}` : ""}
                      </span>
                    </span>
                    <span className={cx("font-semibold tnum", p.type === "Refund" && "text-brick")}>
                      {p.type === "Refund" ? "−" : ""}
                      {formatMoney(p.amountMinor, currency)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          {/* Review */}
          {b.review && (
            <Card>
              <SectionTitle>Review</SectionTitle>
              <p className="flex gap-0.5 text-amber" aria-label={`${b.review.rating} out of 5`}>
                {[1, 2, 3, 4, 5].map((i) => (
                  <StarIcon key={i} filled={i <= b.review!.rating} className="h-5 w-5" />
                ))}
              </p>
              {b.review.comment && <p className="mt-2 text-sm whitespace-pre-line">{b.review.comment}</p>}
              <p className="mt-1 text-xs text-muted">{formatStamp(b.review.createdAt, tz)}</p>
            </Card>
          )}

          {/* Reminders */}
          <Card>
            <SectionTitle>Reminders</SectionTitle>
            {b.reminders.length === 0 ? (
              <p className="text-sm text-muted">
                No automatic reminders for this booking. Email reminders go out when the customer gave an email and your plan includes them.
              </p>
            ) : (
              <ul className="divide-y divide-line-soft text-sm">
                {b.reminders.map((r, i) => (
                  <li key={i} className="flex items-center justify-between gap-3 py-2">
                    <span>
                      {r.channel} reminder
                      <span className="block text-xs text-muted">
                        {r.sentAt ? `Sent ${formatStamp(r.sentAt, tz)}` : `Due ${formatStamp(r.sendAt, tz)}`}
                      </span>
                    </span>
                    <StatusBadge tone={r.sentAt ? "green" : /fail/i.test(r.status) ? "red" : "neutral"}>{r.status}</StatusBadge>
                  </li>
                ))}
              </ul>
            )}
            {b.manageUrl && (
              <div className="mt-4 border-t border-line-soft pt-3">
                <p className="text-sm text-slate-700">The customer&apos;s own link to view, move or cancel this booking:</p>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <code className="min-w-0 flex-1 truncate rounded-control bg-mist px-3 py-2 text-xs">{b.manageUrl}</code>
                  <CopyButton text={b.manageUrl} label="Copy link" />
                </div>
              </div>
            )}
          </Card>

          {/* History */}
          <Card>
            <SectionTitle>History</SectionTitle>
            <ol className="relative space-y-4 border-l-2 border-line pl-5">
              {[...b.history]
                .sort((x, y) => x.createdAt.localeCompare(y.createdAt))
                .map((h, i) => (
                  <li key={i} className="relative">
                    <span className="absolute top-1.5 -left-[1.6rem] h-2.5 w-2.5 rounded-full border-2 border-surface bg-rose" aria-hidden="true" />
                    <p className="text-sm font-semibold">{STATUS_LABEL[h.status] ?? h.status}</p>
                    {h.note && <p className="text-sm text-slate-700">{h.note}</p>}
                    <p className="text-xs text-muted">{formatStamp(h.createdAt, tz)}</p>
                  </li>
                ))}
            </ol>
          </Card>
        </div>
      </div>

      <Modal open={panel === "payment"} title="Record a payment" onClose={() => setPanel("none")}>
        {panel === "payment" && (
          <PaymentForm booking={b} balance={balance} currency={currency} onCancel={() => setPanel("none")} onDone={(nb) => updated(nb, "Payment recorded.")} />
        )}
      </Modal>
      <Modal open={panel === "refund"} title="Refund the online deposit" onClose={() => setPanel("none")}>
        {panel === "refund" && (
          <RefundForm booking={b} max={refundable} currency={currency} onCancel={() => setPanel("none")} onDone={(nb) => updated(nb, "Refund sent through Paystack.")} />
        )}
      </Modal>
      <Modal open={panel === "cancel"} title="Cancel this booking?" onClose={() => setPanel("none")}>
        {panel === "cancel" && (
          <CancelForm
            busy={busy === "Cancelled"}
            online={b.refundableMinor > 0}
            offline={b.paidMinor - b.refundableMinor > 0}
            onCancel={() => setPanel("none")}
            onConfirm={(note) => changeStatus("Cancelled", note)}
          />
        )}
      </Modal>
    </div>
  );
}

function NotesEditor({ booking, canEdit, onSaved }: { booking: BookingDto; canEdit: boolean; onSaved: (b: BookingDto) => void }) {
  const [editing, setEditing] = useState(false);
  const [notes, setNotes] = useState(booking.notes ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      onSaved(await api<BookingDto>(`/api/bookings/${booking.id}`, { method: "PATCH", body: { notes: notes.trim() || null } }));
      setEditing(false);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  if (!editing) {
    return (
      <div className="mt-4 border-t border-line-soft pt-3">
        <div className="flex items-center justify-between gap-3">
          <p className="text-sm font-medium text-slate-700">Booking notes</p>
          {canEdit && (
            <button type="button" className="text-sm font-semibold text-rose underline-offset-2 hover:underline" onClick={() => setEditing(true)}>
              {booking.notes ? "Edit" : "Add notes"}
            </button>
          )}
        </div>
        <p className="mt-1 text-sm whitespace-pre-line text-slate-700">{booking.notes || <span className="text-muted">No notes.</span>}</p>
      </div>
    );
  }
  return (
    <div className="mt-4 space-y-2 border-t border-line-soft pt-3">
      <Textarea label="Booking notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} autoFocus />
      <ErrorBox error={error} />
      <div className="flex gap-2">
        <Button loading={busy} onClick={save}>
          Save notes
        </Button>
        <Button variant="ghost" onClick={() => setEditing(false)}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

function ReschedulePanel({ booking, tz, onClose, onDone }: { booking: BookingDto; tz: string; onClose: () => void; onDone: (b: BookingDto) => void }) {
  const { services } = useServices();
  const { staff } = useStaff();
  const service = services.find((s) => s.id === booking.serviceId);
  const eligible = staffForService(staff, service);
  const [staffId, setStaffId] = useState(booking.staffId);
  const [slot, setSlot] = useState<SlotDto | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const today = todayIn(tz);
  const current = dateKey(booking.startsAt, tz);

  async function save() {
    if (!slot) return;
    setBusy(true);
    setError(null);
    try {
      const target = staffId || slot.staffIds[0] || booking.staffId;
      onDone(await api<BookingDto>(`/api/bookings/${booking.id}`, { method: "PATCH", body: { startsAt: slot.startsAt, staffId: target } }));
    } catch (e) {
      const err = toApiError(e);
      if (err.status === 409) {
        setSlot(null);
        setRefreshKey((k) => k + 1);
      }
      setError(err);
      setBusy(false);
    }
  }

  return (
    <Card className="space-y-4">
      <SectionTitle
        action={
          <Button variant="ghost" className="min-h-8 px-2 py-1" onClick={onClose}>
            Close
          </Button>
        }
      >
        Move to a new time
      </SectionTitle>
      <Select label="With" value={staffId} onChange={(e) => setStaffId(e.target.value)} className="sm:max-w-xs">
        {(eligible.length ? eligible : staff).map((s) => (
          <option key={s.id} value={s.id}>
            {s.displayName}
          </option>
        ))}
      </Select>
      <DateSlotPicker
        key={staffId}
        source={{ kind: "app", serviceId: booking.serviceId, staffId: staffId || null }}
        timezone={tz}
        today={today}
        maxDate={addDays(today, 365)}
        initialDate={current >= today ? current : today}
        value={slot}
        onChange={setSlot}
        refreshKey={refreshKey}
        excludeStartsAt={booking.startsAt}
        showZoneNote
      />
      <ErrorBox error={error} />
      <Button disabled={!slot} loading={busy} onClick={save}>
        {slot ? `Move to ${formatDateTime(slot.startsAt, tz)}` : "Pick a new time"}
      </Button>
    </Card>
  );
}

function PaymentForm({
  booking,
  balance,
  currency,
  onCancel,
  onDone,
}: {
  booking: BookingDto;
  balance: number;
  currency: string;
  onCancel: () => void;
  onDone: (b: BookingDto) => void;
}) {
  const depositOwed = booking.paidMinor < booking.depositMinor;
  const [type, setType] = useState<Exclude<PaymentType, "Refund">>(depositOwed ? "Deposit" : "Balance");
  const [amount, setAmount] = useState(toMajorInput(depositOwed ? booking.depositMinor - booking.paidMinor : balance));
  const [method, setMethod] = useState<Exclude<PaymentMethod, "Online">>("Cash");
  const [reference, setReference] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const minor = toMinor(amount);
    if (!minor || minor <= 0) return setError(toApiError(new Error("Enter the amount received.")));
    setBusy(true);
    setError(null);
    try {
      onDone(
        await api<BookingDto>(`/api/bookings/${booking.id}/payments`, {
          method: "POST",
          body: { amountMinor: minor, type, method, reference: reference.trim() || null },
        }),
      );
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="grid grid-cols-2 gap-3">
        <Select
          label="For"
          value={type}
          onChange={(e) => {
            const t = e.target.value as "Deposit" | "Balance";
            setType(t);
            setAmount(toMajorInput(t === "Deposit" ? Math.max(0, booking.depositMinor - booking.paidMinor) : balance));
          }}
        >
          <option value="Deposit">Deposit</option>
          <option value="Balance">Balance</option>
        </Select>
        <Input label={`Amount (${currency})`} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} required />
      </div>
      <Select label="Paid by" value={method} onChange={(e) => setMethod(e.target.value as "Cash" | "Transfer" | "Card")}>
        <option value="Cash">{PAYMENT_METHOD_LABEL.Cash}</option>
        <option value="Transfer">{PAYMENT_METHOD_LABEL.Transfer}</option>
        <option value="Card">{PAYMENT_METHOD_LABEL.Card}</option>
      </Select>
      <Input label="Reference (optional)" value={reference} onChange={(e) => setReference(e.target.value)} hint="Transfer or POS reference, to match your statement." />
      <p className="text-sm text-muted">Balance before this payment: {formatMoney(balance, currency)}.</p>
      <ErrorBox error={error} />
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          Record payment
        </Button>
      </div>
    </form>
  );
}

function RefundForm({
  booking,
  max,
  currency,
  onCancel,
  onDone,
}: {
  booking: BookingDto;
  max: number;
  currency: string;
  onCancel: () => void;
  onDone: (b: BookingDto) => void;
}) {
  const [amount, setAmount] = useState(toMajorInput(max));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const minor = toMinor(amount);
    if (!minor || minor <= 0 || minor > max) return setError(toApiError(new Error(`Enter an amount up to ${formatMoney(max, currency)}.`)));
    setBusy(true);
    setError(null);
    try {
      onDone(await api<BookingDto>(`/api/bookings/${booking.id}/refund`, { method: "POST", body: { amountMinor: minor } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <p className="text-sm text-slate-700">
        The money goes back to {booking.customer.name}&apos;s card through your Paystack account. Up to {formatMoney(max, currency)} paid online can still be refunded.
      </p>
      <Input label={`Amount (${currency})`} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} required />
      <ErrorBox error={error} />
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Keep the deposit
        </Button>
        <Button type="submit" variant="danger" loading={busy}>
          Refund {toMinor(amount) ? formatMoney(toMinor(amount), currency) : ""}
        </Button>
      </div>
    </form>
  );
}

function CancelForm({
  busy,
  online,
  offline,
  onCancel,
  onConfirm,
}: {
  busy: boolean;
  online: boolean;
  offline: boolean;
  onCancel: () => void;
  onConfirm: (note: string) => void;
}) {
  const [note, setNote] = useState("");
  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-700">The time is released straight away and can be booked by someone else.</p>
      {(online || offline) && (
        <Notice tone="warning">
          Cancelling doesn&apos;t refund any payment.
          {online && " The online deposit can be returned with Refund deposit afterwards."}
          {offline && " Cash, transfer and card payments are refunded outside BookLink."}
        </Notice>
      )}
      <Textarea label="Reason (optional)" value={note} onChange={(e) => setNote(e.target.value)} rows={2} placeholder="e.g. Customer called to cancel" />
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Keep booking
        </Button>
        <Button variant="danger" loading={busy} onClick={() => onConfirm(note.trim())}>
          Cancel booking
        </Button>
      </div>
    </div>
  );
}
