"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { logout } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { APP_NAME } from "@/lib/config";
import { PageLoader, cx } from "@/components/ui";
import { LogoutIcon, MenuIcon, PlusIcon } from "@/components/icons";
import { navFor, primaryNav, secondaryNav } from "@/components/app-nav";
import { BusinessSwitcher } from "@/components/business-switcher";
import { can } from "@/lib/permissions";

function isActive(pathname: string, href: string, exact?: boolean) {
  return exact ? pathname === href : pathname === href || pathname.startsWith(href + "/");
}

export function AppShell({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (hydrated && !auth) {
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [hydrated, auth, pathname, router]);

  if (!hydrated || !auth) {
    return <PageLoader />;
  }

  async function signOut() {
    await logout();
    router.replace("/login");
  }

  const role = auth.tenant.role;
  const canCreate = can(role, "docs.edit");
  const sideNav = navFor([...primaryNav, ...secondaryNav], role);
  const moreActive = pathname === "/app/more" || secondaryNav.some((n) => isActive(pathname, n.href));

  return (
    <div className="min-h-screen bg-desk lg:flex">
      {/* Desktop sidebar */}
      <aside className="on-graphite sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-64 shrink-0 flex-col bg-graphite text-on-graphite lg:flex">
        <div className="px-5 pt-6 pb-4">
          <Link href="/app" className="font-display text-[1.375rem] font-semibold tracking-[-0.01em] text-white">
            {APP_NAME}
          </Link>
          <div className="mt-5 rounded-control bg-graphite-2 px-3 py-2.5">
            <p className="truncate text-sm font-semibold text-white">{auth.tenant.name}</p>
            {auth.tenant.role && <p className="text-xs text-on-graphite">Signed in as {auth.tenant.role.toLowerCase()}</p>}
            <BusinessSwitcher className="mt-2" dark />
          </div>
        </div>
        {canCreate && (
          <div className="px-5 pb-4">
            <Link
              href="/app/invoices/new"
              className="flex min-h-10 items-center justify-center gap-2 rounded-control bg-ink px-3 py-2 text-sm font-semibold text-white hover:bg-[#3553d6]"
            >
              <PlusIcon className="h-4 w-4" /> New invoice
            </Link>
          </div>
        )}
        <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 pb-4" aria-label="App">
          {sideNav.map((item) => {
            const active = isActive(pathname, item.href, item.exact === true);
            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "relative flex items-center gap-3 rounded-control px-3 py-2 text-sm font-medium transition-colors",
                  active ? "bg-graphite-3 text-white" : "text-on-graphite hover:bg-graphite-2 hover:text-white",
                )}
              >
                {active && <span className="absolute inset-y-1.5 left-0 w-[3px] rounded-full bg-ink-light" aria-hidden="true" />}
                <item.icon className={active ? "text-ink-light" : "opacity-80"} />
                {item.label}
              </Link>
            );
          })}
        </nav>
        <div className="border-t border-white/10 px-3 py-3">
          <p className="truncate px-3 text-xs text-on-graphite/80">{auth.user.email}</p>
          {!auth.support && (
            <button
              type="button"
              onClick={signOut}
              className="mt-1 flex w-full items-center gap-3 rounded-control px-3 py-2 text-sm font-medium text-on-graphite hover:bg-graphite-2 hover:text-white"
            >
              <LogoutIcon className="opacity-80" /> Log out
            </button>
          )}
        </div>
      </aside>

      {/* Mobile and tablet top bar */}
      <header className="sticky top-[var(--support-bar,0px)] z-30 flex h-14 items-center justify-between gap-3 border-b border-rule bg-paper/95 px-4 backdrop-blur lg:hidden">
        <Link href="/app" className="font-display text-lg font-semibold text-graphite">
          {APP_NAME}
        </Link>
        {canCreate && (
          <Link
            href="/app/invoices/new"
            className="flex min-h-9 items-center gap-1.5 rounded-control bg-ink px-3 py-1.5 text-sm font-semibold text-white hover:bg-ink-dark"
          >
            <PlusIcon className="h-4 w-4" /> New invoice
          </Link>
        )}
      </header>

      <main className="min-w-0 flex-1 px-4 pt-5 pb-[calc(5.5rem+env(safe-area-inset-bottom))] sm:px-6 lg:px-10 lg:pt-9 lg:pb-12">
        <div className="mx-auto max-w-6xl">{children}</div>
      </main>

      {/* Mobile and tablet bottom bar */}
      <nav
        className="on-graphite fixed inset-x-0 bottom-0 z-30 grid grid-cols-4 bg-graphite pb-[env(safe-area-inset-bottom)] lg:hidden"
        aria-label="App"
      >
        {[...primaryNav, { href: "/app/more", label: "More", icon: MenuIcon, exact: false }].map((item) => {
          const active = item.href === "/app/more" ? moreActive : isActive(pathname, item.href, item.exact);
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-current={active ? "page" : undefined}
              className={cx(
                "relative flex h-16 flex-col items-center justify-center gap-1 text-xs font-medium",
                active ? "text-white" : "text-on-graphite",
              )}
            >
              {active && <span className="absolute inset-x-6 top-0 h-[3px] rounded-b-full bg-ink-light" aria-hidden="true" />}
              <item.icon className={active ? "text-ink-light" : undefined} />
              {item.label}
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
