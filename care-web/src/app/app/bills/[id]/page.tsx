"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, downloadFile } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { stageLabel } from "@/lib/clinical";
import { formatDay, formatStamp, koboInput, money, toKobo } from "@/lib/format";
import { ADJUSTMENT_KINDS, BILL_STATUS, DESK_METHODS, PAYMENT_STATUS, methodLabel, newIdempotencyKey } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import { canTakeMoney } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { PriceItemDto } from "@/lib/types";
import type { AdjustmentKind, BillDto, BillLineDto, CarePaymentDto, CareReversalResult } from "@/lib/types/billing";
import type { VisitDto } from "@/lib/types/clinical";
import type { PayMethod } from "@/lib/types/inpatient";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  Textarea,
  Wristband,
  cx,
} from "@/components/ui";

/**
 * One bill (the cashier and leadership; the support view reads). Lines (price-item names only: the clinical reference the API sends to
 * clinical callers is never shown), adjustments (leadership), payments with receipts and reversals, the desk payment (an Idempotency-Key
 * header, so a retry after a lost answer records it once), the review flag and, for a paid visit waiting to pay, closing the visit.
 */
export default function BillPage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const allowed = canTakeMoney(access);
  const bill = useApi<BillDto>(allowed ? `/api/care/bills/${encodeURIComponent(id)}` : null);
  const visit = useApi<VisitDto>(allowed && bill.data?.visitId ? `/api/care/visits/${bill.data.visitId}` : null);
  const [message, setMessage] = useState<string | null>(null);
  const reviewed = useAction();
  const close = useAction();

  if (!allowed) return <NoAccess text="Bills are for the cashier, admins and the medical director." />;
  if (bill.error && !bill.data)
    return (
      <div className="space-y-4">
        <PageHeader title="Bill" back="/app/bills" />
        <ApiErrorView error={bill.error} onRetry={bill.reload} />
      </div>
    );
  if (!bill.data) return <PageLoader />;
  const b = bill.data;
  const write = !access.readOnly;
  const final = !!b.finalisedAt;
  const s = BILL_STATUS[b.status];

  function updated(next: BillDto, note?: string) {
    bill.setData(next);
    setMessage(note ?? null);
  }
  const refresh = (note?: string) => {
    bill.reload();
    visit.reload();
    setMessage(note ?? null);
  };

  async function markReviewed() {
    const res = await reviewed.run(() => api<BillDto>(`/api/care/bills/${b.id}/reviewed`, { method: "POST" }));
    if (res.ok) updated(res.data, "Marked as reviewed.");
  }
  async function closeVisit() {
    if (!visit.data || !window.confirm(`Close ${b.patientName}'s visit? It leaves the queue as done.`)) return;
    const res = await close.run(() => api<VisitDto>(`/api/care/visits/${visit.data!.id}/stage`, { method: "POST", body: { stage: "Done" } }));
    if (res.ok) {
      visit.setData(res.data);
      setMessage("The visit is closed.");
    }
  }

  return (
    <div className="space-y-5">
      <PageHeader
        back="/app/bills"
        title={
          <span className="tnum flex flex-wrap items-center gap-3">
            {b.number}
            <Badge tone={s.tone}>{s.label}</Badge>
            {final && <Badge>Final</Badge>}
          </span>
        }
        subtitle={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Wristband number={b.patientNumber} />
            <Link href={`/app/patients/${b.patientId}`} className="font-bold text-ink hover:underline">
              {b.patientName}
            </Link>
            <span>
              {b.admissionId ? "Admission" : "Visit"} bill, opened {formatStamp(b.createdAt)}
              {b.finalisedAt && `, final since ${formatStamp(b.finalisedAt)}`}
            </span>
          </span>
        }
      />
      {message && <Notice tone="success">{message}</Notice>}

      <dl className="tnum grid grid-cols-2 gap-2 sm:grid-cols-5">
        {(
          [
            ["Total", money(b.totalMinor)],
            ["Discounts and waivers", b.adjustedMinor ? `−${money(b.adjustedMinor)}` : money(0)],
            ["Paid", money(b.paidMinor)],
            ["Balance", money(b.balanceMinor)],
            ["Credit held", money(b.creditMinor)],
          ] as [string, string][]
        ).map(([label, value]) => (
          <div
            key={label}
            className={cx(
              "rounded-control border bg-surface px-3 py-2",
              label === "Balance" && b.balanceMinor > 0 ? "border-arterial/50" : label === "Credit held" && b.creditMinor > 0 ? "border-scrub-line" : "border-rule",
            )}
          >
            <dt className="text-xs text-muted">{label}</dt>
            <dd className="display text-lg font-bold text-ink">{value}</dd>
          </div>
        ))}
      </dl>
      {b.status === "InCredit" && (
        <Notice>
          This bill is in credit: {money(b.creditMinor)} has been paid beyond what is owed. It stays on the bill (a later charge uses it) until you reverse a payment.
        </Notice>
      )}
      {b.needsReview && (
        <Notice tone="error">
          <p className="font-bold">This bill needs review.</p>
          <p>An online payment came in for more than was owed, or for a different amount than the checkout. Check it with Paystack and the patient, then mark it reviewed.</p>
          {write && (
            <Button variant="secondary" className="mt-2" loading={reviewed.busy} onClick={markReviewed}>
              Mark as reviewed
            </Button>
          )}
          <ErrorBox error={reviewed.error} />
        </Notice>
      )}

      {b.visitId && visit.data && (
        <Card className="flex flex-wrap items-center gap-3 text-sm">
          <span>
            Visit: <span className="font-bold text-ink">{stageLabel(visit.data.stage)}</span>
          </span>
          {visit.data.stage === "WaitingPayment" && write && (
            <>
              {b.balanceMinor > 0 && <span className="text-muted">Take the payment, then close the visit.</span>}
              <Button className="ml-auto" variant={b.balanceMinor > 0 ? "secondary" : "primary"} loading={close.busy} onClick={closeVisit}>
                Close the visit
              </Button>
            </>
          )}
          <div className="w-full">
            <ErrorBox error={close.error} />
          </div>
        </Card>
      )}

      {write && b.balanceMinor > 0 && <TakePayment bill={b} onPaid={(p) => refresh(`Payment of ${money(p.amountMinor)} received, receipt ${p.receiptNumber}.`)} />}

      <Lines bill={b} canAdd={write && !final} canRemove={write} onChanged={updated} final={final} categoriesOnly={access.readOnly || (!access.isLeadership && !access.isClinical)} />
      <Payments bill={b} canEdit={write} onChanged={refresh} />
      <Adjustments bill={b} canAdjust={write && access.isLeadership} onChanged={updated} />
    </div>
  );
}

