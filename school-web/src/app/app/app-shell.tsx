"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ComponentType, ReactNode, SVGProps } from "react";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { can, roleLabel, type Action } from "@/lib/roles";
import { AccountIcon, HomeIcon, LogoutIcon, MoreIcon, PromoteIcon, SettingsIcon, SetupIcon, StaffIcon, StudentsIcon } from "@/components/icons";
import { Wordmark } from "@/components/site-chrome";
import { cx } from "@/components/ui";
import { AnnouncementIcon, MessageIcon, NotificationBell } from "@/components/notification-bell";
import { useApi } from "@/lib/use-api";
import type { StaffMeDto } from "@/lib/types/communication";
import { FeesIcon } from "./fees/fees-shared";
import { confirmSignOutWithUnsent, RegisterSync } from "@/lib/offline-register";
import {
  AssignmentsIcon,
  AttendanceIcon,
  CbtIcon,
  ExamsIcon,
  LessonPlansIcon,
  PlayIcon,
  QuestionsIcon,
  ResultsIcon,
  ScoresIcon,
  TimetableIcon,
} from "./scores/academic-shared";

interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  exact?: boolean;
  needs?: Action;
}

const NAV: NavItem[] = [
  { href: "/app", label: "Home", icon: HomeIcon, exact: true },
  { href: "/app/students", label: "Students", icon: StudentsIcon, needs: "students.read" },
  { href: "/app/setup", label: "Setup", icon: SetupIcon },
  { href: "/app/staff", label: "Staff", icon: StaffIcon, needs: "school.manage" },
  { href: "/app/promotion", label: "Next session", icon: PromoteIcon, needs: "school.manage" },
  { href: "/app/attendance", label: "Attendance", icon: AttendanceIcon, needs: "academic.read" },
  { href: "/app/scores", label: "Scores", icon: ScoresIcon, needs: "academic.read" },
  { href: "/app/results", label: "Results", icon: ResultsIcon, needs: "academic.read" },
  { href: "/app/timetable", label: "Timetable", icon: TimetableIcon, needs: "academic.read" },
  { href: "/app/assignments", label: "Assignments", icon: AssignmentsIcon, needs: "academic.read" },
  { href: "/app/lesson-plans", label: "Lesson plans", icon: LessonPlansIcon, needs: "academic.read" },
  { href: "/app/exams", label: "Exams", icon: ExamsIcon, needs: "academic.read" },
  { href: "/app/questions", label: "Questions", icon: QuestionsIcon, needs: "academic.read" },
  { href: "/app/cbt", label: "CBT", icon: CbtIcon, needs: "academic.read" },
  { href: "/app/play", label: "Play", icon: PlayIcon, needs: "academic.read" },
  { href: "/app/fees", label: "Fees", icon: FeesIcon, needs: "fees.manage" },
  { href: "/app/messages", label: "Messages", icon: MessageIcon },
  { href: "/app/announcements", label: "Announcements", icon: AnnouncementIcon },
  // The school's name, motto, address, logo and the principal's signature: Owner and Admin (the API's admin policy).
  { href: "/app/settings/school", label: "School settings", icon: SettingsIcon, needs: "school.manage" },
  { href: "/app/account", label: "Account", icon: AccountIcon },
];

/** On phones the tab bar holds four; the rest live under More. */
const BOTTOM = ["/app", "/app/students", "/app/setup", "/app/staff"];

function isActive(pathname: string, item: NavItem) {
  return item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");
}

/** Blazer deep rail from 1024px; a top bar and a bottom tab bar below. Role-aware. */
export function AppShell({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const me = useApi<StaffMeDto>(auth?.tenant ? "/api/school/staff/me" : null, { nonce: auth?.tenant?.id });
  if (!auth?.tenant) return null;

  const role = auth.tenant.role;
  const jobTitle = me.data?.jobTitle;
  const items = NAV.filter((n) => !n.needs || can(role, n.needs));
  const bottom = items.filter((n) => BOTTOM.includes(n.href));
  const overflow = items.filter((n) => !BOTTOM.includes(n.href));
  const moreActive = pathname === "/app/more" || overflow.some((n) => isActive(pathname, n));

  async function signOut() {
    if (!(await confirmSignOutWithUnsent())) return;
    await logout();
    router.replace("/login");
  }

  return (
    <div className="min-h-screen bg-ruled lg:flex">
      <RegisterSync />
      <aside className="on-blazer sticky top-[var(--support-bar,0px)] hidden h-[calc(100vh-var(--support-bar,0px))] w-60 shrink-0 flex-col bg-blazer-deep text-on-blazer lg:flex">
        <div className="flex items-start gap-2 pt-6 pr-2 pb-5 pl-5">
          <div className="min-w-0 flex-1">
            <p className="slab text-xl leading-tight font-bold text-white">{auth.tenant.name}</p>
            <p className="mt-1 text-xs">
              {auth.user.fullName}, {roleLabel(role, jobTitle).toLowerCase()}
            </p>
          </div>
          <NotificationBell href="/app/notifications" align="start" className="-mt-2" />
        </div>
        <nav className="min-h-0 flex-1 space-y-0.5 overflow-y-auto px-3" aria-label="School">
          {items.map((n) => {
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
            <Link href="/app" className="min-w-0 rounded-control">
              <p className="slab truncate text-lg leading-tight font-bold">{auth.tenant.name}</p>
              <p className="truncate text-xs text-on-blazer">{roleLabel(role, jobTitle)}</p>
            </Link>
            <NotificationBell href="/app/notifications" className="-mr-2" />
          </div>
        </header>
        <main className="mx-auto w-full max-w-5xl px-4 pt-5 pb-28 sm:px-6 lg:px-8 lg:pt-8 lg:pb-12">{children}</main>
      </div>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 grid border-t border-rule bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
        style={{ gridTemplateColumns: `repeat(${bottom.length + 1}, minmax(0, 1fr))` }}
        aria-label="School"
      >
        {bottom.map((n) => {
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
          href="/app/more"
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

/** The phone "More" page: the rest of the nav and sign out. */
export function MoreMenu() {
  const auth = useAuth();
  const router = useRouter();
  const role = auth?.tenant?.role;
  const items = NAV.filter((n) => !BOTTOM.includes(n.href) && (!n.needs || can(role, n.needs)));
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
              if (!(await confirmSignOutWithUnsent())) return;
              await logout();
              router.replace("/login");
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
