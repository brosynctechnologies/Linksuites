import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { OrderTicket } from "./order-ticket";

export function AuthShell({ title, subtitle, children, footer }: { title: string; subtitle?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex min-h-screen bg-paper lg:grid lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
      {/* Calm brand panel (desktop) */}
      <aside className="relative hidden flex-col justify-between overflow-hidden bg-ink px-10 py-10 text-white lg:flex xl:px-14">
        <Link href="/" className="inline-flex min-h-11 w-fit items-center gap-2 font-display text-xl font-extrabold text-white">
          <span className="h-3 w-3 rounded-full bg-marigold" aria-hidden="true" />
          {APP_NAME}
        </Link>
        <div className="max-w-sm">
          <p className="font-display text-3xl leading-tight font-bold">Every order, written out and waiting for you.</p>
          <p className="mt-3 text-white/75">Customers order from your link. You confirm, get paid and deliver from one list.</p>
          <div className="mt-10 w-72 rotate-[-2deg]" aria-hidden="true">
            <OrderTicket
              compact
              number={1042}
              kicker="Ada"
              lines={[
                { key: "a", quantity: 2, name: "Jollof rice", variant: "Large", amount: "₦6,000" },
                { key: "b", quantity: 1, name: "Chapman", amount: "₦1,500" },
              ]}
              rows={[{ label: "Delivery (Lekki)", value: "₦1,500" }]}
              total="₦9,000"
            />
          </div>
        </div>
        <p className="text-sm text-white/60">Free for up to 20 products.</p>
      </aside>

      {/* Form column */}
      <div className="flex min-h-screen w-full flex-col">
        <header className="px-4 py-3 sm:px-6 lg:hidden">
          <Link href="/" className="inline-flex min-h-11 items-center gap-2 font-display text-xl font-extrabold text-ink">
            <span className="h-3 w-3 rounded-full bg-marigold" aria-hidden="true" />
            {APP_NAME}
          </Link>
        </header>
        <main className="flex flex-1 items-start justify-center px-4 pt-4 pb-12 sm:px-6 sm:pt-10 lg:items-center lg:py-12">
          <div className="w-full max-w-md">
            <h1 className="font-display text-3xl font-bold text-ink">{title}</h1>
            {subtitle && <p className="mt-1.5 text-ink-2">{subtitle}</p>}
            <div className="mt-8">{children}</div>
            {footer && <div className="mt-8 border-t border-line pt-5 text-sm text-ink-2">{footer}</div>}
          </div>
        </main>
      </div>
    </div>
  );
}

/** Where to go after authenticating: a safe local `?next=` or the dashboard. */
export function nextPath(): string {
  if (typeof window === "undefined") return "/dashboard";
  const next = new URLSearchParams(window.location.search).get("next");
  return next && next.startsWith("/") && !next.startsWith("//") ? next : "/dashboard";
}
