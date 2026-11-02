"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ComponentType, ReactNode, SVGProps } from "react";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { CareMeProvider, useCare } from "@/lib/care-me";
import { careRoleLabel, meets, type CareAccess, type NavNeed } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { CareMeDto } from "@/lib/types";
import { AccountIcon, HomeIcon, LogoutIcon, MoreIcon, PatientsIcon, PriceIcon, SettingsIcon, SetupIcon, StaffIcon } from "@/components/icons";
import { NotificationBell } from "@/components/notification-bell";
import { Wordmark } from "@/components/site-chrome";
import { ApiErrorView, PageLoader, cx } from "@/components/ui";

interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  exact?: boolean;
  /** Shown only when the member's access meets this (lib/roles.ts `meets`). */
  needs?: NavNeed;
}

/**
 * The staff navigation, filtered by role:
 * - "clinical": clinical access only (doctors, nurses, the medical director, admins with a clinical role). Front desk and the cashier
 *   never see these. Tasks 18-19 add the queue's clinical views, consultations, ward care and the record here.
 * - "money": hidden from clinical-only staff (a doctor or nurse who isn't leadership). Price list now; bills and reports in Task 19.
 * - "leadership": the medical director and admins (staff, setup, settings).
 * - "desk": everyone but the cashier.
 */
export const NAV: NavItem[] = [
  { href: "/app", label: "Home", icon: HomeIcon, exact: true },
  { href: "/app/patients", label: "Patients", icon: PatientsIcon },
  { href: "/app/price-list", label: "Price list", icon: PriceIcon, needs: "money" },
  { href: "/app/staff", label: "Staff", icon: StaffIcon, needs: "leadership" },
  { href: "/app/setup", label: "Setup", icon: SetupIcon, needs: "leadership" },
  { href: "/app/settings", label: "Settings", icon: SettingsIcon, needs: "leadership" },
  { href: "/app/account", label: "Account", icon: AccountIcon },
];

/** On phones the tab bar holds four; the rest live under More. */
const BOTTOM = ["/app", "/app/patients", "/app/price-list", "/app/staff"];

export function navFor(access: CareAccess): NavItem[] {
  return NAV.filter((n) => meets(access, n.needs));
}

function isActive(pathname: string, item: NavItem) {
  return item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");
}

