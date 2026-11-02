"use client";

import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { formatDay, formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto } from "@/lib/types";
import type {
  DiscountKind,
  DiscountRequest,
  FeePaymentDto,
  FeeStructureDto,
  InstalmentInput,
  InvoiceDto,
  ReversePaymentResult,
} from "@/lib/types/fees";
import {
  ApiErrorView,
  Button,
  Card,
  Checkbox,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  RuledSheet,
  SectionTitle,
  Segmented,
  Textarea,
  ruledRowClass,
} from "@/components/ui";
import {
  InvoiceStatusBadge,
  METHOD_LABEL,
  PAYMENT_STATUS_TEXT,
  PaymentStatusBadge,
  ReceiptButton,
  RecordPaymentModal,
  money,
  nairaInput,
  parseNaira,
  useTerms,
} from "../../fees-shared";

type Dialog = "pay" | "discount" | "optional" | "instalments" | "cancel" | { reverse: FeePaymentDto } | null;

export default function InvoicePage() {
  const { id } = useParams<{ id: string }>();
  const canEdit = useCan("fees.manage");
  const { data: inv, error, reload, setData } = useApi<InvoiceDto>(`/api/school/invoices/${id}`);
  const [dialog, setDialog] = useState<Dialog>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const review = useAction();

  if (!inv) {
    return (
      <div>
        <PageHeader title="Invoice" back="/app/fees/invoices" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const cancelled = inv.status === "Cancelled";
  const editable = canEdit && !cancelled;
  const mismatched = inv.payments.filter((p) => p.status === "Mismatch");
  const abandoned = inv.payments.filter((p) => p.status === "Abandoned");

  function saved(next: InvoiceDto, message?: string) {
    setData(next);
    setDialog(null);
    setNotice(message ?? null);
  }

  async function markReviewed() {
    const res = await review.run(() => api<InvoiceDto>(`/api/school/invoices/${id}/reviewed`, { method: "POST" }));
    if (res.ok) saved(res.data, "Marked as reviewed.");
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title={inv.studentName}
        back="/app/fees/invoices"
        subtitle={
          <span className="flex flex-wrap items-center gap-2">
            <span className="tnum">Invoice {inv.number}</span>
            <span>{inv.className}</span>
            <span className="tnum">{inv.admissionNo}</span>
            <InvoiceStatusBadge status={inv.status} />
          </span>
        }
        action={
          editable ? (
            <>
              {inv.balance > 0 && <Button onClick={() => setDialog("pay")}>Record a payment</Button>}
              <Button variant="danger" onClick={() => setDialog("cancel")}>
                Cancel invoice
              </Button>
            </>
          ) : undefined
        }
      />

      {notice && <Notice tone="success">{notice}</Notice>}
      {cancelled && <Notice tone="info">This invoice is cancelled. It no longer counts towards debtors, reminders or the finance totals, and it can&apos;t change.</Notice>}

      {inv.needsReview && (
        <Notice tone="warning">
          <p className="font-semibold">This invoice needs checking</p>
          <p className="mt-1 max-w-prose">
            A payment came in for more than the balance, it was paid after being cancelled, or generating invoices after a change to the fee
            structure didn&apos;t fit (a discount was left out, the instalments were cleared, or the level has no fees this term). Check the lines and
            payments below, make any change, then mark it reviewed.
          </p>
          {canEdit && (
            <Button variant="secondary" className="mt-2" loading={review.busy} onClick={markReviewed}>
              Mark reviewed
            </Button>
          )}
          <ErrorBox error={review.error} />
        </Notice>
      )}

      {mismatched.length > 0 && (
        <Notice tone="error">
          <p className="font-semibold">
            {mismatched.length === 1 ? "An online payment was not applied" : `${mismatched.length} online payments were not applied`}
          </p>
          <p className="mt-1 max-w-prose">
            {PAYMENT_STATUS_TEXT.Mismatch} If the money did reach the school, record what was received as a payment, with the Paystack reference.
          </p>
          <ul className="mt-1 list-disc pl-5">
            {mismatched.map((p) => (
              <li key={p.id} className="tnum">
                {money(p.amountMinor)} on {formatDay(p.paidOn)}
                {p.reference ? `, reference ${p.reference}` : ""}
              </li>
            ))}
          </ul>
        </Notice>
      )}

      {abandoned.length > 0 && <Notice tone="warning">{PAYMENT_STATUS_TEXT.Abandoned}</Notice>}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Figure label="Total" value={money(inv.total)} />
        <Figure label="Paid" value={money(inv.paid)} />
        <Figure label="Balance" value={cancelled ? "—" : money(inv.balance)} strong />
        <Figure label="Due" value={formatDay(inv.dueDate)} />
      </div>

      <Lines invoice={inv} editable={editable} onDiscount={() => setDialog("discount")} onOptional={() => setDialog("optional")} />
      <Instalments invoice={inv} editable={editable} onEdit={() => setDialog("instalments")} />
      <Payments invoice={inv} canEdit={canEdit} onReverse={(p) => setDialog({ reverse: p })} />

      {dialog === "pay" && (
        <RecordPaymentModal
          invoice={inv}
          onClose={() => setDialog(null)}
          onSaved={(p) => {
            setDialog(null);
            setNotice(`Payment of ${money(p.amountMinor)} recorded${p.receiptNumber ? `. Receipt ${p.receiptNumber} is ready to print below` : ""}.`);
            reload();
          }}
        />
      )}
      {dialog === "discount" && <DiscountModal invoice={inv} onClose={() => setDialog(null)} onSaved={(d) => saved(d, "Discount added.")} />}
      {dialog === "optional" && <OptionalItemsModal invoice={inv} onClose={() => setDialog(null)} onSaved={(d) => saved(d, "Optional items updated.")} />}
      {dialog === "instalments" && <InstalmentsModal invoice={inv} onClose={() => setDialog(null)} onSaved={(d) => saved(d, "Instalments saved.")} />}
      {dialog === "cancel" && <CancelModal invoice={inv} onClose={() => setDialog(null)} onSaved={(d) => saved(d, "Invoice cancelled.")} />}
      {dialog && typeof dialog === "object" && (
        <ReverseModal
          payment={dialog.reverse}
          onClose={() => setDialog(null)}
          onSaved={(r) => {
            setDialog(null);
            setNotice(r.message);
            reload();
          }}
        />
      )}
    </div>
  );
}

function Figure({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <Card className="p-4">
      <p className="text-sm text-muted">{label}</p>
      <p className={strong ? "slab tnum mt-1 text-xl font-bold text-ink" : "tnum mt-1 text-lg font-semibold text-ink"}>{value}</p>
    </Card>
  );
}

// ---------- Lines ----------

function Lines({ invoice, editable, onDiscount, onOptional }: { invoice: InvoiceDto; editable: boolean; onDiscount: () => void; onOptional: () => void }) {
  return (
    <Card>
      <SectionTitle
        action={
          editable ? (
            <span className="flex flex-wrap gap-1">
              <Button variant="ghost" className="px-2.5" onClick={onOptional}>
                Optional items
              </Button>
              <Button variant="ghost" className="px-2.5" onClick={onDiscount}>
                Add a discount
              </Button>
            </span>
          ) : undefined
        }
      >
        Fees
      </SectionTitle>
      {invoice.lines.length === 0 ? (
        <p className="text-sm text-muted">This invoice has no lines. Set the fee structure for the level, then generate invoices again.</p>
      ) : (
        <RuledSheet label="Invoice lines" template="minmax(0,1fr) 9rem">
          {invoice.lines.map((l) => (
            <li key={l.id} className={ruledRowClass}>
              <span className={l.feeItemId === null ? "text-slate-700" : "font-bold text-ink"}>{l.description}</span>
              <span className={l.amountMinor < 0 ? "tnum text-right text-pass" : "tnum text-right font-semibold"}>{money(l.amountMinor)}</span>
            </li>
          ))}
          <li className={ruledRowClass}>
            <span className="font-bold text-ink">Total</span>
            <span className="tnum text-right font-bold">{money(invoice.total)}</span>
          </li>
        </RuledSheet>
      )}
    </Card>
  );
}

function DiscountModal({ invoice, onClose, onSaved }: { invoice: InvoiceDto; onClose: () => void; onSaved: (d: InvoiceDto) => void }) {
  const [kind, setKind] = useState<DiscountKind>("Percent");
  const [value, setValue] = useState("");
  const [reason, setReason] = useState("");
  const [valueError, setValueError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    let v: number | null;
    if (kind === "Percent") {
      const text = value.replace(/[%\s]/g, "");
      v = /^\d+(\.\d{1,2})?$/.test(text) && Number(text) > 0 && Number(text) <= 100 ? Number(text) : null;
      if (v === null) return setValueError("Enter a percentage above 0 and up to 100, for example 10.");
    } else {
      v = parseNaira(value);
      if (v === null) return setValueError("Enter the discount in naira, for example 5000.");
    }
    setValueError(null);
    const body: DiscountRequest = { kind, value: v, reason: reason.trim() };
    const res = await run(() => api<InvoiceDto>(`/api/school/invoices/${invoice.id}/discount`, { method: "POST", body }));
    if (res.ok) onSaved(res.data);
  }

  const other = error && !error.field("value") && !error.field("reason") && !error.field("kind") ? error : null;
  return (
    <Modal open title="Add a discount" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Segmented
          label="Discount type"
          value={kind}
          onChange={(k) => (setKind(k), setValue(""), setValueError(null))}
          items={[
            { value: "Percent", label: "Percentage of tuition" },
            { value: "Amount", label: "Fixed amount" },
          ]}
        />
        <Input
          label={kind === "Percent" ? "Percentage (%)" : "Amount (₦)"}
          inputMode="decimal"
          required
          placeholder={kind === "Percent" ? "e.g. 10" : "e.g. 5000"}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          hint={kind === "Percent" ? "Worked out from the tuition lines only. Name the fee item with the word \"tuition\"." : undefined}
          error={valueError ?? error?.field("value") ?? error?.field("kind")}
        />
        <Input
          label="Reason"
          required
          maxLength={150}
          placeholder="e.g. Second child in the school"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          error={error?.field("reason")}
        />
        <p className="text-xs text-muted">The discount shows on the invoice as its own line with the reason. If the total changes, instalments that no longer add up are cleared.</p>
        <ErrorBox error={other} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Add discount
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function OptionalItemsModal({ invoice, onClose, onSaved }: { invoice: InvoiceDto; onClose: () => void; onSaved: (d: InvoiceDto) => void }) {
  const terms = useTerms();
  const term = terms.terms.find((t) => t.id === invoice.termId);
  const classes = useApi<ClassDto[]>(term ? `/api/school/classes${qs({ sessionId: term.sessionId })}` : null);
  const structure = useApi<FeeStructureDto[]>(`/api/school/fee-structure/${invoice.termId}`);
  const levelId = classes.data?.find((c) => c.id === invoice.classId)?.levelId;
  const optional = (structure.data?.find((s) => s.levelId === levelId)?.lines ?? []).filter((l) => l.isOptional);
  const [chosen, setChosen] = useState<string[] | null>(null);
  const picked = chosen ?? invoice.lines.filter((l) => l.feeItemId && optional.some((o) => o.feeItemId === l.feeItemId)).map((l) => l.feeItemId!);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => api<InvoiceDto>(`/api/school/invoices/${invoice.id}/optional-items`, { method: "PUT", body: { feeItemIds: picked } }));
    if (res.ok) onSaved(res.data);
  }

  const loadError = terms.error ?? classes.error ?? structure.error;
  const loaded = terms.loaded && !!classes.data && !!structure.data;
  return (
    <Modal open title="Optional items" onClose={onClose}>
      {loadError ? (
        <ApiErrorView error={loadError} onRetry={() => (terms.reload(), classes.reload(), structure.reload())} />
      ) : !loaded ? (
        <PageLoader />
      ) : optional.length === 0 ? (
        <div className="space-y-4">
          <p className="text-sm text-slate-700">This level has no optional fee items this term. Add them on the fee structure page first.</p>
          <div className="flex justify-end">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
          </div>
        </div>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <p className="text-sm text-slate-700">Tick the optional items {invoice.studentName} takes this term.</p>
          {optional.map((o) => (
            <Checkbox
              key={o.feeItemId}
              checked={picked.includes(o.feeItemId)}
              onChange={(on) => setChosen(on ? [...picked, o.feeItemId] : picked.filter((x) => x !== o.feeItemId))}
              label={
                <>
                  {o.feeItemName} <span className="tnum text-muted">{money(o.amountMinor)}</span>
                </>
              }
            />
          ))}
          <ErrorBox error={error} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={busy}>
              Save optional items
            </Button>
          </div>
        </form>
      )}
    </Modal>
  );
}

// ---------- Instalments ----------

function Instalments({ invoice, editable, onEdit }: { invoice: InvoiceDto; editable: boolean; onEdit: () => void }) {
  return (
    <Card>
      <SectionTitle
        action={
          editable && invoice.total > 0 ? (
            <Button variant="ghost" className="px-2.5" onClick={onEdit}>
              {invoice.instalments.length ? "Change instalments" : "Split into instalments"}
            </Button>
          ) : undefined
        }
      >
        Instalments
      </SectionTitle>
      {invoice.instalments.length === 0 ? (
        <p className="text-sm text-muted">One payment, due {formatDay(invoice.dueDate)}.</p>
      ) : (
        <RuledSheet label="Instalments" template="minmax(0,1fr) 9rem">
          {invoice.instalments.map((x, i) => (
            <li key={x.id} className={ruledRowClass}>
              <span className="text-ink">
                <span className="font-bold">Instalment {i + 1}</span> <span className="tnum text-muted">due {formatDay(x.dueDate)}</span>
              </span>
              <span className="tnum text-right font-semibold">{money(x.amountMinor)}</span>
            </li>
          ))}
        </RuledSheet>
      )}
    </Card>
  );
}

interface InstalmentRow {
  key: number;
  dueDate: string;
  amount: string;
}

function InstalmentsModal({ invoice, onClose, onSaved }: { invoice: InvoiceDto; onClose: () => void; onSaved: (d: InvoiceDto) => void }) {
  const [rows, setRows] = useState<InstalmentRow[]>(() =>
    invoice.instalments.length
      ? invoice.instalments.map((x, i) => ({ key: i, dueDate: x.dueDate, amount: nairaInput(x.amountMinor) }))
      : [
          { key: 0, dueDate: invoice.dueDate, amount: nairaInput(Math.ceil(invoice.total / 2)) },
          { key: 1, dueDate: "", amount: nairaInput(invoice.total - Math.ceil(invoice.total / 2)) },
        ],
  );
  const [bad, setBad] = useState<string | null>(null);
  const { busy, error, run } = useAction();
  const sum = rows.reduce((s, r) => s + (parseNaira(r.amount) ?? 0), 0);

  function update(key: number, patch: Partial<InstalmentRow>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function save(body: InstalmentInput[]) {
    const res = await run(() => api<InvoiceDto>(`/api/school/invoices/${invoice.id}/instalments`, { method: "PUT", body }));
    if (res.ok) onSaved(res.data);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: InstalmentInput[] = [];
    for (const r of rows) {
      const minor = parseNaira(r.amount);
      if (!r.dueDate || minor === null) return setBad("Give every instalment a due date and an amount above zero.");
      body.push({ dueDate: r.dueDate, amountMinor: minor });
    }
    if (sum !== invoice.total) return setBad(`The instalments add up to ${money(sum)}. Make them add up to the invoice total of ${money(invoice.total)}.`);
    setBad(null);
    await save(body);
  }

  return (
    <Modal open wide title="Instalments" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">
          Split the invoice total of <span className="tnum font-semibold">{money(invoice.total)}</span> into up to 12 payments. Due dates must fall
          within the session.
        </p>
        <ul className="space-y-3">
          {rows.map((r, i) => (
            <li key={r.key} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] items-end gap-2">
              <Input label={`Instalment ${i + 1} due`} type="date" required value={r.dueDate} onChange={(e) => update(r.key, { dueDate: e.target.value })} />
              <Input label="Amount (₦)" inputMode="decimal" required value={r.amount} onChange={(e) => update(r.key, { amount: e.target.value })} />
              <Button
                variant="ghost"
                className="px-2.5 text-brick"
                disabled={rows.length <= 1}
                onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                aria-label={`Remove instalment ${i + 1}`}
              >
                Remove
              </Button>
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Button
            variant="secondary"
            disabled={rows.length >= 12}
            onClick={() => setRows((rs) => [...rs, { key: Math.max(0, ...rs.map((x) => x.key)) + 1, dueDate: "", amount: "" }])}
          >
            Add an instalment
          </Button>
          <p className={sum === invoice.total ? "tnum text-sm text-pass" : "tnum text-sm text-brick"} aria-live="polite">
            {money(sum)} of {money(invoice.total)}
          </p>
        </div>
        {bad && <ErrorBox error={bad} />}
        <ErrorBox error={error} />
        <div className="flex flex-wrap justify-between gap-2">
          {invoice.instalments.length > 0 ? (
            <Button variant="danger" loading={busy} onClick={() => save([])}>
              Clear instalments
            </Button>
          ) : (
            <span />
          )}
          <span className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={busy}>
              Save instalments
            </Button>
          </span>
        </div>
      </form>
    </Modal>
  );
}

// ---------- Payments ----------

function Payments({ invoice, canEdit, onReverse }: { invoice: InvoiceDto; canEdit: boolean; onReverse: (p: FeePaymentDto) => void }) {
  return (
    <Card>
      <SectionTitle>Payments</SectionTitle>
      {invoice.payments.length === 0 ? (
        <p className="text-sm text-muted">No payments yet.</p>
      ) : (
        <RuledSheet
          label="Payments"
          template="minmax(0,1fr) 9rem 8rem minmax(0,12rem)"
          head={[{ label: "Payment" }, { label: "Amount", align: "right" }, { label: "Status" }, { label: "" }]}
        >
          {invoice.payments.map((p) => {
            const live = p.status === "Succeeded" && !p.reversedAt;
            return (
              <li key={p.id} className={ruledRowClass}>
                <span className="min-w-0">
                  <span className="font-bold text-ink">{METHOD_LABEL[p.method] ?? p.method}</span>
                  <span className="tnum block text-xs text-muted">
                    {formatDay(p.paidOn)}
                    {p.reference ? ` · ${p.reference}` : ""}
                    {p.receiptNumber ? ` · ${p.receiptNumber}` : ""}
                  </span>
                  {p.reversedAt && (
                    <span className="block text-xs text-slate-700">
                      Reversed {formatStamp(p.reversedAt)}: {p.reversalReason}
                    </span>
                  )}
                  {(p.status === "Mismatch" || p.status === "Abandoned") && <span className="block text-xs text-slate-700">{PAYMENT_STATUS_TEXT[p.status]}</span>}
                </span>
                <span className={p.reversedAt || !live ? "tnum col-start-2 row-start-1 text-right text-muted line-through md:col-start-auto md:row-start-auto" : "tnum col-start-2 row-start-1 text-right font-semibold md:col-start-auto md:row-start-auto"}>
                  {money(p.amountMinor)}
                </span>
                <span className="col-start-1 md:col-start-auto">
                  <PaymentStatusBadge payment={p} />
                </span>
                <span className="col-span-2 flex flex-wrap justify-start gap-1 md:col-span-1 md:justify-end">
                  <ReceiptButton payment={p} />
                  {canEdit && live && (
                    <Button variant="ghost" className="px-2.5 text-brick" onClick={() => onReverse(p)}>
                      Reverse
                    </Button>
                  )}
                </span>
              </li>
            );
          })}
        </RuledSheet>
      )}
    </Card>
  );
}

function ReverseModal({ payment, onClose, onSaved }: { payment: FeePaymentDto; onClose: () => void; onSaved: (r: ReversePaymentResult) => void }) {
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => api<ReversePaymentResult>(`/api/school/payments/${payment.id}/reverse`, { method: "POST", body: { reason: reason.trim() } }));
    if (res.ok) onSaved(res.data);
  }

  return (
    <Modal open title="Reverse this payment" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">
          <span className="tnum font-semibold">{money(payment.amountMinor)}</span> by {(METHOD_LABEL[payment.method] ?? payment.method).toLowerCase()} on{" "}
          {formatDay(payment.paidOn)}. Reversing takes it off the invoice and restores the balance. The receipt is marked reversed and the guardian is told.
          Payments can&apos;t be edited, so reverse a wrong one and record it again.
        </p>
        {payment.method === "Online" && (
          <Notice tone="warning">This only changes the school&apos;s books. No refund is sent through Paystack: refund the parent from your Paystack dashboard if you need to.</Notice>
        )}
        <Textarea label="Reason" required minLength={5} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} />
        <ErrorBox error={error && !error.field("reason") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Keep payment
          </Button>
          <Button type="submit" variant="danger" loading={busy}>
            Reverse payment
          </Button>
        </div>
      </form>
    </Modal>
  );
}

// ---------- Cancel ----------

function CancelModal({ invoice, onClose, onSaved }: { invoice: InvoiceDto; onClose: () => void; onSaved: (d: InvoiceDto) => void }) {
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();
  const hasPayments = invoice.payments.some((p) => p.status === "Succeeded" && !p.reversedAt);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => api<InvoiceDto>(`/api/school/invoices/${invoice.id}/cancel`, { method: "POST", body: { reason: reason.trim() } }));
    if (res.ok) onSaved(res.data);
  }

  return (
    <Modal open title="Cancel this invoice" onClose={onClose}>
      {hasPayments ? (
        <div className="space-y-4">
          <p className="text-sm text-slate-700">This invoice has payments. Reverse them first, then cancel it.</p>
          <div className="flex justify-end">
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
          </div>
        </div>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <p className="text-sm text-slate-700">
            {invoice.studentName}&apos;s invoice {invoice.number} will leave the debtors list, reminders and the finance totals. A cancelled invoice
            can&apos;t be changed or paid, and generating invoices again won&apos;t bring it back.
          </p>
          <Textarea
            label="Reason"
            required
            minLength={5}
            maxLength={300}
            placeholder="e.g. Left the school before term started"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            error={error?.field("reason")}
          />
          <ErrorBox error={error && !error.field("reason") ? error : null} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Keep invoice
            </Button>
            <Button type="submit" variant="danger" loading={busy}>
              Cancel invoice
            </Button>
          </div>
        </form>
      )}
    </Modal>
  );
}
