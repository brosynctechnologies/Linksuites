"use client";

import { useState, type FormEvent } from "react";
import { BillIcon, DownloadIcon } from "@/components/icons";
import { BILL_STATUS, PAYMENT_STATUS, PAY_METHOD, PortalError, StatusBadge, lagosShort } from "@/components/portal-parts";
import { Button, Card, EmptyState, ErrorBox, Input, Modal, PageHeader, PageLoader, Segmented } from "@/components/ui";
import { api, downloadFile } from "@/lib/api";
import { koboInput, money, toKobo } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { rememberCheckout, usePatientApi, usePortal } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { CareCheckoutResult, PortalBillDto, PortalPaymentDto } from "@/lib/types/portal";

/** The smallest part payment the API takes (PortalPayRequest: the balance, or a part of at least ₦1,000). */
const MIN_PART_MINOR = 100_000;

/** The patient's bills, newest first: lines, totals, payments with receipts, and Pay now through the clinic's Paystack checkout. */
export default function PortalBillsPage() {
  const { patient } = usePortal();
  const bills = useApi<PortalBillDto[]>(usePatientApi("/bills"));
  const [paying, setPaying] = useState<PortalBillDto | null>(null);
  return (
    <div>
      <PageHeader title="Bills and receipts" subtitle={patient ? `At ${patient.clinicName}.` : undefined} />
      {bills.error && !bills.data && <PortalError error={bills.error} onRetry={bills.reload} />}
      {!bills.data && !bills.error && <PageLoader />}
      {bills.data && bills.data.length === 0 && <EmptyState icon={<BillIcon />} title="No bills yet" text="Bills from visits and hospital stays will show here." />}
      {bills.data && bills.data.length > 0 && (
        <div className="space-y-4">
          {bills.data.map((b) => (
            <Bill key={b.id} bill={b} onPay={() => setPaying(b)} />
          ))}
        </div>
      )}
      {paying && patient && <PayDialog patientId={patient.id} bill={paying} onClose={() => setPaying(null)} />}
    </div>
  );
}

