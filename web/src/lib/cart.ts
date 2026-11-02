import { useSyncExternalStore } from "react";

/** One cart line. Prices are a display snapshot only; the server recomputes totals. */
export interface CartLine {
  productId: string;
  variantId: string | null;
  quantity: number;
  name: string;
  variantTitle: string | null;
  unitPriceMinor: number;
  imageUrl: string | null;
  productSlug: string;
}

const EMPTY: CartLine[] = [];
const listeners = new Set<() => void>();
const cache = new Map<string, { raw: string | null; lines: CartLine[] }>();

const keyFor = (slug: string) => `orderlink.cart.${slug}`;
export const lastOrderKey = (slug: string) => `orderlink.lastOrder.${slug}`;

export function readCart(slug: string): CartLine[] {
  if (typeof window === "undefined") return EMPTY;
  let raw: string | null = null;
  try {
    raw = window.localStorage.getItem(keyFor(slug));
  } catch {
    /* ignore */
  }
  const hit = cache.get(slug);
  if (hit && hit.raw === raw) return hit.lines;
  let lines: CartLine[] = EMPTY;
  try {
    const parsed = raw ? JSON.parse(raw) : null;
    if (Array.isArray(parsed)) lines = parsed as CartLine[];
  } catch {
    /* corrupted — start fresh */
  }
  cache.set(slug, { raw, lines });
  return lines;
}

function writeCart(slug: string, lines: CartLine[]) {
  try {
    if (lines.length) window.localStorage.setItem(keyFor(slug), JSON.stringify(lines));
    else window.localStorage.removeItem(keyFor(slug));
  } catch {
    /* ignore */
  }
  listeners.forEach((l) => l());
}

const same = (a: CartLine, productId: string, variantId: string | null) =>
  a.productId === productId && (a.variantId ?? null) === (variantId ?? null);

export function addToCart(slug: string, line: CartLine) {
  const lines = readCart(slug);
  const existing = lines.find((l) => same(l, line.productId, line.variantId));
  const next = existing
    ? lines.map((l) => (l === existing ? { ...line, quantity: Math.min(99, l.quantity + line.quantity) } : l))
    : [...lines, line];
  writeCart(slug, next);
}

export function setQuantity(slug: string, productId: string, variantId: string | null, quantity: number) {
  const lines = readCart(slug);
  const next =
    quantity <= 0
      ? lines.filter((l) => !same(l, productId, variantId))
      : lines.map((l) => (same(l, productId, variantId) ? { ...l, quantity: Math.min(99, quantity) } : l));
  writeCart(slug, next);
}

export function clearCart(slug: string) {
  writeCart(slug, EMPTY);
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  window.addEventListener("storage", cb);
  return () => {
    listeners.delete(cb);
    window.removeEventListener("storage", cb);
  };
}

/** Reactive cart for a store. Empty during SSR. */
export function useCart(slug: string): CartLine[] {
  return useSyncExternalStore(
    subscribe,
    () => readCart(slug),
    () => EMPTY,
  );
}

export function cartCount(lines: CartLine[]) {
  return lines.reduce((n, l) => n + l.quantity, 0);
}

export function cartSubtotal(lines: CartLine[]) {
  return lines.reduce((n, l) => n + l.quantity * l.unitPriceMinor, 0);
}
