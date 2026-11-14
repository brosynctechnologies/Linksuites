import type { CSSProperties, ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { cx } from "../styles";
import { DOC_FONT_STACK, docFontVariables } from "./fonts";
import { nameSizePt, nameWidthPt, type DocView, type LogoSize, type StampKind } from "./model";

/*
 * Shared blocks for the ten designs. Every design reads in the same order:
 * letterhead (logo, name, line under the name), title, number and dates, bill to, line table,
 * totals with balance due (the signature beside them), payment details and contact, notes and terms, footer line.
 *
 * Common measures (web; the PDF maps rem to pt at 1rem = 12pt unless a design says otherwise):
 * - Body text 0.8125rem, labels 0.75rem semibold muted (#5b6474), ink #1e2430, hairlines #e8ebf0.
 * - Business name 1.75rem bold, line height 1.15, never cut (a name too long for two lines steps down one size); the line under it 0.875rem #4a5262.
 * - Page padding clamp(1.25rem, 6.5cqw, 3.5rem) (the PDF uses its 40pt margin).
 */

export interface DesignProps {
  v: DocView;
  className?: string;
  animateStamp?: boolean;
}

/** Heading family: the serif for Elegant and Prestige, the body family otherwise. */
export const headingFont = "font-(family-name:--doc-heading)";

export const labelText = "text-[0.75rem] font-semibold text-muted";

export function Multiline({ text, className }: { text?: string | null; className?: string }) {
  if (!text) return null;
  return <p className={cx("whitespace-pre-line", className)}>{text}</p>;
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

/**
 * The white A4 sheet on the desk. Sets the document fonts and colour variables, pads the content,
 * and closes with the footer line (and the free-plan "Made with" line) at the bottom.
 */
export function Sheet({
  v,
  className,
  style,
  footerRule = "#e8ebf0",
  footerClass,
  children,
}: {
  v: DocView;
  className?: string;
  style?: CSSProperties;
  footerRule?: string;
  footerClass?: string;
  children: ReactNode;
}) {
  const serifHeadings = v.design === "elegant" || v.design === "prestige";
  const vars = {
    "--doc-accent": v.accent,
    "--doc-second": v.second,
    "--doc-heading": serifHeadings ? DOC_FONT_STACK.serif : DOC_FONT_STACK[v.fontStyle],
    "--pad": "clamp(1.25rem, 6.5cqw, 3.5rem)",
    fontFamily: DOC_FONT_STACK[v.fontStyle],
  } as CSSProperties;
  return (
    <div
      className={cx(
        docFontVariables,
        "@container relative flex flex-col rounded-doc bg-paper text-[0.8125rem] leading-snug text-graphite shadow-paper sm:aspect-[210/297]",
        className,
      )}
      style={{ ...vars, ...style }}
    >
      <div className="relative flex flex-1 flex-col p-(--pad)">
        {children}
        {(v.footerText || v.doc.showBranding) && (
          <div className="mt-auto pt-8">
            {v.footerText && (
              <p className={cx("border-t pt-3 text-center text-[0.75rem] text-muted", footerClass)} style={{ borderColor: footerRule }}>
                {v.footerText}
              </p>
            )}
            {v.doc.showBranding && (
              <p className={cx("text-center text-[0.75rem] text-muted", v.footerText && "mt-2")}>Made with {APP_NAME} — free invoices in 60 seconds</p>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

const LOGO_BOX: Record<LogoSize, string> = {
  small: "max-h-[2.5rem] max-w-[7rem]",
  medium: "max-h-[3.5rem] max-w-[10rem]",
  large: "max-h-[4.75rem] max-w-[13rem]",
};

/** The logo inside its size box (small 2.5 × 7rem, medium 3.5 × 10rem, large 4.75 × 13rem), aspect kept. */
export function Logo({ v, className, style }: { v: DocView; className?: string; style?: CSSProperties }) {
  if (!v.logoUrl) return null;
  return (
    <img src={v.logoUrl} alt={`${v.name} logo`} className={cx("block h-auto w-auto object-contain", LOGO_BOX[v.logoSize], className)} style={style} />
  );
}

/**
 * The business name: 21pt (1.75rem) bold ink by default; designs change size, weight, colour through props.
 * Never cut: it wraps, and a name too long for two lines steps down one size, by the PDF's rule (nameSizePt).
 */
export function BusinessName({
  v,
  sizePt = 21,
  stepToPt,
  weight = 700,
  lineHeight = 1.15,
  color = "#1e2430",
  className,
}: {
  v: DocView;
  /** Size in PDF points (1rem = 12pt). */
  sizePt?: number;
  /** The size a long name steps down to, when the design sets one (Studio). */
  stepToPt?: number;
  weight?: number;
  lineHeight?: number;
  color?: string;
  className?: string;
}) {
  const size = nameSizePt(v.name, sizePt, nameWidthPt(v), stepToPt) / 12;
  return (
    <p className={cx(headingFont, "[overflow-wrap:anywhere]", className)} style={{ fontSize: `${size}rem`, fontWeight: weight, lineHeight, color }}>
      {v.name}
    </p>
  );
}

/** The chosen line under the name (tagline, address on one line, or "Reg. no. …"), 0.875rem. */
export function LetterLine({ v, color = "#4a5262", className, style }: { v: DocView; color?: string; className?: string; style?: CSSProperties }) {
  if (!v.letterLine) return null;
  return (
    <p className={cx("mt-1 text-[0.875rem] [overflow-wrap:anywhere]", className)} style={{ color, ...style }}>
      {v.letterLine}
    </p>
  );
}

/**
 * Number and dates. "stack": label left, value right, three rows. "row": three columns, label above value.
 */
export function Meta({
  v,
  layout = "stack",
  className,
  labelClass = "text-muted",
  valueClass,
  style,
}: {
  v: DocView;
  layout?: "stack" | "row";
  className?: string;
  labelClass?: string;
  valueClass?: string;
  style?: CSSProperties;
}) {
  const items = [
    { label: v.numberLabel, value: v.doc.number || "—", strong: true },
    { label: "Issue date", value: formatDay(v.doc.issueDate) },
    { label: v.secondDateLabel, value: formatDay(v.doc.secondDate) },
  ];
  if (layout === "row") {
    return (
      <dl className={cx("flex flex-wrap", className)} style={{ columnGap: "2rem", rowGap: "0.5rem", ...style }}>
        {items.map((it) => (
          <div key={it.label} className="min-w-0 max-w-full">
            <dt className={cx("text-[0.75rem]", labelClass)}>{it.label}</dt>
            <dd className={cx("tabular-nums [overflow-wrap:anywhere]", it.strong && "font-semibold", valueClass)}>{it.value}</dd>
          </div>
        ))}
      </dl>
    );
  }
  // Labels keep their width; values take the rest of the 200pt (16.75rem) box and wrap, as in the PDF.
  return (
    <dl className={cx("inline-grid max-w-[16.75rem] grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-0.5", className)} style={style}>
      {items.map((it) => (
        <div key={it.label} className="contents">
          <dt className={cx("whitespace-nowrap", labelClass)}>{it.label}</dt>
          <dd className={cx("text-right tabular-nums [overflow-wrap:anywhere]", it.strong && "font-semibold", valueClass)}>{it.value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** Bill to (or Prepared for) with the status stamp on the right. */
export function BillTo({ v, animateStamp, labelClass = labelText, className }: { v: DocView; animateStamp?: boolean; labelClass?: string; className?: string }) {
  const p = v.doc.client;
  return (
    <div className={cx("mb-4 flex items-start justify-between gap-4", className)}>
      <div className="min-w-0 leading-snug text-slate-600 [overflow-wrap:anywhere]">
        <p className={cx("mb-1", labelClass)}>{v.doc.kind === "invoice" ? "Bill to" : "Prepared for"}</p>
        <p className="font-semibold text-graphite">{p.name || "—"}</p>
        {p.company && <p>{p.company}</p>}
        <Multiline text={p.address} />
        {p.email && <p className="break-all">{p.email}</p>}
        {p.phone && <p>{p.phone}</p>}
        {p.taxId && <p>Tax ID: {p.taxId}</p>}
      </div>
      {v.doc.stamp && (
        <div className="mt-2 mr-1 @md:mr-4">
          <DocStamp key={v.doc.stamp} kind={v.doc.stamp} animate={animateStamp} />
        </div>
      )}
    </div>
  );
}

function fmtQty(q: number) {
  return Number.isInteger(q) ? String(q) : String(Number(q.toFixed(4)));
}

/**
 * Line items. Four columns (Description, Qty, Price, Amount) from the @lg container width;
 * below that, description and amount with quantity × price underneath.
 */
export function LineTable({
  v,
  headClass,
  headStyle,
  rowClass,
  rowStyle,
  columnRules,
  figureClass,
  className,
}: {
  v: DocView;
  headClass?: string;
  headStyle?: CSSProperties;
  rowClass?: (index: number) => string | false | undefined;
  rowStyle?: CSSProperties;
  /** Light vertical rules between columns (Corporate, Ledger). */
  columnRules?: string;
  figureClass?: string;
  className?: string;
}) {
  const c = v.doc.currency;
  const grid = "grid grid-cols-[1fr_auto] @lg:grid-cols-[minmax(0,1fr)_3.5rem_7rem_8rem]";
  const cell = columnRules ? "px-2" : "";
  const ruled = (first: boolean) => (columnRules && !first ? "@lg:border-l" : "");
  const ruleStyle = columnRules ? { borderColor: columnRules } : undefined;
  return (
    <div className={cx("text-[0.8125rem]", className)} role="table" aria-label="Line items">
      <div
        role="row"
        className={cx(grid, !columnRules && "gap-x-3 px-2", "hidden py-2 text-[0.75rem] font-semibold @lg:grid", headClass)}
        style={headStyle}
      >
        <span role="columnheader" className={cell}>
          Description
        </span>
        <span role="columnheader" className={cx("text-right", cell, ruled(false))} style={ruleStyle}>
          Qty
        </span>
        <span role="columnheader" className={cx("text-right", cell, ruled(false))} style={ruleStyle}>
          Price
        </span>
        <span role="columnheader" className={cx("text-right", cell, ruled(false))} style={ruleStyle}>
          Amount
        </span>
      </div>
      {v.doc.lines.length === 0 && <p className="px-2 py-6 text-center text-muted">Add a line item and it appears here.</p>}
      {v.doc.lines.map((l, i) => (
        <div
          key={i}
          role="row"
          className={cx(grid, !columnRules && "gap-x-3 px-2", "border-b border-rule-soft py-2.5", rowClass?.(i))}
          style={rowStyle}
        >
          <div role="cell" className={cx("min-w-0", cell)}>
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
          <span role="cell" className={cx("hidden text-right tabular-nums @lg:block", cell, ruled(false))} style={ruleStyle}>
            {fmtQty(l.quantity)}
          </span>
          <span role="cell" className={cx("amount hidden text-right @lg:block", cell, ruled(false), figureClass)} style={ruleStyle}>
            {formatMoney(l.unitPriceMinor, c)}
          </span>
          <span role="cell" className={cx("amount text-right font-medium", cell, ruled(false), figureClass)} style={ruleStyle}>
            {formatMoney(l.lineTotalMinor, c)}
          </span>
        </div>
      ))}
    </div>
  );
}

/**
 * Subtotal, discount, taxes, shipping, the invoice total and amount paid, as label/value rows.
 * The design draws the emphasised figure (balance due or quote total) itself.
 */
export function TotalsRows({
  v,
  rowClass,
  totalClass = "font-semibold text-graphite",
  totalStyle,
  labelClass = "text-muted",
  figureClass,
}: {
  v: DocView;
  rowClass?: string;
  totalClass?: string;
  totalStyle?: CSSProperties;
  labelClass?: string;
  figureClass?: string;
}) {
  return (
    <>
      {v.rows.map((r) => (
        <div
          key={r.label}
          className={cx("flex items-baseline justify-between gap-4", rowClass, r.tone === "total" && totalClass)}
          style={r.tone === "total" ? totalStyle : undefined}
        >
          <dt className={r.tone === "total" ? undefined : labelClass}>{r.label}</dt>
          <dd className={cx("amount", figureClass)}>{r.value}</dd>
        </div>
      ))}
      {v.amountPaid && (
        <div className={cx("flex items-baseline justify-between gap-4", rowClass)}>
          <dt className={labelClass}>Amount paid</dt>
          <dd className={cx("amount", figureClass)}>{v.amountPaid}</dd>
        </div>
      )}
    </>
  );
}

/**
 * Payment details and the contact row side by side, then notes and terms.
 * `contact` lets a design drop an item it already prints in its letterhead (Ledger's reg. no.).
 */
export function InfoBlocks({
  v,
  labelClass = labelText,
  className,
  style,
  contact = v.contact,
}: {
  v: DocView;
  labelClass?: string;
  className?: string;
  style?: CSSProperties;
  contact?: string[];
}) {
  const { notes, terms } = v.doc;
  if (!v.bankDetails && !contact.length && !notes && !terms) return null;
  return (
    <div className={cx("space-y-3.5 text-slate-600 [overflow-wrap:anywhere]", className)} style={{ marginTop: "1.25rem", ...style }}>
      {(v.bankDetails || contact.length > 0) && (
        <div className="grid gap-x-5 gap-y-3.5 @md:grid-cols-2">
          {v.bankDetails && (
            <div>
              <p className={cx("mb-1", labelClass)}>Payment details</p>
              <Multiline text={v.bankDetails} />
            </div>
          )}
          {contact.length > 0 && (
            <div className={v.bankDetails ? undefined : "@md:col-span-2"}>
              <p className={cx("mb-1", labelClass)}>Contact</p>
              <ul className="flex flex-wrap gap-x-4 gap-y-0.5">
                {contact.map((item) => (
                  <li key={item} className="break-all">
                    {item}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
      {(notes || terms) && (
        <div className="grid gap-x-5 gap-y-3.5 @md:grid-cols-2">
          {notes && (
            <div>
              <p className={cx("mb-1", labelClass)}>Notes</p>
              <Multiline text={notes} />
            </div>
          )}
          {terms && (
            <div>
              <p className={cx("mb-1", labelClass)}>Terms</p>
              <Multiline text={terms} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * Signature block: space for the signature image sitting on a rule labelled "Authorised signature",
 * name and role centred below. The stamp image sits beside it at the left end of the rule (three
 * quarters outside, a quarter over the rule), two thirds above the rule and one third below, as a
 * real stamp would. Image space is only kept when there is a signature or stamp image; otherwise
 * there is 1.5rem (18pt) to sign in. PDF: rule 200pt wide, signature image max 48pt tall, stamp max
 * 80pt square. Placed beside the totals by TotalsWithSignature.
 */
export function SignatureBlock({ v, ruleColor = "#1e2430", className }: { v: DocView; ruleColor?: string; className?: string }) {
  const s = v.signature;
  if (!s) return null;
  const images = !!(s.imageUrl || s.stampUrl);
  return (
    <div className={cx("shrink-0", s.stampUrl && "pl-[max(3rem,10cqw)]", className)}>
      <div className="relative w-[max(11rem,34cqw)] text-center">
        {s.stampUrl && (
          <img
            src={s.stampUrl}
            alt="Company stamp"
            className="absolute left-0 size-[max(4rem,13.5cqw)] -translate-x-3/4 object-contain opacity-90 mix-blend-multiply"
            style={{ top: "calc(max(2.75rem, 8cqw) - max(4rem, 13.5cqw) * 2 / 3)" }}
          />
        )}
        <div className={cx("flex items-end justify-center", images ? "h-[max(2.75rem,8cqw)]" : "h-6")}>
          {s.imageUrl && <img src={s.imageUrl} alt="Signature" className="relative max-h-full w-auto max-w-full object-contain" />}
        </div>
        <div className="border-t pt-1.5 [overflow-wrap:anywhere]" style={{ borderColor: ruleColor }}>
          <p className="text-[0.75rem] text-muted">Authorised signature</p>
          {s.name && <p className="font-semibold text-graphite">{s.name}</p>}
          {s.role && <p className="text-slate-600">{s.role}</p>}
        </div>
      </div>
    </div>
  );
}

/**
 * The totals (`children`, the design's 20rem column) on the right with the signature block beside
 * them on the left, both bottom-aligned, as in the PDF: the signature sits with the amounts it
 * authorises. On narrow sheets the signature goes under the totals. Without a signature the totals
 * sit on the right, or in the centre with `center` (Elegant).
 */
export function TotalsWithSignature({
  v,
  children,
  center = false,
  ruleColor,
  className = "mt-4",
}: {
  v: DocView;
  children: ReactNode;
  center?: boolean;
  ruleColor?: string;
  className?: string;
}) {
  const align = v.signature ? "items-end @md:justify-between" : center ? "items-center @md:justify-center" : "items-end @md:justify-end";
  return (
    <div className={cx("flex flex-col-reverse gap-6 @md:flex-row @md:items-end", align, className)}>
      <SignatureBlock v={v} ruleColor={ruleColor} />
      {children}
    </div>
  );
}
