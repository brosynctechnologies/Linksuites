"use client";

import Link from "next/link";
import { useState } from "react";
import { addToCart } from "@/lib/cart";
import { formatMoney } from "@/lib/money";
import type { PublicProductDto } from "@/lib/types";

const stepBtn = "flex h-11 w-11 items-center justify-center text-xl text-ink hover:bg-paper disabled:opacity-40";

export function ProductPurchase({
  product,
  slug,
  currency,
  isOpen,
}: {
  product: PublicProductDto;
  slug: string;
  currency: string;
  isOpen: boolean;
}) {
  const hasOptions = product.options.length > 0 && product.variants.length > 0;
  const [selected, setSelected] = useState<(string | null)[]>(() => product.options.map(() => null));
  const [qty, setQty] = useState(1);
  const [added, setAdded] = useState(false);

  const complete = !hasOptions || selected.every((v) => v !== null);
  const variant = hasOptions && complete ? product.variants.find((v) => v.optionValues.every((ov, i) => ov === selected[i])) : undefined;
  const comboMissing = hasOptions && complete && !variant;

  const unitPrice = variant?.priceMinor ?? product.priceMinor;
  const inStock = product.inStock && (!variant || variant.inStock) && !comboMissing;
  const showCompare = !variant?.priceMinor && product.compareAtPriceMinor != null && product.compareAtPriceMinor > product.priceMinor;

  /** Is there any in-stock variant that matches the current selection with `value` at option `index`? */
  function available(index: number, value: string) {
    return product.variants.some(
      (v) => v.inStock && v.optionValues[index] === value && v.optionValues.every((ov, i) => i === index || selected[i] === null || selected[i] === ov),
    );
  }

  function choose(index: number, value: string) {
    setAdded(false);
    setSelected((s) => s.map((v, i) => (i === index ? (v === value ? null : value) : v)));
  }

  function add() {
    if (!complete || !inStock) return;
    addToCart(slug, {
      productId: product.id,
      variantId: variant?.id ?? null,
      quantity: qty,
      name: product.name,
      variantTitle: variant?.title ?? null,
      unitPriceMinor: unitPrice,
      imageUrl: product.images[0]?.url ?? null,
      productSlug: product.slug,
    });
    setAdded(true);
  }

  return (
    <div className="space-y-6">
      <p className="flex flex-wrap items-baseline gap-x-3">
        <span className="font-display text-3xl font-bold text-ink">{formatMoney(unitPrice, currency)}</span>
        {showCompare && <span className="text-base text-subtle line-through">{formatMoney(product.compareAtPriceMinor, currency)}</span>}
      </p>

      {hasOptions &&
        product.options.map((opt, i) => (
          <fieldset key={opt.name}>
            <legend className="mb-2 text-sm font-semibold text-ink">
              {opt.name}
              {selected[i] && <span className="ml-1 font-normal text-muted">: {selected[i]}</span>}
            </legend>
            <div className="flex flex-wrap gap-2">
              {opt.values.map((val) => {
                const active = selected[i] === val;
                const ok = available(i, val);
                return (
                  <button
                    key={val}
                    type="button"
                    aria-pressed={active}
                    onClick={() => choose(i, val)}
                    className={`min-h-11 min-w-11 rounded-control px-3.5 text-sm font-medium transition-colors ${
                      active ? "bg-accent text-accent-ink" : "bg-surface text-ink ring-1 ring-line-strong ring-inset hover:ring-ink"
                    } ${ok ? "" : "text-subtle line-through opacity-60"}`}
                  >
                    {val}
                    {!ok && <span className="sr-only"> (unavailable)</span>}
                  </button>
                );
              })}
            </div>
          </fieldset>
        ))}

      <div className="flex items-center gap-4">
        <span className="text-sm font-semibold text-ink" id="qty-label">
          Quantity
        </span>
        <div className="flex items-center overflow-hidden rounded-control ring-1 ring-line-strong ring-inset" role="group" aria-labelledby="qty-label">
          <button type="button" className={stepBtn} onClick={() => setQty((q) => Math.max(1, q - 1))} aria-label="Decrease quantity">
            −
          </button>
          <span className="w-9 text-center font-semibold text-ink" aria-live="polite">
            {qty}
          </span>
          <button type="button" className={stepBtn} onClick={() => setQty((q) => Math.min(99, q + 1))} aria-label="Increase quantity">
            +
          </button>
        </div>
      </div>

      {!isOpen && <p className="text-sm font-medium text-marigold-deep">This store is closed right now, so checkout is unavailable.</p>}

      <div className="fixed inset-x-0 bottom-0 z-20">
        <div className="mx-auto max-w-[560px] border-t border-line bg-surface px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] shadow-float sm:border-x">
          {added ? (
            <div className="flex gap-2" role="status">
              <Link
                href={`/${slug}`}
                className="flex min-h-12 flex-1 items-center justify-center rounded-control font-semibold text-ink ring-1 ring-line-strong ring-inset hover:bg-paper"
              >
                Keep shopping
              </Link>
              <Link
                href={`/${slug}/cart`}
                className="flex min-h-12 flex-1 items-center justify-center gap-2 rounded-control bg-accent font-semibold text-accent-ink"
              >
                <svg viewBox="0 0 24 24" width={18} height={18} fill="none" stroke="currentColor" strokeWidth={2.5} aria-hidden="true">
                  <path d="M20 6 9 17l-5-5" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                View cart
              </Link>
            </div>
          ) : (
            <button
              type="button"
              onClick={add}
              disabled={!complete || !inStock}
              className="flex min-h-12 w-full items-center justify-between gap-3 rounded-control bg-accent px-5 font-semibold text-accent-ink disabled:justify-center disabled:bg-line disabled:text-muted"
            >
              {!inStock ? (
                comboMissing ? (
                  "Not available"
                ) : (
                  "Out of stock"
                )
              ) : !complete ? (
                `Choose ${product.options
                  .filter((_, i) => selected[i] === null)
                  .map((o) => o.name.toLowerCase())
                  .join(" & ")}`
              ) : (
                <>
                  <span>Add to cart</span>
                  <span className="font-display text-lg font-bold">{formatMoney(unitPrice * qty, currency)}</span>
                </>
              )}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
