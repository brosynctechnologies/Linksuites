"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, downloadFile, toApiError, type ApiError } from "@/lib/api";
import { formatDay, todayISO } from "@/lib/dates";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { formatRate } from "@/lib/fx";
import { RatesAttribution } from "@/components/fx";
import { useQueryParam } from "@/lib/use-debounced";
import type { BusinessDto, InvoiceDto, PaymentMethod, RecordPaymentRequest } from "@/lib/types";
import { CopyLinkButton, SendModal, Timeline, previewFromDoc } from "@/components/doc-view-parts";
import { InvoicePreview } from "@/components/invoice-preview";
import {
  ApiErrorView,
  Button,
  Checkbox,
  DocumentSkeleton,
  ErrorBox,
  Input,
  InvoiceStatusBadge,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  Textarea,
  buttonClass,
  cx,
  linkClass,
  panelClass,
} from "@/components/ui";
import { DownloadIcon, EditIcon, SendIcon, TrashIcon, WhatsAppIcon } from "@/components/icons";

const METHODS: { value: PaymentMethod; label: string }[] = [
  { value: "BankTransfer", label: "Bank transfer" },
  { value: "Cash", label: "Cash" },
  { value: "Card", label: "Card" },
  { value: "Online", label: "Online" },
  { value: "Other", label: "Other" },
];
const methodLabel = (m: string) => METHODS.find((x) => x.value === m)?.label ?? m;

