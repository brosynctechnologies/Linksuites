import Link from "next/link";
import { APP_NAME } from "@/lib/config";
import { buttonClass, cx } from "./styles";

/** The CareLink wordmark: Red Hat Display, with a small wristband mark (a band with its punched hole). */
export function Wordmark({ className, dark }: { className?: string; dark?: boolean }) {
  return (
    <span className={cx("display inline-flex items-center gap-1.5 text-[1.375rem] leading-none font-bold", dark ? "text-white" : "text-ink", className)}>
      <svg viewBox="0 0 22 14" width="22" height="14" aria-hidden="true">
        <rect x="1" y="1" width="20" height="12" rx="6" fill={dark ? "#9fe0d5" : "#0f766e"} />
        <circle cx="7" cy="7" r="2.2" fill={dark ? "#0a3a36" : "#fff"} />
      </svg>
      <span>
        Care<span className={dark ? "text-on-scrub" : "text-scrub"}>Link</span>
      </span>
    </span>
  );
}

/** Public header: wordmark, pricing, sign in, start free. */
export function SiteHeader() {
  return (
    <header className="border-b border-rule bg-surface">
      <div className="mx-auto flex max-w-6xl items-center gap-2 px-4 py-2 sm:px-6">
        <Link href="/" className="flex min-h-11 shrink-0 items-center rounded-control" aria-label={`${APP_NAME} home`}>
          <Wordmark />
        </Link>
        <nav className="ml-auto flex items-center gap-1" aria-label="Main">
          <Link href="/pricing" className="hidden min-h-11 items-center rounded-control px-3 text-sm font-bold text-ink hover:bg-ink/[0.06] sm:flex">
            Pricing
          </Link>
          <Link href="/login" className="flex min-h-11 items-center rounded-control px-3 text-sm font-bold text-ink hover:bg-ink/[0.06]">
            Staff sign in
          </Link>
          <Link href="/signup" className={buttonClass("primary", "px-3 sm:px-4")}>
            Start free
          </Link>
        </nav>
      </div>
      <nav className="flex gap-1 border-t border-rule-soft px-2 sm:hidden" aria-label="More">
        <Link href="/pricing" className="flex min-h-11 items-center px-3 text-sm font-bold text-ink">
          Pricing
        </Link>
      </nav>
    </header>
  );
}

export function SiteFooter() {
  return (
    <footer className="mt-auto border-t border-rule bg-surface">
      <div className="mx-auto flex max-w-6xl flex-wrap items-start justify-between gap-6 px-4 py-8 text-sm text-muted sm:px-6">
        <div className="max-w-sm">
          <Wordmark className="text-lg" />
          <p className="mt-2">Patients, wards and billing for private clinics and small hospitals.</p>
        </div>
        <nav className="flex flex-wrap gap-x-5 gap-y-1" aria-label="Footer">
          <Link href="/pricing" className="min-h-11 py-2.5 hover:text-ink">
            Pricing
          </Link>
          <Link href="/signup" className="min-h-11 py-2.5 hover:text-ink">
            Set up your clinic
          </Link>
          <Link href="/login" className="min-h-11 py-2.5 hover:text-ink">
            Staff sign in
          </Link>
        </nav>
      </div>
    </footer>
  );
}
