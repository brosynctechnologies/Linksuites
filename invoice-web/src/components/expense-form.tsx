"use client";

import { useRouter } from "next/navigation";
import { useId, useRef, useState, type FormEvent } from "react";
import { api, asList, toApiError, upload, type ApiError } from "@/lib/api";
import { CURRENCIES } from "@/lib/config";
import { todayISO } from "@/lib/dates";
import { parseRate, rateFor, rateInput, useExchangeRates } from "@/lib/fx";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { BusinessDto, ClientDto, ExpenseDto, PaymentMethod, SaveExpenseRequest } from "@/lib/types";
import { ExchangeRateField } from "./fx";
import { ImageIcon, TrashIcon } from "./icons";
import { ApiErrorView, Button, Card, ErrorBox, Input, Notice, PageLoader, SectionTitle, Select, Textarea, UpgradePrompt } from "./ui";

export const PAYMENT_METHODS: { value: PaymentMethod; label: string }[] = [
  { value: "BankTransfer", label: "Bank transfer" },
  { value: "Cash", label: "Cash" },
  { value: "Card", label: "Card" },
  { value: "Online", label: "Online" },
  { value: "Other", label: "Other" },
];

const DEFAULT_CATEGORIES = [
  "Advertising",
  "Bank charges",
  "Equipment",
  "Internet & phone",
  "Rent",
  "Salaries",
  "Software",
  "Supplies",
  "Transport",
  "Utilities",
  "Other",
];
const OTHER = "__other__";
const RECEIPT_TYPES = ["image/jpeg", "image/png", "image/webp", "application/pdf"];
const MAX_RECEIPT = 5 * 1024 * 1024;

/** Loads business, categories, clients and rates, then shows the add/edit expense form. */
export function ExpenseEditor({ existing, onChange }: { existing?: ExpenseDto; onChange?: (e: ExpenseDto) => void }) {
  const business = useApi<BusinessDto>("/api/business");
  const categories = useApi<string[]>("/api/expenses/categories");
  const clients = useApi<ClientDto[]>("/api/clients");
  const rates = useExchangeRates();

  if (business.error) return <ApiErrorView error={business.error} onRetry={business.reload} />;
  if (categories.error?.isPlanLimit) return <UpgradePrompt plan="Business" message={categories.error.detail} />;
  if (!business.data) return <PageLoader />;

  const cats = Array.isArray(categories.data) && categories.data.length ? categories.data : DEFAULT_CATEGORIES;
  return (
    <ExpenseForm
      existing={existing}
      onChange={onChange}
      business={business.data}
      categories={cats}
      clients={asList(clients.data)}
      rates={rates}
    />
  );
}

