import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { getProduct, getStore } from "@/lib/storefront";
import { ProductPurchase } from "@/components/store/product-purchase";
import { ClosedBanner, StoreShell, StoreSkeleton, StoreTopBar } from "@/components/store/store-shell";

type Props = PageProps<"/[slug]/p/[productSlug]">;

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug, productSlug } = await params;
  const [store, product] = await Promise.all([getStore(slug).catch(() => null), getProduct(slug, productSlug).catch(() => null)]);
  if (!store || !product) return { title: "Product not found" };
  return {
    title: { absolute: `${product.name} · ${store.name}` },
    description: product.description?.slice(0, 160) ?? `Order ${product.name} from ${store.name}.`,
    openGraph: { title: product.name, images: product.images[0]?.url },
  };
}

export default function ProductPage(props: Props) {
  return (
    <Suspense fallback={<StoreSkeleton />}>
      <Product {...props} />
    </Suspense>
  );
}

async function Product({ params }: Props) {
  const { slug, productSlug } = await params;
  const [store, product] = await Promise.all([getStore(slug), getProduct(slug, productSlug)]);
  if (!store || !product) notFound();

  return (
    <StoreShell store={store}>
      <StoreTopBar store={store} />
      <ClosedBanner store={store} />
      {product.images.length > 0 ? (
        <div className="relative bg-paper">
          <div className="no-scrollbar flex snap-x snap-mandatory overflow-x-auto" aria-label="Product photos" tabIndex={0}>
            {product.images.map((img, i) => (
              <img
                key={img.url}
                src={img.url}
                alt={i === 0 ? product.name : `${product.name} photo ${i + 1}`}
                className="aspect-square w-full shrink-0 snap-center object-cover"
                loading={i === 0 ? "eager" : "lazy"}
              />
            ))}
          </div>
          {product.images.length > 1 && (
            <span className="absolute right-3 bottom-3 rounded-full bg-ink/75 px-2.5 py-0.5 text-xs font-medium text-white">
              {product.images.length} photos, swipe to see more
            </span>
          )}
        </div>
      ) : (
        <div className="flex aspect-[4/3] items-center justify-center bg-paper text-line-strong" aria-hidden="true">
          <svg viewBox="0 0 24 24" width={48} height={48} fill="none" stroke="currentColor" strokeWidth={1.5}>
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <circle cx="8.5" cy="8.5" r="1.5" />
            <path d="m21 15-5-5L5 21" />
          </svg>
        </div>
      )}
      <div className="space-y-5 px-4 pt-5">
        <h1 className="font-display text-2xl font-bold text-ink">{product.name}</h1>
        <ProductPurchase product={product} slug={slug} currency={store.currency} isOpen={store.isOpen} />
        {product.description && (
          <div className="border-t border-line pt-5">
            <h2 className="mb-1 text-sm font-semibold text-ink">Description</h2>
            <p className="max-w-prose text-sm whitespace-pre-line text-ink-2">{product.description}</p>
          </div>
        )}
      </div>
    </StoreShell>
  );
}
