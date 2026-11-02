"use client";

import Link from "next/link";
import { setQuantity } from "@/lib/cart";
import { formatMoney } from "@/lib/money";
import { useHydrated } from "@/lib/auth";
import type { PublicStoreDto } from "@/lib/types";
import { quotedLine, unsellableLines, useLastOrder, usePricedCart } from "./use-quote";

const stepBtn = "flex h-11 w-11 items-center justify-center text-lg text-ink hover:bg-paper disabled:opacity-40";

export function CartView({ store }: { store: PublicStoreDto }) {
  const slug = store.slug;
  const { lines, quote, error, loading } = usePricedCart(slug);
  const hydrated = useHydrated();
  const lastOrder = useLastOrder(slug);
  const c = store.currency;

  if (!hydrated) {
    return (
      <div className="px-4 py-16 text-sm text-muted" role="status">
        Loading your cart…
      </div>
    );
  }

  if (lines.length === 0) {
    return (
      <div className="px-4 py-14">
        <p className="font-display text-xl font-bold text-ink">Your cart is empty</p>
        <p className="mt-1 text-sm text-muted">Pick something from {store.name} and it will show up here.</p>
        <Link
          href={`/${slug}`}
          className="mt-5 inline-flex min-h-12 items-center rounded-control bg-accent px-6 font-semibold text-accent-ink"
        >
          Browse products
        </Link>
        {lastOrder && (
          <p className="mt-6 text-sm text-muted">
            Placed an order?{" "}
            <Link href={`/o/${lastOrder}`} className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
              Track order {lastOrder}
            </Link>
          </p>
        )}
      </div>
    );
  }

  // Checking out with any of these would fail, so checkout waits until they are removed.
  const blocked = unsellableLines(lines, quote).length > 0;

  return (
    <div className="px-4 pt-2">
      <ul className="divide-y divide-line">
        {lines.map((l) => {
          const s = quotedLine(quote, l);
          // Priced by the server but this line is missing: it can't be sold any more and isn't in the subtotal.
          const unavailable = quote != null && s == null;
          // Still for sale, but other options of the same product already took all its stock.
          const soldOut = s != null && s.quantity === 0;
          const unit = s?.unitPriceMinor ?? l.unitPriceMinor;
          const stock = s?.stockQty ?? null;
          const qty = stock != null ? Math.min(l.quantity, stock) : l.quantity;
          const lineTotal = s?.lineTotalMinor ?? unit * qty;
          const atStockLimit = stock != null && qty >= stock;
          return (
            <li key={`${l.productId}:${l.variantId ?? ""}`} className="flex gap-3 py-4">
              <Link href={`/${slug}/p/${l.productSlug}`} className="shrink-0 rounded-control" tabIndex={-1} aria-hidden="true">
                {l.imageUrl ? (
                  <img src={l.imageUrl} alt="" className="h-20 w-20 rounded-control bg-paper object-cover" />
                ) : (
                  <span className="block h-20 w-20 rounded-control bg-paper" />
                )}
              </Link>
              <div className="flex min-w-0 flex-1 flex-col">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <Link href={`/${slug}/p/${l.productSlug}`} className="block truncate font-medium text-ink hover:underline">
                      {l.name}
                    </Link>
                    {l.variantTitle && <p className="text-xs text-muted">{l.variantTitle}</p>}
                    {atStockLimit && !soldOut && <p className="text-xs text-muted">Only {stock} in stock</p>}
                    {soldOut && (
                      <p className="text-xs font-medium text-hibiscus-deep">None left after your other options. Remove it to check out.</p>
                    )}
                    {unavailable && <p className="text-xs font-medium text-hibiscus-deep">No longer available. Remove it to check out.</p>}
                  </div>
                  <p className={`shrink-0 font-display font-bold ${unavailable ? "text-muted line-through" : "text-ink"}`}>
                    {formatMoney(lineTotal, c)}
                  </p>
                </div>
                <div className="mt-auto flex items-center justify-between pt-2">
                  <div
                    className="flex items-center overflow-hidden rounded-control ring-1 ring-line-strong ring-inset"
                    role="group"
                    aria-label={`Quantity of ${l.name}`}
                  >
                    <button
                      type="button"
                      className={stepBtn}
                      onClick={() => setQuantity(slug, l.productId, l.variantId, qty - 1)}
                      aria-label="Decrease quantity"
                    >
                      −
                    </button>
                    <span className="w-8 text-center text-sm font-semibold text-ink">{qty}</span>
                    <button
                      type="button"
                      className={stepBtn}
                      onClick={() => setQuantity(slug, l.productId, l.variantId, qty + 1)}
                      aria-label="Increase quantity"
                      disabled={atStockLimit}
                    >
                      +
                    </button>
                  </div>
                  <button
                    type="button"
                    onClick={() => setQuantity(slug, l.productId, l.variantId, 0)}
                    className="min-h-11 px-2 text-sm font-medium text-hibiscus-deep hover:underline"
                  >
                    Remove
                  </button>
                </div>
              </div>
            </li>
          );
        })}
      </ul>

      <div className="space-y-1 border-t border-line pt-4 text-sm">
        {error && (
          <p className="mb-2 rounded-control bg-hibiscus-soft p-3 text-hibiscus-deep" role="alert">
            {error.message}
          </p>
        )}
        <div className="flex items-baseline justify-between">
          <span className="text-base font-semibold text-ink">Subtotal</span>
          <span className={`font-display text-2xl font-bold text-ink ${loading ? "opacity-50" : ""}`}>
            {formatMoney(quote?.subtotalMinor ?? lines.reduce((n, l) => n + l.unitPriceMinor * l.quantity, 0), c)}
          </span>
        </div>
        <p className="text-xs text-subtle">Delivery and discounts are added at checkout.</p>
      </div>

      <div className="fixed inset-x-0 bottom-0 z-20">
        <div className="mx-auto max-w-[560px] border-t border-line bg-surface px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] shadow-float sm:border-x">
          {store.isOpen && blocked ? (
            <button type="button" disabled className="min-h-12 w-full rounded-control bg-line font-semibold text-muted">
              Remove unavailable items to check out
            </button>
          ) : store.isOpen ? (
            <Link
              href={`/${slug}/checkout`}
              className="flex min-h-12 w-full items-center justify-center rounded-control bg-accent font-semibold text-accent-ink"
            >
              Checkout
            </Link>
          ) : (
            <button type="button" disabled className="min-h-12 w-full rounded-control bg-line font-semibold text-muted">
              Store is closed
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
