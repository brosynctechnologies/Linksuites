"use client";

import { api, asList } from "./api";
import { DEFAULT_ACCENT, STORAGE_PREFIX } from "./config";
import { addDays, todayISO } from "./dates";
import { parseNumber } from "./invoice-math";
import { toMinor } from "./money";
import { FREE_TEMPLATES } from "./templates";
import { DOCUMENT_TITLES, FONT_STYLE_OPTIONS, HEADER_LINE_OPTIONS, secondaryColorValue, type LetterheadText } from "./letterhead";
import type {
  BusinessDto,
  ClientDto,
  DocumentTitle,
  FontStyle,
  HeaderLine,
  InvoiceDto,
  PreviewRequest,
  SaveInvoiceRequest,
  TaxRateDto,
  UpdateBusinessRequest,
} from "./types";
import { isBlankLine, newLine, type EditorLine } from "@/components/line-items-editor";

/** The generator's business block: contact details plus the free letterhead choices (no images). */
export interface DraftBusiness extends LetterheadText {
  name: string;
  address: string;
  email: string;
  phone: string;
  taxId: string;
  bankDetails: string;
  brandColor: string;
  headerLine: HeaderLine;
  fontStyle: FontStyle;
  documentTitle: DocumentTitle;
  showSignature: boolean;
}

/** The free generator's form state, auto-saved to localStorage. */
export interface GeneratorDraft {
  business: DraftBusiness;
  client: { name: string; company: string; email: string; phone: string; address: string };
  number: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  template: string;
  lines: EditorLine[];
  shipping: string;
  notes: string;
  terms: string;
}

const KEY = `${STORAGE_PREFIX}draft`;

export function emptyDraft(): GeneratorDraft {
  const today = todayISO();
  return {
    business: {
      name: "",
      address: "",
      email: "",
      phone: "",
      taxId: "",
      bankDetails: "",
      brandColor: DEFAULT_ACCENT,
      tagline: "",
      registrationNumber: "",
      headerLine: "address",
      secondaryColor: "",
      fontStyle: "modern",
      documentTitle: "Invoice",
      showSignature: false,
      signatureName: "",
      signatureRole: "",
      footerText: "",
    },
    client: { name: "", company: "", email: "", phone: "", address: "" },
    number: "INV-0001",
    issueDate: today,
    dueDate: addDays(today, 14),
    currency: "NGN",
    template: "classic",
    lines: [newLine()],
    shipping: "",
    notes: "",
    terms: "",
  };
}

function oneOf<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return typeof value === "string" && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}

const text = (value: unknown) => (typeof value === "string" ? value : "");

export function loadDraft(): GeneratorDraft | null {
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return null;
    const d = JSON.parse(raw) as Partial<GeneratorDraft>;
    const base = emptyDraft();
    const business = { ...base.business, ...d.business };
    return {
      ...base,
      ...d,
      template: oneOf(d.template, FREE_TEMPLATES.map((t) => t.key), base.template),
      business: {
        ...business,
        tagline: text(business.tagline),
        registrationNumber: text(business.registrationNumber),
        secondaryColor: text(business.secondaryColor),
        signatureName: text(business.signatureName),
        signatureRole: text(business.signatureRole),
        footerText: text(business.footerText),
        headerLine: oneOf(business.headerLine, HEADER_LINE_OPTIONS.map((o) => o.value), base.business.headerLine),
        fontStyle: oneOf(business.fontStyle, FONT_STYLE_OPTIONS.map((o) => o.value), base.business.fontStyle),
        documentTitle: oneOf(business.documentTitle, DOCUMENT_TITLES, base.business.documentTitle),
        showSignature: business.showSignature === true,
      },
      client: { ...base.client, ...d.client },
      lines: Array.isArray(d.lines) && d.lines.length ? d.lines.map((l) => newLine(l)) : base.lines,
    };
  } catch {
    return null;
  }
}

export function saveDraft(d: GeneratorDraft) {
  try {
    window.localStorage.setItem(KEY, JSON.stringify(d));
  } catch {
    /* storage unavailable */
  }
}

export function clearDraft() {
  try {
    window.localStorage.removeItem(KEY);
  } catch {
    /* ignore */
  }
}

/** True when the stored draft has something worth importing. */
export function hasImportableDraft(d: GeneratorDraft | null): d is GeneratorDraft {
  return !!d && (d.lines.some((l) => !isBlankLine(l)) || !!d.client.name.trim());
}

