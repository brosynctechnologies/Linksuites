"use client";

import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { CURRENCIES } from "@/lib/config";
import { formatDay, todayISO } from "@/lib/dates";
import { computeTotals } from "@/lib/invoice-math";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { TEMPLATES } from "@/lib/templates";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import type { BusinessDto, ItemDto, RecurringDto, RecurringFrequency, SaveRecurringRequest, TaxRateDto, TemplateDto } from "@/lib/types";
import { ClientPicker, type PickedClient } from "@/components/client-picker";
import { LineItemsEditor, editorLinesFrom, isBlankLine, mathInput, newLine, toLineRequests, type EditorLine } from "@/components/line-items-editor";
import { TemplatePicker } from "@/components/template-picker";
import {
  ApiErrorView,
  Button,
  Card,
  DataList,
  EmptyState,
  ErrorBox,
  Input,
  Notice,
  PageHeader,
  PageLoader,
  Select,
  Textarea,
  StatusBadge,
  Toggle,
  dataRowClass,
} from "@/components/ui";
import { PlusIcon, RepeatIcon } from "@/components/icons";

const FREQUENCIES: RecurringFrequency[] = ["Weekly", "Monthly", "Quarterly", "Yearly"];

export default function RecurringPage() {
  const list = useApi<RecurringDto[]>("/api/recurring");
  const business = useApi<BusinessDto>("/api/business");
  const taxRates = useApi<TaxRateDto[]>("/api/tax-rates");
  const templates = useApi<TemplateDto[]>("/api/invoice-templates");
  const items = useApi<ItemDto[]>("/api/items");
  const [editing, setEditing] = useState<RecurringDto | "new" | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const canEdit = useCan("docs.edit");

  const schedules = asList(list.data);
  const ready = business.data && taxRates.data;

  async function remove(r: RecurringDto) {
    if (!window.confirm(`Delete the recurring invoice for ${r.clientName}? Invoices already created are kept.`)) return;
    setBusy(r.id);
    setError(null);
    try {
      await api(`/api/recurring/${r.id}`, { method: "DELETE" });
      list.setData((prev) => asList(prev).filter((x) => x.id !== r.id));
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  if (editing && ready && canEdit) {
    const tpl = asList(templates.data);
    return (
      <div>
        <PageHeader title={editing === "new" ? "New recurring invoice" : "Edit recurring invoice"} />
        <RecurringForm
          key={editing === "new" ? "new" : editing.id}
          existing={editing === "new" ? undefined : editing}
          business={business.data!}
          taxRates={asList(taxRates.data)}
          items={asList(items.data)}
          templates={tpl.length ? tpl : TEMPLATES.map((t) => ({ ...t, available: !t.isPremium }))}
          onCancel={() => setEditing(null)}
          onSaved={(saved) => {
            list.setData((prev) => {
              const arr = asList(prev);
              return arr.some((x) => x.id === saved.id) ? arr.map((x) => (x.id === saved.id ? saved : x)) : [saved, ...arr];
            });
            setEditing(null);
          }}
        />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Recurring invoices"
        back="/app/more"
        subtitle="Bill retainers and subscriptions automatically (Pro)."
        action={
          canEdit && (
            <Button onClick={() => setEditing("new")} disabled={!ready}>
              <PlusIcon className="h-4 w-4" /> New
            </Button>
          )
        }
      />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {error && <ApiErrorView error={error} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && schedules.length === 0 && (
        <EmptyState
          icon={<RepeatIcon width={24} height={24} />}
          title="No recurring invoices"
          text="Create a schedule and we'll generate the invoice on each date, and email it to your client if you like."
          action={
            canEdit && (
              <Button onClick={() => setEditing("new")} disabled={!ready}>
                Create a schedule
              </Button>
            )
          }
        />
      )}
      {schedules.length > 0 && (
        <DataList
          template={canEdit ? "minmax(0,1fr) 10rem 9rem minmax(8rem,auto) 10.5rem" : "minmax(0,1fr) 10rem 9rem minmax(8rem,auto)"}
          head={[
            { label: "Client" },
            { label: "Repeats" },
            { label: "Next invoice" },
            { label: "Amount", align: "right" },
            ...(canEdit ? [{ label: "" }] : []),
          ]}
        >
          {schedules.map((r) => (
            <li key={r.id} className={dataRowClass}>
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 font-medium text-graphite">
                  <span className="truncate">{r.clientName}</span>
                  <StatusBadge tone={r.isActive ? "green" : "void"}>{r.isActive ? "Active" : "Paused"}</StatusBadge>
                </p>
                <p className="text-xs text-muted">
                  Created {r.invoicesCreated} invoice{r.invoicesCreated === 1 ? "" : "s"}
                  {r.endDate ? `, ends ${formatDay(r.endDate)}` : ""}
                </p>
                <p className="text-xs text-muted md:hidden">
                  {r.frequency}
                  {r.autoSend ? ", emailed automatically" : ""}. Next: {r.nextRunDate ? formatDay(r.nextRunDate) : "—"}
                </p>
              </div>
              <p className="hidden text-sm text-slate-600 md:block">
                {r.frequency}
                {r.autoSend && <span className="block text-xs text-muted">Emailed automatically</span>}
              </p>
              <p className="hidden text-sm text-slate-600 tabular-nums md:block">{r.nextRunDate ? formatDay(r.nextRunDate) : "—"}</p>
              <p className="amount self-start text-right font-semibold md:self-center">{formatMoney(r.totalMinor, r.currency)}</p>
              {canEdit && (
                <div className="col-span-2 mt-2 flex justify-end gap-2 md:col-span-1 md:mt-0">
                  <Button variant="secondary" className="min-h-8 px-3 py-1" onClick={() => setEditing(r)} disabled={!ready}>
                    Edit
                  </Button>
                  <Button variant="danger" className="min-h-8 px-3 py-1" loading={busy === r.id} onClick={() => remove(r)}>
                    Delete
                  </Button>
                </div>
              )}
            </li>
          ))}
        </DataList>
      )}
    </div>
  );
}

function RecurringForm({
  existing,
  business,
  taxRates,
  items,
  templates,
  onCancel,
  onSaved,
}: {
  existing?: RecurringDto;
  business: BusinessDto;
  taxRates: TaxRateDto[];
  items: ItemDto[];
  templates: TemplateDto[];
  onCancel: () => void;
  onSaved: (r: RecurringDto) => void;
}) {
  const [client, setClient] = useState<PickedClient | null>(existing ? { id: existing.clientId, name: existing.clientName } : null);
  const [frequency, setFrequency] = useState<RecurringFrequency>(existing?.frequency ?? "Monthly");
  const [startDate, setStartDate] = useState(existing?.startDate ?? todayISO());
  const [endDate, setEndDate] = useState(existing?.endDate ?? "");
  const [dueInDays, setDueInDays] = useState(String(existing?.dueInDays ?? business.paymentTermsDays ?? 14));
  const [autoSend, setAutoSend] = useState(existing?.autoSend ?? true);
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);
  const [currency, setCurrency] = useState(existing?.currency ?? business.currency ?? "NGN");
  const [template, setTemplate] = useState(existing?.template ?? business.defaultTemplate ?? "classic");
  const [lines, setLines] = useState<EditorLine[]>(() => (existing?.items?.length ? editorLinesFrom(existing.items) : [newLine()]));
  const [shipping, setShipping] = useState(existing?.shippingMinor ? toMajorInput(existing.shippingMinor) : "");
  const [notes, setNotes] = useState(existing ? (existing.notes ?? "") : (business.defaultNotes ?? ""));
  const [terms, setTerms] = useState(existing ? (existing.terms ?? "") : (business.defaultTerms ?? ""));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  const total = computeTotals(
    lines.filter((l) => !isBlankLine(l)).map((l) => mathInput(l, taxRates)),
    toMinor(shipping) ?? 0,
  ).totalMinor;

  async function save() {
    setError(null);
    if (!client) return setError("Choose or add a client.");
    const reqLines = toLineRequests(lines);
    if (!reqLines.length) return setError("Add at least one line item.");
    if (endDate && endDate < startDate) return setError("The end date can't be before the start date.");
    const body: SaveRecurringRequest = {
      clientId: client.id,
      frequency,
      startDate,
      endDate: endDate || null,
      dueInDays: Math.max(0, Math.round(Number(dueInDays) || 0)),
      autoSend,
      isActive,
      currency,
      template,
      items: reqLines,
      shippingMinor: toMinor(shipping) ?? 0,
      notes: notes.trim() || null,
      terms: terms.trim() || null,
    };
    setBusy(true);
    try {
      const saved = existing
        ? await api<RecurringDto>(`/api/recurring/${existing.id}`, { method: "PUT", body })
        : await api<RecurringDto>("/api/recurring", { method: "POST", body });
      onSaved(saved);
    } catch (e) {
      setError(toApiError(e));
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      <Card className="space-y-4">
        <ClientPicker value={client} onChange={setClient} />
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Select label="Frequency" value={frequency} onChange={(e) => setFrequency(e.target.value as RecurringFrequency)}>
            {FREQUENCIES.map((f) => (
              <option key={f}>{f}</option>
            ))}
          </Select>
          <Input label="Start date" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
          <Input label="End date (optional)" type="date" value={endDate} min={startDate} onChange={(e) => setEndDate(e.target.value)} />
          <Input label="Due in (days)" inputMode="numeric" value={dueInDays} onChange={(e) => setDueInDays(e.target.value.replace(/\D/g, ""))} />
        </div>
        <Toggle checked={autoSend} onChange={setAutoSend} label="Email each invoice automatically" description="Otherwise invoices are created as drafts for you to review." />
        <Toggle checked={isActive} onChange={setIsActive} label="Active" description="Pause to stop creating new invoices." />
      </Card>

      <Card>
        <h2 className="mb-3 font-semibold text-graphite">Items</h2>
        <LineItemsEditor lines={lines} onChange={setLines} currency={currency} taxRates={taxRates} items={items} />
        <div className="mt-4 grid grid-cols-2 gap-3">
          <Input label={`Shipping (${currency})`} inputMode="decimal" placeholder="0" value={shipping} onChange={(e) => setShipping(e.target.value)} />
          <Select label="Currency" value={currency} onChange={(e) => setCurrency(e.target.value)}>
            {[...new Set([currency, ...CURRENCIES])].map((c) => (
              <option key={c}>{c}</option>
            ))}
          </Select>
        </div>
      </Card>

      <Card className="space-y-3">
        <Textarea label="Notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
        <Textarea label="Terms" rows={2} value={terms} onChange={(e) => setTerms(e.target.value)} />
        <TemplatePicker
          templates={templates}
          value={template}
          onChange={setTemplate}
          brandColor={business.brandColor}
          secondaryColor={business.secondaryColor}
        />
      </Card>

      {error && (typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />)}
      {!existing && <Notice tone="info">Recurring invoices are a Pro feature.</Notice>}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-panel border border-rule bg-paper p-3 sm:px-4">
        <p className="text-sm text-muted">
          Each invoice <span className="font-figure amount ml-1 text-xl text-graphite">{formatMoney(total, currency)}</span>
        </p>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={save} loading={busy}>
            {existing ? "Save changes" : "Create schedule"}
          </Button>
        </div>
      </div>
    </div>
  );
}
