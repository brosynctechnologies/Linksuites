import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { suiteFonts } from "@/lib/suite-fonts";
import { hubUrl, productByKey } from "@/lib/suite";
import { AccountLinks } from "./account-links";
import { SuiteFooter } from "./suite/suite-footer";
import { SuiteMark } from "./suite/suite-mark";
import { lsButton } from "./suite/ui";
import { cx } from "./styles";

/** PropertyLink's entry in the LinkSuite product list (name, colours, free and paid plan lines, sign-up paths). */
export const propertyApp = productByKey("property");

const NAV = [
  { href: "/rent", label: "Rent" },
  { href: "/buy", label: "Buy" },
  { href: "/shortlet", label: "Short-let" },
];

/**
 * The public header: the LinkSuite header (docs/design/linksuite.md, suite/suite-header.tsx) with the marketplace's
 * three searches and the seeker's account on the right, because signed-in seekers need their saved homes on every page.
 * Not sticky: on a phone the screen belongs to the listings.
 */
export function SiteHeader() {
  return (
    <header className={cx(suiteFonts, "border-b border-ls-line bg-ls-chalk font-ls-text text-ls-ink")}>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-ls-control focus:bg-ls-ink focus:px-4 focus:py-3 focus:text-white"
      >
        Skip to content
      </a>
      <div className="ls-container flex min-h-16 items-center gap-2">
        <div className="flex min-w-0 items-center gap-1">
          <a href={hubUrl} aria-label="Part of LinkSuite: all apps" className="flex min-h-11 min-w-11 items-center justify-center rounded-ls-control">
            <SuiteMark size={20} />
          </a>
          <Link href="/" className="flex min-h-11 items-center rounded-ls-control px-1 font-ls-display text-[1.375rem] font-extrabold text-app-text">
            {APP_NAME}
          </Link>
        </div>
        <nav aria-label="Main" className="ml-3 hidden items-center gap-0.5 md:flex">
          {NAV.map((n) => (
            <Link key={n.href} href={n.href} className={lsButton("quiet", "md")}>
              {n.label}
            </Link>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-0.5">
          <Link href="/for-agents" className={lsButton("quiet", "md", "max-lg:hidden")}>
            For agents
          </Link>
          <AccountLinks />
        </div>
      </div>
      <nav aria-label="Main" className="no-scrollbar flex gap-0.5 overflow-x-auto border-t border-ls-line px-2 md:hidden">
        {[...NAV, { href: "/for-agents", label: "For agents" }].map((n) => (
          <Link key={n.href} href={n.href} className={lsButton("quiet", "md", "px-3")}>
            {n.label}
          </Link>
        ))}
      </nav>
    </header>
  );
}

const FOOTER_LINKS = [
  { href: "/rent", label: "Homes for rent" },
  { href: "/buy", label: "Homes for sale" },
  { href: "/shortlet", label: "Short-lets" },
  { href: "/how-verification-works", label: "How verification works" },
  { href: "/for-agents", label: "For agents and landlords" },
  { href: "/studio/login", label: "Log in to the studio" },
];

/** The LinkSuite footer, with PropertyLink's own links. */
export function SiteFooter() {
  return (
    <div className={cx(suiteFonts, "mt-auto flex flex-col font-ls-text")}>
      <SuiteFooter app={propertyApp} links={FOOTER_LINKS} />
    </div>
  );
}

/** Standard public page frame. The skip link in the header targets `#main`. */
export function SitePage({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main id="main" tabIndex={-1} className={cx("flex-1 outline-none", className)}>
        {children}
      </main>
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