export function toPreviewRequest(d: GeneratorDraft): PreviewRequest {
  const b = d.business;
  return {
    business: {
      name: b.name,
      address: b.address,
      email: b.email,
      phone: b.phone,
      taxId: b.taxId,
      bankDetails: b.bankDetails,
      brandColor: b.brandColor,
      tagline: b.tagline.trim(),
      registrationNumber: b.registrationNumber.trim(),
      headerLine: b.headerLine,
      secondaryColor: secondaryColorValue(b.secondaryColor),
      fontStyle: b.fontStyle,
      documentTitle: b.documentTitle,
      showSignature: b.showSignature,
      signatureName: b.signatureName.trim(),
      signatureRole: b.signatureRole.trim(),
      footerText: b.footerText.trim(),
    },
    client: { ...d.client },
    number: d.number,
    issueDate: d.issueDate,
    dueDate: d.dueDate,
    currency: d.currency,
    template: d.template,
    items: d.lines
      .filter((l) => !isBlankLine(l))
      .map((l) => ({
        description: clip(l.description, MAX.description),
        quantity: parseNumber(l.quantity),
        unitPriceMinor: toMinor(l.unitPrice) ?? 0,
        discountPercent: Math.min(100, Math.max(0, parseNumber(l.discountPercent))),
        taxRatePercent: parseNumber(l.taxPercent),
      })),
    shippingMinor: toMinor(d.shipping) ?? 0,
    notes: d.notes,
    terms: d.terms,
  };
}

const blank = (v: string | null | undefined) => !v || !v.trim();

/**
 * The API's length limits (InvoiceDtos.cs validators). The generator doesn't enforce them, so a long field is cut here
 * rather than failing the whole import with a 400.
 */
const MAX = {
  legalName: 120, address: 300, phone: 30, taxId: 50, bankDetails: 300, notes: 1000, terms: 2000,
  tagline: 100, registrationNumber: 40, signature: 80, footer: 160, clientName: 120, company: 120, description: 500,
} as const;
const clip = (v: string | null | undefined, max: number) => (v == null ? v : v.trim().slice(0, max)) as string;
const clipOrNull = (v: string | null | undefined, max: number) => (blank(v) ? null : clip(v, max));

/**
 * The generator's invoice number ("INV-0042") as the account's prefix and next number, so the saved invoice keeps the
 * number already on the PDF the client has. Null when it doesn't fit the account's rules (prefix of letters, digits,
 * - / _ up to 12 characters, then a number).
 */
export function splitInvoiceNumber(number: string | null | undefined): { prefix: string; next: number } | null {
  const m = /^([A-Za-z0-9/_-]*?)(\d{1,8})$/.exec((number ?? "").trim());
  if (!m || m[1].length > 12) return null;
  const next = Number(m[2]);
  return next >= 1 ? { prefix: m[1], next } : null;
}

/**
 * Turn the generator draft into real records for the signed-in account:
 * fills empty business settings, maps tax % to (new or existing) tax rates,
 * creates the client and a draft invoice. Returns the new invoice.
 */