function Bill({ bill: b, onPay }: { bill: PortalBillDto; onPay: () => void }) {
  const { patient } = usePortal();
  // adjustedMinor is the sum of discounts and waivers (not the net): show it as such, and the net only when there is one.
  const adjusted = b.adjustedMinor > 0;
  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-2">
            <span className="tnum font-bold text-ink">{b.number}</span>
            <StatusBadge map={BILL_STATUS} value={b.status} />
          </p>
          <p className="text-sm text-muted">
            Started {lagosShort(b.createdAt)}
            {b.finalisedAt ? `, final ${lagosShort(b.finalisedAt)}` : ""}
          </p>
        </div>
        {b.balanceMinor > 0 && (
          <div className="text-right">
            <p className="display tnum text-xl font-bold text-ink">{money(b.balanceMinor)}</p>
            <p className="text-xs text-muted">to pay</p>
          </div>
        )}
      </div>

      {b.lines.length > 0 && (
        <table className="mt-3 w-full text-sm">
          <caption className="sr-only">What the bill is for</caption>
          <thead>
            <tr className="border-b border-rule text-xs text-muted">
              <th className="py-2 pr-3 text-left font-bold">Item</th>
              <th className="py-2 pr-3 text-right font-bold">Qty</th>
              <th className="py-2 text-right font-bold">Amount</th>
            </tr>
          </thead>
          <tbody className="tnum">
            {b.lines.map((l, i) => (
              <tr key={i} className="border-b border-rule-soft">
                <td className="py-2 pr-3 text-ink">
                  {l.description}
                  {l.quantity > 1 && <span className="block text-xs text-muted">{money(l.unitPriceMinor)} each</span>}
                </td>
                <td className="py-2 pr-3 text-right">{l.quantity}</td>
                <td className="py-2 text-right">{money(l.amountMinor)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <dl className="tnum mt-3 space-y-1 text-sm">
        <Row label="Total" value={money(b.totalMinor)} />
        {adjusted && <Row label="Discounts and waivers" value={`−${money(b.adjustedMinor)}`} />}
        {adjusted && <Row label="After discounts" value={money(Math.max(0, b.totalMinor - b.adjustedMinor))} />}
        <Row label="Paid" value={money(b.paidMinor)} />
        <Row label="Balance" value={money(Math.max(0, b.balanceMinor))} strong />
        {b.creditMinor > 0 && <Row label="In credit" value={money(b.creditMinor)} />}
      </dl>

      {b.payments.length > 0 && patient && <Payments patientId={patient.id} payments={b.payments} />}

      {b.balanceMinor > 0 && (
        <Button className="mt-4 w-full sm:w-auto" onClick={onPay}>
          Pay now
        </Button>
      )}
    </Card>
  );
}

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className={strong ? "font-bold text-ink" : "text-slate-700"}>{label}</dt>
      <dd className={strong ? "font-bold text-ink" : "text-ink"}>{value}</dd>
    </div>
  );
}

function Payments({ patientId, payments }: { patientId: string; payments: PortalPaymentDto[] }) {
  return (
    <div className="mt-4 border-t border-rule-soft pt-3">
      <p className="text-sm font-bold text-ink">Payments</p>
      <ul className="mt-1 divide-y divide-rule-soft">
        {payments.map((p) => (
          <PaymentRow key={p.id} patientId={patientId} payment={p} />
        ))}
      </ul>
    </div>
  );
}

function PaymentRow({ patientId, payment: p }: { patientId: string; payment: PortalPaymentDto }) {
  const pdf = useAction();
  return (
    <li className="flex flex-wrap items-center justify-between gap-2 py-2">
      <span className="min-w-0 text-sm">
        <span className="tnum font-bold text-ink">{money(p.amountMinor)}</span>{" "}
        <span className="text-slate-700">
          {PAY_METHOD[p.method]}, {lagosShort(p.paidAt)}
        </span>{" "}
        <StatusBadge map={PAYMENT_STATUS} value={p.status} />
      </span>
      {p.receiptNumber && (
        <span className="flex flex-col items-end">
          <Button
            variant="secondary"
            className="min-h-10 px-3 text-sm"
            loading={pdf.busy}
            onClick={() => pdf.run(() => downloadFile(`/api/care-portal/patients/${patientId}/receipts/${p.id}.pdf`, `${p.receiptNumber}.pdf`))}
          >
            <DownloadIcon className="h-4 w-4" /> Receipt {p.receiptNumber}
          </Button>
          {pdf.error && <span className="mt-1 max-w-xs text-right text-xs text-arterial">{pdf.error.message}</span>}
        </span>
      )}
    </li>
  );
}

/** Pay the balance or a part, then off to Paystack; it returns to /portal/pay/return?reference=. */
function PayDialog({ patientId, bill, onClose }: { patientId: string; bill: PortalBillDto; onClose: () => void }) {
  const [mode, setMode] = useState<"all" | "part">("all");
  const [amount, setAmount] = useState(koboInput(Math.min(bill.balanceMinor, MIN_PART_MINOR)));
  const [fieldError, setFieldError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    setFieldError(null);
    let amountMinor: number | undefined;
    if (mode === "part") {
      const kobo = toKobo(amount);
      if (kobo === null || kobo <= 0) return setFieldError("Enter an amount in naira.");
      if (kobo > bill.balanceMinor) return setFieldError(`That is more than the balance of ${money(bill.balanceMinor)}.`);
      if (kobo < MIN_PART_MINOR && kobo !== bill.balanceMinor) return setFieldError(`Pay at least ${money(MIN_PART_MINOR)}, or the whole balance.`);
      amountMinor = kobo;
    }
    const res = await run(() =>
      api<CareCheckoutResult>(`/api/care-portal/patients/${patientId}/bills/${bill.id}/pay`, {
        method: "POST",
        body: amountMinor === undefined ? {} : { amountMinor },
      }),
    );
    if (!res.ok) return;
    if (!/^https?:\/\//i.test(res.data.authorizationUrl)) return;
    rememberCheckout(res.data.reference, patientId);
    window.location.assign(res.data.authorizationUrl);
  }

  return (
    <Modal open title={`Pay bill ${bill.number}`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">
          Balance <span className="tnum font-bold text-ink">{money(bill.balanceMinor)}</span>. You will pay on Paystack&apos;s secure page, then come
          back here.
        </p>
        {bill.balanceMinor > MIN_PART_MINOR && (
          <Segmented
            label="How much"
            value={mode}
            onChange={setMode}
            items={[
              { value: "all", label: "The whole balance" },
              { value: "part", label: "Part of it" },
            ]}
          />
        )}
        {mode === "part" && (
          <Input
            label="Amount in naira"
            inputMode="decimal"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            error={fieldError ?? error?.field("amountMinor") ?? undefined}
            hint={`At least ${money(MIN_PART_MINOR)}.`}
          />
        )}
        <ErrorBox error={error && !error.field("amountMinor") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Pay {mode === "all" ? money(bill.balanceMinor) : "now"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
