"use client";

import Link from "next/link";
import { useRole } from "@/lib/roles";
import type { StockStatus } from "@/lib/types";
import { Badge, buttonClass, cx } from "@/components/ui";

/** Status as colour plus a word: In stock (ink), Low (marigold), Sold out (hibiscus). */
export function StockStatusBadge({ status }: { status: StockStatus }) {
  if (status === "SoldOut")
    return (
      <Badge tone="hibiscus" dot>
        Sold out
      </Badge>
    );
  if (status === "Low")
    return (
      <Badge tone="marigold" dot>
        Low
      </Badge>
    );
  return (
    <Badge tone="ink" dot>
      In stock
    </Badge>
  );
}

/** "Stock" and "History" tabs shared by the two stock pages. */
export function StockTabs({ current }: { current: "stock" | "history" }) {
  const tabs = [
    { key: "stock", href: "/dashboard/inventory", label: "Stock" },
    { key: "history", href: "/dashboard/inventory/history", label: "History" },
  ] as const;
  return (
    <nav className="mb-5 flex gap-1.5" aria-label="Stock pages">
      {tabs.map((t) => (
        <Link
          key={t.key}
          href={t.href}
          aria-current={current === t.key ? "page" : undefined}
          className={cx(
            "inline-flex min-h-11 items-center rounded-full px-4 text-sm font-medium transition-colors",
            current === t.key ? "bg-ink text-white" : "text-ink-2 hover:bg-ink-soft hover:text-ink",
          )}
        >
          {t.label}
        </Link>
      ))}
    </nav>
  );
}

/** Shown instead of the stock pages on plans without stock tracking. */
export function StockUpsell() {
  const role = useRole();
  return (
    <div className="rounded-panel border border-marigold/50 bg-marigold-soft p-4 text-sm text-ink sm:p-5">
      <p className="font-display text-lg font-bold">Track stock with Pro</p>
      <p className="mt-1 max-w-prose text-ink-2">
        Set how many of each product and size you have. Sales take stock off as orders come in, customers see &ldquo;Only 2 left&rdquo; or
        &ldquo;Sold out&rdquo;, and you get a note when something runs low.
      </p>
      {role === "Owner" ? (
        <Link href="/dashboard/billing" className={buttonClass("primary", "mt-3")}>
          See Pro plan
        </Link>
      ) : (
        <p className="mt-2 text-ink-2">Ask the store owner to upgrade.</p>
      )}
    </div>
  );
}

export function itemName(productName: string, variantTitle: string | null) {
  return variantTitle ? `${productName} (${variantTitle})` : productName;
}

/** +5 / −2, with a real minus sign. */
export function formatDelta(delta: number) {
  return delta > 0 ? `+${delta}` : `−${Math.abs(delta)}`;
}
