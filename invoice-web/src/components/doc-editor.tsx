"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useId, useMemo, useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { CURRENCIES } from "@/lib/config";
import { addDays, todayISO } from "@/lib/dates";
import { computeTotals } from "@/lib/invoice-math";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { parseRate, rateFor, rateInput, toBaseMinor, useExchangeRates } from "@/lib/fx";
import { useCan } from "@/lib/permissions";
import { TEMPLATES } from "@/lib/templates";
import { useApi } from "@/lib/use-api";
import { useQueryParam } from "@/lib/use-debounced";
import type {
  BusinessDto,
  ClientDto,
  ExchangeRatesDto,
  InvoiceDto,
  ItemDto,
  QuoteDto,
  SaveInvoiceRequest,
  SaveQuoteRequest,
  TaxRateDto,
  TemplateDto,
} from "@/lib/types";
import { ClientPicker, type PickedClient } from "./client-picker";
import { InvoicePreview, type PreviewDoc } from "./invoice-preview";
import { LineItemsEditor, editorLinesFrom, isBlankLine, mathInput, newLine, previewLines, toLineRequests, type EditorLine } from "./line-items-editor";
import { TemplatePicker } from "./template-picker";
import { ExchangeRateField } from "./fx";
import { ApiErrorView, Button, Card, ErrorBox, Input, NoAccess, PageLoader, Segmented, Select, Textarea, cx, linkClass } from "./ui";

export type DocKind = "invoice" | "quote";

/** Loads business settings, tax rates, templates and saved items, then shows the editor. */
export function DocEditor({ kind, existing }: { kind: DocKind; existing?: InvoiceDto | QuoteDto }) {
  const canEdit = useCan("docs.edit");
  const business = useApi<BusinessDto>("/api/business");
  const rates = useExchangeRates();
  const taxRates = useApi<TaxRateDto[]>("/api/tax-rates");
  const templates = useApi<TemplateDto[]>("/api/invoice-templates");
  const items = useApi<ItemDto[]>("/api/items");

  if (!canEdit) return <NoAccess text={`Your role can view ${kind}s but not create or edit them.`} />;
  const error = business.error ?? taxRates.error;
  if (error) {
    return (
      <ApiErrorView
        error={error}
        onRetry={() => {
          business.reload();
          taxRates.reload();
        }}
      />
    );
  }
  if (!business.data || !taxRates.data) return <PageLoader />;

  const tpl = asList(templates.data);
  return (
    <EditorForm
      kind={kind}
      existing={existing}
      business={business.data}
      taxRates={asList(taxRates.data)}
      templates={tpl.length ? tpl : TEMPLATES.map((t) => ({ ...t, available: !t.isPremium }))}
      items={asList(items.data)}
      rates={rates.data}
      ratesLoading={rates.loading}
    />
  );
}

