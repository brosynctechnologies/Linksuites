"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatDay, money, plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { PriceItemDto } from "@/lib/types";
import type { StockItemDto } from "@/lib/types/lab-pharmacy";
import {
  ApiErrorView,
  Badge,
  Button,
  CaseSheet,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  PageHeader,
  PageLoader,
  SearchField,
  Select,
  Toggle,
  caseRowClass,
} from "@/components/ui";

/**
 * Wave 4: pharmacy stock on hand (`GET /api/care/pharmacy/items`): pharmacists and leadership. Each item with its usable stock (batches not
 * expired), expired stock waiting to be counted out, the next expiry and its value at cost. Pharmacists and leadership add and edit items,
 * each linked to the Drug price-list item it is sold under so a prescription finds its stock.
 */
export default function PharmacyStockPage() {
  const { access } = useCare();
  const items = useApi<StockItemDto[]>(access.canManageStock ? "/api/care/pharmacy/items" : null);
  const [q, setQ] = useState("");
  const [editing, setEditing] = useState<StockItemDto | "new" | null>(null);
  if (!access.canManageStock)
    return access.canDispense ? (
      <NoAccess text="Stock is managed by pharmacists and the clinic's leadership. Use Dispense above to fill prescriptions." />
    ) : (
      <NoAccess text="The pharmacy is for pharmacists, doctors, nurses and the clinic's leadership." />
    );

  const canEdit = !access.readOnly;
  const term = q.trim().toLowerCase();
  const shown = (items.data ?? []).filter((i) => !term || i.name.toLowerCase().includes(term));
  const value = shown.reduce((sum, i) => sum + i.valueMinor, 0);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Pharmacy stock"
        subtitle="What is on hand. Dispensing takes the batch that expires first; expired stock is never dispensed."
        action={canEdit && <Button onClick={() => setEditing("new")}>Add item</Button>}
      />
      <SearchField id="stock-search" label="Search stock" placeholder="Search by name" value={q} onChange={setQ} />
      {items.error && <ApiErrorView error={items.error} onRetry={items.reload} />}
      {!items.data && !items.error && <PageLoader />}
      {items.data && items.data.length === 0 && (
        <EmptyState title="No stock items yet" text="Add the drugs the pharmacy holds, then receive stock against them." action={canEdit && <Button onClick={() => setEditing("new")}>Add the first item</Button>} />
      )}
      {shown.length > 0 && (
        <>
          <p className="text-sm text-muted">
            {plural(shown.length, "item")}, worth {money(value)} at cost
          </p>
          <CaseSheet
            label="Stock"
            template={canEdit ? "minmax(0,1fr) 8rem 8rem 8rem 5rem" : "minmax(0,1fr) 8rem 8rem 8rem"}
            head={[{ label: "Item" }, { label: "On hand", align: "right" }, { label: "Next expiry" }, { label: "Value", align: "right" }, ...(canEdit ? [{ label: "" }] : [])]}
          >
            {shown.map((i) => (
              <li key={i.id} className={caseRowClass}>
                <span className="min-w-0">
                  <Link href={`/app/pharmacy/items/${i.id}`} className="block font-bold text-scrub hover:underline">
                    {i.name}
                  </Link>
                  <span className="mt-0.5 flex flex-wrap gap-1">
                    {i.low && i.active && <Badge tone="iodine">Low stock</Badge>}
                    {i.expiredOnHand > 0 && <Badge tone="arterial">{i.expiredOnHand} expired</Badge>}
                    {!i.active && <Badge>Not in use</Badge>}
                  </span>
                </span>
                <span className="tnum text-right text-sm font-bold text-ink">
                  {i.onHand} {i.unit}
                  <span className="block text-xs font-normal text-muted">reorder at {i.reorderLevel}</span>
                </span>
                <span className="text-sm text-slate-700">{i.nextExpiry ? formatDay(i.nextExpiry) : "None"}</span>
                <span className="tnum text-right text-sm">{money(i.valueMinor)}</span>
                {canEdit && (
                  <span className="md:text-right">
                    <Button variant="ghost" className="-mr-2 px-2.5" onClick={() => setEditing(i)}>
                      Edit<span className="sr-only"> {i.name}</span>
                    </Button>
                  </span>
                )}
              </li>
            ))}
          </CaseSheet>
        </>
      )}
      {editing && (
        <StockItemForm
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            items.reload();
          }}
        />
      )}
    </div>
  );
}

function StockItemForm({ item, onClose, onSaved }: { item: StockItemDto | null; onClose: () => void; onSaved: () => void }) {
  const prices = useApi<PriceItemDto[]>("/api/care/price-list?active=true&category=Drug");
  const [name, setName] = useState(item?.name ?? "");
  const [form, setForm] = useState(item?.form ?? "");
  const [unit, setUnit] = useState(item?.unit ?? "tablet");
  const [priceItemId, setPriceItemId] = useState(item?.priceItemId ?? "");
  const [reorder, setReorder] = useState(String(item?.reorderLevel ?? 0));
  const [active, setActive] = useState(item?.active ?? true);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = { name: name.trim(), form: form.trim() || null, unit: unit.trim() || null, priceItemId: priceItemId || null, reorderLevel: Number(reorder) || 0, active };
    const res = await run(() => (item ? api(`/api/care/pharmacy/items/${item.id}`, { method: "PUT", body }) : api("/api/care/pharmacy/items", { method: "POST", body })));
    if (res.ok) onSaved();
  }

  const fields = ["name", "form", "unit", "priceItemId", "reorderLevel"];
  return (
    <Modal open title={item ? `Edit ${item.name}` : "Add a stock item"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} placeholder="e.g. Amoxicillin 500 mg" />
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Form" value={form} onChange={(e) => setForm(e.target.value)} error={error?.field("form")} placeholder="Capsule, syrup…" />
          <Input label="Counted in" value={unit} onChange={(e) => setUnit(e.target.value)} error={error?.field("unit")} placeholder="tablet, bottle, vial" />
        </div>
        <Select
          label="Sold as"
          value={priceItemId}
          onChange={(e) => setPriceItemId(e.target.value)}
          error={error?.field("priceItemId")}
          hint="A prescription with this price item is dispensed from this stock."
        >
          <option value="">Not linked</option>
          {(prices.data ?? []).map((p) => (
            <option key={p.id} value={p.id}>
              {p.name} ({money(p.priceMinor)})
            </option>
          ))}
        </Select>
        <Input label="Reorder level" inputMode="numeric" value={reorder} onChange={(e) => setReorder(e.target.value)} error={error?.field("reorderLevel")} hint="Pharmacy staff are told when usable stock falls to this." className="tnum" />
        <Toggle checked={active} onChange={setActive} label="In use" />
        <ErrorBox error={error && !fields.some((f) => error.field(f)) ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {item ? "Save" : "Add item"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
