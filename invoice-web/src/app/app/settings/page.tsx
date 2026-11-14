"use client";

import Link from "next/link";
import { useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { ApiError, api, asList, toApiError, upload } from "@/lib/api";
import { patchAuth, useAuth } from "@/lib/auth";
import { CURRENCIES, DEFAULT_ACCENT } from "@/lib/config";
import { addDays, todayISO } from "@/lib/dates";
import { computeLine, computeTotals, type MathLineInput } from "@/lib/invoice-math";
import {
  DOCUMENT_TITLES,
  FONT_STYLE_OPTIONS,
  HEADER_LINE_OPTIONS,
  LETTERHEAD_LIMITS,
  LOGO_SIZE_OPTIONS,
  letterheadErrors,
  secondaryColorValue,
  type LetterheadText,
} from "@/lib/letterhead";
import { TEMPLATES, normalizeColor, parseColor } from "@/lib/templates";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { formatRate, useExchangeRates } from "@/lib/fx";
import { RatesAttribution } from "@/components/fx";
import type { BillingDto, BusinessDto, DocumentTitle, FontStyle, HeaderLine, LogoSize, TaxRateDto, TemplateDto, UpdateBusinessRequest } from "@/lib/types";
import { TemplatePicker } from "@/components/template-picker";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { businessForPreview } from "@/components/doc-view-parts";
import { useFocusFirstInvalid } from "@/components/form-focus";
import { InvoicePreview, type PreviewDoc } from "@/components/invoice-preview";
import { SecondAccentField } from "@/components/letterhead-fields";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  Input,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  StatusBadge,
  Textarea,
  Toggle,
  cx,
  labelClass,
  linkClass,
} from "@/components/ui";
import { ImageIcon, LockIcon, TrashIcon } from "@/components/icons";

