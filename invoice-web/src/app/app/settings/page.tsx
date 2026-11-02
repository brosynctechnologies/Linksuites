"use client";

import Link from "next/link";
import { useRef, useState, type FormEvent } from "react";
import { api, asList, toApiError, upload, type ApiError } from "@/lib/api";
import { patchAuth, useAuth } from "@/lib/auth";
import { CURRENCIES, DEFAULT_ACCENT } from "@/lib/config";
import { TEMPLATES, normalizeColor } from "@/lib/templates";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { formatRate, useExchangeRates } from "@/lib/fx";
import { RatesAttribution } from "@/components/fx";
import type { BillingDto, BusinessDto, TaxRateDto, TemplateDto, UpdateBusinessRequest } from "@/lib/types";
import { TemplatePicker } from "@/components/template-picker";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
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
} from "@/components/ui";
import { ImageIcon, TrashIcon } from "@/components/icons";

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
          <LogoCard business={business.data} onChange={business.setData} />
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

function LogoCard({ business, onChange }: { business: BusinessDto; onChange: (b: BusinessDto) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function onFile(file: File | undefined) {
    if (!file) return;
    if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) return setError("Use a JPEG, PNG or WebP image.");
    if (file.size > 5 * 1024 * 1024) return setError("The logo must be 5 MB or smaller.");
    setBusy(true);
    setError(null);
    try {
      onChange(await upload<BusinessDto>("/api/business/logo", file));
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  }

  return (
    <Card>
      <SectionTitle>Logo</SectionTitle>
      <div className="flex items-center gap-4">
        <div className="flex h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-control border border-rule bg-slate-50">
          {business.logoUrl ? (
            <img src={business.logoUrl} alt="Business logo" className="max-h-full max-w-full object-contain" />
          ) : (
            <ImageIcon className="text-faint" width={28} height={28} />
          )}
        </div>
        <div>
          <input
            ref={input}
            id="logo-file"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="sr-only"
            onChange={(e) => onFile(e.target.files?.[0])}
          />
          <Button variant="secondary" loading={busy} onClick={() => input.current?.click()}>
            {business.logoUrl ? "Change logo" : "Upload logo"}
          </Button>
          <p className="mt-1.5 text-xs text-muted">JPEG, PNG or WebP, up to 5 MB. Shown on invoices and quotes.</p>
        </div>
      </div>
      {error && (
        <div className="mt-3">
          <ErrorBox error={error} />
        </div>
      )}
    </Card>
  );
}

type FormState = Omit<UpdateBusinessRequest, "paymentTermsDays" | "nextInvoiceNumber" | "nextQuoteNumber" | "reminderDaysBefore" | "reminderDaysAfter"> & {
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

  async function save(section: string, e?: FormEvent) {
    e?.preventDefault();
    setError(null);
    setSaved(null);
    if (!f.legalName.trim()) return setError({ section, error: "Business name is required." });
    const after = f.reminderDaysAfter
      .split(/[,\s]+/)
      .map((s) => s.trim())
      .filter(Boolean)
      .map(Number);
    if (after.some((n) => !Number.isInteger(n) || n < 0)) return setError({ section, error: "Reminder days must be whole numbers, e.g. 1, 7, 14." });
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
    <>
      <form onSubmit={(e) => save("profile", e)}>
        <Card>
          <SectionTitle>Business profile</SectionTitle>
          <div className="grid gap-3 sm:grid-cols-2">
            <Input label="Business / legal name" required value={f.legalName} onChange={set("legalName")} />
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
            <div className="col-span-2 flex items-end gap-3">
              <div>
                <label htmlFor="brand-color" className="block text-sm font-medium text-slate-700">
                  Brand colour
                </label>
                <input
                  id="brand-color"
                  type="color"
                  value={f.brandColor ?? DEFAULT_ACCENT}
                  onChange={set("brandColor")}
                  className="mt-1.5 h-10 w-16 cursor-pointer rounded-control border border-rule bg-paper p-1"
                />
              </div>
              <span className="pb-2.5 text-sm text-muted tabular-nums">{f.brandColor}</span>
            </div>
          </div>
          <div className="mt-4">
            <TemplatePicker
              label="Default template"
              templates={templates}
              value={f.defaultTemplate}
              onChange={(k) => setF((s) => ({ ...s, defaultTemplate: k }))}
              brandColor={f.brandColor}
            />
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
              />
            </div>
          </div>
          {isPro !== false && footer("reminders")}
        </Card>
      </form>
    </>
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
