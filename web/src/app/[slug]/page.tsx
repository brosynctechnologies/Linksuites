import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { formatMoney } from "@/lib/money";
import { getProducts, getStore } from "@/lib/storefront";
import type { PublicProductDto } from "@/lib/types";
import { CartButton } from "@/components/store/cart-button";
import { ClosedBanner, StoreShell, StoreSkeleton } from "@/components/store/store-shell";

export async function generateMetadata({ params }: PageProps<"/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const store = await getStore(slug).catch(() => null);
  if (!store) return { title: "Store not found" };
  return {
    title: { absolute: store.name },
    description: store.description ?? `Order from ${store.name} on WhatsApp.`,
    openGraph: { title: store.name, description: store.description ?? undefined, images: store.coverUrl ?? store.logoUrl ?? undefined },
  };
}

export default function StorePage(props: PageProps<"/[slug]">) {
  return (
    <Suspense fallback={<StoreSkeleton />}>
      <Store {...props} />
    </Suspense>
  );
}

function ProductCard({ p, slug, currency }: { p: PublicProductDto; slug: string; currency: string }) {
  const onSale = p.compareAtPriceMinor != null && p.compareAtPriceMinor > p.priceMinor;
  return (
    <li>
      <Link href={`/${slug}/p/${p.slug}`} className="group block rounded-control">
        <div className="relative aspect-square overflow-hidden rounded-control bg-paper">
          {p.images[0] ? (
            <img
              src={p.images[0].url}
              alt={p.name}
              loading="lazy"
              className={`h-full w-full object-cover ${p.inStock ? "" : "opacity-60 grayscale"}`}
            />
          ) : (
            <div className="flex h-full items-center justify-center text-line-strong" aria-hidden="true">
              <svg viewBox="0 0 24 24" width={36} height={36} fill="none" stroke="currentColor" strokeWidth={1.5}>
                <rect x="3" y="3" width="18" height="18" rx="2" />
                <circle cx="8.5" cy="8.5" r="1.5" />
                <path d="m21 15-5-5L5 21" />
              </svg>
            </div>
          )}
          {!p.inStock && (
            <span className="absolute top-2 left-2 rounded-full bg-surface px-2.5 py-0.5 text-xs font-semibold text-hibiscus-deep">Out of stock</span>
          )}
          {p.inStock && onSale && (
            <span className="absolute top-2 left-2 rounded-full bg-accent px-2.5 py-0.5 text-xs font-semibold text-accent-ink">Sale</span>
          )}
        </div>
        <p className="mt-2 line-clamp-2 text-sm font-medium text-ink group-hover:underline group-hover:underline-offset-2">{p.name}</p>
        <p className="mt-0.5 flex flex-wrap items-baseline gap-x-2">
          <span className="font-display text-lg font-bold text-ink">{formatMoney(p.priceMinor, currency)}</span>
          {onSale && <span className="text-xs text-subtle line-through">{formatMoney(p.compareAtPriceMinor, currency)}</span>}
        </p>
      </Link>
    </li>
  );
}