export default function SettingsPage() {
  const canView = useCan("settings.view");
  const canEdit = useCan("settings.edit");
  const canManageBilling = useCan("billing.manage");
  const business = useApi<BusinessDto>(canView ? "/api/business" : null);
  const billing = useApi<BillingDto>(canView ? "/api/billing" : null);
  const templates = useApi<TemplateDto[]>(canView ? "/api/invoice-templates" : null);

  if (!canView) {
    return (
      <div>
        <PageHeader title="Settings" back="/app/more" />
        <NoAccess />
        <div className="mt-6 max-w-3xl space-y-6">
          <ChangePassword />
          <AccountActivity />
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-3xl space-y-6">
      <PageHeader title="Settings" back="/app/more" />
      {!canEdit && <Notice tone="info">You can view these settings. Only the owner or an admin can change them.</Notice>}
      {business.error && <ApiErrorView error={business.error} onRetry={business.reload} />}
      {!business.data && !business.error && <PageLoader />}
      {business.data && (
        <fieldset disabled={!canEdit} className="min-w-0 space-y-6">
          <BusinessForm
            readOnly={!canEdit}
            business={business.data}
            onSaved={business.setData}
            isPro={billing.data ? billing.data.currentPlan.priceMinor > 0 : null}
            templates={asList(templates.data).length ? asList(templates.data) : TEMPLATES.map((t) => ({ ...t, available: !t.isPremium }))}
          />
          <ExchangeRatesCard base={business.data.currency} />
          <TaxRatesCard readOnly={!canEdit} />
          <PaystackCard business={business.data} onChange={business.setData} canManage={canManageBilling} />
        </fieldset>
      )}
      <ChangePassword />
      <AccountActivity />
    </div>
  );
}

const BRAND_IMAGES = [
  {
    kind: "logo",
    field: "logoUrl",
    label: "Logo",
    noun: "logo",
    hint: "JPEG, PNG or WebP, up to 5 MB. Shown at the top of every document.",
  },
  {
    kind: "signature",
    field: "signatureImageUrl",
    label: "Signature",
    noun: "signature",
    hint: "A transparent PNG of your handwritten signature looks best. Printed on the signature line.",
  },
  {
    kind: "stamp",
    field: "stampImageUrl",
    label: "Company stamp",
    noun: "stamp",
    hint: "Your stamp or seal, placed beside the signature. A transparent PNG looks best.",
  },
] as const;

type BrandImage = (typeof BRAND_IMAGES)[number];

/** Logo, signature and stamp uploads. Uploading needs Pro or Business; removing works on every plan. */
function BrandImages({ business, onChange }: { business: BusinessDto; onChange: (b: BusinessDto) => void }) {
  const available = business.brandImagesAvailable;
  return (
    <div className="space-y-3">
      {!available && (
        <p className="flex items-center gap-2 text-sm text-slate-600">
          <LockIcon className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
          <Link href="/app/billing" className={linkClass}>
            Add your logo, signature and stamp with Pro
          </Link>
        </p>
      )}
      <ul className="space-y-3">
        {BRAND_IMAGES.map((spec) => (
          <ImageSlot key={spec.kind} spec={spec} business={business} available={available} onChange={onChange} />
        ))}
      </ul>
    </div>
  );
}

function ImageSlot({
  spec,
  business,
  available,
  onChange,
}: {
  spec: BrandImage;
  business: BusinessDto;
  available: boolean;
  onChange: (b: BusinessDto) => void;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState<"upload" | "remove" | null>(null);
  const [error, setError] = useState<ApiError | string | null>(null);
  const url = business[spec.field];

  async function onFile(file: File | undefined) {
    if (!file) return;
    if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) return setError("Use a JPEG, PNG or WebP image.");
    if (file.size > 5 * 1024 * 1024) return setError("The image must be 5 MB or smaller.");
    setBusy("upload");
    setError(null);
    try {
      onChange(await upload<BusinessDto>(`/api/business/${spec.kind}`, file));
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
      if (input.current) input.current.value = "";
    }
  }

  async function remove() {
    if (!window.confirm(`Remove your ${spec.noun}? You can upload it again at any time.`)) return;
    setBusy("remove");
    setError(null);
    try {
      await api(`/api/business/${spec.kind}`, { method: "DELETE" });
      onChange({ ...business, [spec.field]: null });
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <li className="flex items-start gap-3">
      <div className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-control border border-rule bg-slate-50">
        {url ? (
          <img src={url} alt={`Your ${spec.noun}`} className={cx("max-h-full max-w-full object-contain", !available && "opacity-50")} />
        ) : available ? (
          <ImageIcon className="text-faint" width={24} height={24} aria-hidden="true" />
        ) : (
          <LockIcon className="h-5 w-5 text-faint" aria-hidden="true" />
        )}
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-slate-800">{spec.label}</p>
        {available && (
          <input
            ref={input}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="sr-only"
            tabIndex={-1}
            aria-label={`Choose a ${spec.noun} image`}
            onChange={(e) => onFile(e.target.files?.[0])}
          />
        )}
        <div className="mt-1 flex flex-wrap gap-2">
          {available ? (
            <Button variant="secondary" className="min-h-8 px-3 py-1" loading={busy === "upload"} disabled={!!busy} onClick={() => input.current?.click()}>
              {url ? `Change ${spec.noun}` : `Upload ${spec.noun}`}
            </Button>
          ) : (
            <Button variant="secondary" className="min-h-8 px-3 py-1" disabled aria-describedby={`${spec.kind}-locked`}>
              <LockIcon className="h-3.5 w-3.5" aria-hidden="true" /> Upload {spec.noun}
            </Button>
          )}
          {url && (
            <Button variant="ghost" className="min-h-8 px-2 py-1 text-overdue" loading={busy === "remove"} disabled={!!busy} onClick={remove}>
              <TrashIcon className="h-4 w-4" aria-hidden="true" /> Remove
            </Button>
          )}
        </div>
        <p id={`${spec.kind}-locked`} className="mt-1 text-xs text-muted">
          {available ? spec.hint : url ? "Kept, but not shown on documents on the Free plan." : "Part of the Pro plan."}
        </p>
        {error && (
          <div className="mt-2">{typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />}</div>
        )}
      </div>
    </li>
  );
}

function DesignGroup({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-3">
      <h3 className="text-sm font-semibold text-graphite">{title}</h3>
      {children}
    </section>
  );
}

/** Sample lines for the design preview (minor units). */
const SAMPLE_LINES: (MathLineInput & { description: string })[] = [
  { description: "Brand identity design", quantity: 1, unitPriceMinor: 35_000_000, discountPercent: 0, ratePercent: 7.5, taxName: "VAT" },
  { description: "Business cards, box of 100", quantity: 4, unitPriceMinor: 1_250_000, discountPercent: 0, ratePercent: 7.5, taxName: "VAT" },
  { description: "Delivery within Lagos", quantity: 1, unitPriceMinor: 500_000, discountPercent: 0, ratePercent: 0 },
];

const SAMPLE_CLIENT = { name: "Ada Okafor", company: "Bright Foods Ltd", address: "14 Admiralty Way, Lekki, Lagos", email: "ada@brightfoods.ng" };

type NumberKeys = "paymentTermsDays" | "nextInvoiceNumber" | "nextQuoteNumber" | "reminderDaysBefore" | "reminderDaysAfter";
type ChoiceKeys = "headerLine" | "logoSize" | "fontStyle" | "documentTitle" | "showTaxId" | "showRegistrationNumber" | "showBankDetails" | "showSignature";

type FormState = Omit<UpdateBusinessRequest, NumberKeys | ChoiceKeys | keyof LetterheadText> &
  LetterheadText &
  Pick<BusinessDto, ChoiceKeys> & {
    paymentTermsDays: string;
    nextInvoiceNumber: string;
    nextQuoteNumber: string;
    reminderDaysBefore: string;
    reminderDaysAfter: string;
  };

function toForm(b: BusinessDto): FormState {
  return {
    legalName: b.legalName ?? "",
    address: b.address ?? "",
    email: b.email ?? "",
    phone: b.phone ?? "",
    taxId: b.taxId ?? "",
    bankDetails: b.bankDetails ?? "",
    currency: b.currency || "NGN",
    paymentTermsDays: String(b.paymentTermsDays ?? 14),
    invoicePrefix: b.invoicePrefix ?? "",
    nextInvoiceNumber: String(b.nextInvoiceNumber ?? 1),
    quotePrefix: b.quotePrefix ?? "",
    nextQuoteNumber: String(b.nextQuoteNumber ?? 1),
    defaultTemplate: b.defaultTemplate || "classic",
    brandColor: normalizeColor(b.brandColor || DEFAULT_ACCENT),
    defaultNotes: b.defaultNotes ?? "",
    defaultTerms: b.defaultTerms ?? "",
    remindersEnabled: !!b.remindersEnabled,
    reminderDaysBefore: b.reminderDaysBefore === null || b.reminderDaysBefore === undefined ? "" : String(b.reminderDaysBefore),
    reminderDaysAfter: (b.reminderDaysAfter ?? []).join(", "),
    tagline: b.tagline ?? "",
    registrationNumber: b.registrationNumber ?? "",
    headerLine: b.headerLine ?? "address",
    secondaryColor: b.secondaryColor ?? "",
    logoSize: b.logoSize ?? "medium",
    fontStyle: b.fontStyle ?? "modern",
    documentTitle: b.documentTitle ?? "Invoice",
    showTaxId: b.showTaxId ?? true,
    showRegistrationNumber: b.showRegistrationNumber ?? true,
    showBankDetails: b.showBankDetails ?? true,
    showSignature: b.showSignature ?? false,
    signatureName: b.signatureName ?? "",
    signatureRole: b.signatureRole ?? "",
    footerText: b.footerText ?? "",
  };
}

const int = (s: string, fallback = 0) => {
  const n = parseInt(s, 10);
  return Number.isFinite(n) && n >= 0 ? n : fallback;
};
const orNull = (s: string | null) => (s && s.trim() ? s.trim() : null);

function BusinessForm({
  readOnly,
  business,
  onSaved,
  isPro,
  templates,
}: {
  readOnly: boolean;
  business: BusinessDto;
  onSaved: (b: BusinessDto) => void;
  isPro: boolean | null;
  templates: TemplateDto[];
}) {
  const auth = useAuth();
  const [f, setF] = useState<FormState>(() => toForm(business));
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<{ section: string; error: ApiError | string } | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  // Set once the API refuses a base-currency change (400: invoices or expenses already exist).
  const [currencyLock, setCurrencyLock] = useState<string | null>(null);
  const set = (k: keyof FormState) => (e: { target: { value: string } }) => setF((s) => ({ ...s, [k]: e.target.value }));
  const setValue = <K extends keyof FormState>(k: K, v: FormState[K]) => setF((s) => ({ ...s, [k]: v }));
  const root = useRef<HTMLDivElement>(null);
  useFocusFirstInvalid(root, error);
  const fieldError = (name: string) => (error && typeof error.error !== "string" ? error.error.field(name) : undefined);

  const preview = useMemo<PreviewDoc>(() => {
    const today = todayISO();
    const totals = computeTotals(SAMPLE_LINES);
    const next = Math.max(1, int(f.nextInvoiceNumber, 1));
    return {
      kind: "invoice",
      template: f.defaultTemplate,
      brandColor: f.brandColor,
      number: `${f.invoicePrefix.trim()}${String(next).padStart(4, "0")}`,
      business: {
        ...businessForPreview(business),
        name: f.legalName.trim() || business.legalName,
        address: orNull(f.address),
        email: orNull(f.email),
        phone: orNull(f.phone),
        taxId: orNull(f.taxId),
        bankDetails: orNull(f.bankDetails),
        tagline: orNull(f.tagline),
        registrationNumber: orNull(f.registrationNumber),
        headerLine: f.headerLine,
        secondaryColor: parseColor(f.secondaryColor),
        logoSize: f.logoSize,
        fontStyle: f.fontStyle,
        documentTitle: f.documentTitle,
        showTaxId: f.showTaxId,
        showRegistrationNumber: f.showRegistrationNumber,
        showBankDetails: f.showBankDetails,
        showSignature: f.showSignature,
        signatureName: orNull(f.signatureName),
        signatureRole: orNull(f.signatureRole),
        footerText: orNull(f.footerText),
      },
      client: SAMPLE_CLIENT,
      issueDate: today,
      secondDate: addDays(today, int(f.paymentTermsDays, 14)),
      currency: f.currency,
      lines: SAMPLE_LINES.map((l) => ({
        description: l.description,
        quantity: l.quantity,
        unitPriceMinor: l.unitPriceMinor,
        discountPercent: l.discountPercent,
        taxLabel: l.ratePercent ? `${l.taxName ?? "Tax"} ${l.ratePercent}%` : null,
        lineTotalMinor: computeLine(l).lineTotal,
      })),
      subtotalMinor: totals.subtotalMinor,
      discountTotalMinor: totals.discountTotalMinor,
      taxes: totals.taxes,
      shippingMinor: totals.shippingMinor,
      totalMinor: totals.totalMinor,
      amountPaidMinor: totals.amountPaidMinor,
      balanceMinor: totals.balanceMinor,
      notes: f.defaultNotes,
      terms: f.defaultTerms,
    };
  }, [f, business]);

  async function save(section: string, e?: FormEvent) {
    e?.preventDefault();
    setError(null);
    setSaved(null);
    const invalid: Record<string, string[]> = {};
    if (!f.legalName.trim()) invalid.legalName = ["Business name is required."];
    const after = f.reminderDaysAfter
      .split(/[,\s]+/)
      .map((s) => s.trim())
      .filter(Boolean)
      .map(Number);
    if (after.some((n) => !Number.isInteger(n) || n < 0)) invalid.reminderDaysAfter = ["Reminder days must be whole numbers, e.g. 1, 7, 14."];
    Object.assign(invalid, letterheadErrors(f));
    if (Object.keys(invalid).length > 0) {
      return setError({ section, error: new ApiError(400, { detail: "Check the highlighted fields:", errors: invalid }) });
    }
    const body: UpdateBusinessRequest = {
      legalName: f.legalName.trim(),
      address: orNull(f.address),
      email: orNull(f.email),
      phone: orNull(f.phone),
      taxId: orNull(f.taxId),
      bankDetails: orNull(f.bankDetails),
      currency: f.currency,
      paymentTermsDays: int(f.paymentTermsDays, 14),
      invoicePrefix: f.invoicePrefix.trim(),
      nextInvoiceNumber: Math.max(1, int(f.nextInvoiceNumber, 1)),
      quotePrefix: f.quotePrefix.trim(),
      nextQuoteNumber: Math.max(1, int(f.nextQuoteNumber, 1)),
      defaultTemplate: f.defaultTemplate,
      brandColor: f.brandColor,
      defaultNotes: orNull(f.defaultNotes),
      defaultTerms: orNull(f.defaultTerms),
      remindersEnabled: f.remindersEnabled,
      reminderDaysBefore: f.reminderDaysBefore.trim() === "" ? null : int(f.reminderDaysBefore),
      reminderDaysAfter: [...new Set(after)].sort((a, b) => a - b),
      // PUT /api/business keeps letterhead text that is left out or null; "" clears it, so an emptied field sends "".
      tagline: f.tagline.trim(),
      registrationNumber: f.registrationNumber.trim(),
      headerLine: f.headerLine,
      secondaryColor: secondaryColorValue(f.secondaryColor) ?? "",
      logoSize: f.logoSize,
      fontStyle: f.fontStyle,
      documentTitle: f.documentTitle,
      showTaxId: f.showTaxId,
      showRegistrationNumber: f.showRegistrationNumber,
      showBankDetails: f.showBankDetails,
      showSignature: f.showSignature,
      signatureName: f.signatureName.trim(),
      signatureRole: f.signatureRole.trim(),
      footerText: f.footerText.trim(),
    };
    setBusy(section);
    try {
      const updated = await api<BusinessDto>("/api/business", { method: "PUT", body });
      onSaved(updated);
      setF(toForm(updated));
      setSaved(section);
      if (auth && updated.legalName !== auth.tenant.name) patchAuth({ tenant: { ...auth.tenant, name: updated.legalName } });
    } catch (err) {
      const apiErr = toApiError(err);
      if (apiErr.status === 400 && f.currency !== business.currency) {
        // Surface the server's reason next to the currency and keep the saved currency.
        setCurrencyLock(apiErr.field("currency") ?? apiErr.message);
        setF((s) => ({ ...s, currency: business.currency }));
      }
      setError({ section, error: apiErr });
    } finally {
      setBusy(null);
    }
  }

  const footer = (section: string, label = "Save") => (
    <div className="mt-4 space-y-2">
      {error?.section === section &&
        (typeof error.error === "string" ? <ErrorBox error={error.error} /> : <ApiErrorView error={error.error} />)}
      {saved === section && <Notice tone="success">Saved.</Notice>}
      {!readOnly && (
        <Button type="submit" loading={busy === section}>
          {label}
        </Button>
      )}
    </div>
  );

  return (
    <div ref={root} className="space-y-6">
      <form onSubmit={(e) => save("profile", e)} noValidate>
        <Card>
          <SectionTitle>Business profile</SectionTitle>
          <div className="grid gap-3 sm:grid-cols-2">
            <Input label="Business / legal name" required value={f.legalName} onChange={set("legalName")} error={fieldError("legalName")} />
            <Input label="Email" type="email" value={f.email ?? ""} onChange={set("email")} hint="Shown on invoices; client replies go here." />
            <Input label="Phone" type="tel" value={f.phone ?? ""} onChange={set("phone")} />
            <Input label="Tax ID" value={f.taxId ?? ""} onChange={set("taxId")} />
            <div className="sm:col-span-2">
              <Textarea label="Address" rows={2} value={f.address ?? ""} onChange={set("address")} />
            </div>
            <div className="sm:col-span-2">
              <Textarea
                label="Bank details"
                rows={3}
                value={f.bankDetails ?? ""}
                onChange={set("bankDetails")}
                placeholder={"Bank: …\nAccount name: …\nAccount number: …"}
                hint="Printed on every invoice so clients can pay by transfer."
              />
            </div>
          </div>
          {footer("profile")}
        </Card>
      </form>

      <form onSubmit={(e) => save("design", e)} noValidate>
        <Card>
          <SectionTitle>Invoice design</SectionTitle>
          <p className="-mt-1 mb-4 text-sm text-slate-600">
            How your invoices, quotes and receipts look. Changes apply to every document, including ones already sent.
          </p>
          <TemplatePicker
            label="Design"
            templates={templates}
            value={f.defaultTemplate}
            onChange={(k) => setValue("defaultTemplate", k)}
            brandColor={f.brandColor}
            secondaryColor={f.secondaryColor}
          />
          <div className="mt-5 grid gap-6 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
            <div className="min-w-0 md:order-2">
              <div className="md:sticky md:top-4">
                <p className="mb-2 text-xs text-muted">Preview with sample lines</p>
                <InvoicePreview doc={preview} />
              </div>
            </div>

            <div className="min-w-0 space-y-6">
              <DesignGroup title="Colours">
                <div className="space-y-1.5">
                  <label htmlFor="brand-color" className={labelClass}>
                    Brand colour
                  </label>
                  <div className="flex items-center gap-3">
                    <input
                      id="brand-color"
                      type="color"
                      value={normalizeColor(f.brandColor ?? DEFAULT_ACCENT)}
                      onChange={set("brandColor")}
                      className="h-10 w-14 cursor-pointer rounded-control border border-rule bg-paper p-1"
                    />
                    <span className="text-sm text-muted tabular-nums">{f.brandColor}</span>
                  </div>
                </div>
                <SecondAccentField
                  id="second-accent"
                  brandColor={f.brandColor}
                  value={f.secondaryColor}
                  onChange={(v) => setValue("secondaryColor", v)}
                  error={fieldError("secondaryColor")}
                />
              </DesignGroup>

              <DesignGroup title="Logo, signature and stamp">
                <BrandImages business={business} onChange={onSaved} />
              </DesignGroup>

              <DesignGroup title="Letterhead">
                <Select
                  label="Logo size"
                  value={f.logoSize}
                  onChange={(e) => setValue("logoSize", e.target.value as LogoSize)}
                  hint={business.brandImagesAvailable ? undefined : "Used when your logo is shown, on Pro."}
                >
                  {LOGO_SIZE_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </Select>
                <Select label="Font style" value={f.fontStyle} onChange={(e) => setValue("fontStyle", e.target.value as FontStyle)}>
                  {FONT_STYLE_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </Select>
                <Select
                  label="Document title"
                  value={f.documentTitle}
                  onChange={(e) => setValue("documentTitle", e.target.value as DocumentTitle)}
                  hint="Used on invoices. Quotes and receipts keep their own titles."
                >
                  {DOCUMENT_TITLES.map((t) => (
                    <option key={t}>{t}</option>
                  ))}
                </Select>
                <Select
                  label="Line under your name"
                  value={f.headerLine}
                  onChange={(e) => setValue("headerLine", e.target.value as HeaderLine)}
                >
                  {HEADER_LINE_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </Select>
                <Input
                  label="Slogan or what you do"
                  value={f.tagline}
                  onChange={set("tagline")}
                  maxLength={LETTERHEAD_LIMITS.tagline}
                  placeholder="Bespoke tailoring for men and women"
                  error={fieldError("tagline")}
                />
                <Input
                  label="Registration number"
                  value={f.registrationNumber}
                  onChange={set("registrationNumber")}
                  maxLength={LETTERHEAD_LIMITS.registrationNumber}
                  placeholder="RC 1234567"
                  hint="Printed as “Reg. no. RC 1234567”."
                  error={fieldError("registrationNumber")}
                />
              </DesignGroup>

              <DesignGroup title="Show on documents">
                <Toggle checked={f.showTaxId} onChange={(v) => setValue("showTaxId", v)} label="Tax ID" description="From your business profile." />
                <Toggle
                  checked={f.showRegistrationNumber}
                  onChange={(v) => setValue("showRegistrationNumber", v)}
                  label="Registration number"
                  description="In the contact details, unless it is the line under your name."
                />
                <Toggle
                  checked={f.showBankDetails}
                  onChange={(v) => setValue("showBankDetails", v)}
                  label="Bank details"
                  description="On invoices, so clients can pay by transfer."
                />
                <Toggle
                  checked={f.showSignature}
                  onChange={(v) => setValue("showSignature", v)}
                  label="Signature line"
                  description="An “Authorised signature” line at the end of the document."
                />
              </DesignGroup>

              <DesignGroup title="Signature line">
                <Input
                  label="Name"
                  value={f.signatureName}
                  onChange={set("signatureName")}
                  maxLength={LETTERHEAD_LIMITS.signatureName}
                  placeholder="Ada Okafor"
                  error={fieldError("signatureName")}
                />
                <Input
                  label="Role"
                  value={f.signatureRole}
                  onChange={set("signatureRole")}
                  maxLength={LETTERHEAD_LIMITS.signatureRole}
                  placeholder="Director"
                  hint={f.showSignature ? undefined : "Printed when the signature line is switched on."}
                  error={fieldError("signatureRole")}
                />
              </DesignGroup>

              <DesignGroup title="Footer">
                <Input
                  label="Footer line"
                  value={f.footerText}
                  onChange={set("footerText")}
                  maxLength={LETTERHEAD_LIMITS.footerText}
                  placeholder="Thank you for your business."
                  hint="Printed at the bottom of every page."
                  error={fieldError("footerText")}
                />
              </DesignGroup>
            </div>
          </div>
          {footer("design")}
        </Card>
      </form>

      <form onSubmit={(e) => save("invoicing", e)}>
        <Card>
          <SectionTitle>Invoicing defaults</SectionTitle>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Select
              label="Base currency"
              value={f.currency}
              onChange={set("currency")}
              disabled={!!currencyLock}
              hint={currencyLock ?? "Used for your dashboard and reports."}
            >
              {[...new Set([f.currency, ...CURRENCIES])].map((c) => (
                <option key={c}>{c}</option>
              ))}
            </Select>
            <Input label="Payment terms (days)" inputMode="numeric" value={f.paymentTermsDays} onChange={set("paymentTermsDays")} />
            <Input label="Invoice prefix" value={f.invoicePrefix} onChange={set("invoicePrefix")} placeholder="INV-" />
            <Input label="Next invoice no." inputMode="numeric" value={f.nextInvoiceNumber} onChange={set("nextInvoiceNumber")} />
            <Input label="Quote prefix" value={f.quotePrefix} onChange={set("quotePrefix")} placeholder="QT-" />
            <Input label="Next quote no." inputMode="numeric" value={f.nextQuoteNumber} onChange={set("nextQuoteNumber")} />
          </div>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            <Textarea label="Default notes" rows={3} value={f.defaultNotes ?? ""} onChange={set("defaultNotes")} placeholder="Thank you for your business!" />
            <Textarea label="Default terms" rows={3} value={f.defaultTerms ?? ""} onChange={set("defaultTerms")} />
          </div>
          {footer("invoicing")}
        </Card>
      </form>

      <form onSubmit={(e) => save("reminders", e)}>
        <Card>
          <SectionTitle action={<Badge className="bg-amber-wash text-amber-text">Pro</Badge>}>Payment reminders</SectionTitle>
          {isPro === false && (
            <div className="mb-3">
              <Notice tone="warning">
                Automatic reminders are part of Pro.{" "}
                <Link href="/app/billing" className="font-semibold underline">
                  Upgrade
                </Link>
              </Notice>
            </div>
          )}
          <div className="space-y-4">
            <Toggle
              checked={f.remindersEnabled}
              onChange={(v) => setF((s) => ({ ...s, remindersEnabled: v }))}
              disabled={isPro === false}
              label="Email clients about unpaid invoices"
              description="Reminders include the invoice link and Pay-now button."
            />
            <div className="grid gap-3 sm:grid-cols-2">
              <Input
                label="Days before due date"
                inputMode="numeric"
                value={f.reminderDaysBefore}
                onChange={(e) => setF((s) => ({ ...s, reminderDaysBefore: e.target.value.replace(/\D/g, "") }))}
                disabled={isPro === false || !f.remindersEnabled}
                hint="Leave empty for no early reminder."
              />
              <Input
                label="Days after due date"
                value={f.reminderDaysAfter}
                onChange={set("reminderDaysAfter")}
                disabled={isPro === false || !f.remindersEnabled}
                placeholder="1, 7, 14"
                hint="Comma-separated, e.g. 1, 7, 14"
                error={fieldError("reminderDaysAfter")}
              />
            </div>
          </div>
          {isPro !== false && footer("reminders")}
        </Card>
      </form>
    </div>
  );
}

function TaxRatesCard({ readOnly }: { readOnly: boolean }) {
  const { data, error, reload, setData } = useApi<TaxRateDto[]>("/api/tax-rates");
  const rates = asList(data);
  const [editingId, setEditingId] = useState<string | "new" | null>(null);
  const [name, setName] = useState("");
  const [rate, setRate] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [formError, setFormError] = useState<ApiError | string | null>(null);

  function startEdit(r: TaxRateDto | null) {
    setFormError(null);
    setEditingId(r ? r.id : "new");
    setName(r?.name ?? "");
    setRate(r ? String(r.ratePercent) : "");
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const ratePercent = Number(rate);
    if (!name.trim()) return setFormError("Name is required, e.g. VAT.");
    if (!Number.isFinite(ratePercent) || ratePercent < 0 || ratePercent > 100) return setFormError("Rate must be between 0 and 100.");
    setBusy("save");
    setFormError(null);
    try {
      const body = { name: name.trim(), ratePercent };
      const saved =
        editingId && editingId !== "new"
          ? await api<TaxRateDto>(`/api/tax-rates/${editingId}`, { method: "PUT", body })
          : await api<TaxRateDto>("/api/tax-rates", { method: "POST", body });
      setData((prev) => {
        const arr = asList(prev);
        return arr.some((x) => x.id === saved.id) ? arr.map((x) => (x.id === saved.id ? saved : x)) : [...arr, saved];
      });
      setEditingId(null);
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  async function remove(r: TaxRateDto) {
    // The server refuses (409) while invoices, quotes or recurring schedules still use the rate; the reason is shown below.
    if (!window.confirm(`Delete tax rate "${r.name}"? Rates used on invoices, quotes or recurring invoices can't be deleted.`)) return;
    setBusy(r.id);
    try {
      await api(`/api/tax-rates/${r.id}`, { method: "DELETE" });
      setData((prev) => asList(prev).filter((x) => x.id !== r.id));
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card>
      <div id="tax-rates" className="scroll-mt-20" />
      <SectionTitle
        action={
          editingId === null &&
          !readOnly && (
            <Button variant="secondary" className="min-h-8 px-3 py-1" onClick={() => startEdit(null)}>
              Add tax rate
            </Button>
          )
        }
      >
        Tax rates
      </SectionTitle>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && rates.length === 0 && editingId === null && <p className="text-sm text-muted">No tax rates yet. Add VAT or any other tax you charge.</p>}
      {rates.length > 0 && (
        <ul className="divide-y divide-rule-soft">
          {rates.map((r) => (
            <li key={r.id} className="flex items-center justify-between gap-3 py-2 text-sm">
              <span>
                <span className="font-medium text-graphite">{r.name}</span> <span className="text-muted tabular-nums">{r.ratePercent}%</span>
              </span>
              {!readOnly && (
              <span className="flex gap-1">
                <Button variant="ghost" className="min-h-8 px-2 py-1" onClick={() => startEdit(r)}>
                  Edit
                </Button>
                <button
                  type="button"
                  className="flex h-8 w-8 items-center justify-center rounded-control text-overdue hover:bg-overdue-wash"
                  aria-label={`Delete ${r.name}`}
                  disabled={busy === r.id}
                  onClick={() => remove(r)}
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              </span>
              )}
            </li>
          ))}
        </ul>
      )}
      {editingId !== null && (
        <form onSubmit={submit} className="mt-3 space-y-3 rounded-control border border-rule-soft bg-slate-50 p-3">
          <div className="grid grid-cols-[1fr_7rem] gap-3">
            <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} placeholder="VAT" autoFocus />
            <Input label="Rate %" inputMode="decimal" value={rate} onChange={(e) => setRate(e.target.value)} placeholder="7.5" />
          </div>
          <ErrorBox error={formError} />
          <div className="flex gap-2">
            <Button type="submit" loading={busy === "save"}>
              {editingId === "new" ? "Add" : "Save"}
            </Button>
            <Button variant="secondary" onClick={() => setEditingId(null)}>
              Cancel
            </Button>
          </div>
        </form>
      )}
      {editingId === null && formError && (
        <div className="mt-2">
          <ErrorBox error={formError} />
        </div>
      )}
    </Card>
  );
}

function PaystackCard({ business, onChange, canManage }: { business: BusinessDto; onChange: (b: BusinessDto) => void; canManage: boolean }) {
  const [key, setKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function connect(e: FormEvent) {
    e.preventDefault();
    const secretKey = key.trim();
    if (!/^sk_(test|live)_\S+$/.test(secretKey)) return setError("Paste your Paystack secret key — it starts with sk_test_ or sk_live_.");
    setBusy(true);
    setError(null);
    try {
      onChange(await api<BusinessDto>("/api/business/paystack", { method: "PUT", body: { secretKey } }));
      setKey("");
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function disconnect() {
    if (!window.confirm("Disconnect Paystack? Clients will no longer see a Pay-now button.")) return;
    setBusy(true);
    setError(null);
    try {
      onChange(await api<BusinessDto>("/api/business/paystack", { method: "DELETE" }));
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <SectionTitle
        action={
          business.paystackConnected ? (
            <StatusBadge tone="green">Connected</StatusBadge>
          ) : (
            <StatusBadge tone="void">Not connected</StatusBadge>
          )
        }
      >
        Online payments (Paystack)
      </SectionTitle>
      <p className="text-sm text-slate-600">
        Connect your own Paystack account to add a <strong>Pay now</strong> button to your invoices. Money goes straight to your Paystack
        account — we never hold your funds. Your secret key is stored encrypted and never shown again. Pay-now links are a Pro feature.
      </p>
      {!canManage ? (
        <p className="mt-3 text-sm text-muted">Only the business owner can connect or disconnect Paystack.</p>
      ) : business.paystackConnected ? (
        <div className="mt-3">
          <Button variant="danger" loading={busy} onClick={disconnect}>
            Disconnect Paystack
          </Button>
        </div>
      ) : (
        <form onSubmit={connect} className="mt-3 space-y-3">
          <Input
            label="Paystack secret key"
            type="password"
            autoComplete="off"
            spellCheck={false}
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="sk_live_…"
            hint="Find it in your Paystack dashboard under Settings → API Keys & Webhooks."
          />
          <Button type="submit" loading={busy} disabled={!key.trim()}>
            Connect Paystack
          </Button>
        </form>
      )}
      {error && (
        <div className="mt-3">
          {typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />}
        </div>
      )}
    </Card>
  );
}

function ExchangeRatesCard({ base }: { base: string }) {
  const { data, error } = useExchangeRates();
  const shown = (data?.rates ?? []).filter((r) => r.currency !== base && (CURRENCIES as readonly string[]).includes(r.currency)).slice(0, 6);
  return (
    <Card>
      <SectionTitle>Exchange rates</SectionTitle>
      <p className="text-sm text-slate-600">
        Invoices, quotes and expenses in other currencies are converted to {base} with the day&apos;s rate. You can override the rate on each
        document.
      </p>
      {error && <p className="mt-2 text-sm text-muted">Today&apos;s rates couldn&apos;t be loaded. You can still type a rate on each document.</p>}
      {shown.length > 0 && (
        <ul className="mt-3 grid gap-x-4 gap-y-1 text-sm text-graphite tabular-nums sm:grid-cols-2">
          {shown.map((r) => (
            <li key={r.currency}>{formatRate(r.currency, r.toBase, data?.base || base)}</li>
          ))}
        </ul>
      )}
      <RatesAttribution asOf={data?.asOf} className="mt-2" />
    </Card>
  );
}
