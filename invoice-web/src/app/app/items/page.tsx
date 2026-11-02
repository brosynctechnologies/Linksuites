"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import type { BusinessDto, ItemDto, SaveItemRequest, TaxRateDto } from "@/lib/types";
import { ApiErrorView, Button, DataList, EmptyState, ErrorBox, Input, Modal, PageHeader, PageLoader, Select, Textarea, dataRowClass } from "@/components/ui";
import { PlusIcon, TagIcon } from "@/components/icons";

export default function ItemsPage() {
  const { data, error, reload, setData } = useApi<ItemDto[]>("/api/items");
  const { data: taxData } = useApi<TaxRateDto[]>("/api/tax-rates");
  const { data: business } = useApi<BusinessDto>("/api/business");
  const [editing, setEditing] = useState<ItemDto | "new" | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const canEdit = useCan("docs.edit");
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const items = asList(data);
  const taxRates = asList(taxData);
  const currency = business?.currency ?? "NGN";

  async function remove(item: ItemDto) {
    if (!window.confirm(`Delete "${item.name}"? Existing invoices are not affected.`)) return;
    setBusy(item.id);
    setActionError(null);
    try {
      await api(`/api/items/${item.id}`, { method: "DELETE" });
      setData((prev) => asList(prev).filter((x) => x.id !== item.id));
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader
        title="Items"
        back="/app/more"
        subtitle="Saved products and services for quick invoicing."
        action={
          canEdit && (
            <Button onClick={() => setEditing("new")}>
              <PlusIcon className="h-4 w-4" /> Add item
            </Button>
          )
        }
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {actionError && (
        <div className="mb-3">
          <ApiErrorView error={actionError} />
        </div>
      )}
      {!data && !error && <PageLoader />}
      {data && items.length === 0 && (
        <EmptyState
          icon={<TagIcon width={24} height={24} />}
          title="No saved items"
          text="Save the things you sell often. When you type in an invoice line, they'll pop up with price and tax filled in."
          action={canEdit ? <Button onClick={() => setEditing("new")}>Add item</Button> : undefined}
        />
      )}
      {items.length > 0 && (
        <DataList
          template={canEdit ? "minmax(0,1fr) 9rem minmax(8rem,auto) 9rem" : "minmax(0,1fr) 9rem minmax(8rem,auto)"}
          head={[{ label: "Item" }, { label: "Tax" }, { label: "Price", align: "right" }, ...(canEdit ? [{ label: "" }] : [])]}
        >
          {items.map((it) => {
            const tax = taxRates.find((t) => t.id === it.taxRateId);
            const taxText = tax ? `${tax.name} ${tax.ratePercent}%` : null;
            return (
              <li key={it.id} className={dataRowClass}>
                <div className="min-w-0">
                  <p className="truncate font-medium text-graphite">{it.name}</p>
                  {it.description && <p className="truncate text-xs text-muted">{it.description}</p>}
                  {taxText && <p className="truncate text-xs text-muted md:hidden">{taxText}</p>}
                </div>
                <p className="hidden text-sm text-slate-600 md:block">{taxText ?? "No tax"}</p>
                <p className="amount text-right text-sm font-semibold">
                  {formatMoney(it.unitPriceMinor, currency)}
                  {it.unit && <span className="text-xs font-normal text-muted">/{it.unit}</span>}
                </p>
                {canEdit && (
                  <div className="col-span-2 -mx-2 flex justify-end gap-1 md:col-span-1 md:mx-0">
                    <Button variant="ghost" className="min-h-8 px-2.5 py-1" onClick={() => setEditing(it)} aria-label={`Edit ${it.name}`}>
                      Edit
                    </Button>
                    <Button
                      variant="danger"
                      className="min-h-8 px-2.5 py-1"
                      loading={busy === it.id}
                      onClick={() => remove(it)}
                      aria-label={`Delete ${it.name}`}
                    >
                      Delete
                    </Button>
                  </div>
                )}
              </li>
            );
          })}
        </DataList>
      )}

      <Modal open={editing !== null} title={editing === "new" ? "Add item" : "Edit item"} onClose={() => setEditing(null)}>
        {editing !== null && (
          <ItemForm
            item={editing === "new" ? undefined : editing}
            taxRates={taxRates}
            currency={currency}
            onCancel={() => setEditing(null)}
            onSaved={(saved) => {
              setData((prev) => {
                const arr = asList(prev);
                return arr.some((x) => x.id === saved.id) ? arr.map((x) => (x.id === saved.id ? saved : x)) : [...arr, saved];
              });
              setEditing(null);
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function ItemForm({
  item,
  taxRates,
  currency,
  onCancel,
  onSaved,
}: {
  item?: ItemDto;
  taxRates: TaxRateDto[];
  currency: string;
  onCancel: () => void;
  onSaved: (i: ItemDto) => void;
}) {
  const [name, setName] = useState(item?.name ?? "");
  const [description, setDescription] = useState(item?.description ?? "");
  const [price, setPrice] = useState(toMajorInput(item?.unitPriceMinor));
  const [taxRateId, setTaxRateId] = useState(item?.taxRateId ?? "");
  const [unit, setUnit] = useState(item?.unit ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return setError("Name is required.");
    const unitPriceMinor = toMinor(price);
    if (unitPriceMinor === null || unitPriceMinor < 0) return setError("Enter a valid price.");
    const body: SaveItemRequest = {
      name: name.trim(),
      description: description.trim() || null,
      unitPriceMinor,
      taxRateId: taxRateId || null,
      unit: unit.trim() || null,
    };
    setBusy(true);
    setError(null);
    try {
      onSaved(
        item
          ? await api<ItemDto>(`/api/items/${item.id}`, { method: "PUT", body })
          : await api<ItemDto>("/api/items", { method: "POST", body }),
      );
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3" noValidate>
      <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Logo design" />
      <Textarea label="Description (optional)" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
      <div className="grid grid-cols-2 gap-3">
        <Input label={`Price (${currency})`} inputMode="decimal" required value={price} onChange={(e) => setPrice(e.target.value)} />
        <Input label="Unit (optional)" value={unit} onChange={(e) => setUnit(e.target.value)} placeholder="hour, piece…" />
      </div>
      <Select label="Tax" value={taxRateId} onChange={(e) => setTaxRateId(e.target.value)}>
        <option value="">No tax</option>
        {taxRates.map((t) => (
          <option key={t.id} value={t.id}>
            {t.name} ({t.ratePercent}%)
          </option>
        ))}
      </Select>
      <ErrorBox error={error} />
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          {item ? "Save item" : "Add item"}
        </Button>
      </div>
    </form>
  );
}
