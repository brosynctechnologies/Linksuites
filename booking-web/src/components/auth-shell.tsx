import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { hubUrl } from "@/lib/suite";
import { SuiteMark } from "./suite/suite-mark";

export function AuthShell({ title, subtitle, children, footer }: { title: string; subtitle?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-mist">
      <header className="mx-auto w-full max-w-6xl px-4 py-4 sm:px-6">
        <Link href="/" className="inline-flex min-h-11 items-center rounded-control font-expanded text-xl font-extrabold text-aubergine">
          {APP_NAME}
        </Link>
      </header>
      <main id="main" tabIndex={-1} className="flex flex-1 items-start justify-center px-4 pt-4 pb-16 outline-none sm:items-center sm:pt-0">
        <div className="w-full max-w-[26rem]">
          <h1 className="text-2xl leading-tight font-bold tracking-[-0.01em] text-aubergine">{title}</h1>
          {subtitle && <div className="mt-1.5 text-sm text-slate-600">{subtitle}</div>}
          <div className="mt-6 rounded-panel border border-line bg-surface p-5 sm:p-6">{children}</div>
          {footer && <div className="mt-5 text-center text-sm text-slate-600">{footer}</div>}
          <p className="mt-6 text-center text-sm">
            <a href={hubUrl} className="inline-flex min-h-11 items-center gap-2 rounded-control px-2 text-slate-600 hover:text-aubergine hover:underline">
              <SuiteMark size={16} />
              Part of LinkSuite
            </a>
          </p>
        </div>
      </main>
    </div>
  );
}

function params() {
  return typeof window === "undefined" ? new URLSearchParams() : new URLSearchParams(window.location.search);
}

/** Where to go after authenticating: a safe local `?next=`, or `fallback`. */
export function nextPath(fallback = "/app"): string {
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

/** Carry `?next=` across the login ↔ signup links. */
export function carryQuery(): string {
  const next = params().get("next");
  return next ? `?${new URLSearchParams({ next }).toString()}` : "";
}
