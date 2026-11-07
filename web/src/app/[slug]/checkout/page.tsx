import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { getStore } from "@/lib/storefront";
import { CheckoutForm } from "@/components/store/checkout-form";
import { StoreShell, StoreSkeleton, StoreTopBar } from "@/components/store/store-shell";

export const metadata: Metadata = { title: "Checkout", robots: { index: false }, alternates: {} };

export default function CheckoutPage(props: PageProps<"/[slug]/checkout">) {
  return (
    <Suspense fallback={<StoreSkeleton />}>
      <Checkout {...props} />
    </Suspense>
  );
}

async function Checkout({ params }: PageProps<"/[slug]/checkout">) {
  const { slug } = await params;
  const store = await getStore(slug);
  if (!store) notFound();
  return (
    <StoreShell store={store}>
      <StoreTopBar store={store} back={`/${slug}/cart`} title="Checkout" />
      <CheckoutForm store={store} />
    </StoreShell>
  );
}
