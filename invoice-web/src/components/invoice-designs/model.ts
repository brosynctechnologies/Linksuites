import type { LineDto, TaxLine } from "@/lib/types";
import { formatMoney } from "@/lib/money";
import { DOC_INK, normalizeColor, onColor, secondAccent, textOnWhite } from "@/lib/templates";
import type { FontStyle } from "./fonts";

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

export type HeaderLine = "tagline" | "address" | "registration" | "none";
export type LogoSize = "small" | "medium" | "large";
export type DocumentTitle = "Invoice" | "Tax invoice" | "Bill" | "Pro forma invoice" | "Statement of account";

/**
 * The business's letterhead settings (same names as BusinessSettings, camelCase). All optional:
 * missing values take the defaults in LETTERHEAD_DEFAULTS.
 */
export interface PreviewLetterhead {
  tagline?: string | null;
  registrationNumber?: string | null;
  headerLine?: HeaderLine | string | null;
  secondaryColor?: string | null;
  logoSize?: LogoSize | string | null;
  fontStyle?: FontStyle | string | null;
  documentTitle?: DocumentTitle | string | null;
  showTaxId?: boolean | null;
  showRegistrationNumber?: boolean | null;
  showBankDetails?: boolean | null;
  showSignature?: boolean | null;
  signatureName?: string | null;
  signatureRole?: string | null;
  signatureImageUrl?: string | null;
  stampImageUrl?: string | null;
  footerText?: string | null;
}

export const LETTERHEAD_DEFAULTS = {
  headerLine: "address" as HeaderLine,
  logoSize: "medium" as LogoSize,
  fontStyle: "modern" as FontStyle,
  documentTitle: "Invoice" as DocumentTitle,
  showTaxId: true,
  showRegistrationNumber: true,
  showBankDetails: true,
  showSignature: false,
};

export type PreviewBusiness = PreviewParty & PreviewLetterhead & { logoUrl?: string | null; bankDetails?: string | null };

