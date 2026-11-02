"use client";

import Link from "next/link";
import { cartCount, cartSubtotal } from "@/lib/cart";
import { formatMoney } from "@/lib/money";
import { CartIcon } from "../icons";
import { usePricedCart } from "./use-quote";

/** Sticky "View cart" bar in the store's colour; hidden while the cart is empty. */
export function CartButton({ slug, currency }: { slug: string; currency: string }) {
  const { lines, quote } = usePricedCart(slug);
  const count = quote ? quote.lines.reduce((n, l) => n + l.quantity, 0) : cartCount(lines);
  const total = quote?.subtotalMinor ?? cartSubtotal(lines);
  if (count === 0) return null;
  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-20 px-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
      <Link
        href={`/${slug}/cart`}
        className="pointer-events-auto mx-auto flex min-h-14 max-w-[528px] items-center justify-between gap-3 rounded-panel bg-accent px-4 py-3 text-accent-ink shadow-lift"
      >
        <span className="flex items-center gap-3">
          <span className="relative">
            <CartIcon />
            <span
              key={count}
              className="absolute -top-2 -right-2.5 flex h-5 min-w-5 animate-bump items-center justify-center rounded-full bg-accent-ink px-1 text-xs font-bold text-accent"
            >
              {count}
            </span>
          </span>
          <span className="ml-1 font-semibold">View cart</span>
          <span className="sr-only">
            , {count} item{count === 1 ? "" : "s"}
          </span>
        </span>
        <span className="font-display text-lg font-bold">{formatMoney(total, currency)}</span>
      </Link>
    </div>
  );
}