export async function importDraft(d: GeneratorDraft, onStep?: (label: string) => void): Promise<InvoiceDto> {
  onStep?.("Saving your business details…");
  const business = await api<BusinessDto>("/api/business");
  const b = d.business;
  const keep = (saved: string | null, draft: string) => (blank(saved) ? draft.trim() : saved);
  // The letterhead was never customised while the slogan and registration number are still empty
  // and the line under the name is still the default (a deliberate "Nothing" is kept).
  const fresh = blank(business.tagline) && blank(business.registrationNumber) && business.headerLine === "address";
  // A new account (no invoice numbered yet) takes the draft's number, so the saved invoice matches the PDF already sent.
  const numbering = business.nextInvoiceNumber === 1 ? splitInvoiceNumber(d.number) : null;
  // PUT /api/business keeps letterhead text sent as null and clears it on "", so every field is sent explicitly.
  const update: UpdateBusinessRequest = {
    legalName: blank(business.legalName) ? clip(b.name, MAX.legalName) : business.legalName,
    address: blank(business.address) ? clip(b.address, MAX.address) : business.address,
    email: blank(business.email) ? b.email : business.email,
    phone: blank(business.phone) ? clip(b.phone, MAX.phone) : business.phone,
    taxId: blank(business.taxId) ? clip(b.taxId, MAX.taxId) : business.taxId,
    bankDetails: blank(business.bankDetails) ? clip(b.bankDetails, MAX.bankDetails) : business.bankDetails,
    currency: business.currency || d.currency,
    paymentTermsDays: business.paymentTermsDays,
    invoicePrefix: numbering ? numbering.prefix : business.invoicePrefix,
    nextInvoiceNumber: numbering ? numbering.next : business.nextInvoiceNumber,
    quotePrefix: business.quotePrefix,
    nextQuoteNumber: business.nextQuoteNumber,
    defaultTemplate: business.defaultTemplate,
    brandColor: blank(business.brandColor) || business.brandColor === DEFAULT_ACCENT ? b.brandColor || business.brandColor : business.brandColor,
    defaultNotes: blank(business.defaultNotes) ? clip(d.notes, MAX.notes) : business.defaultNotes,
    defaultTerms: blank(business.defaultTerms) ? clip(d.terms, MAX.terms) : business.defaultTerms,
    remindersEnabled: business.remindersEnabled,
    reminderDaysBefore: business.reminderDaysBefore,
    reminderDaysAfter: business.reminderDaysAfter ?? [],
    tagline: keep(business.tagline, clip(b.tagline, MAX.tagline)),
    registrationNumber: keep(business.registrationNumber, clip(b.registrationNumber, MAX.registrationNumber)),
    headerLine: fresh ? b.headerLine : business.headerLine,
    secondaryColor: business.secondaryColor ?? secondaryColorValue(b.secondaryColor),
    logoSize: business.logoSize,
    fontStyle: business.fontStyle === "modern" ? b.fontStyle : business.fontStyle,
    documentTitle: business.documentTitle === "Invoice" ? b.documentTitle : business.documentTitle,
    showTaxId: business.showTaxId,
    showRegistrationNumber: business.showRegistrationNumber,
    showBankDetails: business.showBankDetails,
    showSignature: business.showSignature || b.showSignature,
    signatureName: keep(business.signatureName, clip(b.signatureName, MAX.signature)),
    signatureRole: keep(business.signatureRole, clip(b.signatureRole, MAX.signature)),
    footerText: keep(business.footerText, clip(b.footerText, MAX.footer)),
  };
  await api<BusinessDto>("/api/business", { method: "PUT", body: update });

  const lines = d.lines.filter((l) => !isBlankLine(l));
  const rates = [...new Set(lines.map((l) => parseNumber(l.taxPercent)).filter((r) => r > 0))];
  const rateIds = new Map<number, string>();
  if (rates.length) {
    onStep?.("Setting up tax rates…");
    const existing = asList(await api<TaxRateDto[]>("/api/tax-rates"));
    for (const r of rates) {
      const hit = existing.find((t) => Number(t.ratePercent) === r);
      if (hit) {
        rateIds.set(r, hit.id);
      } else {
        const created = await api<TaxRateDto>("/api/tax-rates", { method: "POST", body: { name: `Tax ${r}%`, ratePercent: r } });
        rateIds.set(r, created.id);
      }
    }
  }

  onStep?.("Adding your client…");
  const c = d.client;
  const client = await api<ClientDto>("/api/clients", {
    method: "POST",
    body: {
      name: clip(c.name.trim() || c.company.trim() || "Client", MAX.clientName),
      company: clipOrNull(c.company, MAX.company),
      email: c.email || null,
      phone: clipOrNull(c.phone, MAX.phone),
      address: clipOrNull(c.address, MAX.address),
      taxId: null,
      notes: null,
    },
  });

  onStep?.("Creating your invoice…");
  const body: SaveInvoiceRequest = {
    clientId: client.id,
    issueDate: d.issueDate,
    dueDate: d.dueDate,
    currency: d.currency,
    template: d.template,
    items: lines.map((l) => ({
      itemId: null,
      description: clip(l.description, MAX.description),
      quantity: parseNumber(l.quantity),
      unitPriceMinor: toMinor(l.unitPrice) ?? 0,
      discountPercent: Math.min(100, Math.max(0, parseNumber(l.discountPercent))),
      taxRateId: rateIds.get(parseNumber(l.taxPercent)) ?? null,
    })),
    shippingMinor: toMinor(d.shipping) ?? 0,
    notes: clipOrNull(d.notes, MAX.notes),
    terms: clipOrNull(d.terms, MAX.terms),
  };
  const invoice = await api<InvoiceDto>("/api/invoices", { method: "POST", body });
  clearDraft();
  return invoice;
}
