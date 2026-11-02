"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { logout } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { APP_NAME, storeLink } from "@/lib/config";
import { useApi } from "@/lib/use-api";
import type { DashboardDto } from "@/lib/types";
import { Skeleton, cx } from "@/components/ui";
import { BoxIcon, ExternalIcon, HomeIcon, LogoutIcon, MenuIcon, OrdersIcon } from "@/components/icons";
import { secondaryNav } from "@/components/dashboard-nav";

const primary = [
  { href: "/dashboard", label: "Home", icon: HomeIcon, exact: true },
  { href: "/dashboard/orders", label: "Orders", icon: OrdersIcon, exact: false },
  { href: "/dashboard/products", label: "Products", icon: BoxIcon, exact: false },
];

function isActive(pathname: string, href: string, exact?: boolean) {
  return exact ? pathname === href : pathname === href || pathname.startsWith(href + "/");
}

function ShellLoader() {
  return (
    <div className="min-h-screen bg-paper lg:flex" role="status">
      <span className="sr-only">Loading your dashboard…</span>
      <div className="hidden w-64 shrink-0 bg-ink lg:block" />
      <div className="flex-1 space-y-4 px-4 py-8 sm:px-6 lg:px-10">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-4 w-64 bg-line/60" />
        <div className="flex gap-4 pt-4">
          <Skeleton className="h-44 w-64 rounded-panel" />
          <Skeleton className="hidden h-44 w-64 rounded-panel sm:block" />
        </div>
      </div>
    </div>
  );
}

function CountPill({ count, className }: { count: number; className?: string }) {
  return (
    <span
      className={cx(
        "inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-marigold px-1.5 text-xs font-bold text-ink num",
        className,
      )}
    >
      {count > 99 ? "99+" : count}
    </span>
  );
}

