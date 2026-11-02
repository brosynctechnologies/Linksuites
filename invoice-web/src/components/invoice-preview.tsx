import type { CSSProperties, ReactNode } from "react";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { normalizeColor, tint } from "@/lib/templates";
import type { LineDto, TaxLine } from "@/lib/types";
import { APP_NAME } from "@/lib/config";
import { cx } from "./styles";

export interface PreviewLine {
  description: string;
  quantity: number;
  unitPriceMinor: number;
  discountPercent: number;
  /** e.g. "VAT 7.5%" */
  taxLabel?: string | null;
  lineTotalMinor: number;
}

export interface PreviewParty {
  name: string;
  company?: string | null;
  address?: string | null;
  email?: string | null;
  phone?: string | null;
  taxId?: string | null;
}

/** Statuses that are printed on the document as an ink stamp. */
export type StampKind = "Paid" | "Overdue" | "Void" | "Accepted" | "Declined" | "Expired";

export interface PreviewDoc {
  kind: "invoice" | "quote";
  template: string;
  brandColor?: string | null;
  number: string;
  business: PreviewParty & { logoUrl?: string | null; bankDetails?: string | null };
  client: PreviewParty;
  issueDate: string;
  /** Due date (invoice) or expiry date (quote). */
  secondDate: string;
  currency: string;
  lines: PreviewLine[];
  subtotalMinor: number;
  discountTotalMinor: number;
  taxes: TaxLine[];
  shippingMinor: number;
  totalMinor: number;
  amountPaidMinor?: number;
  balanceMinor?: number;
  notes?: string | null;
  terms?: string | null;
  showBranding?: boolean;
  /** Status stamp printed on the sheet (Paid, Overdue, Void, Accepted, Declined, Expired). */
  stamp?: StampKind | null;
}

/** Convert server line DTOs to preview lines. */
export function linesFromDto(items: LineDto[]): PreviewLine[] {
  return items.map((l) => ({
    description: l.description,
    quantity: l.quantity,
    unitPriceMinor: l.unitPriceMinor,
    discountPercent: l.discountPercent,
    taxLabel: l.taxRatePercent ? `${l.taxName ?? "Tax"} ${l.taxRatePercent}%` : null,
    lineTotalMinor: l.lineTotalMinor,
  }));
}

function fmtQty(q: number) {
  return Number.isInteger(q) ? String(q) : String(Number(q.toFixed(4)));
}

function Multiline({ text, className }: { text?: string | null; className?: string }) {
  if (!text) return null;
  return <p className={`whitespace-pre-line ${className ?? ""}`}>{text}</p>;
}

const STAMP_COLOR: Record<StampKind, string> = {
  Paid: "#13795b",
  Accepted: "#13795b",
  Overdue: "#b83227",
  Declined: "#b83227",
  Void: "#4a5262",
  Expired: "#a86a12",
};

/**
 * The rubber stamp: rotated, double-ruled, status colour at 85% opacity.
 * The word is uppercase here only; screen readers hear "Status: Paid".
 */
export function DocStamp({ kind, animate }: { kind: StampKind; animate?: boolean }) {
  return (
    <p
      className={cx("stamp shrink-0 text-[clamp(1rem,4.6cqw,1.75rem)]", animate && "animate-stamp")}
      style={{ color: STAMP_COLOR[kind], "--stamp-rotate": kind === "Void" || kind === "Declined" ? "-11deg" : "-7deg" } as CSSProperties}
    >
      <span className="sr-only">Status: </span>
      {kind}
    </p>
  );
}

function PartyBlock({ party, label, labelClass }: { party: PreviewParty; label: string; labelClass: string }) {
  return (
    <div className="min-w-0 text-[0.8125rem] leading-snug text-slate-600">
      <p className={labelClass}>{label}</p>
      <p className="font-semibold text-graphite">{party.name || "—"}</p>
      {party.company && <p>{party.company}</p>}
      <Multiline text={party.address} />
      {party.email && <p className="break-all">{party.email}</p>}
      {party.phone && <p>{party.phone}</p>}
      {party.taxId && <p>Tax ID: {party.taxId}</p>}
    </div>
  );
}

