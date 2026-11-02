"use client";

import Link from "next/link";
import { useState } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import { toSaveRequest } from "@/lib/products";
import { useApi } from "@/lib/use-api";
import type { ProductDto, StoreDto } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  EmptyState,
  ErrorBox,
  PageHeader,
  PageLoader,
  Toggle,
  buttonClass,
  cx,
  inputClass,
  listClass,
} from "@/components/ui";
import { BoxIcon, ImageIcon, PlusIcon, SearchIcon } from "@/components/icons";

function StockBadge({ p }: { p: ProductDto }) {
  if (!p.inStock || p.stockQty === 0) return <Badge tone="hibiscus" dot>Out of stock</Badge>;
  if (p.stockQty !== null) return <Badge tone="neutral">{p.stockQty} in stock</Badge>;
  return <Badge tone="ink">In stock</Badge>;
}

const cols = "md:grid md:grid-cols-[minmax(0,1fr)_8rem_9rem_6.5rem] md:items-center md:gap-4";

export default function ProductsPage() {
  const { data: products, error, loading, reload, setData } = useApi<ProductDto[]>("/api/products");
  const { data: store } = useApi<StoreDto>("/api/store");
  const [filter, setFilter] = useState("");
  const [toggleError, setToggleError] = useState<ApiError | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const currency = store?.currency ?? "NGN";

  async function toggleActive(p: ProductDto) {
    setPending(p.id);
    setToggleError(null);
    try {
      const updated = await api<ProductDto>(`/api/products/${p.id}`, {
        method: "PUT",
        body: toSaveRequest(p, { isActive: !p.isActive }),
      });
      setData((list) => (list ?? []).map((x) => (x.id === updated.id ? updated : x)));
    } catch (e) {
      setToggleError(toApiError(e));
    } finally {
      setPending(null);
    }
  }

  const f = filter.trim().toLowerCase();
  const visible = (products ?? []).filter((p) => !f || p.name.toLowerCase().includes(f));

  return (
    <div>
      <PageHeader
        title="Products"
        subtitle={products ? `${products.length} product${products.length === 1 ? "" : "s"}` : undefined}
        action={
          <Link href="/dashboard/products/new" className={buttonClass("primary")}>
            <PlusIcon width={18} height={18} /> Add product
          </Link>
        }
      />

      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!products && loading && <PageLoader label="Loading products…" rows={5} />}
      {toggleError && (
        <div className="mb-4">
          <ErrorBox error={toggleError} />
        </div>
      )}

      {products &&
        (products.length === 0 ? (
          <EmptyState
            icon={<BoxIcon />}
            title="No products yet"
            text="Add your first product with a photo and a price. It takes less than a minute."
            action={
              <Link href="/dashboard/products/new" className={buttonClass("primary")}>
                Add product
              </Link>
            }
          />
        ) : (
          <>
            {products.length > 6 && (
              <div className="relative mb-4 sm:max-w-sm">
                <label htmlFor="product-filter" className="sr-only">
                  Filter products
                </label>
                <SearchIcon className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-subtle" />
                <input
                  id="product-filter"
                  type="search"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  placeholder="Filter products"
                  className={cx(inputClass, "pl-10")}
                />
              </div>
            )}
            <div className={listClass}>
              <div className={cx("hidden border-b border-line px-4 py-2.5 text-xs font-medium text-subtle", cols)} aria-hidden="true">
                <span>Product</span>
                <span className="text-right">Price</span>
                <span>Stock</span>
                <span>In store</span>
              </div>
              <ul className="divide-y divide-line">
                {visible.map((p) => (
                  <li key={p.id} className={cx("relative flex items-center gap-3 px-4 py-3 transition-colors hover:bg-paper", cols)}>
                    <div className="flex min-w-0 flex-1 items-center gap-3 md:contents">
                      <span className="flex min-w-0 items-center gap-3">
                        {p.images[0] ? (
                          <img src={p.images[0].url} alt="" className="h-14 w-14 shrink-0 rounded-control bg-paper object-cover" loading="lazy" />
                        ) : (
                          <span className="flex h-14 w-14 shrink-0 items-center justify-center rounded-control bg-paper text-subtle">
                            <ImageIcon />
                          </span>
                        )}
                        <span className="min-w-0">
                          <Link
                            href={`/dashboard/products/${p.id}`}
                            className="block truncate rounded-sm font-medium text-ink after:absolute after:inset-0 after:content-['']"
                          >
                            {p.name}
                          </Link>
                          {p.categoryName && <span className="block truncate text-xs text-subtle">{p.categoryName}</span>}
                          <span className="mt-0.5 flex flex-wrap items-baseline gap-x-2 md:hidden">
                            <span className="text-sm font-semibold text-ink num">{formatMoney(p.priceMinor, currency)}</span>
                            {p.compareAtPriceMinor && p.compareAtPriceMinor > p.priceMinor && (
                              <span className="text-xs text-subtle line-through num">{formatMoney(p.compareAtPriceMinor, currency)}</span>
                            )}
                          </span>
                          <span className="mt-1 flex md:hidden">
                            <StockBadge p={p} />
                          </span>
                        </span>
                      </span>
                      <span className="hidden text-right md:block">
                        <span className="block font-semibold text-ink num">{formatMoney(p.priceMinor, currency)}</span>
                        {p.compareAtPriceMinor && p.compareAtPriceMinor > p.priceMinor && (
                          <span className="block text-xs text-subtle line-through num">{formatMoney(p.compareAtPriceMinor, currency)}</span>
                        )}
                      </span>
                      <span className="hidden md:block">
                        <StockBadge p={p} />
                      </span>
                    </div>
                    <div className="relative z-10 flex shrink-0 items-center gap-2" title={p.isActive ? "Visible in store" : "Hidden from store"}>
                      <span className={cx("hidden text-xs font-medium sm:inline md:hidden lg:inline", p.isActive ? "text-ink" : "text-subtle")}>
                        {p.isActive ? "Active" : "Hidden"}
                      </span>
                      <Toggle
                        hideLabel
                        label={`${p.name}: ${p.isActive ? "Active" : "Hidden"}`}
                        checked={p.isActive}
                        disabled={pending === p.id}
                        onChange={() => toggleActive(p)}
                      />
                    </div>
                  </li>
                ))}
                {visible.length === 0 && (
                  <li className="px-4 py-6 text-sm text-muted">
                    No products match “{filter}”. Check the spelling or clear the filter.
                  </li>
                )}
              </ul>
            </div>
          </>
        ))}
    </div>
  );
}