function ExpenseForm({
  existing,
  onChange,
  business,
  categories,
  clients,
  rates,
}: {
  existing?: ExpenseDto;
  onChange?: (e: ExpenseDto) => void;
  business: BusinessDto;
  categories: string[];
  clients: ClientDto[];
  rates: ReturnType<typeof useExchangeRates>;
}) {
  const router = useRouter();
  const canEdit = useCan("expenses.edit");
  const rateId = useId();
  const fileInput = useRef<HTMLInputElement>(null);
  const base = business.currency || "NGN";

  const knownCategory = !existing || categories.includes(existing.category);
  const [date, setDate] = useState(existing?.date ?? todayISO());
  const [categoryChoice, setCategoryChoice] = useState(existing ? (knownCategory ? existing.category : OTHER) : "");
  const [otherCategory, setOtherCategory] = useState(existing && !knownCategory ? existing.category : "");
  const [vendor, setVendor] = useState(existing?.vendor ?? "");
  const [description, setDescription] = useState(existing?.description ?? "");
  const [amount, setAmount] = useState(existing ? toMajorInput(existing.amountMinor) : "");
  const [currency, setCurrency] = useState(existing?.currency ?? base);
  const [tax, setTax] = useState(existing?.taxMinor ? toMajorInput(existing.taxMinor) : "");
  const [method, setMethod] = useState<PaymentMethod>(existing?.paymentMethod ?? "BankTransfer");
  const [reference, setReference] = useState(existing?.reference ?? "");
  const [clientId, setClientId] = useState(existing?.clientId ?? "");
  const [notes, setNotes] = useState(existing?.notes ?? "");
  const [rateEdit, setRateEdit] = useState<{ currency: string; value: string } | null>(() =>
    existing?.exchangeRate && existing.currency !== base ? { currency: existing.currency, value: rateInput(existing.exchangeRate) } : null,
  );
  const [rateError, setRateError] = useState<string | null>(null);
  const [pendingReceipt, setPendingReceipt] = useState<File | null>(null);
  const [receiptBusy, setReceiptBusy] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [saved, setSaved] = useState(false);

  const foreign = currency !== base;
  const rateValue = rateEdit?.currency === currency ? rateEdit.value : rateInput(rateFor(rates.data, currency));
  const rate = foreign ? parseRate(rateValue) : 1;
  const amountMinor = toMinor(amount);

  function pickReceipt(file: File | undefined): File | null {
    if (!file) return null;
    if (!RECEIPT_TYPES.includes(file.type)) {
      setError("Receipts must be a JPEG, PNG or WebP image, or a PDF.");
      return null;
    }
    if (file.size > MAX_RECEIPT) {
      setError("Receipts must be 5 MB or smaller.");
      return null;
    }
    return file;
  }

  async function uploadReceipt(id: string, file: File) {
    return upload<ExpenseDto>(`/api/expenses/${id}/receipt`, file);
  }

  async function onReceiptChosen(file: File | undefined) {
    setError(null);
    const ok = pickReceipt(file);
    if (fileInput.current) fileInput.current.value = "";
    if (!ok) return;
    if (!existing) {
      setPendingReceipt(ok);
      return;
    }
    setReceiptBusy(true);
    try {
      onChange?.(await uploadReceipt(existing.id, ok));
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setReceiptBusy(false);
    }
  }

  async function removeReceipt() {
    if (!existing || !window.confirm("Remove the receipt from this expense?")) return;
    setReceiptBusy(true);
    setError(null);
    try {
      onChange?.(await api<ExpenseDto>(`/api/expenses/${existing.id}/receipt`, { method: "DELETE" }));
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setReceiptBusy(false);
    }
  }

  async function save(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setSaved(false);
    const category = (categoryChoice === OTHER ? otherCategory : categoryChoice).trim();
    if (!category) return setError("Choose a category.");
    if (!amountMinor || amountMinor <= 0) return setError("Enter the amount you paid.");
    const taxMinor = toMinor(tax) ?? 0;
    if (taxMinor < 0 || taxMinor > amountMinor) return setError("VAT included can't be more than the amount.");
    if (foreign && rateValue.trim() && rate === null) {
      setRateError("Enter a valid exchange rate, e.g. 1580.25.");
      return;
    }
    const body: SaveExpenseRequest = {
      date,
      category,
      vendor: vendor.trim() || null,
      description: description.trim() || null,
      amountMinor,
      currency,
      taxMinor,
      paymentMethod: method,
      reference: reference.trim() || null,
      clientId: clientId || null,
      notes: notes.trim() || null,
      ...(foreign && rate ? { exchangeRate: rate } : {}),
    };
    setBusy("save");
    try {
      let result = existing
        ? await api<ExpenseDto>(`/api/expenses/${existing.id}`, { method: "PUT", body })
        : await api<ExpenseDto>("/api/expenses", { method: "POST", body });
      if (!existing && pendingReceipt) {
        try {
          result = await uploadReceipt(result.id, pendingReceipt);
        } catch {
          // The expense is saved; the receipt can be added again from its page.
          router.push(`/app/expenses/${result.id}?receipt=failed`);
          return;
        }
      }
      if (existing) {
        onChange?.(result);
        setSaved(true);
        setBusy(null);
      } else {
        router.push("/app/expenses");
      }
    } catch (err) {
      const apiErr = toApiError(err);
      setBusy(null);
      if (apiErr.status === 503 && apiErr.code === "fx_unavailable") {
        setRateError(`We couldn't get today's ${currency} → ${base} rate. Type the rate to continue.`);
        setTimeout(() => document.getElementById(rateId)?.focus(), 0);
        return;
      }
      setError(apiErr);
    }
  }

  async function remove() {
    if (!existing || !window.confirm("Delete this expense?")) return;
    setBusy("delete");
    setError(null);
    try {
      await api(`/api/expenses/${existing.id}`, { method: "DELETE" });
      router.replace("/app/expenses");
    } catch (e) {
      setError(toApiError(e));
      setBusy(null);
    }
  }

  const receiptUrl = existing?.receiptUrl ?? null;
  const isPdf = !!receiptUrl && /\.pdf($|\?)/i.test(receiptUrl);

  return (
    <form onSubmit={save} className="space-y-4">
      {!canEdit && <Notice>You can view expenses but not change them.</Notice>}
      <fieldset disabled={!canEdit} className="min-w-0 space-y-4">
        <Card className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Input label="Date" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            <div className="space-y-2">
              <Select label="Category" required value={categoryChoice} onChange={(e) => setCategoryChoice(e.target.value)}>
                <option value="" disabled>
                  Choose…
                </option>
                {categories.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
                <option value={OTHER}>Other…</option>
              </Select>
              {categoryChoice === OTHER && (
                <Input label="Category name" srLabel required placeholder="New category" value={otherCategory} onChange={(e) => setOtherCategory(e.target.value)} />
              )}
            </div>
            <Input label="Vendor" value={vendor} onChange={(e) => setVendor(e.target.value)} placeholder="Who you paid" />
            <Input label="Description" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="What it was for" />
          </div>
        </Card>

        <Card className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Input label={`Amount (${currency})`} inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0.00" />
            <Select
              label="Currency"
              value={currency}
              onChange={(e) => {
                setCurrency(e.target.value);
                setRateError(null);
              }}
            >
              {[...new Set([currency, base, ...CURRENCIES])].map((c) => (
                <option key={c}>{c}</option>
              ))}
            </Select>
            <Input
              label="VAT included"
              inputMode="decimal"
              value={tax}
              onChange={(e) => setTax(e.target.value)}
              placeholder="0"
              hint="Tax already inside the amount."
            />
          </div>
          {foreign && (
            <ExchangeRateField
              id={rateId}
              currency={currency}
              base={base}
              value={rateValue}
              onChange={(v) => {
                setRateEdit({ currency, value: v });
                setRateError(null);
              }}
              amountMinor={amountMinor ?? undefined}
              error={rateError}
              asOf={rates.data?.asOf}
              loading={rates.loading}
            />
          )}
          {foreign && existing && existing.currency === currency && (
            <p className="text-xs text-slate-500">Saved as {formatMoney(existing.baseAmountMinor, base)}.</p>
          )}
          <div className="grid gap-3 sm:grid-cols-3">
            <Select label="Payment method" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
              {PAYMENT_METHODS.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </Select>
            <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} placeholder="e.g. transfer ID" />
            <Select label="Client (optional)" value={clientId} onChange={(e) => setClientId(e.target.value)} hint="For costs you re-bill.">
              <option value="">None</option>
              {existing?.clientId && !clients.some((c) => c.id === existing.clientId) && (
                <option value={existing.clientId}>{existing.clientName ?? "Current client"}</option>
              )}
              {clients.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </div>
          <Textarea label="Notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Card>

        <Card>
          <SectionTitle>Receipt</SectionTitle>
          <input
            ref={fileInput}
            type="file"
            accept={RECEIPT_TYPES.join(",")}
            className="sr-only"
            aria-label="Receipt file"
            onChange={(e) => onReceiptChosen(e.target.files?.[0])}
          />
          {receiptUrl ? (
            <div className="flex flex-wrap items-center gap-3">
              {isPdf ? (
                <span className="flex h-16 w-16 items-center justify-center rounded-control border border-rule bg-slate-50 text-xs font-semibold text-slate-600">
                  PDF
                </span>
              ) : (
                <img src={receiptUrl} alt="Receipt" className="h-16 w-16 rounded-control border border-rule object-cover" />
              )}
              <a href={receiptUrl} target="_blank" rel="noopener noreferrer" className="text-sm font-semibold text-ink underline-offset-2 hover:underline">
                View receipt
              </a>
              {canEdit && (
                <>
                  <Button variant="secondary" className="min-h-8 px-3 py-1" loading={receiptBusy} onClick={() => fileInput.current?.click()}>
                    Replace
                  </Button>
                  <Button variant="danger" className="min-h-8 px-3 py-1" disabled={receiptBusy} onClick={removeReceipt}>
                    <TrashIcon className="h-4 w-4" /> Remove
                  </Button>
                </>
              )}
            </div>
          ) : pendingReceipt ? (
            <div className="flex flex-wrap items-center gap-3 text-sm">
              <ImageIcon className="text-faint" />
              <span className="min-w-0 flex-1 truncate">{pendingReceipt.name}</span>
              <Button variant="ghost" className="min-h-8 px-2 py-1" onClick={() => setPendingReceipt(null)}>
                Remove
              </Button>
            </div>
          ) : canEdit ? (
            <div>
              <Button variant="secondary" loading={receiptBusy} onClick={() => fileInput.current?.click()}>
                <ImageIcon className="h-4 w-4" /> Attach receipt
              </Button>
              <p className="mt-1 text-xs text-slate-500">Photo or PDF, up to 5 MB.</p>
            </div>
          ) : (
            <p className="text-sm text-slate-500">No receipt attached.</p>
          )}
        </Card>
      </fieldset>

      {error && (typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} plan="Business" />)}
      {saved && <Notice tone="success">Expense saved.</Notice>}

      {canEdit && (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            {existing && (
              <Button variant="danger" loading={busy === "delete"} disabled={!!busy} onClick={remove}>
                <TrashIcon className="h-4 w-4" /> Delete
              </Button>
            )}
          </div>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => router.push("/app/expenses")} disabled={!!busy}>
              Cancel
            </Button>
            <Button type="submit" loading={busy === "save"} disabled={!!busy}>
              {existing ? "Save changes" : "Save expense"}
            </Button>
          </div>
        </div>
      )}
    </form>
  );
}
