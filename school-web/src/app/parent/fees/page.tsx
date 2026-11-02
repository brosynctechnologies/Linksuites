"use client";

import { useState, type FormEvent } from "react";
import { DownloadIcon } from "@/components/icons";
import {
  ApiErrorView,
  Button,
  Card,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  PageHeader,
  PageLoader,
  RuledSheet,
  SectionTitle,
  ruledRowClass,
} from "@/components/ui";
import { api, downloadFile } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { FeePaymentDto } from "@/lib/types/fees";
import type { ParentInvoiceDto, PayOnlineResult } from "@/lib/types/portals";
import {
  ABANDONED_COPY,
  ChildGate,
  DueLine,
  InvoiceStatusBadge,
  money,
  useChild,
  useChildPath,
} from "../_shared";

export default function ParentFeesPage() {
  const { child } = useChild();
  return (
    <ChildGate>
      <Fees key={child?.studentId ?? "none"} />
    </ChildGate>
  );
}

function Fees() {
  const { child } = useChild();
  const { data, error, loading, reload } = useApi<ParentInvoiceDto[]>(useChildPath("/invoices"));
  const [paying, setPaying] = useState<ParentInvoiceDto | null>(null);
  const invoices = data ?? [];
  const outstanding = invoices.filter((i) => i.status !== "Cancelled").reduce((sum, i) => sum + i.balance, 0);

  return (
    <div className="space-y-5">
      <PageHeader title="Fees" subtitle={child ? `${child.name}${child.className ? ` · ${child.className}` : ""}` : undefined} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && data && data.length === 0 && <EmptyState title="No invoices yet" text="When the school bills this child, the invoices will show here." />}
      {!loading && data && data.length > 0 && (
        <>
          <Card>
            <SectionTitle>Outstanding</SectionTitle>
            <p className="slab tnum text-2xl font-bold text-ink">{money(outstanding)}</p>
            <p className="mt-1 text-sm text-muted">Across every invoice that isn&apos;t cancelled.</p>
          </Card>
          <RuledSheet label="Invoices">
            {invoices.map((inv) => (
              <li key={inv.id}>
                <InvoiceRow invoice={inv} onPay={() => setPaying(inv)} />
              </li>
            ))}
          </RuledSheet>
        </>
      )}
      {paying && child && !loading && (
        <PayModal
          studentId={child.studentId}
          invoice={paying}
          onClose={() => setPaying(null)}
        />
      )}
    </div>
  );
}

