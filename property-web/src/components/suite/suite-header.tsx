import Link from "next/link";
import { hubUrl, type Product } from "@/lib/suite";
import { SuiteMark } from "./suite-mark";
import { cx, lsButton } from "./ui";

export interface NavLink {
  href: string;
  label: string;
  /** Hide below the sm breakpoint, where only the main action and one or two links fit. */
  wide?: boolean;
}

/**
 * The LinkSuite header, the same on all eight sites.
 * - Hub: the LinkSuite wordmark.
 * - App: the suite mark (a link back to the hub, labelled "Part of LinkSuite"), then the app's name in its colour.
 * Then up to four plain links and, on app sites, the one main action ("Start free").
 * Not sticky: on a phone the screen belongs to the content.
 */
export function SuiteHeader({ app, nav, action }: { app?: Product; nav: NavLink[]; action?: NavLink }) {
  return (
    <header className="border-b border-ls-line bg-ls-chalk">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-ls-control focus:bg-ls-ink focus:px-4 focus:py-3 focus:text-white"
      >
        Skip to content
      </a>
      <div className="ls-container flex min-h-16 items-center gap-2">
        {app ? (
          <div className="flex min-w-0 items-center gap-1">
            <a href={hubUrl} aria-label="Part of LinkSuite: all apps" className="flex min-h-11 min-w-11 items-center justify-center rounded-ls-control">
              <SuiteMark size={20} />
            </a>
            <Link href="/" className="flex min-h-11 items-center rounded-ls-control px-1 font-ls-display text-[1.375rem] font-extrabold text-app-text">
              {app.name}
            </Link>
          </div>
        ) : (
          <Link href="/" className="flex min-h-11 items-center gap-2.5 rounded-ls-control pr-1" aria-label="LinkSuite home">
            <SuiteMark size={24} />
            <span className="font-ls-display text-[1.375rem] font-extrabold tracking-[-0.01em] text-ls-ink">LinkSuite</span>
          </Link>
        )}
        <nav aria-label="Main" className="ml-auto flex items-center gap-0.5">
          {nav.map((l) => (
            <Link key={l.href} href={l.href} className={lsButton("quiet", "md", cx("px-2.5 sm:px-3.5", l.wide && "max-sm:hidden"))}>
              {l.label}
            </Link>
          ))}
          {action && (
            <Link href={action.href} className={lsButton("app", "md", "ml-1")}>
              {action.label}
            </Link>
          )}
        </nav>
      </div>
    </header>
  );
}
