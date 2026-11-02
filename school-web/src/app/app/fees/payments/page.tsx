"use client";

import Link from "next/link";
import { useState } from "react";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { FeePaymentDto, InvoiceSummaryDto } from "@/lib/types/fees";
import { ImportDialog } from "@/components/import-dialog";
import { ApiErrorView, Button, EmptyState, Notice, PageLoader, RuledSheet, SearchField, linkClass, ruledRowClass } from "@/components/ui";
import { InvoiceStatusBadge, ReceiptButton, RecordPaymentModal, TermSelect, money, useTermChoice } from "../fees-shared";

const TAKE = 2000;

/** The bursar's counter: find a student with a balance and record what they paid in person. */
export default function PaymentsPage() {
  const canEdit = useCan("fees.manage");
  const t = useTermChoice();
  const [q, setQ] = useState("");
  const [paying, setPaying] = useState<InvoiceSummaryDto | null>(null);
  const [last, setLast] = useState<{ invoice: InvoiceSummaryDto; payment: FeePaymentDto } | null>(null);
  const list = useApi<InvoiceSummaryDto[]>(t.termId ? `/api/school/invoices${qs({ termId: t.termId, take: TAKE })}` : null);

  if (t.error) return <ApiErrorView error={t.error} onRetry={t.reload} />;
  if (!t.loaded) return <PageLoader />;
  if (t.terms.length === 0) return <EmptyState title="No terms yet" text="Add the session and its terms on the setup page first." />;

  const needle = q.trim().toLowerCase();
  const owing = (list.data ?? []).filter((i) => i.status !== "Cancelled" && i.balance > 0);
  const rows = needle
    ? owing.filter((i) => i.studentName.toLowerCase().includes(needle) || i.admissionNo.toLowerCase().includes(needle) || i.number.toLowerCase().includes(needle))
    : owing;

  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        Record money paid at the school by cash, bank transfer or POS. Online payments through Paystack are recorded on their own. To print a receipt
        again or reverse a payment, open the student&apos;s invoice.
      </p>
      <div className="flex flex-wrap items-end gap-2">
        <TermSelect terms={t.terms} value={t.termId} onChange={(id) => (t.setTermId(id), setLast(null))} />
        <SearchField id="payment-search" label="Find a student" placeholder="Name, admission or invoice number" value={q} onChange={setQ} className="min-w-52 flex-1" />
        {canEdit && t.termId && (
          <ImportDialog
            kind="payments"
            scope={{ termId: t.termId }}
            title={t.term ? `Import payments for ${t.term.label}` : "Import payments"}
            onDone={() => (setLast(null), list.reload())}
          />
        )}
      </div>

      {last && (
        <Notice tone="success">
          <p>
            Recorded <span className="tnum font-semibold">{money(last.payment.amountMinor)}</span> for {last.invoice.studentName}
            {last.payment.receiptNumber ? `, receipt ${last.payment.receiptNumber}` : ""}.
          </p>
          <span className="mt-1 flex flex-wrap items-center gap-2">
            <ReceiptButton payment={last.payment} />
            <Link href={`/app/fees/invoices/${last.invoice.id}`} className={linkClass}>
              Open the invoice
            </Link>
          </span>
        </Notice>
      )}

      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && owing.length === 0 && (
        <EmptyState
          title="Nobody owes anything this term"
          text={list.data.length === 0 ? "There are no invoices for this term yet. Generate them on the invoices page." : "Every invoice for this term is paid or cancelled."}
          action={
            list.data.length === 0 ? (
              <Link href="/app/fees/invoices" className={linkClass}>
                Go to invoices
              </Link>
            ) : undefined
          }
        />
      )}
      {list.data && owing.length > 0 && rows.length === 0 && <p className="text-sm text-muted">No student with a balance matches. Try another name or number.</p>}
      {list.data && list.data.length >= TAKE && <Notice tone="warning">Showing the first {TAKE.toLocaleString("en-NG")} invoices. Search by name to find the rest.</Notice>}
      {rows.length > 0 && (
        <RuledSheet
          label="Invoices with a balance"
          busy={list.loading}
          template="minmax(0,1fr) 8rem 8rem 7rem 10rem"
          head={[{ label: "Student" }, { label: "Class" }, { label: "Balance", align: "right" }, { label: "Status" }, { label: "" }]}
        >
          {rows.map((i) => (
            <li key={i.id} className={ruledRowClass}>
              <span className="min-w-0">
                <Link href={`/app/fees/invoices/${i.id}`} className="font-bold text-ink hover:text-blazer hover:underline">
                  {i.studentName}
                </Link>
                <span className="tnum block text-xs text-muted">
                  {i.number} · {i.admissionNo}
                </span>
              </span>
              <span className="col-start-1 text-sm text-slate-700 md:col-start-auto">{i.className}</span>
              <span className="tnum col-start-2 row-start-1 text-right text-sm font-semibold md:col-start-auto md:row-start-auto">{money(i.balance)}</span>
              <span className="col-start-1 md:col-start-auto">
                <InvoiceStatusBadge status={i.status} />
              </span>
              <span className="col-start-2 row-span-2 row-start-2 flex justify-end md:col-start-auto md:row-span-1 md:row-start-auto">
                {canEdit && (
                  <Button variant="secondary" onClick={() => setPaying(i)}>
                    Record payment
                  </Button>
                )}
              </span>
            </li>
          ))}
        </RuledSheet>
      )}

      {paying && (
        <RecordPaymentModal
          invoice={paying}
          onClose={() => setPaying(null)}
          onSaved={(payment) => {
            setLast({ invoice: paying, payment });
            setPaying(null);
            list.reload();
          }}
        />
      )}
    </div>
  );
}