/**
 * The paper document: an HTML approximation of the server-rendered PDF, styled per template key,
 * drawn as a white sheet on the desk (A4 proportion from the sm breakpoint up).
 * Pure component (no hooks) so it works in both server and client components.
 * Uses container queries, so it adapts to the width of whatever column it sits in.
 */
export function InvoicePreview({ doc, className, animateStamp }: { doc: PreviewDoc; className?: string; animateStamp?: boolean }) {
  const accent = normalizeColor(doc.brandColor);
  const t = ["classic", "modern", "minimal", "bold", "elegant"].includes(doc.template) ? doc.template : "classic";
  const title = doc.kind === "invoice" ? "Invoice" : "Quote";
  const secondLabel = doc.kind === "invoice" ? "Due date" : "Valid until";
  const c = doc.currency;
  const isElegant = t === "elegant";
  const labelClass = cx("mb-1 text-[0.75rem] font-semibold", t === "minimal" ? "text-slate-600" : "text-muted");
  const style = { "--doc-accent": accent, "--pad": "clamp(1.25rem, 6.5cqw, 3.5rem)" } as CSSProperties;

  const logo = doc.business.logoUrl ? (
    <img src={doc.business.logoUrl} alt={`${doc.business.name} logo`} className="mb-2 max-h-14 max-w-[10rem] object-contain" />
  ) : null;

  const meta = (
    <dl className={cx("grid grid-cols-[auto_auto] gap-x-4 gap-y-0.5 text-[0.8125rem]", t === "modern" && "text-white/90")}>
      <dt className={t === "modern" ? "text-white/75" : "text-muted"}>{title} no.</dt>
      <dd className="text-right font-semibold tabular-nums">{doc.number || "—"}</dd>
      <dt className={t === "modern" ? "text-white/75" : "text-muted"}>Issue date</dt>
      <dd className="text-right tabular-nums">{formatDay(doc.issueDate)}</dd>
      <dt className={t === "modern" ? "text-white/75" : "text-muted"}>{secondLabel}</dt>
      <dd className="text-right tabular-nums">{formatDay(doc.secondDate)}</dd>
    </dl>
  );

  const businessText = (
    <div className={cx("min-w-0 text-[0.8125rem] leading-snug", t === "modern" ? "text-white/85" : "text-slate-600")}>
      <p className={cx("text-base font-bold", t === "modern" ? "text-white" : "text-graphite", isElegant && "font-display text-lg font-semibold")}>
        {doc.business.name || "Your business"}
      </p>
      <Multiline text={doc.business.address} />
      {doc.business.email && <p className="break-all">{doc.business.email}</p>}
      {doc.business.phone && <p>{doc.business.phone}</p>}
      {doc.business.taxId && <p>Tax ID: {doc.business.taxId}</p>}
    </div>
  );

  let header: ReactNode;
  if (t === "modern") {
    header = (
      <div
        className="mx-[calc(var(--pad)*-1)] mt-[calc(var(--pad)*-1)] mb-6 flex flex-col gap-4 rounded-t-doc px-(--pad) py-6 @md:flex-row @md:items-start @md:justify-between"
        style={{ background: accent }}
      >
        <div className="min-w-0">
          {logo && <div className="mb-2 inline-block rounded bg-white p-1.5">{logo}</div>}
          {businessText}
        </div>
        <div className="@md:text-right">
          <p className="mb-2 font-display text-[2rem] leading-none font-semibold text-white">{title}</p>
          {meta}
        </div>
      </div>
    );
  } else if (t === "bold") {
    header = (
      <div className="mb-6">
        <p className="font-display text-[2.75rem] leading-[0.9] font-[850] tracking-[-0.02em] @md:text-[3.75rem]" style={{ color: accent }}>
          {title}
        </p>
        <div className="mt-5 flex flex-col gap-4 @md:flex-row @md:justify-between">
          <div className="flex gap-3">
            {logo}
            {businessText}
          </div>
          {meta}
        </div>
      </div>
    );
  } else if (isElegant) {
    header = (
      <div className="mb-6 text-center">
        {logo && <div className="flex justify-center">{logo}</div>}
        <div className="[&_p]:text-center">{businessText}</div>
        <div className="my-5 border-y-4 border-double py-2" style={{ borderColor: accent }}>
          <p className="font-display text-2xl font-normal italic" style={{ color: accent }}>
            {title}
          </p>
        </div>
        <div className="flex justify-center">{meta}</div>
      </div>
    );
  } else {
    // classic + minimal
    header = (
      <div
        className={cx("mb-6 flex flex-col gap-4 @md:flex-row @md:justify-between", t === "classic" && "border-b-2 pb-6")}
        style={t === "classic" ? { borderColor: accent } : undefined}
      >
        <div className="min-w-0">
          {logo}
          {businessText}
        </div>
        <div className="@md:text-right">
          <p
            className={cx(
              "mb-2 font-display leading-none",
              t === "minimal" ? "text-[2.25rem] font-light text-graphite" : "text-[2rem] font-semibold",
            )}
            style={t === "classic" ? { color: accent } : undefined}
          >
            {title}
          </p>
          {meta}
        </div>
      </div>
    );
  }

  const headRowStyle: CSSProperties | undefined =
    t === "classic"
      ? { background: tint(accent, 0.1), color: "#1e2430" }
      : t === "modern"
        ? { background: "#1e2430", color: "#fff" }
        : t === "bold"
          ? { background: accent, color: "#fff" }
          : undefined;

  const rowGrid = "grid grid-cols-[1fr_auto] gap-x-3 @lg:grid-cols-[minmax(0,1fr)_3.5rem_7rem_8rem]";

  const balance = doc.balanceMinor ?? doc.totalMinor;
  const showPaid = doc.kind === "invoice" && (doc.amountPaidMinor ?? 0) > 0;

  return (
    <div
      className={cx(
        "@container relative flex flex-col rounded-doc bg-paper text-graphite shadow-paper sm:aspect-[210/297]",
        isElegant && "font-serif",
        t === "bold" && "border-l-8",
        className,
      )}
      style={{ ...style, ...(t === "bold" ? { borderLeftColor: accent } : {}) }}
    >
      <div className="flex flex-1 flex-col p-(--pad)">
        {header}

        <div className="mb-6 flex items-start justify-between gap-4">
          <PartyBlock party={doc.client} label={doc.kind === "invoice" ? "Bill to" : "Prepared for"} labelClass={labelClass} />
          {doc.stamp && (
            <div className="mt-2 mr-1 @md:mr-4">
              <DocStamp key={doc.stamp} kind={doc.stamp} animate={animateStamp} />
            </div>
          )}
        </div>

        {/* Line items */}
        <div className="text-[0.8125rem]" role="table" aria-label="Line items">
          <div
            role="row"
            className={cx(
              rowGrid,
              "hidden px-2 py-2 text-[0.75rem] font-semibold @lg:grid",
              t === "minimal" && "border-b border-graphite text-graphite",
              isElegant && "border-b text-muted",
              (t === "classic" || t === "modern" || t === "bold") && "rounded-[2px]",
            )}
            style={isElegant ? { ...headRowStyle, borderColor: accent } : headRowStyle}
          >
            <span role="columnheader">Description</span>
            <span role="columnheader" className="text-right">
              Qty
            </span>
            <span role="columnheader" className="text-right">
              Price
            </span>
            <span role="columnheader" className="text-right">
              Amount
            </span>
          </div>
          {doc.lines.length === 0 && <p className="px-2 py-6 text-center text-muted">Add a line item and it appears here.</p>}
          {doc.lines.map((l, i) => (
            <div
              key={i}
              role="row"
              className={cx(rowGrid, "border-b border-rule-soft px-2 py-2.5", t === "modern" && i % 2 === 1 && "bg-slate-50")}
            >
              <div role="cell" className="min-w-0">
                <p className="font-medium break-words whitespace-pre-line text-graphite">{l.description || "—"}</p>
                <p className="text-[0.75rem] text-muted tabular-nums @lg:hidden">
                  {fmtQty(l.quantity)} × {formatMoney(l.unitPriceMinor, c)}
                  {l.discountPercent > 0 && <span className="whitespace-nowrap">, less {l.discountPercent}%</span>}
                  {l.taxLabel && <span className="whitespace-nowrap">, {l.taxLabel}</span>}
                </p>
                {(l.discountPercent > 0 || l.taxLabel) && (
                  <p className="hidden text-[0.75rem] text-muted @lg:block">
                    {[l.discountPercent > 0 ? `Discount ${l.discountPercent}%` : null, l.taxLabel].filter(Boolean).join(", ")}
                  </p>
                )}
              </div>
              <span role="cell" className="hidden text-right tabular-nums @lg:block">
                {fmtQty(l.quantity)}
              </span>
              <span role="cell" className="amount hidden text-right @lg:block">
                {formatMoney(l.unitPriceMinor, c)}
              </span>
              <span role="cell" className="amount text-right font-medium">
                {formatMoney(l.lineTotalMinor, c)}
              </span>
            </div>
          ))}
        </div>

        {/* Totals */}
        <div className="mt-5 flex justify-end">
          <dl className="w-full max-w-xs space-y-1 text-[0.8125rem]">
            <Row label="Subtotal" value={formatMoney(doc.subtotalMinor, c)} />
            {doc.discountTotalMinor > 0 && <Row label="Discount" value={`−${formatMoney(doc.discountTotalMinor, c)}`} />}
            {doc.taxes.map((tx) => (
              <Row key={`${tx.name}-${tx.ratePercent}`} label={`${tx.name} (${tx.ratePercent}%)`} value={formatMoney(tx.amountMinor, c)} />
            ))}
            {doc.shippingMinor > 0 && <Row label="Shipping" value={formatMoney(doc.shippingMinor, c)} />}
            <div
              className={cx(
                "mt-1.5 flex items-baseline justify-between gap-4 pt-2.5",
                t === "bold" ? "-mx-2 rounded-[2px] px-2 pb-2.5 text-white" : "border-t-2",
                t === "minimal" && "border-graphite",
              )}
              style={
                t === "bold"
                  ? { background: accent }
                  : t === "minimal"
                    ? undefined
                    : { borderColor: accent, color: t === "modern" || t === "classic" ? accent : undefined }
              }
            >
              <dt className="text-sm font-semibold">Total</dt>
              <dd className="font-figure amount text-[1.375rem] leading-none">{formatMoney(doc.totalMinor, c)}</dd>
            </div>
            {showPaid && (
              <>
                <Row label="Amount paid" value={`−${formatMoney(doc.amountPaidMinor, c)}`} />
                <div className="flex items-baseline justify-between gap-4 border-t border-rule-soft pt-1.5 font-semibold">
                  <dt>Balance due</dt>
                  <dd className="amount text-base">{formatMoney(balance, c)}</dd>
                </div>
              </>
            )}
          </dl>
        </div>

        {(doc.business.bankDetails || doc.notes || doc.terms) && (
          <div className="mt-8 grid gap-5 border-t border-rule-soft pt-5 text-[0.8125rem] text-slate-600 @md:grid-cols-2">
            {doc.business.bankDetails && doc.kind === "invoice" && (
              <div>
                <p className={labelClass}>Payment details</p>
                <Multiline text={doc.business.bankDetails} />
              </div>
            )}
            {doc.notes && (
              <div>
                <p className={labelClass}>Notes</p>
                <Multiline text={doc.notes} />
              </div>
            )}
            {doc.terms && (
              <div>
                <p className={labelClass}>Terms</p>
                <Multiline text={doc.terms} />
              </div>
            )}
          </div>
        )}

        {doc.showBranding && (
          <p className="mt-auto pt-8 text-center text-[0.75rem] text-muted">Made with {APP_NAME} — free invoices in 60 seconds</p>
        )}
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted">{label}</dt>
      <dd className="amount">{value}</dd>
    </div>
  );
}