// ---------- Desk payment ----------

function TakePayment({ bill, onPaid }: { bill: BillDto; onPaid: (p: CarePaymentDto) => void }) {
  const [amount, setAmount] = useState(koboInput(bill.balanceMinor));
  // Fix round 1 (I1): while the cashier hasn't typed an amount, it follows the balance (a removed line, a discount, a payment), so a
  // stale prefill can't overpay the bill into credit.
  const [touched, setTouched] = useState(false);
  const [prefilledFor, setPrefilledFor] = useState(bill.balanceMinor);
  const [method, setMethod] = useState<PayMethod>("Cash");
  const [reference, setReference] = useState("");
  const [amountError, setAmountError] = useState<string | null>(null);
  // One key per payment the cashier means to take: a retry of the same submission reuses it, so the API records the payment once.
  const [key, setKey] = useState(newIdempotencyKey);
  const [last, setLast] = useState<CarePaymentDto | null>(null);
  const { busy, error, run } = useAction();
  const receipt = useAction();

  // The balance changed since the prefill (adjusting state during render, React's pattern for props that change): an untouched amount
  // follows it, with a fresh key since it is now a different payment.
  if (prefilledFor !== bill.balanceMinor) {
    setPrefilledFor(bill.balanceMinor);
    if (!touched) {
      setAmount(koboInput(bill.balanceMinor));
      setKey(newIdempotencyKey());
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const kobo = toKobo(amount);
    if (kobo === null || kobo <= 0) {
      setAmountError("Enter the amount in naira, such as 1500.");
      return;
    }
    setAmountError(null);
    // Fix round 1 (I1): more than the balance becomes credit on the bill, so it is never taken without saying so.
    if (
      kobo > bill.balanceMinor &&
      !window.confirm(
        `This is ${money(kobo - bill.balanceMinor)} more than the balance of ${money(bill.balanceMinor)}. The extra ${money(kobo - bill.balanceMinor)} is kept as credit on the bill. Record ${money(kobo)}?`,
      )
    )
      return;
    const res = await run(() =>
      api<CarePaymentDto>(`/api/care/bills/${bill.id}/payments`, {
        method: "POST",
        body: { amountMinor: kobo, method, reference: reference.trim() || null },
        headers: { "Idempotency-Key": key },
      }),
    );
    if (res.ok) {
      setLast(res.data);
      setKey(newIdempotencyKey());
      setReference("");
      setTouched(false);
      onPaid(res.data);
    } else if (res.error.code === "idempotency_key_reused") setKey(newIdempotencyKey());
  }

  return (
    <Card className="space-y-3">
      <SectionTitle>Take a payment</SectionTitle>
      <form onSubmit={submit} className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_10rem_minmax(0,1fr)_auto] sm:items-end">
        <Input
          label="Amount (naira)"
          inputMode="decimal"
          value={amount}
          onChange={(e) => {
            setAmount(e.target.value);
            setTouched(true);
            setKey(newIdempotencyKey());
          }}
          error={amountError ?? error?.field("amountMinor")}
        />
        <Select
          label="Method"
          value={method}
          onChange={(e) => {
            setMethod(e.target.value as PayMethod);
            setKey(newIdempotencyKey());
          }}
          error={error?.field("method")}
        >
          {DESK_METHODS.map((m) => (
            <option key={m.value} value={m.value}>
              {m.label}
            </option>
          ))}
        </Select>
        <Input label="Reference" maxLength={100} value={reference} onChange={(e) => setReference(e.target.value)} error={error?.field("reference")} hint="POS slip or transfer reference." />
        <Button type="submit" loading={busy}>
          Record payment
        </Button>
      </form>
      <p className="text-xs text-muted">More than the balance is kept as credit on the bill.</p>
      <ErrorBox error={error && !["amountMinor", "method", "reference"].some((f) => error.field(f)) ? error : null} />
      {last?.receiptNumber && (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="secondary"
            loading={receipt.busy}
            onClick={() => receipt.run(() => downloadFile(`/api/care/payments/${last.id}/receipt.pdf`, `${last.receiptNumber}.pdf`))}
          >
            Print receipt {last.receiptNumber}
          </Button>
          <ErrorBox error={receipt.error} />
        </div>
      )}
    </Card>
  );
}

