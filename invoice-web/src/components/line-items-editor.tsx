"use client";

import { useId, useState } from "react";
import { computeLine, parseNumber, type MathLineInput } from "@/lib/invoice-math";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import type { ItemDto, LineDto, LineRequest, TaxRateDto } from "@/lib/types";
import { PlusIcon, TrashIcon } from "./icons";
import { Button, cx, inputClass } from "./ui";
import type { PreviewLine } from "./invoice-preview";

/** Form state for one line (numbers kept as strings while typing). */
export interface EditorLine {
  key: string;
  itemId: string | null;
  description: string;
  quantity: string;
  unitPrice: string;
  discountPercent: string;
  /** Used in "rates" mode (signed-in editor). */
  taxRateId: string | null;
  /** Used in "percent" mode (free generator). */
  taxPercent: string;
}

let seq = 0;
function nextKey() {
  seq += 1;
  return `l${seq}-${Date.now().toString(36)}`;
}

export function newLine(partial: Partial<EditorLine> = {}): EditorLine {
  return {
    itemId: null,
    description: "",
    quantity: "1",
    unitPrice: "",
    discountPercent: "",
    taxRateId: null,
    taxPercent: "",
    ...partial,
    key: partial.key || nextKey(),
  };
}

/** Lines from the server (InvoiceDto / QuoteDto / RecurringDto items). */
export function editorLinesFrom(items: (LineRequest | LineDto)[]): EditorLine[] {
  return items.map((l) =>
    newLine({
      itemId: l.itemId ?? null,
      description: l.description ?? "",
      quantity: String(l.quantity ?? 1),
      unitPrice: toMajorInput(l.unitPriceMinor),
      discountPercent: l.discountPercent ? String(l.discountPercent) : "",
      taxRateId: l.taxRateId ?? null,
      taxPercent: "taxRatePercent" in l && l.taxRatePercent ? String(l.taxRatePercent) : "",
    }),
  );
}

export function isBlankLine(l: EditorLine) {
  return !l.description.trim() && !toMinor(l.unitPrice);
}

export function mathInput(l: EditorLine, taxRates?: TaxRateDto[]): MathLineInput {
  const rate = taxRates ? taxRates.find((r) => r.id === l.taxRateId) : undefined;
  return {
    quantity: parseNumber(l.quantity),
    unitPriceMinor: toMinor(l.unitPrice) ?? 0,
    discountPercent: parseNumber(l.discountPercent),
    ratePercent: taxRates ? (rate?.ratePercent ?? 0) : parseNumber(l.taxPercent),
    taxName: taxRates ? (rate?.name ?? null) : "Tax",
  };
}

export function toLineRequests(lines: EditorLine[]): LineRequest[] {
  return lines
    .filter((l) => !isBlankLine(l))
    .map((l) => ({
      itemId: l.itemId,
      description: l.description.trim(),
      quantity: parseNumber(l.quantity),
      unitPriceMinor: toMinor(l.unitPrice) ?? 0,
      discountPercent: Math.min(100, Math.max(0, parseNumber(l.discountPercent))),
      taxRateId: l.taxRateId,
    }));
}

/** Preview lines for the live HTML preview. */
export function previewLines(lines: EditorLine[], taxRates?: TaxRateDto[]): PreviewLine[] {
  return lines
    .filter((l) => !isBlankLine(l))
    .map((l) => {
      const m = mathInput(l, taxRates);
      return {
        description: l.description,
        quantity: m.quantity,
        unitPriceMinor: m.unitPriceMinor,
        discountPercent: m.discountPercent,
        taxLabel: m.ratePercent ? `${m.taxName ?? "Tax"} ${m.ratePercent}%` : null,
        lineTotalMinor: computeLine(m).lineTotal,
      };
    });
}

const small = "block text-xs font-medium text-slate-700";

