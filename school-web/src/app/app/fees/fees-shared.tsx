"use client";

import { useState, type FormEvent, type SVGProps } from "react";
import { api, downloadFile } from "@/lib/api";
import { formatDay, lagosToday, naira } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { can, useRole, type Action } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { SchoolSetupDto, TermDto } from "@/lib/types";
import type { FeePaymentDto, FeePaymentMethod, FeePaymentStatus, InvoiceStatus, RecordPaymentRequest } from "@/lib/types/fees";
import { Badge, Button, ErrorBox, Input, Modal, NoAccess, Select, type BadgeTone } from "@/components/ui";

/** A banknote: the Fees nav icon. */
export function FeesIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      width={20}
      height={20}
      aria-hidden="true"
      {...p}
    >
      <rect x="2.5" y="6" width="19" height="12" rx="2" />
      <circle cx="12" cy="12" r="2.5" />
      <path d="M6 9.5v5M18 9.5v5" />
    </svg>
  );
}

/** True when the signed-in role may see this fee area (a support view reads it too). */
export function useFeeRole(action: Action = "fees.manage"): boolean {
  return can(useRole(), action);
}

export function FeesNoAccess() {
  return <NoAccess text="Fees are for the proprietor, the principal and the bursar." />;
}

export interface TermOption extends TermDto {
  sessionId: string;
  sessionName: string;
  label: string;
}

/** Every term of every session, newest first, plus the current one. */
export function useTerms() {
  const { data, error, reload } = useApi<SchoolSetupDto>("/api/school/setup");
  const terms: TermOption[] = (data?.sessions ?? [])
    .flatMap((s) =>
      s.terms.map((t) => ({ ...t, sessionId: s.id, sessionName: s.name, label: `${t.name}, ${s.name}`, isCurrent: s.isCurrent && t.isCurrent })),
    )
    .sort((a, b) => b.startDate.localeCompare(a.startDate));
  const current = terms.find((t) => t.isCurrent) ?? terms[0];
  return { terms, current, loaded: !!data, error, reload };
}

/** The chosen term, falling back to the current one until the user picks. */
export function useTermChoice() {
  const t = useTerms();
  const [picked, setPicked] = useState("");
  const termId = picked || t.current?.id || "";
  return { ...t, termId, setTermId: setPicked, term: t.terms.find((x) => x.id === termId) };
}

export function TermSelect({ terms, value, onChange, className = "w-56" }: { terms: TermOption[]; value: string; onChange: (id: string) => void; className?: string }) {
  return (
    <div className={className}>
      <Select label="Term" srLabel value={value} onChange={(e) => onChange(e.target.value)}>
        {terms.length === 0 && <option value="">No terms yet</option>}
        {terms.map((t) => (
          <option key={t.id} value={t.id}>
            {t.label}
            {t.isCurrent ? " (current)" : ""}
          </option>
        ))}
      </Select>
    </div>
  );
}

// ---------- Money ----------

/** "60,000" or "60000.50" (naira) to kobo; null when it isn't a positive amount. */
export function parseNaira(text: string): number | null {
  const clean = text.replace(/[₦,\s]/g, "");
  if (!/^\d+(\.\d{1,2})?$/.test(clean)) return null;
  const kobo = Math.round(Number(clean) * 100);
  return kobo > 0 ? kobo : null;
}

/** Kobo to an editable naira string: 6000000 → "60000". */
export function nairaInput(minor: number): string {
  return minor % 100 === 0 ? String(minor / 100) : (minor / 100).toFixed(2);
}

