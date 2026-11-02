import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";

export function AuthShell({ title, subtitle, children, footer }: { title: string; subtitle?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-desk">
      <header className="mx-auto w-full max-w-6xl px-4 py-5 sm:px-6">
        <Link href="/" className="font-display text-[1.375rem] font-semibold tracking-[-0.01em] text-graphite">
          {APP_NAME}
        </Link>
      </header>
      <main className="flex flex-1 items-start justify-center px-4 pt-4 pb-16 sm:items-center sm:pt-0">
        <div className="w-full max-w-[26rem]">
          <h1 className="font-display text-[1.75rem] leading-tight font-[560] tracking-[-0.01em] text-graphite">{title}</h1>
          {subtitle && <div className="mt-1.5 text-sm text-slate-600">{subtitle}</div>}
          <div className="mt-6 rounded-panel border border-rule bg-paper p-5 sm:p-6">{children}</div>
          {footer && <div className="mt-5 text-center text-sm text-slate-600">{footer}</div>}
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
  const next = params().get("next");
  return next && next.startsWith("/") && !next.startsWith("//") ? next : "/app";
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
