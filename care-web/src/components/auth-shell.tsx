import Link from "next/link";
import type { ReactNode } from "react";
import { Wordmark } from "./site-chrome";

export function AuthShell({
  title,
  subtitle,
  children,
  footer,
  aside,
}: {
  title: string;
  subtitle?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  /** Signup shows what they're signing up for beside the form on wide screens. */
  aside?: ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-gauze">
      <header className="mx-auto w-full max-w-6xl px-4 py-4 sm:px-6">
        <Link href="/" className="inline-flex min-h-11 items-center rounded-control">
          <Wordmark />
        </Link>
      </header>
      <main className="flex flex-1 items-start justify-center px-4 pt-4 pb-16 sm:items-center sm:pt-0">
        <div className={aside ? "grid w-full max-w-4xl gap-10 md:grid-cols-[minmax(0,26rem)_minmax(0,1fr)] md:items-start" : "w-full max-w-[26rem]"}>
          <div>
            <h1 className="display text-2xl font-bold text-ink">{title}</h1>
            {subtitle && <div className="mt-1.5 text-sm text-slate-600">{subtitle}</div>}
            <div className="mt-6 rounded-panel border border-rule bg-surface p-5 sm:p-6">{children}</div>
            {footer && <div className="mt-5 text-center text-sm text-slate-600">{footer}</div>}
          </div>
          {aside && <div className="hidden md:block md:pt-12">{aside}</div>}
        </div>
      </main>
    </div>
  );
}

function params() {
  return typeof window === "undefined" ? new URLSearchParams() : new URLSearchParams(window.location.search);
}

/** Where to go after authenticating: a safe local `?next=`, or `fallback`. */
export function nextPath(fallback: string): string {
  const next = params().get("next");
  if (!next || !next.startsWith("/") || next.includes("\\")) return fallback;
  // Browsers treat "/\host" and "//host" as other sites; only accept paths that resolve to this origin.
  try {
    const url = new URL(next, window.location.origin);
    return url.origin === window.location.origin ? `${url.pathname}${url.search}${url.hash}` : fallback;
  } catch {
    return fallback;
  }
}
