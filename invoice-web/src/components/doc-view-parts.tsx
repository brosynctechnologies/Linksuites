"use client";

import { useState, type FormEvent } from "react";
import { toApiError, type ApiError } from "@/lib/api";
import { formatDateTime } from "@/lib/dates";
import type { BusinessDto, InvoiceDto, InvoiceEventDto, PublicBusiness, PublicInvoiceDto, PublicQuoteDto, QuoteDto } from "@/lib/types";
import { CheckIcon, CopyIcon } from "./icons";
import { linesFromDto, type PreviewDoc, type StampKind } from "./invoice-preview";
import { Button, ErrorBox, Input, Modal, Textarea } from "./ui";

type AnyDoc = InvoiceDto | QuoteDto | PublicInvoiceDto | PublicQuoteDto;

export function businessForPreview(b: BusinessDto | PublicBusiness | null | undefined): PreviewDoc["business"] & { brandColor?: string | null } {
  if (!b) return { name: "" };
  if ("legalName" in b) {
    return {
      name: b.legalName,
      logoUrl: b.logoUrl,
      address: b.address,
      email: b.email,
      phone: b.phone,
      taxId: b.taxId,
      bankDetails: b.bankDetails,
      brandColor: b.brandColor,
    };
  }
  return { ...b };
}

const STAMPS: Record<string, StampKind> = {
  Paid: "Paid",
  Overdue: "Overdue",
  Void: "Void",
  Accepted: "Accepted",
  Declined: "Declined",
  Expired: "Expired",
};

/** The status stamp printed on a document, if its status has one. */
export function stampFor(status: string | null | undefined): StampKind | null {
  return (status && STAMPS[status]) || null;
}

/** Build the HTML preview model from a server document (server totals are used as-is). */
export function previewFromDoc(doc: AnyDoc, business: BusinessDto | PublicBusiness | null | undefined, showBranding = false): PreviewDoc {
  const b = businessForPreview(business);
  const isInvoice = "dueDate" in doc;
  return {
    kind: isInvoice ? "invoice" : "quote",
    template: doc.template,
    brandColor: b.brandColor,
    number: doc.number,
    business: b,
    client: doc.client,
    issueDate: doc.issueDate,
    secondDate: isInvoice ? doc.dueDate : doc.expiryDate,
    currency: doc.currency,
    lines: linesFromDto(doc.items),
    subtotalMinor: doc.subtotalMinor,
    discountTotalMinor: doc.discountTotalMinor,
    taxes: doc.taxes ?? [],
    shippingMinor: doc.shippingMinor,
    totalMinor: doc.totalMinor,
    amountPaidMinor: "amountPaidMinor" in doc ? doc.amountPaidMinor : undefined,
    balanceMinor: "balanceMinor" in doc ? doc.balanceMinor : undefined,
    notes: doc.notes,
    terms: doc.terms,
    showBranding,
    stamp: stampFor(doc.status),
  };
}

const EVENT_LABEL: Record<string, string> = {
  Created: "Created",
  Sent: "Sent",
  Viewed: "Viewed by client",
  Reminded: "Reminder sent",
  PaymentRecorded: "Payment recorded",
  PaymentRemoved: "Payment removed",
  Paid: "Paid in full",
  Voided: "Voided",
  Overpaid: "Overpaid",
  Accepted: "Accepted by client",
  Declined: "Declined by client",
  Converted: "Converted to invoice",
};

export function Timeline({ events }: { events: InvoiceEventDto[] }) {
  if (!events?.length) return <p className="text-sm text-muted">No activity yet. Sending it starts the record.</p>;
  const sorted = [...events].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1));
  return (
    <ol className="relative space-y-4 before:absolute before:top-2 before:bottom-2 before:left-[3.5px] before:w-px before:bg-rule">
      {sorted.map((e, i) => (
        <li key={i} className="relative flex gap-3 text-sm">
          <span
            className={`relative mt-1.5 h-2 w-2 shrink-0 rounded-full ring-4 ring-paper ${i === 0 ? "bg-ink" : "bg-slate-300"}`}
            aria-hidden="true"
          />
          <div className="min-w-0">
            <p className="font-medium text-graphite">{EVENT_LABEL[e.type] ?? e.type}</p>
            {e.note && <p className="text-slate-600">{e.note}</p>}
            <p className="text-xs text-muted tabular-nums">{formatDateTime(e.createdAt)}</p>
          </div>
        </li>
      ))}
    </ol>
  );
}

/** Email the document link to the client. */
export function SendModal({
  open,
  onClose,
  defaultTo,
  kind,
  onSend,
}: {
  open: boolean;
  onClose: () => void;
  defaultTo: string | null | undefined;
  kind: "invoice" | "quote";
  onSend: (body: { to: string | null; message: string | null }) => Promise<void>;
}) {
  return (
    <Modal open={open} title={`Email ${kind}`} onClose={onClose}>
      {open && <SendForm defaultTo={defaultTo} kind={kind} onSend={onSend} onClose={onClose} />}
    </Modal>
  );
}

function SendForm({
  defaultTo,
  kind,
  onSend,
  onClose,
}: {
  defaultTo: string | null | undefined;
  kind: "invoice" | "quote";
  onSend: (body: { to: string | null; message: string | null }) => Promise<void>;
  onClose: () => void;
}) {
  const [to, setTo] = useState(defaultTo ?? "");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await onSend({ to: to.trim() || null, message: message.trim() || null });
      onClose();
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <Input label="To" type="email" required={!defaultTo} value={to} onChange={(e) => setTo(e.target.value)} placeholder="client@example.com" />
      <Textarea
        label="Message (optional)"
        rows={4}
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        placeholder={`Hi, please find your ${kind} attached. Thank you!`}
      />
      <p className="text-xs text-slate-500">Your client gets an email with a secure link to view and download the {kind}.</p>
      <ErrorBox error={error} />
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          Send {kind}
        </Button>
      </div>
    </form>
  );
}

export function CopyLinkButton({ url, className }: { url: string; className?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      variant="secondary"
      className={className}
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(url);
        } catch {
          window.prompt("Copy this link", url);
        }
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      }}
    >
      {copied ? <CheckIcon className="h-4 w-4" /> : <CopyIcon className="h-4 w-4" />}
      {copied ? "Copied" : "Copy link"}
    </Button>
  );
}