function InvoiceRow({ invoice, onPay }: { invoice: ParentInvoiceDto; onPay: () => void }) {
  const payable = invoice.canPayOnline && invoice.balance > 0 && invoice.status !== "Cancelled";
  return (
    <div className="space-y-3 py-1">
      <div className={ruledRowClass} style={{ ["--cols" as string]: "minmax(0,1.4fr) 6rem 7rem auto" }}>
        <div className="min-w-0">
          <p className="font-bold text-ink">
            Invoice {invoice.number}
            <span className="ml-2 font-medium text-muted">{invoice.termName}</span>
          </p>
          <DueLine dueDate={invoice.dueDate} />
        </div>
        <span className="tnum text-sm text-ink">{money(invoice.total)}</span>
        {invoice.status === "Cancelled" ? (
          <span className="tnum text-sm text-muted" aria-label="No balance: cancelled">—</span>
        ) : (
          <span className="tnum text-sm font-bold text-ink">{money(invoice.balance)}</span>
        )}
        <span className="flex flex-wrap items-center justify-end gap-2">
          <InvoiceStatusBadge status={invoice.status} />
          {payable && (
            <Button onClick={onPay} className="px-3">
              Pay now
            </Button>
          )}
        </span>
      </div>
      {invoice.status !== "Cancelled" && invoice.balance > 0 && !invoice.canPayOnline && (
        <p className="ruled-pad pt-0 text-sm text-muted">This school doesn&apos;t take online payments yet. Pay at the school office.</p>
      )}
      {invoice.lines.length > 0 && (
        <ul className="ruled-pad space-y-1 pt-0 text-sm text-slate-700">
          {invoice.lines.map((l) => (
            <li key={l.id} className="flex justify-between gap-3">
              <span>{l.description}</span>
              <span className="tnum">{money(l.amountMinor)}</span>
            </li>
          ))}
        </ul>
      )}
      {invoice.instalments.length > 0 && (
        <p className="ruled-pad pt-0 text-xs text-muted">
          Instalments:{" "}
          {invoice.instalments.map((x) => `${money(x.amountMinor)} due ${formatDay(x.dueDate)}`).join(" · ")}
        </p>
      )}
      {invoice.payments.length > 0 && (
        <ul className="ruled-pad space-y-2 pt-0">
          {invoice.payments.map((p) => (
            <li key={p.id}>
              <PaymentLine payment={p} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function PaymentLine({ payment }: { payment: FeePaymentDto }) {
  const receipt = useAction();
  if (payment.status === "Abandoned") {
    return <p className="text-sm text-slate-700">{ABANDONED_COPY}</p>;
  }
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
      <span className="text-slate-700">
        <span className="tnum font-bold text-ink">{money(payment.amountMinor)}</span>
        {" · "}
        {payment.method === "Online" ? "Online" : payment.method}
        {" · "}
        <span className="tnum">{formatDay(payment.paidOn)}</span>
        {payment.reversedAt && <span className="text-muted"> · Reversed</span>}
      </span>
      {payment.status === "Succeeded" && payment.receiptNumber && !payment.reversedAt && (
        <Button
          variant="ghost"
          className="px-2.5"
          loading={receipt.busy}
          onClick={() => receipt.run(() => downloadFile(`/api/parent/payments/${payment.id}/receipt.pdf`, `${payment.receiptNumber}.pdf`))}
        >
          <DownloadIcon className="h-4 w-4" /> Receipt {payment.receiptNumber}
        </Button>
      )}
      {receipt.error && <span className="w-full text-xs text-brick">{receipt.error.message}</span>}
    </div>
  );
}

function PayModal({ studentId, invoice, onClose }: { studentId: string; invoice: ParentInvoiceDto; onClose: () => void }) {
  const suggested = invoice.nextInstalmentMinor ?? invoice.balance;
  const [amount, setAmount] = useState(() => (suggested % 100 === 0 ? String(suggested / 100) : (suggested / 100).toFixed(2)));
  const [amountHint, setAmountHint] = useState<string | null>(null);
  const pay = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const clean = amount.replace(/[₦,\s]/g, "");
    if (!/^\d+(\.\d{1,2})?$/.test(clean)) {
      setAmountHint("Enter an amount in naira.");
      return;
    }
    setAmountHint(null);
    const amountMinor = Math.round(Number(clean) * 100);
    const res = await pay.run(() =>
      api<PayOnlineResult>(`/api/parent/children/${studentId}/invoices/${invoice.id}/pay`, { method: "POST", body: { amountMinor } }),
    );
    if (!res.ok) return;
    window.location.assign(res.data.authorizationUrl);
  }

  const min = Math.min(invoice.minimumPartPaymentMinor, invoice.balance);
  const next = invoice.nextInstalmentMinor;

  return (
    <Modal
      open
      title={`Pay invoice ${invoice.number}`}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="parent-pay" loading={pay.busy}>
            Pay now
          </Button>
        </>
      }
    >
      <form id="parent-pay" onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">
          Balance {money(invoice.balance)}
          {next ? `. Next instalment ${money(next)}.` : "."} Pay at least {money(min)}, the next instalment, or the full balance.
        </p>
        <Input
          label="Amount (naira)"
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          error={amountHint ?? pay.error?.field("amountMinor")}
        />
        <ErrorBox error={pay.error && !pay.error.field("amountMinor") ? pay.error : null} />
        {next && next !== invoice.balance && (
          <Button
            type="button"
            variant="secondary"
            className="w-full"
            onClick={() => setAmount(next % 100 === 0 ? String(next / 100) : (next / 100).toFixed(2))}
          >
            Use next instalment ({money(next)})
          </Button>
        )}
      </form>
    </Modal>
  );
}