async function Store({ params, searchParams }: PageProps<"/[slug]">) {
  const { slug } = await params;
  const sp = await searchParams;
  const q = (typeof sp.q === "string" ? sp.q : "").trim();
  const category = typeof sp.category === "string" ? sp.category : "";

  const [store, products] = await Promise.all([getStore(slug), getProducts(slug).catch(() => [] as PublicProductDto[])]);
  if (!store) notFound();

  const needle = q.toLowerCase();
  const visible = products.filter(
    (p) =>
      (!category || p.categoryId === category) &&
      (!needle || p.name.toLowerCase().includes(needle) || (p.description ?? "").toLowerCase().includes(needle)),
  );
  const usedCategories = store.categories.filter((c) => products.some((p) => p.categoryId === c.id));
  const chipHref = (id: string) => {
    const qs = new URLSearchParams();
    if (q) qs.set("q", q);
    if (id) qs.set("category", id);
    const s = qs.toString();
    return s ? `/${slug}?${s}` : `/${slug}`;
  };

  return (
    <StoreShell store={store}>
      <ClosedBanner store={store} />
      <header>
        {store.coverUrl ? (
          <img src={store.coverUrl} alt="" className="h-36 w-full object-cover sm:h-44" />
        ) : (
          <div className="h-24 bg-accent" />
        )}
        <div className="px-4 sm:text-center">
          <div className="-mt-10 flex sm:justify-center">
            {store.logoUrl ? (
              <img
                src={store.logoUrl}
                alt={`${store.name} logo`}
                className="h-20 w-20 rounded-panel border-4 border-surface bg-surface object-cover"
              />
            ) : (
              <div className="flex h-20 w-20 items-center justify-center rounded-panel border-4 border-surface bg-accent font-display text-3xl font-bold text-accent-ink">
                {store.name.charAt(0).toUpperCase()}
              </div>
            )}
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 sm:justify-center">
            <h1 className="font-display text-2xl font-bold text-ink">{store.name}</h1>
            <span
              className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                store.isOpen ? "bg-ink-soft text-ink" : "bg-hibiscus-soft text-hibiscus-deep"
              }`}
            >
              <span className={`h-1.5 w-1.5 rounded-full ${store.isOpen ? "bg-ink" : "bg-hibiscus"}`} aria-hidden="true" />
              {store.isOpen ? "Open" : "Closed"}
            </span>
          </div>
          {store.description && (
            <p className="mt-1.5 max-w-prose text-sm whitespace-pre-line text-muted sm:mx-auto">{store.description}</p>
          )}
        </div>
      </header>

      <div className="px-4">
        <form action={`/${slug}`} method="get" role="search" className="relative mt-5">
          <label htmlFor="store-search" className="sr-only">
            Search products
          </label>
          <svg
            viewBox="0 0 24 24"
            width={18}
            height={18}
            fill="none"
            stroke="currentColor"
            strokeWidth={2}
            className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-subtle"
            aria-hidden="true"
          >
            <circle cx="11" cy="11" r="7" />
            <path d="m21 21-4.3-4.3" />
          </svg>
          <input
            id="store-search"
            name="q"
            type="search"
            defaultValue={q}
            placeholder={`Search ${store.name}`}
            className="block min-h-11 w-full rounded-control border border-line bg-paper py-2.5 pr-4 pl-10 text-base text-ink placeholder:text-subtle focus:border-ink focus:bg-surface"
          />
          {category && <input type="hidden" name="category" value={category} />}
        </form>

        {usedCategories.length > 0 && (
          <nav className="no-scrollbar -mx-4 mt-3 flex gap-2 overflow-x-auto px-4 py-1" aria-label="Categories">
            {[{ id: "", name: "All" }, ...usedCategories].map((c) => {
              const active = c.id === category;
              return (
                <Link
                  key={c.id || "all"}
                  href={chipHref(c.id)}
                  aria-current={active ? "page" : undefined}
                  className={`inline-flex min-h-11 shrink-0 items-center rounded-full px-4 text-sm font-medium transition-colors ${
                    active ? "bg-accent text-accent-ink" : "bg-paper text-ink-2 ring-1 ring-line ring-inset hover:text-ink"
                  }`}
                >
                  {c.name}
                </Link>
              );
            })}
          </nav>
        )}

        {visible.length === 0 ? (
          <div className="py-14">
            <p className="font-display text-lg font-bold text-ink">{products.length === 0 ? "No products yet" : "Nothing matches your search"}</p>
            <p className="mt-1 text-sm text-muted">
              {products.length === 0 ? (
                "This store is still adding products. Check back soon."
              ) : (
                <Link href={`/${slug}`} className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
                  Show all products
                </Link>
              )}
            </p>
          </div>
        ) : (
          <ul className="mt-4 grid grid-cols-2 gap-x-4 gap-y-6 pb-4">
            {visible.map((p) => (
              <ProductCard key={p.id} p={p} slug={slug} currency={store.currency} />
            ))}
          </ul>
        )}
      </div>
      <CartButton slug={slug} currency={store.currency} />
    </StoreShell>
  );
}
