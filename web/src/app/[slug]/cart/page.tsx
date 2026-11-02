import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { getStore } from "@/lib/storefront";
import { CartView } from "@/components/store/cart-view";
import { StoreShell, StoreSkeleton, StoreTopBar } from "@/components/store/store-shell";

export const metadata: Metadata = { title: "Cart", robots: { index: false } };

export default function CartPage(props: PageProps<"/[slug]/cart">) {
  return (
    <Suspense fallback={<StoreSkeleton />}>
      <Cart {...props} />
    </Suspense>
  );
}

async function Cart({ params }: PageProps<"/[slug]/cart">) {
  const { slug } = await params;
  const store = await getStore(slug);
  if (!store) notFound();
  return (
    <StoreShell store={store}>
      <StoreTopBar store={store} title="Your cart" />
      <CartView store={store} />
    </StoreShell>
  );
}
