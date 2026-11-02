"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ReactNode } from "react";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { ADMIN_URL } from "@/lib/config";
import { can } from "@/lib/permissions";
import { AcademySwitcher } from "@/components/academy-switcher";
import { ExternalIcon, LogoutIcon, MenuIcon, PlusIcon, ShieldIcon } from "@/components/icons";
import { Wordmark } from "@/components/site-chrome";
import { elsewhereNav, navFor, primaryNav, secondaryNav } from "@/components/studio-nav";
import { cx } from "@/components/ui";

function isActive(pathname: string, href: string, exact?: boolean) {
  return exact ? pathname === href : pathname === href || pathname.startsWith(href + "/");
}

/** Deep-ink sidebar from 1024px; a top bar and a bottom tab bar on phones and tablets. Role-aware. */
export function StudioShell({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  if (!auth?.tenant) return null;

  async function signOut() {
    await logout();
    router.replace("/teach/login");
  }

  const role = auth.tenant.role;
  const canCreate = can(role, "courses.edit");
  const sideNav = navFor([...primaryNav, ...secondaryNav], role);
  const bottomNav = navFor(primaryNav, role).slice(0, 4);
  const moreActive = pathname === "/studio/more" || secondaryNav.some((n) => isActive(pathname, n.href));
  const academyHref = `/a/${auth.tenant.slug}`;
  const wide = pathname.startsWith("/studio/courses/") || pathname.startsWith("/studio/lessons/");
  const sideLink = "flex items-center gap-3 rounded-control px-3 py-2 text-sm font-medium text-on-ink hover:bg-ink-2 hover:text-white";

  return (
    <div className="min-h-screen bg-mist lg:flex">
      {/* Desktop sidebar */}
      <aside className="on-ink sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-60 shrink-0 flex-col bg-ink text-on-ink lg:flex">
        <div className="px-5 pt-6 pb-4">
          <Link href="/studio" className="inline-block rounded-control">
            <Wordmark dark />
            <span className="mt-1 block text-xs text-on-ink">Studio</span>
          </Link>
          <div className="mt-5 rounded-control bg-ink-2 px-3 py-2.5">
            <p className="truncate text-sm font-semibold text-white">{auth.tenant.name}</p>
            {auth.tenant.role && <p className="text-xs text-on-ink">Signed in as {String(auth.tenant.role).toLowerCase()}</p>}
            <AcademySwitcher className="mt-2" dark />
          </div>
        </div>
        {canCreate && (
          <div className="px-5 pb-4">
            <Link
              href="/studio/courses/new"
              className="flex min-h-10 w-full items-center justify-center gap-2 rounded-control bg-lagoon px-3 py-2 text-sm font-semibold text-on-lagoon hover:bg-lagoon-dark"
            >
              <PlusIcon className="h-4 w-4" /> New course
            </Link>
          </div>
        )}
        <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 pb-4" aria-label="Studio">
          {sideNav.map((item) => {
            const active = isActive(pathname, item.href, item.exact === true);
            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "relative flex items-center gap-3 rounded-control px-3 py-2 text-sm font-medium transition-colors",
                  active ? "bg-ink-3 text-white" : "text-on-ink hover:bg-ink-2 hover:text-white",
                )}
              >
                {active && <span className="absolute inset-y-1.5 left-0 w-[3px] rounded-full bg-[#7fd1c9]" aria-hidden="true" />}
                <item.icon className={active ? "text-[#7fd1c9]" : "opacity-80"} />
                {item.label}
              </Link>
            );
          })}
          {auth.user.isPlatformAdmin && (
            <a href={ADMIN_URL} className={sideLink}>
              <ShieldIcon className="opacity-80" /> Admin console
            </a>
          )}
        </nav>
        <div className="space-y-0.5 border-t border-white/10 px-3 py-3">
          <a href={academyHref} target="_blank" rel="noopener noreferrer" className={sideLink}>
            <ExternalIcon className="opacity-80" /> View academy
          </a>
          {elsewhereNav.map((item) => (
            <Link key={item.href} href={item.href} className={sideLink}>
              <item.icon className="opacity-80" /> {item.label}
            </Link>
          ))}
          <p className="truncate px-3 pt-2 text-xs text-on-ink/80">{auth.user.email}</p>
          {!auth.support && (
            <button type="button" onClick={signOut} className={cx(sideLink, "w-full")}>
              <LogoutIcon className="opacity-80" /> Log out
            </button>
          )}
        </div>
      </aside>

      {/* Phone and tablet top bar */}
      <header className="sticky top-[var(--support-bar,0px)] z-30 flex h-14 items-center justify-between gap-3 border-b border-line bg-surface/95 px-4 backdrop-blur lg:hidden">
        <Link href="/studio" className="flex min-w-0 items-baseline gap-2 truncate">
          <Wordmark className="text-lg" />
          <span className="text-xs text-muted">Studio</span>
        </Link>
        {canCreate && (
          <Link
            href="/studio/courses/new"
            className="flex min-h-9 shrink-0 items-center gap-1.5 rounded-control bg-lagoon px-3 py-1.5 text-sm font-semibold text-on-lagoon hover:bg-lagoon-dark"
          >
            <PlusIcon className="h-4 w-4" /> New course
          </Link>
        )}
      </header>

      <main className="min-w-0 flex-1 px-4 pt-5 pb-[calc(5.5rem+env(safe-area-inset-bottom))] sm:px-6 lg:px-10 lg:pt-9 lg:pb-12">
        <div className={cx("mx-auto", wide ? "max-w-6xl" : "max-w-5xl")}>{children}</div>
      </main>

      {/* Phone and tablet bottom bar */}
      <nav
        className="on-ink fixed inset-x-0 bottom-0 z-30 grid bg-ink pb-[env(safe-area-inset-bottom)] lg:hidden"
        style={{ gridTemplateColumns: `repeat(${bottomNav.length + 1}, minmax(0, 1fr))` }}
        aria-label="Studio"
      >
        {[...bottomNav, { href: "/studio/more", label: "More", icon: MenuIcon, exact: false }].map((item) => {
          const active = item.href === "/studio/more" ? moreActive : isActive(pathname, item.href, item.exact);
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-current={active ? "page" : undefined}
              className={cx("relative flex h-16 flex-col items-center justify-center gap-1 text-xs font-medium", active ? "text-white" : "text-on-ink")}
            >
              {active && <span className="absolute inset-x-5 top-0 h-[3px] rounded-b-full bg-[#7fd1c9]" aria-hidden="true" />}
              <item.icon className={active ? "text-[#7fd1c9]" : undefined} />
              {item.label}
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