export function LineItemsEditor({
  lines,
  onChange,
  currency,
  taxRates,
  items,
}: {
  lines: EditorLine[];
  onChange: (lines: EditorLine[]) => void;
  currency: string;
  /** When given, taxes are picked from saved rates; otherwise a free-typed tax % is used. */
  taxRates?: TaxRateDto[];
  /** Saved products/services for autocomplete. */
  items?: ItemDto[];
}) {
  const baseId = useId();
  const [openKey, setOpenKey] = useState<string | null>(null);

  const update = (key: string, patch: Partial<EditorLine>) => onChange(lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  const remove = (key: string) => {
    const rest = lines.filter((l) => l.key !== key);
    onChange(rest.length ? rest : [newLine()]);
  };

  function pick(line: EditorLine, item: ItemDto) {
    update(line.key, {
      itemId: item.id,
      description: item.description ? `${item.name} — ${item.description}` : item.name,
      unitPrice: toMajorInput(item.unitPriceMinor),
      taxRateId: item.taxRateId ?? line.taxRateId,
    });
    setOpenKey(null);
  }

  return (
    <div className="space-y-3">
      {lines.map((l, idx) => {
        const id = `${baseId}-${idx}`;
        const m = mathInput(l, taxRates);
        const total = computeLine(m).lineTotal;
        const q = l.description.trim().toLowerCase();
        const matches =
          items && openKey === l.key
            ? items.filter((it) => !q || it.name.toLowerCase().includes(q) || (it.description ?? "").toLowerCase().includes(q)).slice(0, 6)
            : [];
        return (
          <fieldset key={l.key} className="rounded-control border border-rule bg-slate-50/60 p-3 sm:p-4">
            <p className="mb-2 text-xs font-semibold text-muted" aria-hidden="true">
              Line {idx + 1}
            </p>
            <legend className="sr-only">Line {idx + 1}</legend>
            <div className="relative">
              <label htmlFor={`${id}-d`} className={small}>
                Description
              </label>
              <input
                id={`${id}-d`}
                className={cx(inputClass, "mt-1")}
                value={l.description}
                placeholder={items?.length ? "Type or pick a saved item" : "e.g. Website design"}
                autoComplete="off"
                role={items ? "combobox" : undefined}
                aria-expanded={items ? matches.length > 0 : undefined}
                aria-controls={items ? `${id}-list` : undefined}
                onFocus={() => setOpenKey(l.key)}
                onBlur={() => setOpenKey((k) => (k === l.key ? null : k))}
                onKeyDown={(e) => {
                  if (e.key === "Escape") setOpenKey(null);
                }}
                onChange={(e) => {
                  update(l.key, { description: e.target.value, itemId: null });
                  setOpenKey(l.key);
                }}
              />
              {matches.length > 0 && (
                <ul
                  id={`${id}-list`}
                  role="listbox"
                  className="absolute inset-x-0 top-full z-20 mt-1 max-h-60 overflow-y-auto rounded-control border border-rule bg-paper py-1 shadow-float"
                >
                  {matches.map((it) => (
                    <li key={it.id} role="option" aria-selected={false}>
                      <button
                        type="button"
                        className="flex w-full items-center justify-between gap-3 px-3 py-2 text-left text-sm hover:bg-ink-wash"
                        onMouseDown={(e) => e.preventDefault()}
                        onClick={() => pick(l, it)}
                      >
                        <span className="min-w-0">
                          <span className="block truncate font-medium text-graphite">{it.name}</span>
                          {it.description && <span className="block truncate text-xs text-muted">{it.description}</span>}
                        </span>
                        <span className="amount shrink-0 text-slate-600">
                          {formatMoney(it.unitPriceMinor, currency)}
                          {it.unit && <span className="text-xs text-muted">/{it.unit}</span>}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <div className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-4">
              <div>
                <label htmlFor={`${id}-q`} className={small}>
                  Qty
                </label>
                <input
                  id={`${id}-q`}
                  className={cx(inputClass, "mt-1")}
                  inputMode="decimal"
                  value={l.quantity}
                  onChange={(e) => update(l.key, { quantity: e.target.value })}
                />
              </div>
              <div>
                <label htmlFor={`${id}-p`} className={small}>
                  Unit price ({currency})
                </label>
                <input
                  id={`${id}-p`}
                  className={cx(inputClass, "mt-1")}
                  inputMode="decimal"
                  placeholder="0"
                  value={l.unitPrice}
                  onChange={(e) => update(l.key, { unitPrice: e.target.value })}
                />
              </div>
              <div>
                <label htmlFor={`${id}-disc`} className={small}>
                  Discount %
                </label>
                <input
                  id={`${id}-disc`}
                  className={cx(inputClass, "mt-1")}
                  inputMode="decimal"
                  placeholder="0"
                  value={l.discountPercent}
                  onChange={(e) => update(l.key, { discountPercent: e.target.value })}
                />
              </div>
              <div>
                <label htmlFor={`${id}-t`} className={small}>
                  Tax{taxRates ? "" : " %"}
                </label>
                {taxRates ? (
                  <select
                    id={`${id}-t`}
                    className={cx(inputClass, "mt-1")}
                    value={l.taxRateId ?? ""}
                    onChange={(e) => update(l.key, { taxRateId: e.target.value || null })}
                  >
                    <option value="">No tax</option>
                    {taxRates.map((r) => (
                      <option key={r.id} value={r.id}>
                        {r.name} ({r.ratePercent}%)
                      </option>
                    ))}
                  </select>
                ) : (
                  <input
                    id={`${id}-t`}
                    className={cx(inputClass, "mt-1")}
                    inputMode="decimal"
                    placeholder="0"
                    value={l.taxPercent}
                    onChange={(e) => update(l.key, { taxPercent: e.target.value })}
                  />
                )}
              </div>
            </div>

            <div className="mt-3 flex items-center justify-between gap-3 border-t border-rule-soft pt-2.5">
              <p className="text-sm text-muted">
                Amount <span className="amount ml-1 font-semibold text-graphite">{formatMoney(total, currency)}</span>
              </p>
              <button
                type="button"
                onClick={() => remove(l.key)}
                className="-mr-1 flex min-h-8 items-center gap-1 rounded-control px-2 py-1 text-sm font-medium text-overdue hover:bg-overdue-wash"
                aria-label={`Remove line ${idx + 1}`}
              >
                <TrashIcon className="h-4 w-4" /> Remove
              </button>
            </div>
          </fieldset>
        );
      })}
      <Button variant="secondary" onClick={() => onChange([...lines, newLine()])}>
        <PlusIcon className="h-4 w-4" /> Add line
      </Button>
    </div>
  );
}
