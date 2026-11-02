import Link from "next/link";
import type { CSSProperties, ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { accentOf, readableOn } from "@/lib/storefront";
import type { PublicStoreDto } from "@/lib/types";

/** Wraps storefront pages: a 560px mobile-first column in the store's own colour. */
export function StoreShell({ store, children }: { store: PublicStoreDto; children: ReactNode }) {
  const accent = accentOf(store.themeColor);
  return (
    <div className="flex min-h-screen flex-col bg-paper" style={{ "--accent": accent, "--accent-ink": readableOn(accent) } as CSSProperties}>
      <div className="mx-auto flex w-full max-w-[560px] flex-1 flex-col bg-surface pb-[calc(7rem+env(safe-area-inset-bottom))] sm:border-x sm:border-line">
        {children}
        {store.showBranding && (
          <footer className="mt-auto px-4 pt-10 text-center text-xs text-subtle">
            Made with{" "}
            <Link href="/" className="font-semibold text-ink-2 underline-offset-2 hover:underline">
              {APP_NAME}
            </Link>
          </footer>
        )}
      </div>
    </div>
  );
}

export function StoreTopBar({ store, back, title }: { store: PublicStoreDto; back?: string; title?: string }) {
  return (
    <header className="sticky top-0 z-10 flex min-h-14 items-center gap-1 border-b border-line bg-surface/95 px-2 backdrop-blur">
      <Link
        href={back ?? `/${store.slug}`}
        className="flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-ink hover:bg-paper"
        aria-label="Back"
      >
        <svg viewBox="0 0 24 24" width={22} height={22} fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true">
          <path d="m15 18-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </Link>
      <span className="min-w-0 truncate font-display text-lg font-bold text-ink">{title ?? store.name}</span>
    </header>
  );
}

export function ClosedBanner({ store }: { store: PublicStoreDto }) {
  if (store.isOpen) return null;
  return (
    <div className="bg-marigold-soft px-4 py-2.5 text-sm font-medium text-marigold-deep" role="status">
      This store is closed right now and not taking orders. You can still look around.
    </div>
  );
}

export function StoreSkeleton() {
  return (
    <div className="min-h-screen bg-paper">
      <div className="mx-auto w-full max-w-[560px] animate-pulse bg-surface" aria-busy="true" aria-label="Loading">
        <div className="h-36 bg-line" />
        <div className="space-y-3 p-4">
          <div className="-mt-12 h-20 w-20 rounded-panel border-4 border-surface bg-line" />
          <div className="h-6 w-1/2 rounded bg-line" />
          <div className="h-4 w-3/4 rounded bg-line/60" />
          <div className="grid grid-cols-2 gap-4 pt-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <div key={i} className="aspect-square rounded-control bg-line/60" />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
