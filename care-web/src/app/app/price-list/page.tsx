"use client";

import { useState, type FormEvent } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api } from "@/lib/api";
import { PRICE_CATEGORIES } from "@/lib/care";
import { useCare } from "@/lib/care-me";
import { koboInput, money, plural, toKobo } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { PriceCategory, PriceItemDto } from "@/lib/types";
import { PriceIcon } from "@/components/icons";
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
  Notice,
  PageHeader,
  PageLoader,
  SearchField,
  Segmented,
  Select,
  Toggle,
  caseRowClass,
  chipClass,
} from "@/components/ui";

type Show = "active" | "inactive" | "all";

/** The price list: everyone but clinical-only staff sees it here (the API lets every member read it); leadership and the cashier edit. */
export default function PriceListPage() {
  const { access } = useCare();
  const list = useApi<PriceItemDto[]>(access.canSeeMoney ? "/api/care/price-list" : null);
  const [category, setCategory] = useState<PriceCategory | "all">("all");
  const [show, setShow] = useState<Show>("active");
  const [q, setQ] = useState("");
  const [editing, setEditing] = useState<PriceItemDto | "new" | null>(null);

  if (!access.canSeeMoney) return <NoAccess text="The price list is for leadership, the front desk and the cashier." />;

  const term = q.trim().toLowerCase();
  const items = (list.data ?? []).filter(
    (i) =>
      (category === "all" || i.category === category) &&
      (show === "all" || (show === "active") === i.active) &&
      (!term || i.name.toLowerCase().includes(term)),
  );
  const canEdit = access.canEditPrices;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Price list"
        subtitle={canEdit ? "What the clinic charges. Bills copy the price at the time of charging, so changes never alter an existing bill." : "What the clinic charges. Leadership and the cashier keep it up to date."}
        action={
          canEdit && (
            <div className="flex flex-wrap items-start gap-2">
              <ImportDialog kind="care-price-list" title="Import the price list" onDone={list.reload} />
              <Button onClick={() => setEditing("new")}>Add item</Button>
            </div>
          )
        }
      />

      <div className="space-y-3">
        <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0" role="group" aria-label="Category">
          {[{ value: "all" as const, label: "All" }, ...PRICE_CATEGORIES].map((c) => (
            <button key={c.value} type="button" aria-pressed={category === c.value} onClick={() => setCategory(c.value)} className={chipClass(category === c.value)}>
              {c.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <SearchField id="price-search" label="Search the price list" placeholder="Search by name" value={q} onChange={setQ} className="min-w-56 flex-1" />
          <Segmented
            label="Show"
            value={show}
            onChange={setShow}
            items={[
              { value: "active", label: "In use" },
              { value: "inactive", label: "Not in use" },
              { value: "all", label: "All" },
            ]}
            className="w-full sm:w-80"
          />
        </div>
      </div>

      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && (
        <EmptyState
          icon={<PriceIcon />}
          title="No prices yet"
          text={canEdit ? "Add your consultation, registration, tests, drugs and bed charges, or import them from Excel." : "Leadership or the cashier adds them."}
          action={canEdit && <Button onClick={() => setEditing("new")}>Add the first item</Button>}
        />
      )}
      {list.data && list.data.length > 0 && items.length === 0 && <p className="text-sm text-muted">Nothing matches. Try another category or search.</p>}
      {items.length > 0 && (
        <>
          <p className="text-sm text-muted">{plural(items.length, "item")}</p>
          <CaseSheet
            label="Price list"
            template={canEdit ? "minmax(0,1fr) 9rem 8rem 5rem" : "minmax(0,1fr) 9rem 8rem"}
            head={[{ label: "Item" }, { label: "Category" }, { label: "Price", align: "right" }, ...(canEdit ? [{ label: "" }] : [])]}
          >
            {items.map((i) => (
              <li key={i.id} className={caseRowClass}>
                <span className="min-w-0">
                  <span className="block font-bold break-words text-ink">{i.name}</span>
                  {!i.active && <Badge className="mt-0.5">Not in use</Badge>}
                  <span className="mt-0.5 block text-xs text-muted md:hidden">{i.category}</span>
                </span>
                <span className="hidden text-sm text-slate-700 md:block">{i.category}</span>
                <span className="tnum text-right font-bold text-ink">{money(i.priceMinor)}</span>
                {canEdit && (
                  <span className="col-span-2 md:col-span-1 md:text-right">
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
        <PriceItemForm
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            list.reload();
          }}
        />
      )}
    </div>
  );
}

function PriceItemForm({ item, onClose, onSaved }: { item: PriceItemDto | null; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(item?.name ?? "");
  const [category, setCategory] = useState<PriceCategory | "">(item?.category ?? "");
  const [price, setPrice] = useState(item ? koboInput(item.priceMinor) : "");
  const [active, setActive] = useState(item?.active ?? true);
  const [priceError, setPriceError] = useState<string | null>(null);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const minor = toKobo(price);
    if (minor === null) return setPriceError("Enter the price in naira, such as 5000 or 2500.50.");
    setPriceError(null);
    const body = { name: name.trim(), category: category || null, priceMinor: minor, active };
    const res = await run(() =>
      item ? api<PriceItemDto>(`/api/care/price-list/${item.id}`, { method: "PUT", body }) : api<PriceItemDto>("/api/care/price-list", { method: "POST", body }),
    );
    if (res.ok) onSaved();
  }

  const fields = ["name", "category", "priceMinor"];
  return (
    <Modal open title={item ? `Edit ${item.name}` : "Add a price item"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} placeholder="e.g. General consultation" />
        <Select label="Category" required value={category} onChange={(e) => setCategory(e.target.value as PriceCategory)} error={error?.field("category")}>
          <option value="" disabled>
            Choose a category
          </option>
          {PRICE_CATEGORIES.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </Select>
        <Input
          label="Price (₦)"
          inputMode="decimal"
          required
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          error={priceError ?? error?.field("priceMinor")}
          hint={category === "Bed" ? "Charged once for each day in a bed." : undefined}
          className="tnum"
        />
        <Toggle checked={active} onChange={setActive} label="In use" description="Items not in use stay on old bills but can't be charged again." />
        {item && item.priceMinor !== toKobo(price) && toKobo(price) !== null && (
          <Notice>Existing bills keep the old price. Only new charges use {money(toKobo(price)!)}.</Notice>
        )}
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
