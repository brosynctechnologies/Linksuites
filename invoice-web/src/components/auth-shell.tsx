import Link from "next/link";
import type { ReactNode } from "react";
import { SuiteMark } from "@/components/suite/suite-mark";
import { APP_NAME } from "@/lib/config";
import { hubUrl } from "@/lib/suite";

export function AuthShell({ title, subtitle, children, footer }: { title: string; subtitle?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-desk">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-control focus:bg-graphite focus:px-4 focus:py-3 focus:text-white"
      >
        Skip to content
      </a>
      <header className="mx-auto w-full max-w-6xl px-4 py-4 sm:px-6">
        <Link href="/" className="inline-flex min-h-11 items-center rounded-control font-display text-[1.375rem] font-semibold tracking-[-0.01em] text-graphite">
          {APP_NAME}
        </Link>
      </header>
      <main id="main" tabIndex={-1} className="flex flex-1 items-start justify-center px-4 pt-4 pb-16 outline-none sm:items-center sm:pt-0">
        <div className="w-full max-w-[26rem]">
          <h1 className="font-display text-[1.75rem] leading-tight font-[560] tracking-[-0.01em] text-graphite">{title}</h1>
          {subtitle && <div className="mt-1.5 text-sm text-slate-600">{subtitle}</div>}
          <div className="mt-6 rounded-panel border border-rule bg-paper p-5 sm:p-6">{children}</div>
          {footer && <div className="mt-5 text-center text-sm text-slate-600">{footer}</div>}
          <p className="mt-8 flex justify-center">
            <a href={hubUrl} className="inline-flex min-h-11 items-center gap-2 rounded-control px-2 text-sm text-muted hover:text-graphite hover:underline">
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

/** True when the visitor came from the free generator's "Save & send" CTA. */
export function wantsImport(): boolean {
  return params().get("import") === "1";
}

/** Where to go after authenticating: the draft import, a safe local `?next=`, or the app home. */
export function nextPath(): string {
  if (wantsImport()) return "/app/import";
  const fallback = "/app";
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

/** Carry `?next=` / `?import=1` across the login ↔ signup links. */
export function carryQuery(): string {
  const p = params();
  const out = new URLSearchParams();
  const next = p.get("next");
  if (next) out.set("next", next);
  if (p.get("import") === "1") out.set("import", "1");
  const s = out.toString();
  return s ? `?${s}` : "";
}