/** Loads who the member is to the clinic (`GET /api/care/staff/me`) before any page renders, then draws the shell. */
export function AppShell({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const me = useApi<CareMeDto>(auth?.tenant ? "/api/care/staff/me" : null, { nonce: `${auth?.tenant?.id}:${auth?.user.id}` });
  if (!auth?.tenant) return null;
  if (me.error && !me.data)
    return (
      <div className="mx-auto max-w-lg px-4 py-16">
        <ApiErrorView error={me.error} onRetry={me.reload} />
      </div>
    );
  if (!me.data) return <PageLoader />;
  return (
    <CareMeProvider me={me.data} support={!!auth.support}>
      <Shell clinicName={auth.tenant.name}>{children}</Shell>
    </CareMeProvider>
  );
}

/** Theatre rail from 1024px; a top bar and a bottom tab bar below. */
function Shell({ clinicName, children }: { clinicName: string; children: ReactNode }) {
  const { me, access } = useCare();
  const pathname = usePathname();
  const router = useRouter();
  const items = navFor(access);
  const bottom = items.filter((n) => BOTTOM.includes(n.href));
  const overflow = items.filter((n) => !BOTTOM.includes(n.href));
  const moreActive = pathname === "/app/more" || overflow.some((n) => isActive(pathname, n));
  const role = careRoleLabel(me.memberRole, me.clinicalRole);

  async function signOut() {
    await logout();
    router.replace("/login");
  }

  return (
    <div className="min-h-screen bg-gauze lg:flex">
      <aside className="on-scrub sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-60 shrink-0 flex-col bg-scrub-deep text-on-scrub lg:flex">
        <div className="flex items-start gap-2 pt-6 pr-2 pb-5 pl-5">
          <div className="min-w-0 flex-1">
            <p className="display text-xl leading-tight font-bold text-white">{clinicName}</p>
            <p className="mt-1 text-xs">
              {me.fullName}, {role.toLowerCase()}
            </p>
          </div>
          <NotificationBell href="/app/notifications" align="start" className="-mt-2" />
        </div>
        <nav className="min-h-0 flex-1 space-y-0.5 overflow-y-auto px-3" aria-label="Clinic">
          {items.map((n) => {
            const active = isActive(pathname, n);
            return (
              <Link
                key={n.href}
                href={n.href}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "flex min-h-11 items-center gap-3 rounded-control px-3 text-sm font-bold",
                  active ? "bg-scrub-deep-2 text-white shadow-[inset_3px_0_0_#9fe0d5]" : "hover:bg-scrub-deep-2 hover:text-white",
                )}
              >
                <n.icon className="h-[18px] w-[18px]" />
                {n.label}
              </Link>
            );
          })}
        </nav>
        <div className="space-y-2 px-3 pb-5">
          <button
            type="button"
            onClick={signOut}
            className="flex min-h-11 w-full items-center gap-3 rounded-control px-3 text-sm font-bold hover:bg-scrub-deep-2 hover:text-white"
          >
            <LogoutIcon className="h-[18px] w-[18px]" /> Sign out
          </button>
          <div className="px-3">
            <Wordmark dark className="text-sm" />
          </div>
        </div>
      </aside>

      <div className="min-w-0 flex-1">
        <header className="on-scrub sticky top-[var(--support-bar,0px)] z-30 bg-scrub-deep text-white lg:hidden">
          <div className="flex min-h-14 items-center justify-between gap-3 px-4">
            <Link href="/app" className="min-w-0 rounded-control">
              <p className="display truncate text-lg leading-tight font-bold">{clinicName}</p>
              <p className="truncate text-xs text-on-scrub">{role}</p>
            </Link>
            <NotificationBell href="/app/notifications" className="-mr-2" />
          </div>
        </header>
        <main className="mx-auto w-full max-w-5xl px-4 pt-5 pb-28 sm:px-6 lg:px-8 lg:pt-8 lg:pb-12">{children}</main>
      </div>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 grid border-t border-rule bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
        style={{ gridTemplateColumns: `repeat(${bottom.length + 1}, minmax(0, 1fr))` }}
        aria-label="Clinic"
      >
        {bottom.map((n) => {
          const active = isActive(pathname, n);
          return (
            <Link
              key={n.href}
              href={n.href}
              aria-current={active ? "page" : undefined}
              className={cx("flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-bold", active ? "text-scrub" : "text-muted")}
            >
              <n.icon className="h-5 w-5" />
              {n.label}
            </Link>
          );
        })}
        <Link
          href="/app/more"
          aria-current={moreActive ? "page" : undefined}
          className={cx("flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-bold", moreActive ? "text-scrub" : "text-muted")}
        >
          <MoreIcon className="h-5 w-5" />
          More
        </Link>
      </nav>
    </div>
  );
}

/** The phone "More" page: the rest of the nav and sign out. */
export function MoreMenu() {
  const { access } = useCare();
  const router = useRouter();
  const items = navFor(access).filter((n) => !BOTTOM.includes(n.href));
  return (
    <div className="case-sheet">
      <ul className="case-rows">
        {items.map((n) => (
          <li key={n.href}>
            <Link href={n.href} className="case-pad flex min-h-12 items-center gap-3 text-sm font-bold text-ink hover:bg-slate-50">
              <n.icon className="h-5 w-5 text-muted" />
              {n.label}
            </Link>
          </li>
        ))}
        <li>
          <button
            type="button"
            onClick={async () => {
              await logout();
              router.replace("/login");
            }}
            className="case-pad flex min-h-12 w-full items-center gap-3 text-left text-sm font-bold text-ink hover:bg-slate-50"
          >
            <LogoutIcon className="h-5 w-5 text-muted" />
            Sign out
          </button>
        </li>
      </ul>
    </div>
  );
}
