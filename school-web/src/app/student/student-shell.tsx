"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { createContext, useContext, type ComponentType, type ReactNode, type SVGProps } from "react";
import { HomeIcon, LogoutIcon, MoreIcon } from "@/components/icons";
import { AnnouncementIcon, NotificationBell } from "@/components/notification-bell";
import { Wordmark } from "@/components/site-chrome";
import { PageLoader, cx } from "@/components/ui";
import { logout } from "@/lib/api";
import { firstName, lastSchoolSlug, useAuth } from "@/lib/auth";
import { useApi } from "@/lib/use-api";
import type { StudentMe } from "@/lib/types";
import { ExamsIcon, HomeworkIcon, PlayIcon, PortalBlocked, ResultsIcon, TestsIcon, TimetableIcon } from "../parent/_shared";

interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  exact?: boolean;
}

const NAV: NavItem[] = [
  { href: "/student", label: "Home", icon: HomeIcon, exact: true },
  { href: "/student/results", label: "Results", icon: ResultsIcon },
  { href: "/student/timetable", label: "Timetable", icon: TimetableIcon },
  { href: "/student/exams", label: "Exams", icon: ExamsIcon },
  { href: "/student/assignments", label: "Homework", icon: HomeworkIcon },
  { href: "/student/cbt", label: "Tests", icon: TestsIcon },
  { href: "/student/play", label: "Play", icon: PlayIcon },
  { href: "/student/announcements", label: "Announcements", icon: AnnouncementIcon },
];

/** On phones the tab bar holds four; the rest live under More. */
const BOTTOM = ["/student", "/student/results", "/student/assignments", "/student/cbt"];

function isActive(pathname: string, item: NavItem) {
  return item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");
}

const MeCtx = createContext<StudentMe | null>(null);
export function useStudentMe() {
  return useContext(MeCtx);
}

export function StudentShell({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const me = useApi<StudentMe>("/api/student/me");
  if (!auth) return null;
  const moreActive = pathname === "/student/more" || NAV.some((n) => !BOTTOM.includes(n.href) && isActive(pathname, n));

  async function signOut() {
    const slug = lastSchoolSlug();
    await logout();
    router.replace(slug ? `/${slug}` : "/");
  }

  if (me.loading && !me.data && !me.error) {
    return (
      <div className="min-h-screen bg-ruled">
        <PageLoader />
      </div>
    );
  }
  if (me.error) {
    return (
      <div className="mx-auto w-full max-w-lg px-4 py-16">
        <PortalBlocked error={me.error} onRetry={me.reload} />
        <button type="button" onClick={signOut} className="mt-6 text-sm font-semibold text-blazer underline-offset-2 hover:underline">
          Sign out
        </button>
      </div>
    );
  }

  const schoolName = me.data?.schoolName ?? "Student";

  return (
    <MeCtx.Provider value={me.data ?? null}>
      <div className="min-h-screen bg-ruled lg:flex">
        <aside className="on-blazer sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-60 shrink-0 flex-col bg-blazer-deep text-on-blazer lg:flex">
          <div className="flex items-start gap-2 pt-6 pr-2 pb-5 pl-5">
            <div className="min-w-0 flex-1">
              <p className="slab text-xl leading-tight font-bold text-white">{schoolName}</p>
              <p className="mt-1 text-xs">{me.data?.name ?? firstName(auth.user.fullName)}</p>
            </div>
            <NotificationBell href="/student/notifications" align="start" className="-mt-2" />
          </div>
          <nav className="min-h-0 flex-1 space-y-0.5 overflow-y-auto px-3" aria-label="Student">
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
                <p className="truncate text-xs text-on-blazer">{me.data?.className || me.data?.name || firstName(auth.user.fullName)}</p>
              </div>
              <NotificationBell href="/student/notifications" className="-mr-2" />
            </div>
          </header>
          <main className="mx-auto w-full max-w-5xl px-4 pt-5 pb-28 sm:px-6 lg:px-8 lg:pt-8 lg:pb-12">{children}</main>
        </div>

        <nav
          className="fixed inset-x-0 bottom-0 z-30 grid border-t border-rule bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
          style={{ gridTemplateColumns: `repeat(${BOTTOM.length + 1}, minmax(0, 1fr))` }}
          aria-label="Student"
        >
          {NAV.filter((n) => BOTTOM.includes(n.href)).map((n) => {
            const active = isActive(pathname, n);
            return (
              <Link
                key={n.href}
                href={n.href}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "flex min-h-14 min-w-0 flex-col items-center justify-center gap-0.5 px-0.5 text-center text-xs leading-tight font-bold",
                  active ? "text-blazer" : "text-muted",
                )}
              >
                <n.icon className="h-5 w-5" />
                {n.label}
              </Link>
            );
          })}
          <Link
            href="/student/more"
            aria-current={moreActive ? "page" : undefined}
            className={cx(
              "flex min-h-14 min-w-0 flex-col items-center justify-center gap-0.5 px-0.5 text-center text-xs leading-tight font-bold",
              moreActive ? "text-blazer" : "text-muted",
            )}
          >
            <MoreIcon className="h-5 w-5" />
            More
          </Link>
        </nav>
      </div>
    </MeCtx.Provider>
  );
}

/** The phone "More" page: the rest of the nav and sign out. */
export function StudentMoreMenu() {
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
