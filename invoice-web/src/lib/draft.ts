"use client";

import { api, asList } from "./api";
import { DEFAULT_ACCENT, STORAGE_PREFIX } from "./config";
import { addDays, todayISO } from "./dates";
import { parseNumber } from "./invoice-math";
import { toMinor } from "./money";
import type { BusinessDto, ClientDto, InvoiceDto, PreviewRequest, SaveInvoiceRequest, TaxRateDto } from "./types";
import { isBlankLine, newLine, type EditorLine } from "@/components/line-items-editor";

/** The free generator's form state, auto-saved to localStorage. */
export interface GeneratorDraft {
  business: { name: string; address: string; email: string; phone: string; taxId: string; bankDetails: string; brandColor: string };
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
    business: { name: "", address: "", email: "", phone: "", taxId: "", bankDetails: "", brandColor: DEFAULT_ACCENT },
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

export function loadDraft(): GeneratorDraft | null {
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return null;
    const d = JSON.parse(raw) as Partial<GeneratorDraft>;
    const base = emptyDraft();
    return {
      ...base,
      ...d,
      business: { ...base.business, ...d.business },
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
  return {
    business: { ...d.business },
    client: { ...d.client },
    number: d.number,
    issueDate: d.issueDate,
    dueDate: d.dueDate,
    currency: d.currency,
    template: d.template,
    items: d.lines
      .filter((l) => !isBlankLine(l))
      .map((l) => ({
        description: l.description.trim(),
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
 * Turn the generator draft into real records for the signed-in account:
 * fills empty business settings, maps tax % to (new or existing) tax rates,
 * creates the client and a draft invoice. Returns the new invoice.
 */
export async function importDraft(d: GeneratorDraft, onStep?: (label: string) => void): Promise<InvoiceDto> {
  onStep?.("Saving your business details…");
  const business = await api<BusinessDto>("/api/business");
  const b = d.business;
  const update = {
    legalName: blank(business.legalName) ? b.name.trim() : business.legalName,
    address: blank(business.address) ? b.address : business.address,
    email: blank(business.email) ? b.email : business.email,
    phone: blank(business.phone) ? b.phone : business.phone,
    taxId: blank(business.taxId) ? b.taxId : business.taxId,
    bankDetails: blank(business.bankDetails) ? b.bankDetails : business.bankDetails,
    currency: business.currency || d.currency,
    paymentTermsDays: business.paymentTermsDays,
    invoicePrefix: business.invoicePrefix,
    nextInvoiceNumber: business.nextInvoiceNumber,
    quotePrefix: business.quotePrefix,
    nextQuoteNumber: business.nextQuoteNumber,
    defaultTemplate: business.defaultTemplate,
    brandColor: blank(business.brandColor) || business.brandColor === DEFAULT_ACCENT ? b.brandColor || business.brandColor : business.brandColor,
    defaultNotes: blank(business.defaultNotes) ? d.notes : business.defaultNotes,
    defaultTerms: blank(business.defaultTerms) ? d.terms : business.defaultTerms,
    remindersEnabled: business.remindersEnabled,
    reminderDaysBefore: business.reminderDaysBefore,
    reminderDaysAfter: business.reminderDaysAfter ?? [],
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
      name: c.name.trim() || c.company.trim() || "Client",
      company: c.company || null,
      email: c.email || null,
      phone: c.phone || null,
      address: c.address || null,
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
      description: l.description.trim(),
      quantity: parseNumber(l.quantity),
      unitPriceMinor: toMinor(l.unitPrice) ?? 0,
      discountPercent: Math.min(100, Math.max(0, parseNumber(l.discountPercent))),
      taxRateId: rateIds.get(parseNumber(l.taxPercent)) ?? null,
    })),
    shippingMinor: toMinor(d.shipping) ?? 0,
    notes: d.notes || null,
    terms: d.terms || null,
  };
  const invoice = await api<InvoiceDto>("/api/invoices", { method: "POST", body });
  clearDraft();
  return invoice;
}
