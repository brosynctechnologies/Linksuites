import Link from "next/link";
import { APP_NAME } from "@/lib/config";

export default function StoreNotFound() {
  return (
    <main id="main" tabIndex={-1} className="mx-auto flex min-h-screen max-w-[560px] flex-col justify-center px-6 outline-none">
      <h1 className="font-display text-2xl font-bold text-ink">We couldn&apos;t find that store</h1>
      <p className="mt-2 max-w-prose text-sm text-muted">
        Check the link you were sent. The store name may have changed, or the product is no longer available.
      </p>
      <Link href="/" className="mt-6 inline-flex min-h-12 w-fit items-center rounded-control bg-ink px-6 font-semibold text-white hover:bg-ink-2">
        Go to {APP_NAME}
      </Link>
    </main>
  );
}