export interface PreviewDoc {
  kind: "invoice" | "quote";
  template: string;
  brandColor?: string | null;
  number: string;
  business: PreviewBusiness;
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

export const DESIGN_KEYS = ["classic", "modern", "minimal", "bold", "elegant", "executive", "corporate", "ledger", "studio", "prestige"] as const;
export type DesignKey = (typeof DESIGN_KEYS)[number];

const TITLES: DocumentTitle[] = ["Invoice", "Tax invoice", "Bill", "Pro forma invoice", "Statement of account"];

function pick<T extends string>(value: string | null | undefined, allowed: readonly T[], fallback: T): T {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}

function clean(s: string | null | undefined) {
  const v = s?.trim();
  return v ? v : null;
}

export interface TotalRow {
  label: string;
  value: string;
  /** "total" is the document total on an invoice (shown above the balance due). */
  tone: "row" | "total";
}

/**
 * Everything a design needs, resolved once: colours, title, the line under the name, the contact
 * row, totals. The PDF composer computes the same values from the same rules.
 */
export interface DocView {
  doc: PreviewDoc;
  design: DesignKey;
  /** Accent (brand colour) and its contrast-safe partners. */
  accent: string;
  onAccent: string;
  accentText: string;
  /** Second accent (secondary colour, or the accent 35% darker) and its partners. */
  second: string;
  onSecond: string;
  secondText: string;
  ink: string;
  fontStyle: FontStyle;
  logoSize: LogoSize;
  logoUrl: string | null;
  name: string;
  /** The line under the name, already formatted, or null. */
  letterLine: string | null;
  headerLine: HeaderLine;
  title: string;
  /** "Invoice no." or "Quote no." */
  numberLabel: string;
  secondDateLabel: string;
  registrationText: string | null;
  taxIdText: string | null;
  /** Email, phone, address (when not the line under the name), tax ID, reg. no. (when not in the letterhead). */
  contact: string[];
  bankDetails: string | null;
  rows: TotalRow[];
  amountPaid: string | null;
  /** The emphasised figure: balance due on invoices, total on quotes. */
  grand: { label: string; value: string };
  signature: { name: string | null; role: string | null; imageUrl: string | null; stampUrl: string | null } | null;
  footerText: string | null;
}

export function resolveDoc(doc: PreviewDoc): DocView {
  const b = doc.business;
  const c = doc.currency;
  const accent = normalizeColor(doc.brandColor);
  const second = secondAccent(accent, b.secondaryColor);
  const design = pick(doc.template, DESIGN_KEYS, "classic");
  const headerLine = pick(b.headerLine, ["tagline", "address", "registration", "none"] as const, LETTERHEAD_DEFAULTS.headerLine);
  const showTaxId = b.showTaxId ?? LETTERHEAD_DEFAULTS.showTaxId;
  const showReg = b.showRegistrationNumber ?? LETTERHEAD_DEFAULTS.showRegistrationNumber;
  const showBank = b.showBankDetails ?? LETTERHEAD_DEFAULTS.showBankDetails;
  const address = clean(b.address);
  const reg = clean(b.registrationNumber);
  const registrationText = reg ? `Reg. no. ${reg}` : null;

  let letterLine: string | null = null;
  if (headerLine === "tagline") letterLine = clean(b.tagline);
  else if (headerLine === "address") letterLine = address ? address.replace(/\s*\n+\s*/g, ", ") : null;
  else if (headerLine === "registration") letterLine = registrationText;

  const taxIdText = showTaxId && clean(b.taxId) ? `Tax ID: ${clean(b.taxId)}` : null;
  const contact = [
    clean(b.email),
    clean(b.phone),
    headerLine === "address" && letterLine ? null : address ? address.replace(/\s*\n+\s*/g, ", ") : null,
    taxIdText,
    showReg && registrationText && !(headerLine === "registration" && letterLine) ? registrationText : null,
  ].filter((x): x is string => !!x);

  const rows: TotalRow[] = [{ label: "Subtotal", value: formatMoney(doc.subtotalMinor, c), tone: "row" }];
  if (doc.discountTotalMinor > 0) rows.push({ label: "Discount", value: `−${formatMoney(doc.discountTotalMinor, c)}`, tone: "row" });
  for (const tx of doc.taxes) rows.push({ label: `${tx.name} (${tx.ratePercent}%)`, value: formatMoney(tx.amountMinor, c), tone: "row" });
  if (doc.shippingMinor > 0) rows.push({ label: "Shipping", value: formatMoney(doc.shippingMinor, c), tone: "row" });

  const isInvoice = doc.kind === "invoice";
  if (isInvoice) rows.push({ label: "Total", value: formatMoney(doc.totalMinor, c), tone: "total" });
  const paid = isInvoice && (doc.amountPaidMinor ?? 0) > 0 ? `−${formatMoney(doc.amountPaidMinor, c)}` : null;
  const grand = isInvoice
    ? { label: "Balance due", value: formatMoney(doc.balanceMinor ?? doc.totalMinor - (doc.amountPaidMinor ?? 0), c) }
    : { label: "Total", value: formatMoney(doc.totalMinor, c) };

  const showSignature = b.showSignature ?? LETTERHEAD_DEFAULTS.showSignature;

  return {
    doc,
    design,
    accent,
    onAccent: onColor(accent),
    accentText: textOnWhite(accent),
    second,
    onSecond: onColor(second),
    secondText: textOnWhite(second),
    ink: DOC_INK,
    fontStyle: pick(b.fontStyle, ["modern", "serif", "sans"] as const, LETTERHEAD_DEFAULTS.fontStyle),
    logoSize: pick(b.logoSize, ["small", "medium", "large"] as const, LETTERHEAD_DEFAULTS.logoSize),
    logoUrl: clean(b.logoUrl),
    name: clean(b.name) ?? "Your business",
    letterLine,
    headerLine,
    title: isInvoice ? pick(b.documentTitle, TITLES, LETTERHEAD_DEFAULTS.documentTitle) : "Quote",
    numberLabel: isInvoice ? "Invoice no." : "Quote no.",
    secondDateLabel: isInvoice ? "Due date" : "Valid until",
    registrationText: showReg ? registrationText : null,
    taxIdText,
    contact,
    bankDetails: isInvoice && showBank ? clean(b.bankDetails) : null,
    rows,
    amountPaid: paid,
    grand,
    signature: showSignature
      ? { name: clean(b.signatureName), role: clean(b.signatureRole), imageUrl: clean(b.signatureImageUrl), stampUrl: clean(b.stampImageUrl) }
      : null,
    footerText: clean(b.footerText),
  };
}

/*
 * Business names are never cut. They wrap, and a name that would take more than two lines at its
 * design's size steps down one size. The rule and the widths are the PDF's (QuestPdfRenderer.NameSize),
 * in points, so the web and the PDF step down for the same names.
 */

/** How many characters fit on two lines `widthPt` wide at `sizePt`: about 0.6em a character, 90% of each line used. */
export function nameCapacity(widthPt: number, sizePt: number) {
  return Math.floor((3 * widthPt) / sizePt);
}

/** The name's size in points: one size down (80%, rounded, unless the design sets `stepToPt`) when it is too long for two lines. */
export function nameSizePt(name: string, sizePt: number, widthPt: number, stepToPt?: number) {
  return name.length > nameCapacity(widthPt, sizePt) ? (stepToPt ?? Math.round(sizePt * 0.8)) : sizePt;
}

const CONTENT_PT = 515.28; // A4 width less two 40pt margins
const LOGO_WIDTH_PT: Record<LogoSize, number> = { small: 100, medium: 140, large: 190 };

/** The room the business name has in each design's letterhead, in PDF points. */
export function nameWidthPt(v: DocView): number {
  const logoRoom = v.logoUrl ? LOGO_WIDTH_PT[v.logoSize] + 9 : 0;
  switch (v.design) {
    case "elegant":
    case "prestige":
      return CONTENT_PT;
    case "executive":
      return 595.28 * 0.4 - 40 - 18; // the sidebar, inside its padding
    case "bold":
      return CONTENT_PT - logoRoom;
    case "ledger": {
      const regBox = v.registrationText && !(v.headerLine === "registration" && v.letterLine);
      return CONTENT_PT - logoRoom - (regBox ? 132 : 0);
    }
    case "studio":
      return 595.28 * 0.58;
    default:
      return CONTENT_PT * 0.55;
  }
}

/** Business initials for the Prestige monogram, e.g. "Ada Designs Ltd" gives "AD". */
export function initials(name: string) {
  const skip = new Set(["ltd", "limited", "plc", "llc", "inc", "and", "of", "the"]);
  const words = name
    .split(/\s+/)
    .filter((w) => /^[\p{L}\p{N}]/u.test(w) && !skip.has(w.toLowerCase().replace(/\.$/, "")));
  return (words.length ? words : [name.trim() || "?"])
    .slice(0, 2)
    .map((w) => w.charAt(0).toUpperCase())
    .join("");
}
