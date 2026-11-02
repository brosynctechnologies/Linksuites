"use client";

import { useEffect, useState, useSyncExternalStore } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { lastOrderKey, readCart, setQuantity, useCart, type CartLine } from "@/lib/cart";
import type { QuoteDto, QuoteRequest } from "@/lib/types";

/** Ask the server to price the cart. Re-runs whenever the request changes. */
export function useQuote(slug: string, request: QuoteRequest | null) {
  const key = request && request.items.length ? JSON.stringify(request) : null;
  const [state, setState] = useState<{ key: string | null; quote?: QuoteDto; error?: ApiError }>({ key: null });

  useEffect(() => {
    if (!key) return;
    const ctrl = new AbortController();
    api<QuoteDto>(`/api/public/stores/${encodeURIComponent(slug)}/quote`, {
      method: "POST",
      body: JSON.parse(key),
      auth: false,
      signal: ctrl.signal,
    }).then(
      (quote) => {
        if (!ctrl.signal.aborted) setState({ key, quote });
      },
      (e) => {
        if (!ctrl.signal.aborted) setState((s) => ({ key, quote: s.quote, error: toApiError(e) }));
      },
    );
    return () => ctrl.abort();
  }, [slug, key]);

  return {
    quote: state.key === key ? state.quote : undefined,
    error: state.key === key ? state.error : undefined,
    loading: key !== null && state.key !== key,
  };
}

/** The quote's line for a cart line, if the server priced it. */
export function quotedLine(quote: QuoteDto | undefined, line: Pick<CartLine, "productId" | "variantId">) {
  return quote?.lines.find((q) => q.productId === line.productId && (q.variantId ?? null) === (line.variantId ?? null));
}

/**
 * Cart lines the server won't sell: dropped from the quote (inactive, deleted, out of stock) or with no stock left
 * for them (variants share their product's stock). Placing the order with any of them would fail.
 */
export function unsellableLines(lines: CartLine[], quote: QuoteDto | undefined): CartLine[] {
  if (!quote) return [];
  return lines.filter((l) => {
    const q = quotedLine(quote, l);
    return q == null || q.quantity === 0;
  });
}

/** Lower cart quantities to what the quote can actually sell. A line with nothing left stays, marked unavailable. */
export function clampCartToStock(slug: string, quote: QuoteDto) {
  for (const line of readCart(slug)) {
    const priced = quotedLine(quote, line);
    if (priced?.stockQty != null && priced.stockQty > 0 && line.quantity > priced.stockQty) {
      setQuantity(slug, line.productId, line.variantId, priced.stockQty);
    }
  }
}

/** Cart priced for pickup, with quantities kept inside tracked stock. */
export function usePricedCart(slug: string) {
  const lines = useCart(slug);
  const request: QuoteRequest | null = lines.length
    ? {
        items: lines.map((l) => ({ productId: l.productId, variantId: l.variantId, quantity: l.quantity })),
        fulfilment: "Pickup",
        deliveryZoneId: null,
        discountCode: null,
      }
    : null;
  const quoted = useQuote(slug, request);

  useEffect(() => {
    if (quoted.quote) clampCartToStock(slug, quoted.quote);
  }, [slug, quoted.quote]);

  return { lines, ...quoted };
}

const noop = () => () => {};

/** Code of the last order placed in this browser for the store, if any. */
export function useLastOrder(slug: string): string | null {
  return useSyncExternalStore(
    noop,
    () => {
      try {
        return window.localStorage.getItem(lastOrderKey(slug));
      } catch {
        return null;
      }
    },
    () => null,
  );
}
