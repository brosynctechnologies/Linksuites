import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { AccountLinks } from "./account-links";
import { SearchIcon } from "./icons";
import { cx, inputClass } from "./styles";

/** The LearnLink wordmark: Literata, the reading face. */
export function Wordmark({ className, dark }: { className?: string; dark?: boolean }) {
  return (
    <span className={cx("font-serif text-[1.375rem] leading-none font-semibold tracking-[-0.015em]", dark ? "text-white" : "text-ink", className)}>
      {APP_NAME}
    </span>
  );
}

/** Course search: a plain GET form to /courses, so it works before any JavaScript loads. */
export function SearchForm({ defaultValue, className, id = "site-search", big }: { defaultValue?: string; className?: string; id?: string; big?: boolean }) {
  return (
    <form action="/courses" role="search" className={cx("relative", className)}>
      <label htmlFor={id} className="sr-only">
        Search courses
      </label>
      <SearchIcon className={cx("pointer-events-none absolute top-1/2 -translate-y-1/2 text-faint", big ? "left-4 h-5 w-5" : "left-3 h-4 w-4")} />
      <input
        id={id}
        name="q"
        type="search"
        defaultValue={defaultValue}
        placeholder="Search courses, e.g. JAMB physics, Excel, UI design"
        className={cx(inputClass, big ? "h-13 rounded-[12px] pl-11 text-base sm:text-base" : "pl-9")}
        enterKeyHint="search"
      />
    </form>
  );
}

/** Marketplace header: wordmark, search, Teach, account. */
export function SiteHeader({ query, hideSearch }: { query?: string; hideSearch?: boolean }) {
  return (
    <header className="border-b border-line bg-surface">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-3 px-4 py-3 sm:px-6">
        <Link href="/" className="shrink-0 rounded-control py-1">
          <Wordmark />
        </Link>
        {!hideSearch && <SearchForm defaultValue={query} className="order-last w-full md:order-none md:max-w-md md:flex-1" />}
        <nav className="ml-auto flex items-center gap-1" aria-label="Site">
          <Link href="/courses" className="hidden rounded-control px-3 py-2 text-sm font-semibold text-ink hover:bg-ink/[0.06] lg:block">
            Browse
          </Link>
          <Link href="/teach" className="hidden rounded-control px-3 py-2 text-sm font-semibold text-ink hover:bg-ink/[0.06] md:block">
            Teach
          </Link>
          <AccountLinks />
        </nav>
      </div>
    </header>
  );
}

export function SiteFooter() {
  return (
    <footer className="mt-auto border-t border-line bg-surface">
      <div className="mx-auto flex max-w-6xl flex-col gap-4 px-4 py-8 text-sm text-muted sm:flex-row sm:items-center sm:justify-between sm:px-6">
        <div>
          <Wordmark className="text-lg" />
          <p className="mt-1">Courses from tutors you trust. Pay in naira, learn on your phone.</p>
        </div>
        <nav className="flex flex-wrap gap-x-5 gap-y-2" aria-label="Footer">
          <Link href="/courses" className="hover:text-ink hover:underline">
            Browse courses
          </Link>
          <Link href="/teach" className="hover:text-ink hover:underline">
            Teach on {APP_NAME}
          </Link>
          <Link href="/learn" className="hover:text-ink hover:underline">
            My learning
          </Link>
        </nav>
      </div>
    </footer>
  );
}

/** Standard public page frame. */
export function SitePage({ children, query, hideSearch }: { children: ReactNode; query?: string; hideSearch?: boolean }) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader query={query} hideSearch={hideSearch} />
      <main className="flex-1">{children}</main>
      <SiteFooter />
    </div>
  );
}

/** A plain explanation when the catalogue can't be loaded. */
export function LoadProblem({ status, what = "courses" }: { status: number; what?: string }) {
  return (
    <div className="rounded-panel border border-line bg-surface px-5 py-8 text-center">
      <p className="font-semibold text-ink">{status === 0 ? `We couldn't reach ${APP_NAME} to load ${what}.` : `Loading ${what} failed (error ${status}).`}</p>
      <p className="mt-1 text-sm text-muted">Check your connection and refresh the page.</p>
    </div>
  );
}