export default function InvoicePage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { data: inv, error, reload, setData } = useApi<InvoiceDto>(`/api/invoices/${id}`);
  const { data: business } = useApi<BusinessDto>("/api/business");
  const canEdit = useCan("docs.edit");
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [sendOpen, setSendOpen] = useState(false);
  const [payOpen, setPayOpen] = useState(false);
  // Lets the stamp land with a brief animation when it appears because of an action on this page.
  const [stampAnim, setStampAnim] = useState(false);
  const imported = useQueryParam("imported");
  const created = useQueryParam("created");
  const [banner, setBanner] = useState<string | null>(() =>
    imported ? "Your invoice from the generator is saved. Send it when you're ready." : created ? "Invoice saved." : null,
  );

  async function act(key: string, fn: () => Promise<void>) {
    setBusy(key);
    setActionError(null);
    try {
      await fn();
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  if (!inv) {
    return (
      <div>
        <PageHeader title="Invoice" back="/app/invoices" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const isVoid = inv.status === "Void";
  const isDraft = inv.status === "Draft";
  const c = inv.currency;
  const base = inv.baseCurrency || business?.currency || c;
  const foreign = base !== c && !!inv.exchangeRate;

  const shareWhatsApp = () => {
    window.open(inv.whatsappShareUrl, "_blank", "noopener,noreferrer");
    if (isDraft) {
      act("wa", async () => {
        setData(await api<InvoiceDto>(`/api/invoices/${inv.id}/mark-sent`, { method: "POST" }));
      });
    }
  };

  return (
    <div className="space-y-4">
      <PageHeader
        title={inv.number}
        back="/app/invoices"
        subtitle={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <InvoiceStatusBadge status={inv.status} />
            <span>{inv.client.name}</span>
            <span className="tabular-nums">Due {formatDay(inv.dueDate)}</span>
          </span>
        }
      />

      {banner && (
        <Notice tone="success">
          <div className="flex items-start justify-between gap-3">
            <span>{banner}</span>
            <button type="button" className="text-xs font-semibold underline" onClick={() => setBanner(null)}>
              Dismiss
            </button>
          </div>
        </Notice>
      )}
      {actionError && <ApiErrorView error={actionError} />}

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start xl:grid-cols-[minmax(0,1fr)_22rem] xl:gap-8">
        {/* Actions: a sticky row above the document on phones, the top of the side panel on desktop. */}
        <div className="sticky top-14 z-20 -mx-4 border-b border-rule bg-desk/95 px-4 py-2.5 backdrop-blur sm:-mx-6 sm:px-6 lg:static lg:col-start-2 lg:row-start-1 lg:mx-0 lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none">
          <div className="no-scrollbar flex gap-2 overflow-x-auto lg:grid lg:grid-cols-2 lg:overflow-visible" role="group" aria-label="Invoice actions">
            {canEdit && !isVoid && (
              <Button onClick={() => setSendOpen(true)} className="shrink-0 lg:col-span-2">
                <SendIcon className="h-4 w-4" /> Send by email
              </Button>
            )}
            {canEdit && !isVoid && inv.balanceMinor > 0 && (
              <Button variant="secondary" className="shrink-0 lg:col-span-2" onClick={() => setPayOpen(true)}>
                Record payment
              </Button>
            )}
            {canEdit && !isVoid && (
              <Button variant="secondary" className="shrink-0" onClick={shareWhatsApp} loading={busy === "wa"}>
                <WhatsAppIcon className="h-4 w-4 text-[#1a9e4b]" /> WhatsApp
              </Button>
            )}
            <CopyLinkButton url={inv.publicUrl} className="shrink-0" />
            <Button
              variant="secondary"
              className="shrink-0"
              loading={busy === "pdf"}
              onClick={() => act("pdf", () => downloadFile(`/api/invoices/${inv.id}/pdf`, `${inv.number}.pdf`, { accept: "application/pdf" }))}
            >
              <DownloadIcon className="h-4 w-4" /> PDF
            </Button>
            {canEdit && !isVoid && (
              <Link href={`/app/invoices/${inv.id}/edit`} className={buttonClass("secondary", "shrink-0")}>
                <EditIcon className="h-4 w-4" /> Edit
              </Link>
            )}
            {canEdit && (
              <Button
                variant="secondary"
                className="shrink-0"
                loading={busy === "dup"}
                onClick={() =>
                  act("dup", async () => {
                    const copy = await api<InvoiceDto>(`/api/invoices/${inv.id}/duplicate`, { method: "POST" });
                    router.push(`/app/invoices/${copy.id}/edit`);
                  })
                }
              >
                Duplicate
              </Button>
            )}
            {canEdit && !isVoid && !isDraft && (
              <Button
                variant="danger"
                className="shrink-0"
                loading={busy === "void"}
                onClick={() => {
                  if (!window.confirm(`Void ${inv.number}? The client can no longer pay it and this can't be undone.`)) return;
                  act("void", async () => {
                    setData(await api<InvoiceDto>(`/api/invoices/${inv.id}/void`, { method: "POST" }));
                    setStampAnim(true);
                  });
                }}
              >
                Void
              </Button>
            )}
            {canEdit && isDraft && (
              <Button
                variant="danger"
                className="shrink-0"
                loading={busy === "del"}
                onClick={() => {
                  if (!window.confirm(`Delete draft ${inv.number}?`)) return;
                  act("del", async () => {
                    await api(`/api/invoices/${inv.id}`, { method: "DELETE" });
                    router.replace("/app/invoices");
                  });
                }}
              >
                <TrashIcon className="h-4 w-4" /> Delete
              </Button>
            )}
          </div>
        </div>

        <div className="min-w-0 lg:col-start-1 lg:row-span-2 lg:row-start-1">
          {business ? <InvoicePreview doc={previewFromDoc(inv, business)} animateStamp={stampAnim} /> : <DocumentSkeleton />}
        </div>

        <aside className={cx(panelClass, "divide-y divide-rule-soft lg:col-start-2 lg:row-start-2")} aria-label="Balance, payments and activity">
          <section className="p-4 sm:p-5">
            <p className="text-sm text-muted">Balance due</p>
            <p className={cx("font-figure amount mt-0.5 text-[2.25rem] leading-none", inv.balanceMinor > 0 ? "text-graphite" : "text-paid")}>
              {formatMoney(inv.balanceMinor, c)}
            </p>
            <dl className="mt-4 space-y-1.5 text-sm">
              <div className="flex justify-between gap-3">
                <dt className="text-muted">Total</dt>
                <dd className="amount font-semibold">{formatMoney(inv.totalMinor, c)}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-muted">Paid</dt>
                <dd className="amount">{formatMoney(inv.amountPaidMinor, c)}</dd>
              </div>
              {inv.amountPaidMinor > inv.totalMinor && (
                // An online payment can land after the invoice was paid another way. It is still recorded (the money
                // arrived), so the balance shows 0 and the extra is flagged here for the business to refund or keep.
                <div className="flex justify-between gap-3 font-semibold text-amber-text" role="note">
                  <dt>Overpaid by</dt>
                  <dd className="amount">{formatMoney(inv.amountPaidMinor - inv.totalMinor, c)}</dd>
                </div>
              )}
            </dl>
            {foreign && (
              <div className="mt-3 space-y-1 border-t border-rule-soft pt-3 text-sm">
                <div className="flex justify-between gap-3">
                  <span className="text-muted">Total in {base}</span>
                  <span className="amount font-medium">{formatMoney(inv.totalBaseMinor, base)}</span>
                </div>
                {inv.balanceMinor > 0 && (
                  <div className="flex justify-between gap-3">
                    <span className="text-muted">Balance in {base}</span>
                    <span className="amount font-medium">{formatMoney(inv.balanceBaseMinor, base)}</span>
                  </div>
                )}
                <p className="text-xs text-muted">Exchange rate: {formatRate(c, inv.exchangeRate, base)}</p>
                <RatesAttribution />
              </div>
            )}
            {inv.quoteId && (
              <Link href={`/app/quotes/${inv.quoteId}`} className={cx(linkClass, "mt-3 block text-sm")}>
                View original quote
              </Link>
            )}
            {inv.recurringScheduleId && <p className="mt-2 text-xs text-muted">Created by a recurring schedule.</p>}
          </section>

          <section className="p-4 sm:p-5">
            <SectionTitle>Payments</SectionTitle>
            {inv.payments.length === 0 ? (
              <p className="text-sm text-muted">No payments yet.</p>
            ) : (
              <ul className="divide-y divide-rule-soft">
                {inv.payments.map((p) => (
                  <li key={p.id} className="flex items-start justify-between gap-2 py-2.5 text-sm first:pt-0">
                    <div className="min-w-0">
                      <p className="amount font-semibold">
                        {formatMoney(p.amountMinor, c)}
                        {foreign && p.baseAmountMinor !== undefined && (
                          <span className="ml-1.5 text-xs font-normal text-muted">≈ {formatMoney(p.baseAmountMinor, base)}</span>
                        )}
                      </p>
                      <p className="flex flex-wrap gap-x-2 text-xs text-muted">
                        <span>{methodLabel(p.method)}</span>
                        <span className="tabular-nums">{formatDay(p.paidAt)}</span>
                        {p.receiptNumber && <span>Receipt {p.receiptNumber}</span>}
                      </p>
                      {p.reference && <p className="truncate text-xs text-muted">Ref: {p.reference}</p>}
                      {p.note && <p className="text-xs text-slate-600">{p.note}</p>}
                    </div>
                    {canEdit && !p.online && !isVoid && (
                      <button
                        type="button"
                        className="-mr-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-control text-overdue hover:bg-overdue-wash"
                        aria-label={`Delete payment of ${formatMoney(p.amountMinor, c)}`}
                        disabled={busy === `pay-${p.id}`}
                        onClick={() => {
                          if (!window.confirm("Delete this payment?")) return;
                          act(`pay-${p.id}`, async () =>
                            setData(await api<InvoiceDto>(`/api/invoices/${inv.id}/payments/${p.id}`, { method: "DELETE" })),
                          );
                        }}
                      >
                        <TrashIcon className="h-4 w-4" />
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="p-4 sm:p-5">
            <SectionTitle>Activity</SectionTitle>
            <Timeline events={inv.events} />
          </section>
        </aside>
      </div>

      <SendModal
        open={sendOpen}
        onClose={() => setSendOpen(false)}
        defaultTo={inv.client.email}
        kind="invoice"
        onSend={async (body) => {
          setData(await api<InvoiceDto>(`/api/invoices/${inv.id}/send`, { method: "POST", body }));
          setBanner("Invoice sent.");
        }}
      />
      <Modal open={payOpen} title="Record payment" onClose={() => setPayOpen(false)}>
        {payOpen && (
          <PaymentForm
            invoice={inv}
            onClose={() => setPayOpen(false)}
            onSaved={(updated) => {
              setData(updated);
              setStampAnim(true);
              setPayOpen(false);
              setBanner("Payment recorded.");
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function PaymentForm({ invoice, onClose, onSaved }: { invoice: InvoiceDto; onClose: () => void; onSaved: (inv: InvoiceDto) => void }) {
  const [amount, setAmount] = useState(toMajorInput(invoice.balanceMinor));
  const [method, setMethod] = useState<PaymentMethod>("BankTransfer");
  const [paidAt, setPaidAt] = useState(todayISO());
  const [reference, setReference] = useState("");
  const [note, setNote] = useState("");
  const [sendReceipt, setSendReceipt] = useState(!!invoice.client.email);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const amountMinor = toMinor(amount);
    if (!amountMinor || amountMinor <= 0) return setError("Enter the amount received.");
    setBusy(true);
    setError(null);
    const body: RecordPaymentRequest = {
      amountMinor,
      method,
      paidAt,
      reference: reference.trim() || null,
      note: note.trim() || null,
      sendReceipt,
    };
    try {
      onSaved(await api<InvoiceDto>(`/api/invoices/${invoice.id}/payments`, { method: "POST", body }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <Input
          label={`Amount (${invoice.currency})`}
          inputMode="decimal"
          required
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          hint={`Balance ${formatMoney(invoice.balanceMinor, invoice.currency)}`}
        />
        <Input label="Date paid" type="date" required value={paidAt} onChange={(e) => setPaidAt(e.target.value)} />
      </div>
      <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
        {METHODS.map((m) => (
          <option key={m.value} value={m.value}>
            {m.label}
          </option>
        ))}
      </Select>
      <Input label="Reference (optional)" value={reference} onChange={(e) => setReference(e.target.value)} placeholder="e.g. transfer ID" />
      <Textarea label="Note (optional)" rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
      <Checkbox
        checked={sendReceipt}
        onChange={setSendReceipt}
        label={invoice.client.email ? `Email a receipt to ${invoice.client.email}` : "Email a receipt (client has no email)"}
      />
      <ErrorBox error={error} />
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          Record payment
        </Button>
      </div>
    </form>
  );
}