// ---------- Lines ----------

/**
 * `categoriesOnly`: final review I2 (user decision). The cashier, front desk and support view get each line as its price category ("Drug",
 * "Test") and amount from the API, never the item name; clinical staff and leadership get the names.
 */
function Lines({
  bill,
  canAdd,
  canRemove,
  final,
  categoriesOnly,
  onChanged,
}: {
  bill: BillDto;
  canAdd: boolean;
  canRemove: boolean;
  final: boolean;
  categoriesOnly: boolean;
  onChanged: (b: BillDto) => void;
}) {
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<BillLineDto | null>(null);
  return (
    <Card className="space-y-3">
      <SectionTitle
        action={
          canAdd &&
          !adding && (
            <Button variant="secondary" onClick={() => setAdding(true)}>
              Add a line
            </Button>
          )
        }
      >
        Lines
      </SectionTitle>
      {final && <p className="text-sm text-muted">This bill is final: no new charges. Lines can still be removed and payments taken.</p>}
      {categoriesOnly && bill.lines.length > 0 && <p className="text-sm text-muted">Lines show their category and amount. Item names are for clinical staff and leadership.</p>}
      {adding && (
        <AddLine
          bill={bill}
          onCancel={() => setAdding(false)}
          onAdded={(b) => {
            setAdding(false);
            onChanged(b);
          }}
        />
      )}
      {bill.lines.length === 0 ? (
        <p className="text-sm text-muted">No charges yet.</p>
      ) : (
        <ul className="divide-y divide-rule-soft text-sm">
          {bill.lines.map((l) => {
            const removed = !!l.removedAt;
            return (
              <li key={l.id} className={cx("flex flex-wrap items-start gap-x-3 gap-y-1 py-2", removed && "text-muted")}>
                <div className="min-w-0 flex-1">
                  <p className={cx("font-bold", removed ? "line-through" : "text-ink")}>
                    {l.chargeDay ? `${l.description}, ${formatDay(l.chargeDay)}` : l.description}
                  </p>
                  <p className="tnum text-xs text-muted">
                    {l.quantity} × {money(l.unitPriceMinor)}, {formatStamp(l.createdAt)}
                    {l.source === "Manual" && l.reason && `. Added: ${l.reason}`}
                  </p>
                  {removed && (
                    <p className="text-xs">
                      Removed {formatStamp(l.removedAt)}: {l.removedReason}
                    </p>
                  )}
                </div>
                <span className={cx("tnum font-bold", removed ? "line-through" : "text-ink")}>{money(l.amountMinor)}</span>
                {!removed && canRemove && (
                  <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => setRemoving(l)}>
                    Remove
                  </Button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {removing && (
        <ReasonDialog
          title="Remove this line"
          intro={`${removing.description}, ${money(removing.amountMinor)}`}
          label="Reason for removing it"
          max={300}
          action="Remove the line"
          onClose={() => setRemoving(null)}
          send={(reason) => api<BillDto>(`/api/care/bills/${bill.id}/lines/${removing.id}/remove`, { method: "POST", body: { reason } })}
          onDone={(b) => {
            setRemoving(null);
            onChanged(b);
          }}
        />
      )}
    </Card>
  );
}


function AddLine({ bill, onCancel, onAdded }: { bill: BillDto; onCancel: () => void; onAdded: (b: BillDto) => void }) {
  const items = useApi<PriceItemDto[]>(`/api/care/price-list${qs({ active: true })}`);
  const [itemId, setItemId] = useState("");
  const [qty, setQty] = useState("1");
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() =>
      api<BillDto>(`/api/care/bills/${bill.id}/lines`, { method: "POST", body: { priceItemId: itemId || null, quantity: Number(qty) || null, reason: reason.trim() } }),
    );
    if (res.ok) onAdded(res.data);
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3">
      <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_7rem]">
        <Select label="Item" value={itemId} onChange={(e) => setItemId(e.target.value)} error={error?.field("priceItemId")}>
          <option value="">Choose from the price list</option>
          {(items.data ?? []).map((i) => (
            <option key={i.id} value={i.id}>
              {i.name} ({money(i.priceMinor)})
            </option>
          ))}
        </Select>
        <Input label="Quantity" type="number" min={1} max={10000} value={qty} onChange={(e) => setQty(e.target.value)} error={error?.field("quantity")} />
      </div>
      <Input label="Reason" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} />
      <ErrorBox error={error && !["priceItemId", "quantity", "reason"].some((f) => error.field(f)) ? error : null} />
      <div className="flex gap-2">
        <Button type="submit" loading={busy} disabled={!itemId || !reason.trim()}>
          Add the line
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

// ---------- Payments ----------

function Payments({ bill, canEdit, onChanged }: { bill: BillDto; canEdit: boolean; onChanged: (note?: string) => void }) {
  const [reversing, setReversing] = useState<CarePaymentDto | null>(null);
  const receipt = useAction();
  const verify = useAction();

  async function check(p: CarePaymentDto) {
    if (!p.reference) return;
    const res = await verify.run(() => api(`/api/care/payments/online/${encodeURIComponent(p.reference!)}/verify`, { method: "POST" }));
    if (res.ok) onChanged("Checked with Paystack.");
  }

  return (
    <Card className="space-y-3">
      <SectionTitle>Payments</SectionTitle>
      {bill.payments.length === 0 ? (
        <p className="text-sm text-muted">No payments yet.</p>
      ) : (
        <ul className="divide-y divide-rule-soft text-sm">
          {bill.payments.map((p) => {
            const st = PAYMENT_STATUS[p.status];
            return (
              <li key={p.id} className="flex flex-wrap items-start gap-x-3 gap-y-1 py-2">
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2">
                    <span className={cx("tnum font-bold", p.status === "Reversed" ? "text-muted line-through" : "text-ink")}>{money(p.amountMinor)}</span>
                    <span className="text-slate-700">{methodLabel(p.method)}</span>
                    <Badge tone={st.tone}>{st.label}</Badge>
                    {p.isDeposit && <Badge tone="scrub">Deposit</Badge>}
                  </p>
                  <p className="tnum text-xs text-muted">
                    {p.status === "Pending" ? "Checkout started" : "Paid"} {formatStamp(p.paidAt)}
                    {p.receiptNumber && `, receipt ${p.receiptNumber}`}
                    {p.reference && `, ref ${p.reference}`}
                  </p>
                  {p.reversedAt && (
                    <p className="text-xs text-iodine">
                      Reversed {formatStamp(p.reversedAt)}
                      {p.reversalReason && `: ${p.reversalReason}`}
                    </p>
                  )}
                </div>
                <span className="flex flex-wrap gap-1">
                  {p.receiptNumber && (p.status === "Succeeded" || p.status === "Reversed") && (
                    <Button
                      variant="ghost"
                      className="min-h-9 px-2.5"
                      loading={receipt.busy}
                      onClick={() => receipt.run(() => downloadFile(`/api/care/payments/${p.id}/receipt.pdf`, `${p.receiptNumber}.pdf`))}
                    >
                      Receipt
                    </Button>
                  )}
                  {canEdit && p.status === "Succeeded" && (
                    <Button variant="ghost" className="min-h-9 px-2.5 text-arterial" onClick={() => setReversing(p)}>
                      Reverse
                    </Button>
                  )}
                  {canEdit && p.status === "Pending" && p.method === "Online" && p.reference && (
                    <Button variant="ghost" className="min-h-9 px-2.5" loading={verify.busy} onClick={() => check(p)}>
                      Check with Paystack
                    </Button>
                  )}
                </span>
              </li>
            );
          })}
        </ul>
      )}
      <ErrorBox error={receipt.error} />
      <ErrorBox error={verify.error} />
      {reversing && (
        <ReasonDialog
          title="Reverse this payment"
          intro={`${money(reversing.amountMinor)} by ${methodLabel(reversing.method).toLowerCase()}${reversing.receiptNumber ? `, receipt ${reversing.receiptNumber}` : ""}. The bill's balance goes back up.`}
          label="Reason for the reversal"
          max={300}
          min={5}
          action="Reverse the payment"
          danger
          onClose={() => setReversing(null)}
          send={(reason) => api<CareReversalResult>(`/api/care/payments/${reversing.id}/reverse`, { method: "POST", body: { reason } })}
          onDone={(r) => {
            setReversing(null);
            onChanged(r.message);
          }}
        />
      )}
    </Card>
  );
}

// ---------- Adjustments ----------

function Adjustments({ bill, canAdjust, onChanged }: { bill: BillDto; canAdjust: boolean; onChanged: (b: BillDto, note?: string) => void }) {
  const [kind, setKind] = useState<AdjustmentKind>("Discount");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [open, setOpen] = useState(false);
  const [amountError, setAmountError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const kobo = toKobo(amount);
    if (kobo === null || kobo <= 0) {
      setAmountError("Enter the amount in naira.");
      return;
    }
    setAmountError(null);
    if (!window.confirm(`Grant a ${kind.toLowerCase()} of ${money(kobo)}? Adjustments can't be undone.`)) return;
    const res = await run(() => api<BillDto>(`/api/care/bills/${bill.id}/adjustments`, { method: "POST", body: { kind, amountMinor: kobo, reason: reason.trim() } }));
    if (res.ok) {
      setOpen(false);
      setAmount("");
      setReason("");
      onChanged(res.data, `${kind} of ${money(kobo)} granted.`);
    }
  }

  if (bill.adjustments.length === 0 && !canAdjust) return null;
  return (
    <Card className="space-y-3">
      <SectionTitle
        action={
          canAdjust &&
          bill.balanceMinor > 0 &&
          !open && (
            <Button variant="secondary" onClick={() => setOpen(true)}>
              Discount or waive
            </Button>
          )
        }
      >
        Discounts and waivers
      </SectionTitle>
      {bill.adjustments.length === 0 && <p className="text-sm text-muted">None.</p>}
      {bill.adjustments.length > 0 && (
        <ul className="divide-y divide-rule-soft text-sm">
          {bill.adjustments.map((a) => (
            <li key={a.id} className="flex flex-wrap items-start gap-x-3 py-2">
              <div className="min-w-0 flex-1">
                <p className="font-bold text-ink">{a.kind}</p>
                <p className="text-xs text-muted">
                  {formatStamp(a.at)}: {a.reason}
                </p>
              </div>
              <span className="tnum font-bold text-ink">−{money(a.amountMinor)}</span>
            </li>
          ))}
        </ul>
      )}
      {open && (
        <form onSubmit={submit} className="space-y-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <Select label="Kind" value={kind} onChange={(e) => setKind(e.target.value as AdjustmentKind)} error={error?.field("kind")}>
              {ADJUSTMENT_KINDS.map((k) => (
                <option key={k.value} value={k.value}>
                  {k.label}
                </option>
              ))}
            </Select>
            <Input
              label="Amount (naira)"
              inputMode="decimal"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              error={amountError ?? error?.field("amountMinor")}
              hint={`Up to the balance, ${money(bill.balanceMinor)}.`}
            />
          </div>
          <Input label="Reason" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} />
          <ErrorBox error={error && !["kind", "amountMinor", "reason"].some((f) => error.field(f)) ? error : null} />
          <div className="flex gap-2">
            <Button type="submit" loading={busy} disabled={!amount.trim() || !reason.trim()}>
              Grant it
            </Button>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Cancel
            </Button>
          </div>
          <p className="text-xs text-muted">Recorded with your name and audited. An adjustment covers only what is on the bill now and can&apos;t be undone.</p>
        </form>
      )}
    </Card>
  );
}

// ---------- A reason dialog ----------

function ReasonDialog<T>({
  title,
  intro,
  label,
  max,
  min = 1,
  action,
  danger,
  onClose,
  send,
  onDone,
}: {
  title: string;
  intro: string;
  label: string;
  max: number;
  min?: number;
  action: string;
  danger?: boolean;
  onClose: () => void;
  send: (reason: string) => Promise<T>;
  onDone: (r: T) => void;
}) {
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();
  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => send(reason.trim()));
    if (res.ok) onDone(res.data);
  }
  return (
    <Modal open title={title} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">{intro}</p>
        <Textarea label={label} rows={2} maxLength={max} value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} />
        <ErrorBox error={error && !error.field("reason") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={danger ? "danger" : "primary"} loading={busy} disabled={reason.trim().length < min}>
            {action}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