function EditorForm({
  kind,
  existing,
  business,
  taxRates,
  templates,
  items,
  rates,
  ratesLoading,
}: {
  kind: DocKind;
  existing?: InvoiceDto | QuoteDto;
  business: BusinessDto;
  taxRates: TaxRateDto[];
  templates: TemplateDto[];
  items: ItemDto[];
  rates: ExchangeRatesDto | undefined;
  ratesLoading: boolean;
}) {
  const router = useRouter();
  const isInvoice = kind === "invoice";
  const defaultDays = isInvoice ? business.paymentTermsDays || 0 : 30;
  const today = todayISO();

  const [client, setClient] = useState<PickedClient | null>(() =>
    existing?.client.id ? { ...existing.client, id: existing.client.id } : null,
  );
  const [issueDate, setIssueDate] = useState(existing?.issueDate ?? today);
  const [secondDate, setSecondDate] = useState(() => {
    if (existing) return "dueDate" in existing ? existing.dueDate : existing.expiryDate;
    return addDays(today, defaultDays);
  });
  const [currency, setCurrency] = useState(existing?.currency ?? business.currency ?? "NGN");
  const [template, setTemplate] = useState(() => {
    const want = existing?.template ?? business.defaultTemplate ?? "classic";
    const t = templates.find((x) => x.key === want);
    return t && (existing || !t.isPremium || t.available !== false) ? want : "classic";
  });
  const [lines, setLines] = useState<EditorLine[]>(() => (existing?.items.length ? editorLinesFrom(existing.items) : [newLine()]));
  const [shipping, setShipping] = useState(existing?.shippingMinor ? toMajorInput(existing.shippingMinor) : "");
  const [notes, setNotes] = useState(existing ? (existing.notes ?? "") : (business.defaultNotes ?? ""));
  const [terms, setTerms] = useState(existing ? (existing.terms ?? "") : (business.defaultTerms ?? ""));
  const [tab, setTab] = useState<"edit" | "preview">("edit");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [clientError, setClientError] = useState<string>();
  const clientIdParam = useQueryParam("clientId");
  const rateId = useId();
  const baseCurrency = business.currency || "NGN";
  // A rate the user typed (or the document's saved rate), remembered per currency; otherwise today's rate is suggested.
  const [rateEdit, setRateEdit] = useState<{ currency: string; value: string } | null>(() =>
    existing && existing.exchangeRate && existing.currency !== baseCurrency ? { currency: existing.currency, value: rateInput(existing.exchangeRate) } : null,
  );
  const [rateError, setRateError] = useState<string | null>(null);
  const foreign = currency !== baseCurrency;
  const rateValue = rateEdit?.currency === currency ? rateEdit.value : rateInput(rateFor(rates, currency));
  const rate = foreign ? parseRate(rateValue) : 1;

  // Prefill the client from ?clientId= (e.g. "New invoice" on a client page).
  useEffect(() => {
    if (existing) return;
    const id = clientIdParam;
    if (!id) return;
    let live = true;
    api<ClientDto>(`/api/clients/${id}`).then(
      (c) => live && setClient((cur) => cur ?? c),
      () => {},
    );
    return () => {
      live = false;
    };
  }, [existing, clientIdParam]);

  const changeIssue = (v: string) => {
    if (!v) return setIssueDate(v);
    // Keep the due/expiry date in step if it still matches the default offset.
    if (secondDate === addDays(issueDate, defaultDays) || secondDate < v) setSecondDate(addDays(v, defaultDays));
    setIssueDate(v);
  };

  const preview = useMemo<PreviewDoc>(() => {
    const filled = lines.filter((l) => !isBlankLine(l));
    const totals = computeTotals(
      filled.map((l) => mathInput(l, taxRates)),
      toMinor(shipping) ?? 0,
      existing && "amountPaidMinor" in existing ? existing.amountPaidMinor : 0,
    );
    const prefix = isInvoice ? business.invoicePrefix : business.quotePrefix;
    const next = isInvoice ? business.nextInvoiceNumber : business.nextQuoteNumber;
    return {
      kind,
      template,
      brandColor: business.brandColor,
      // Padded to 4 digits like the server (InvoiceWorkspace.NextNumberAsync, "D4"): INV-0007, not INV-7.
      number: existing?.number ?? `${prefix ?? ""}${next != null ? String(next).padStart(4, "0") : ""}`,
      business: {
        name: business.legalName,
        logoUrl: business.logoUrl,
        address: business.address,
        email: business.email,
        phone: business.phone,
        taxId: business.taxId,
        bankDetails: business.bankDetails,
      },
      client: client ?? { name: "Choose a client" },
      issueDate,
      secondDate,
      currency,
      lines: previewLines(lines, taxRates),
      subtotalMinor: totals.subtotalMinor,
      discountTotalMinor: totals.discountTotalMinor,
      taxes: totals.taxes,
      shippingMinor: totals.shippingMinor,
      totalMinor: totals.totalMinor,
      amountPaidMinor: totals.amountPaidMinor,
      balanceMinor: totals.balanceMinor,
      notes,
      terms,
    };
  }, [lines, taxRates, shipping, existing, isInvoice, business, kind, template, client, issueDate, secondDate, currency, notes, terms]);

  async function save() {
    setError(null);
    setClientError(undefined);
    if (!client) {
      setClientError("Choose or add a client.");
      setTab("edit");
      return;
    }
    const reqLines = toLineRequests(lines);
    if (reqLines.length === 0) {
      setError("Add at least one line item.");
      setTab("edit");
      return;
    }
    if (reqLines.some((l) => !l.description)) {
      setError("Every line needs a description.");
      setTab("edit");
      return;
    }
    if (secondDate && issueDate && secondDate < issueDate) {
      setError(isInvoice ? "The due date can't be before the issue date." : "The expiry date can't be before the issue date.");
      return;
    }
    const common = {
      clientId: client.id,
      issueDate,
      currency,
      template,
      items: reqLines,
      shippingMinor: toMinor(shipping) ?? 0,
      notes: notes.trim() || null,
      terms: terms.trim() || null,
    };
    if (foreign && rateValue.trim() && rate === null) {
      setRateError("Enter a valid exchange rate, e.g. 1580.25.");
      setTab("edit");
      return;
    }
    const withRate = foreign && rate ? { ...common, exchangeRate: rate } : common;
    const base = isInvoice ? "/api/invoices" : "/api/quotes";
    const body: SaveInvoiceRequest | SaveQuoteRequest = isInvoice ? { ...withRate, dueDate: secondDate } : { ...withRate, expiryDate: secondDate };
    setBusy(true);
    try {
      const saved = existing
        ? await api<{ id: string }>(`${base}/${existing.id}`, { method: "PUT", body })
        : await api<{ id: string }>(base, { method: "POST", body });
      router.push(`/app/${isInvoice ? "invoices" : "quotes"}/${saved.id}${existing ? "" : "?created=1"}`);
    } catch (e) {
      const err = toApiError(e);
      setBusy(false);
      if (err.status === 503 && err.code === "fx_unavailable") {
        setRateError(`We couldn't get today's ${currency} → ${baseCurrency} rate. Type the rate to continue.`);
        setTab("edit");
        setTimeout(() => document.getElementById(rateId)?.focus(), 0);
        return;
      }
      setError(err);
    }
  }

  const label = isInvoice ? "invoice" : "quote";

  return (
    <div>
      <Segmented
        className="mb-4 max-w-xs xl:hidden"
        label="Editor view"
        items={[
          { value: "edit", label: "Edit" },
          { value: "preview", label: "Preview" },
        ]}
        value={tab}
        onChange={setTab}
      />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,1.05fr)] xl:gap-8">
        <div className={cx("min-w-0 space-y-4", tab !== "edit" && "hidden xl:block")}>
          <Card className="space-y-4">
            <ClientPicker value={client} onChange={setClient} error={clientError} />
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              <Input label="Issue date" type="date" value={issueDate} onChange={(e) => changeIssue(e.target.value)} />
              <Input
                label={isInvoice ? "Due date" : "Valid until"}
                type="date"
                value={secondDate}
                min={issueDate}
                onChange={(e) => setSecondDate(e.target.value)}
              />
              <Select
                label="Currency"
                value={currency}
                onChange={(e) => {
                  setCurrency(e.target.value);
                  setRateError(null);
                }}
              >
                {[...new Set([currency, baseCurrency, ...CURRENCIES])].map((c) => (
                  <option key={c}>{c}</option>
                ))}
              </Select>
            </div>
            {foreign && (
              <ExchangeRateField
                id={rateId}
                currency={currency}
                base={baseCurrency}
                value={rateValue}
                onChange={(v) => {
                  setRateEdit({ currency, value: v });
                  setRateError(null);
                }}
                error={rateError}
                asOf={rates?.asOf}
                loading={ratesLoading}
              />
            )}
          </Card>

          <Card>
            <h2 className="mb-3 font-semibold text-graphite">Items</h2>
            <LineItemsEditor lines={lines} onChange={setLines} currency={currency} taxRates={taxRates} items={items} />
            {taxRates.length === 0 && (
              <p className="mt-3 text-xs text-muted">
                Need VAT or another tax? Add tax rates in{" "}
                <Link href="/app/settings#tax-rates" className={linkClass}>
                  Settings
                </Link>
                .
              </p>
            )}
            <div className="mt-4 grid grid-cols-2 gap-3">
              <Input label={`Shipping (${currency})`} inputMode="decimal" placeholder="0" value={shipping} onChange={(e) => setShipping(e.target.value)} />
            </div>
          </Card>

          <Card className="space-y-3">
            <Textarea label="Notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
            <Textarea label="Terms" rows={2} value={terms} onChange={(e) => setTerms(e.target.value)} />
          </Card>

          <Card>
            <TemplatePicker templates={templates} value={template} onChange={setTemplate} brandColor={business.brandColor} />
          </Card>
        </div>

        <div className={cx("min-w-0 space-y-3 xl:sticky xl:top-8 xl:self-start", tab !== "preview" && "hidden xl:block")}>
          <p className="text-xs text-muted">Live preview. Final totals are calculated when you save.</p>
          <InvoicePreview doc={preview} />
          {foreign && rate !== null && (
            <p className="amount text-right text-sm text-slate-600">
              ≈ {formatMoney(toBaseMinor(preview.totalMinor, rate), baseCurrency)} in {baseCurrency}
            </p>
          )}
        </div>
      </div>

      <div className="sticky bottom-[calc(4.75rem+env(safe-area-inset-bottom))] z-20 mt-5 rounded-panel border border-rule bg-paper/95 p-3 shadow-float backdrop-blur sm:px-4 lg:bottom-4">
        {error && (
          <div className="mb-3">{typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />}</div>
        )}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="min-w-0 text-sm text-muted">
            Total{" "}
            <span className="font-figure amount block text-xl leading-tight text-graphite sm:inline sm:text-[1.375rem]">
              {formatMoney(preview.totalMinor, currency)}
            </span>
            {foreign && rate !== null && (
              <span className="amount block text-xs text-muted">≈ {formatMoney(toBaseMinor(preview.totalMinor, rate), baseCurrency)}</span>
            )}
          </p>
          <div className="flex shrink-0 gap-2">
            <Button variant="secondary" onClick={() => router.back()} disabled={busy}>
              Cancel
            </Button>
            <Button onClick={save} loading={busy}>
              {existing ? "Save changes" : `Save ${label}`}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