/** ₦ with kobo when there are any: ₦1,250.50. */
export function money(minor: number): string {
  if (minor % 100 === 0) return naira(minor);
  const sign = minor < 0 ? "-" : "";
  return `${sign}₦${(Math.abs(minor) / 100).toLocaleString("en-NG", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

// ---------- Status words ----------

const INVOICE_STATUS: Record<InvoiceStatus, { label: string; tone: BadgeTone }> = {
  Unpaid: { label: "Unpaid", tone: "brick" },
  PartlyPaid: { label: "Part paid", tone: "crest" },
  Paid: { label: "Paid", tone: "pass" },
  Cancelled: { label: "Cancelled", tone: "neutral" },
};

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  const s = INVOICE_STATUS[status] ?? { label: status, tone: "neutral" as const };
  return <Badge tone={s.tone}>{s.label}</Badge>;
}

export const METHOD_LABEL: Record<FeePaymentMethod, string> = {
  Online: "Online (Paystack)",
  Cash: "Cash",
  Transfer: "Bank transfer",
  Pos: "POS",
};

/** What each online status means, in the school's words. */
export const PAYMENT_STATUS_TEXT: Record<FeePaymentStatus, string> = {
  Pending: "The parent started paying online and hasn't finished.",
  Succeeded: "Received. It counts towards the invoice.",
  Mismatch: "Paystack reported a different amount or currency, so it was not applied. Check the reference in your Paystack dashboard.",
  Abandoned: "Payment not confirmed. If you were charged, it will update automatically.",
};

export function PaymentStatusBadge({ payment }: { payment: Pick<FeePaymentDto, "status" | "reversedAt"> }) {
  if (payment.status === "Succeeded" && payment.reversedAt) return <Badge tone="neutral">Reversed</Badge>;
  switch (payment.status) {
    case "Succeeded":
      return <Badge tone="pass">Received</Badge>;
    case "Mismatch":
      return <Badge tone="brick">Amount mismatch</Badge>;
    case "Abandoned":
      return <Badge tone="crest">Not confirmed</Badge>;
    default:
      return <Badge tone="neutral">Pending</Badge>;
  }
}

/** Downloads a received payment's receipt PDF. */
export function ReceiptButton({ payment }: { payment: FeePaymentDto }) {
  const { busy, error, run } = useAction();
  if (payment.status !== "Succeeded" || !payment.receiptNumber) return null;
  return (
    <span className="inline-flex flex-col items-end">
      <Button
        variant="ghost"
        className="px-2.5"
        loading={busy}
        onClick={() => run(() => downloadFile(`/api/school/payments/${payment.id}/receipt.pdf`, `${payment.receiptNumber}.pdf`))}
      >
        Receipt {payment.receiptNumber}
      </Button>
      {error && <span className="text-xs text-brick">{error.message}</span>}
    </span>
  );
}

// ---------- Record a payment ----------

const MANUAL_METHODS: RecordPaymentRequest["method"][] = ["Cash", "Transfer", "Pos"];

/** Record money the school received in person (cash, transfer, POS). Overpayment is refused by the API. */
export function RecordPaymentModal({
  invoice,
  onClose,
  onSaved,
}: {
  invoice: { id: string; number: string; studentName: string; balance: number };
  onClose: () => void;
  onSaved: (payment: FeePaymentDto) => void;
}) {
  const [amount, setAmount] = useState(nairaInput(invoice.balance));
  const [method, setMethod] = useState<RecordPaymentRequest["method"]>("Cash");
  const [reference, setReference] = useState("");
  const [paidOn, setPaidOn] = useState(lagosToday());
  const [amountError, setAmountError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const minor = parseNaira(amount);
    if (minor === null) {
      setAmountError("Enter the amount in naira, for example 25000.");
      return;
    }
    if (minor > invoice.balance) {
      setAmountError(`That is more than the balance of ${money(invoice.balance)}. Enter that amount or less.`);
      return;
    }
    setAmountError(null);
    const body: RecordPaymentRequest = { amountMinor: minor, method, reference: reference.trim() || null, paidOn };
    const res = await run(() => api<FeePaymentDto>(`/api/school/invoices/${invoice.id}/payments`, { method: "POST", body }));
    if (res.ok) onSaved(res.data);
  }

  const known = ["amountMinor", "method", "reference", "paidOn"];
  const other = error && !known.some((f) => error.field(f)) ? error : null;
  return (
    <Modal open title="Record a payment" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">
          {invoice.studentName}, invoice <span className="tnum">{invoice.number}</span>. Balance <span className="tnum font-semibold">{money(invoice.balance)}</span>.
        </p>
        <Input
          label="Amount (₦)"
          inputMode="decimal"
          required
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          error={amountError ?? error?.field("amountMinor")}
        />
        <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value as RecordPaymentRequest["method"])} error={error?.field("method")}>
          {MANUAL_METHODS.map((m) => (
            <option key={m} value={m}>
              {METHOD_LABEL[m]}
            </option>
          ))}
        </Select>
        <Input
          label="Reference (optional)"
          placeholder="e.g. teller or transfer reference"
          maxLength={100}
          value={reference}
          onChange={(e) => setReference(e.target.value)}
          error={error?.field("reference")}
        />
        <Input label="Date received" type="date" required max={lagosToday()} value={paidOn} onChange={(e) => setPaidOn(e.target.value)} error={error?.field("paidOn")} />
        <ErrorBox error={other} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Record payment
          </Button>
        </div>
      </form>
    </Modal>
  );
}

/** "Due 12 Oct 2026". */
export function dueText(date: string) {
  return `Due ${formatDay(date)}`;
}
