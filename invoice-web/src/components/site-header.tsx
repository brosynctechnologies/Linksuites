import Link from "next/link";
import { APP_NAME } from "@/lib/config";

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-30 border-b border-rule bg-desk/90 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6">
        <Link href="/" className="font-display text-[1.375rem] font-semibold tracking-[-0.01em] text-graphite">
          {APP_NAME}
        </Link>
        <nav className="flex items-center gap-1 text-sm font-semibold" aria-label="Main">
          <Link href="/#templates" className="hidden rounded-control px-3 py-2 text-slate-600 hover:text-graphite md:inline">
            Templates
          </Link>
          <Link href="/#pricing" className="hidden rounded-control px-3 py-2 text-slate-600 hover:text-graphite sm:inline">
            Pricing
          </Link>
          <Link href="/login" className="rounded-control px-3 py-2 text-slate-700 hover:text-graphite">
            Log in
          </Link>
          <Link href="/signup" className="rounded-control bg-ink px-3.5 py-2 text-white hover:bg-ink-dark">
            Sign up<span className="hidden sm:inline"> free</span>
          </Link>
        </nav>
      </div>
    </header>
  );
}

export function SiteFooter() {
  return (
    <footer className="border-t border-rule bg-desk">
      <div className="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-10 sm:flex-row sm:items-start sm:justify-between sm:px-6">
        <div className="max-w-xs">
          <p className="font-display text-lg font-semibold text-graphite">{APP_NAME}</p>
          <p className="mt-1 text-sm text-muted">Invoices, quotes and payments for freelancers and small businesses.</p>
        </div>
        <nav aria-label="Footer" className="flex flex-col gap-2 text-sm sm:items-end">
          <Link href="/create" className="text-slate-700 hover:text-graphite hover:underline">
            Free invoice generator
          </Link>
          <Link href="/signup" className="text-slate-700 hover:text-graphite hover:underline">
            Create an account
          </Link>
          <Link href="/login" className="text-slate-700 hover:text-graphite hover:underline">
            Log in
          </Link>
        </nav>
      </div>
    </footer>
  );
}