export function DashboardShell({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const pathname = usePathname();
  const router = useRouter();
  const { data: summary, reload } = useApi<DashboardDto>(hydrated && auth ? "/api/dashboard" : null);

  useEffect(() => {
    if (hydrated && !auth) {
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [hydrated, auth, pathname, router]);

  // Keep the new-order count fresh when moving around the order screens.
  const ordersArea = pathname === "/dashboard" || pathname.startsWith("/dashboard/orders");
  useEffect(() => {
    if (ordersArea) reload();
  }, [pathname, ordersArea, reload]);

  if (!hydrated || !auth) {
    return <ShellLoader />;
  }

  async function signOut() {
    await logout();
    router.replace("/login");
  }

  const newCount = summary?.newOrders ?? 0;
  const moreActive = pathname === "/dashboard/more" || secondaryNav.some((n) => isActive(pathname, n.href));
  const countLabel = newCount > 0 ? `, ${newCount} new` : "";

  return (
    <div className="min-h-screen bg-paper lg:flex">
      <a
        href="#main"
        className="sr-only z-50 rounded-control bg-marigold px-4 py-2 font-semibold text-ink focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
      >
        Skip to content
      </a>

      {/* Desktop rail */}
      <aside className="sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-64 shrink-0 flex-col bg-ink text-white lg:flex">
        <div className="px-5 pt-6 pb-5">
          <Link href="/dashboard" className="inline-flex min-h-11 items-center gap-2 text-sm font-semibold text-white/75 hover:text-white">
            <span className="h-2.5 w-2.5 rounded-full bg-marigold" aria-hidden="true" />
            {APP_NAME}
          </Link>
          <p className="mt-2 font-display text-xl leading-tight font-bold break-words text-white">{auth.tenant.name}</p>
          <a
            href={storeLink(auth.tenant.slug)}
            target="_blank"
            rel="noopener noreferrer"
            className="-ml-1 mt-1 inline-flex min-h-11 items-center gap-1.5 rounded-control px-1 text-sm text-white/75 hover:text-white"
          >
            View my store
            <ExternalIcon width={15} height={15} />
            <span className="sr-only">(opens in a new tab)</span>
          </a>
        </div>

        <nav className="flex-1 overflow-y-auto px-3" aria-label="Dashboard">
          <ul className="space-y-0.5">
            {primary.map((item) => {
              const active = isActive(pathname, item.href, item.exact);
              const isOrders = item.href === "/dashboard/orders";
              return (
                <li key={item.href}>
                  <RailLink href={item.href} active={active} icon={<item.icon />}>
                    <span className="flex-1">
                      {item.label}
                      {isOrders && <span className="sr-only">{countLabel}</span>}
                    </span>
                    {isOrders && newCount > 0 && <CountPill count={newCount} />}
                  </RailLink>
                </li>
              );
            })}
          </ul>
          <p className="mt-6 mb-1 px-3 text-xs font-medium text-white/55">Store setup</p>
          <ul className="space-y-0.5">
            {secondaryNav.map((item) => (
              <li key={item.href}>
                <RailLink href={item.href} active={isActive(pathname, item.href)} icon={<item.icon />}>
                  {item.label}
                </RailLink>
              </li>
            ))}
          </ul>
        </nav>

        <div className="border-t border-white/10 p-3">
          <p className="truncate px-3 text-xs text-white/60">{auth.user.email}</p>
          {!auth.support && (
            <button
              type="button"
              onClick={signOut}
              className="mt-1 flex min-h-11 w-full items-center gap-3 rounded-control px-3 text-sm font-medium text-white/80 hover:bg-white/10 hover:text-white"
            >
              <LogoutIcon /> Log out
            </button>
          )}
        </div>
      </aside>

      {/* Mobile top bar */}
      <header className="sticky top-[var(--support-bar,0px)] z-20 flex min-h-14 items-center justify-between gap-3 border-b border-line bg-surface/95 px-4 backdrop-blur lg:hidden">
        <Link href="/dashboard" className="flex min-h-11 min-w-0 items-center gap-2">
          <span className="h-2.5 w-2.5 shrink-0 rounded-full bg-marigold" aria-hidden="true" />
          <span className="truncate font-display text-lg font-bold text-ink">{auth.tenant.name}</span>
        </Link>
        {newCount > 0 && (
          <Link
            href="/dashboard/orders?status=New"
            className="flex min-h-11 shrink-0 items-center gap-2 rounded-full px-2 text-sm font-medium text-ink-2"
          >
            <CountPill count={newCount} />
            new
          </Link>
        )}
      </header>

      <main id="main" className="min-w-0 flex-1 px-4 pt-5 pb-safe-bar sm:px-6 lg:px-10 lg:pt-10 lg:pb-12">
        <div className="mx-auto max-w-5xl">{children}</div>
      </main>

      {/* Mobile bottom bar */}
      <nav
        className="fixed inset-x-0 bottom-0 z-30 border-t border-line bg-surface pb-[env(safe-area-inset-bottom)] shadow-float lg:hidden"
        aria-label="Dashboard"
      >
        <ul className="mx-auto grid max-w-lg grid-cols-4">
          {[...primary, { href: "/dashboard/more", label: "More", icon: MenuIcon, exact: false }].map((item) => {
            const active = item.href === "/dashboard/more" ? moreActive : isActive(pathname, item.href, item.exact);
            const isOrders = item.href === "/dashboard/orders";
            return (
              <li key={item.href}>
                <Link
                  href={item.href}
                  aria-current={active ? "page" : undefined}
                  className={cx(
                    "relative flex min-h-16 flex-col items-center justify-center gap-1 text-xs font-medium",
                    active ? "text-ink" : "text-subtle",
                  )}
                >
                  <span
                    className={cx("absolute top-0 h-[3px] w-10 rounded-b-full", active ? "bg-marigold" : "bg-transparent")}
                    aria-hidden="true"
                  />
                  <span className="relative">
                    <item.icon />
                    {isOrders && newCount > 0 && <CountPill count={newCount} className="absolute -top-2 -right-3.5" />}
                  </span>
                  <span>
                    {item.label}
                    {isOrders && <span className="sr-only">{countLabel}</span>}
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
    </div>
  );
}

function RailLink({ href, active, icon, children }: { href: string; active: boolean; icon: ReactNode; children: ReactNode }) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      className={cx(
        "relative flex min-h-11 items-center gap-3 rounded-control px-3 text-sm font-medium transition-colors",
        active ? "bg-white/12 text-white" : "text-white/72 hover:bg-white/6 hover:text-white",
      )}
    >
      <span
        className={cx("absolute top-2 bottom-2 -left-3 w-[3px] rounded-r-full", active ? "bg-marigold" : "bg-transparent")}
        aria-hidden="true"
      />
      <span className={active ? "text-marigold" : ""}>{icon}</span>
      {children}
    </Link>
  );
}
