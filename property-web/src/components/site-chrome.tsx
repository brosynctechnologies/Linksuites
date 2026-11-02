import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { AccountLinks } from "./account-links";
import { cx } from "./styles";

/** The PropertyLink wordmark: Archivo at signboard width. */
export function Wordmark({ className, dark }: { className?: string; dark?: boolean }) {
  return (
    <span className={cx("display-wide text-[1.375rem] leading-none font-bold", dark ? "text-white" : "text-asphalt", className)}>
      Property<span className={dark ? "text-adire-light" : "text-adire"}>Link</span>
    </span>
  );
}

const NAV = [
  { href: "/rent", label: "Rent" },
  { href: "/buy", label: "Buy" },
  { href: "/shortlet", label: "Short-let" },
];

/** Public header: wordmark, the three ways to search, agents link, account. */
export function SiteHeader() {
  return (
    <header className="border-b border-line bg-surface">
      <div className="mx-auto flex max-w-7xl items-center gap-x-2 px-4 py-2 sm:gap-x-4 sm:px-6">
        <Link href="/" className="flex min-h-11 shrink-0 items-center rounded-control" aria-label={`${APP_NAME} home`}>
          <Wordmark />
        </Link>
        <nav className="hidden items-center gap-1 md:flex" aria-label="Search">
          {NAV.map((n) => (
            <Link key={n.href} href={n.href} className="flex min-h-11 items-center rounded-control px-3 text-sm font-semibold text-asphalt hover:bg-asphalt/[0.06]">
              {n.label}
            </Link>
          ))}
        </nav>
        <nav className="ml-auto flex items-center gap-1" aria-label="Account">
          <Link href="/studio" className="hidden min-h-11 items-center rounded-control px-3 text-sm font-semibold text-asphalt hover:bg-asphalt/[0.06] lg:flex">
            For agents
          </Link>
          <AccountLinks />
        </nav>
      </div>
      <nav className="no-scrollbar flex gap-1 overflow-x-auto border-t border-line-soft px-2 md:hidden" aria-label="Search">
        {NAV.map((n) => (
          <Link key={n.href} href={n.href} className="flex min-h-11 items-center px-3 text-sm font-semibold text-asphalt">
            {n.label}
          </Link>
        ))}
      </nav>
    </header>
  );
}

export function SiteFooter() {
  return (
    <footer className="mt-auto border-t border-line bg-surface">
      <div className="mx-auto grid max-w-7xl gap-6 px-4 py-8 text-sm text-muted sm:grid-cols-[minmax(0,1fr)_auto] sm:px-6">
        <div>
          <Wordmark className="text-lg" />
          <p className="mt-1 max-w-sm">What you see is real, and what you pay is all here.</p>
        </div>
        <nav className="flex flex-wrap gap-x-5 gap-y-1" aria-label="Footer">
          {[
            { href: "/rent", label: "Homes for rent" },
            { href: "/buy", label: "Homes for sale" },
            { href: "/shortlet", label: "Short-lets" },
            { href: "/how-verification-works", label: "How verification works" },
            { href: "/pricing", label: "Pricing for agents" },
            { href: "/studio/signup", label: "List a property" },
          ].map((l) => (
            <Link key={l.href} href={l.href} className="flex min-h-11 items-center hover:text-asphalt hover:underline">
              {l.label}
            </Link>
          ))}
        </nav>
      </div>
    </footer>
  );
}

/** Standard public page frame. */
export function SitePage({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className={cx("flex-1", className)}>{children}</main>
      <SiteFooter />
    </div>
  );
}

/** A plain explanation when public data can't be loaded. Never "Oops". */
export function LoadProblem({ status, what = "listings" }: { status: number; what?: string }) {
  return (
    <div className="rounded-panel border border-line bg-surface px-5 py-8 text-center" role="status">
      <p className="font-semibold text-asphalt">
        {status === 0 ? `We couldn't reach ${APP_NAME} to load ${what}.` : `Loading ${what} failed (error ${status}).`}
      </p>
      <p className="mt-1 text-sm text-muted">Check your connection and refresh the page in a moment.</p>
    </div>
  );
}
