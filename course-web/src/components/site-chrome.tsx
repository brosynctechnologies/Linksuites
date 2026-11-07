import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { productByKey, hubUrl } from "@/lib/suite";
import { AccountLinks } from "./account-links";
import { SearchIcon } from "./icons";
import { cx, inputClass } from "./styles";
import { SuiteFooter } from "./suite/suite-footer";
import { SuiteMark } from "./suite/suite-mark";
import { lsButton } from "./suite/ui";

export const learnLink = productByKey("courses");

/** The LearnLink wordmark: Literata, the reading face. Used inside the product (studio, sign-in, academy pages). */
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
      <SearchIcon className={cx("pointer-events-none absolute top-1/2 -translate-y-1/2 text-muted", big ? "left-4 h-5 w-5" : "left-3 h-4 w-4")} />
      <input
        id={id}
        name="q"
        type="search"
        defaultValue={defaultValue}
        placeholder="Search courses, e.g. JAMB physics, Excel, UI design"
        className={cx(inputClass, big ? "h-13 rounded-[12px] pl-11 text-base sm:text-base" : "min-h-11 pl-9")}
        enterKeyHint="search"
      />
    </form>
  );
}

/**
 * The marketplace header, on the LinkSuite frame: the suite mark (back to the hub), LearnLink, course search, Browse,
 * Teach and the learner's account. It matches SuiteHeader, which has no room for search or a signed-in menu.
 */
export function SiteHeader({ query, hideSearch }: { query?: string; hideSearch?: boolean }) {
  return (
    <header className="border-b border-ls-line bg-ls-chalk">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-ls-control focus:bg-ls-ink focus:px-4 focus:py-3 focus:text-white"
      >
        Skip to content
      </a>
      <div className="ls-container flex min-h-16 flex-wrap items-center gap-x-2 gap-y-2 py-2.5">
        <div className="flex min-w-0 items-center gap-1">
          <a href={hubUrl} aria-label="Part of LinkSuite: all apps" className="flex min-h-11 min-w-11 items-center justify-center rounded-ls-control">
            <SuiteMark size={20} />
          </a>
          <Link href="/" className="flex min-h-11 items-center rounded-ls-control px-1 font-ls-display text-[1.375rem] font-extrabold text-app-text">
            {APP_NAME}
          </Link>
        </div>
        {!hideSearch && <SearchForm defaultValue={query} className="order-last w-full md:order-none md:mx-3 md:max-w-md md:flex-1" />}
        <nav aria-label="Main" className="ml-auto flex items-center gap-0.5">
          <Link href="/courses" className={lsButton("quiet", "md", "px-2.5 max-md:hidden sm:px-3.5")}>
            Browse
          </Link>
          <Link href="/teach" className={lsButton("quiet", "md", "px-2.5 max-sm:hidden sm:px-3.5")}>
            Teach
          </Link>
          <AccountLinks />
        </nav>
      </div>
    </header>
  );
}

/** The LinkSuite footer, with the marketplace's own links. */
export function SiteFooter() {
  return (
    <SuiteFooter
      app={learnLink}
      links={[
        { href: "/courses", label: "Browse courses" },
        { href: "/learn", label: "My learning" },
        { href: "/teach", label: "Teach on LearnLink" },
        { href: "/teach#plans", label: "Plans for tutors" },
        { href: "/teach/login", label: "Tutor log in" },
      ]}
    />
  );
}

/** The public page body: the one main landmark, which the skip link targets. */
export function SiteMain({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <main id="main" tabIndex={-1} className={cx("flex-1 outline-none", className)}>
      {children}
    </main>
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
