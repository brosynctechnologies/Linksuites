"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ComponentType, ReactNode, SVGProps } from "react";
import { AccountIcon, HomeIcon, LogoutIcon, MoreIcon } from "@/components/icons";
import { AnnouncementIcon, MessageIcon, NotificationBell } from "@/components/notification-bell";
import { Wordmark } from "@/components/site-chrome";
import { cx } from "@/components/ui";
import { logout } from "@/lib/api";
import { firstName, lastSchoolSlug, useAuth } from "@/lib/auth";
import { AttendanceIcon, ChildSwitcher, ExamsIcon, FeesIcon, HomeworkIcon, ParentChildProvider, ResultsIcon, TimetableIcon, useChild } from "./_shared";

interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  exact?: boolean;
}

const NAV: NavItem[] = [
  { href: "/parent", label: "Home", icon: HomeIcon, exact: true },
  { href: "/parent/results", label: "Results", icon: ResultsIcon },
  { href: "/parent/attendance", label: "Attendance", icon: AttendanceIcon },
  { href: "/parent/fees", label: "Fees", icon: FeesIcon },
  { href: "/parent/announcements", label: "Announcements", icon: AnnouncementIcon },
  { href: "/parent/messages", label: "Messages", icon: MessageIcon },
  { href: "/parent/timetable", label: "Timetable", icon: TimetableIcon },
  { href: "/parent/exams", label: "Exams", icon: ExamsIcon },
  { href: "/parent/assignments", label: "Homework", icon: HomeworkIcon },
  { href: "/parent/account", label: "Account", icon: AccountIcon },
];

const BOTTOM = ["/parent", "/parent/results", "/parent/fees"];

function isActive(pathname: string, item: NavItem) {
  return item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");
}

export function ParentShell({ children }: { children: ReactNode }) {
  return (
    <ParentChildProvider>
      <ParentChrome>{children}</ParentChrome>
    </ParentChildProvider>
  );
}

function ParentChrome({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const { child } = useChild();
  if (!auth) return null;

  const overflow = NAV.filter((n) => !BOTTOM.includes(n.href));
  const moreActive = pathname === "/parent/more" || overflow.some((n) => isActive(pathname, n));
  const schoolName = child?.schoolName ?? "Parent";

  async function signOut() {
    const slug = lastSchoolSlug();
    await logout();
    router.replace(slug ? `/${slug}` : "/");
  }

  return (
    <div className="min-h-screen bg-ruled lg:flex">
      <aside className="on-blazer sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-60 shrink-0 flex-col bg-blazer-deep text-on-blazer lg:flex">
        <div className="flex items-start gap-2 pt-6 pr-2 pb-5 pl-5">
          <div className="min-w-0 flex-1">
            <p className="slab text-xl leading-tight font-bold text-white">{schoolName}</p>
            <p className="mt-1 text-xs">{firstName(auth.user.fullName)}</p>
          </div>
          <NotificationBell href="/parent/notifications" align="start" className="-mt-2" />
        </div>
        <div className="px-4 pb-3">
          <ChildSwitcher dark />
        </div>
        <nav className="min-h-0 flex-1 space-y-0.5 overflow-y-auto px-3" aria-label="Parent">
          {NAV.map((n) => {
            const active = isActive(pathname, n);
            return (
              <Link
                key={n.href}
                href={n.href}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "flex min-h-11 items-center gap-3 rounded-control px-3 text-sm font-bold",
                  active ? "bg-blazer-deep-2 text-white" : "hover:bg-blazer-deep-2 hover:text-white",
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
            className="flex min-h-11 w-full items-center gap-3 rounded-control px-3 text-sm font-bold hover:bg-blazer-deep-2 hover:text-white"
          >
            <LogoutIcon className="h-[18px] w-[18px]" /> Sign out
          </button>
          <div className="px-3">
            <Wordmark dark className="text-sm" />
          </div>
        </div>
      </aside>

      <div className="min-w-0 flex-1">
        <header className="on-blazer sticky top-[var(--support-bar,0px)] z-30 bg-blazer-deep text-white lg:hidden">
          <div className="flex min-h-14 items-center justify-between gap-3 px-4">
            <div className="min-w-0">
              <p className="slab truncate text-lg leading-tight font-bold">{schoolName}</p>
              <p className="truncate text-xs text-on-blazer">{firstName(auth.user.fullName)}</p>
            </div>
            <NotificationBell href="/parent/notifications" className="-mr-2" />
          </div>
          <div className="px-4 pb-3">
            <ChildSwitcher dark />
          </div>
        </header>
        <main className="mx-auto w-full max-w-5xl px-4 pt-5 pb-28 sm:px-6 lg:px-8 lg:pt-8 lg:pb-12">{children}</main>
      </div>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-4 border-t border-rule bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
        aria-label="Parent"
      >
        {NAV.filter((n) => BOTTOM.includes(n.href)).map((n) => {
          const active = isActive(pathname, n);
          return (
            <Link
              key={n.href}
              href={n.href}
              aria-current={active ? "page" : undefined}
              className={cx("flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-bold", active ? "text-blazer" : "text-muted")}
            >
              <n.icon className="h-5 w-5" />
              {n.label}
            </Link>
          );
        })}
        <Link
          href="/parent/more"
          aria-current={moreActive ? "page" : undefined}
          className={cx("flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-bold", moreActive ? "text-blazer" : "text-muted")}
        >
          <MoreIcon className="h-5 w-5" />
          More
        </Link>
      </nav>
    </div>
  );
}

export function ParentMoreMenu() {
  const router = useRouter();
  const items = NAV.filter((n) => !BOTTOM.includes(n.href));
  return (
    <div className="ruled-sheet">
      <ul className="ruled-rows">
        {items.map((n) => (
          <li key={n.href}>
            <Link href={n.href} className="ruled-pad flex min-h-12 items-center gap-3 text-sm font-bold text-ink hover:bg-slate-50">
              <n.icon className="h-5 w-5 text-muted" />
              {n.label}
            </Link>
          </li>
        ))}
        <li>
          <button
            type="button"
            onClick={async () => {
              const slug = lastSchoolSlug();
              await logout();
              router.replace(slug ? `/${slug}` : "/");
            }}
            className="ruled-pad flex min-h-12 w-full items-center gap-3 text-left text-sm font-bold text-ink hover:bg-slate-50"
          >
            <LogoutIcon className="h-5 w-5 text-muted" />
            Sign out
          </button>
        </li>
      </ul>
    </div>
  );
}
