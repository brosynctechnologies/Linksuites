"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api, ApiError } from "@/lib/api";
import { timeLeft } from "@/lib/format";
import { can, ROLE_LABEL, type Area } from "@/lib/roles";
import { ACCESS_REMOVED_NOTE, applyMe, endSession, SESSION_ENDED_NOTE, useHydrated, useSession, type AdminSession } from "@/lib/session";
import type { OverviewDto, PlatformMe } from "@/lib/types";
import { useNow } from "@/lib/use-now";
import { CloseIcon, LogoutIcon, MenuIcon } from "./icons";
import { isActive, NAV, type NavItem } from "./nav";
import { SignIn } from "./sign-in";
import { ToastProvider } from "./toast";
import { AreaProvider, Loading, NoAccess, cx } from "./ui";

// ---------- Queue counts (from the overview), refreshed after decisions ----------

interface QueueState {
  queues: OverviewDto["queues"] | null;
  refresh: () => void;
}
const QueueContext = createContext<QueueState>({ queues: null, refresh: () => {} });
export const useQueues = () => useContext(QueueContext);

function QueueProvider({ children }: { children: ReactNode }) {
  const [queues, setQueues] = useState<OverviewDto["queues"] | null>(null);
  const [nonce, setNonce] = useState(0);
  const refresh = useCallback(() => setNonce((n) => n + 1), []);
  useEffect(() => {
    const ctrl = new AbortController();
    api<OverviewDto>("/api/platform/overview", { signal: ctrl.signal }).then(
      (o) => setQueues(o.queues),
      () => {},
    );
    const t = setInterval(() => setNonce((n) => n + 1), 120_000);
    return () => {
      ctrl.abort();
      clearInterval(t);
    };
  }, [nonce]);
  const value = useMemo(() => ({ queues, refresh }), [queues, refresh]);
  return <QueueContext.Provider value={value}>{children}</QueueContext.Provider>;
}

// ---------- Session ----------

const SessionContext = createContext<AdminSession | null>(null);
/** The signed-in admin (always set inside the console). */
export function useAdmin(): AdminSession {
  const s = useContext(SessionContext);
  if (!s) throw new Error("useAdmin outside the console");
  return s;
}

const ME_POLL_MS = 60_000;

/**
 * Check the session against GET /api/platform/me every minute and when the tab regains focus, and keep the stored
 * name, role and session end in step with the server. A 401 (signed out elsewhere, removed, password reset) already
 * ends the session in `api`; a 403 means the admin role was taken away. Network failures wait for the next check.
 */
function useMeCheck(token: string | null) {
  useEffect(() => {
    if (!token) return;
    let ctrl: AbortController | null = null;
    let last = 0;
    const check = () => {
      // Focus and visibilitychange often fire together.
      if (Date.now() - last < 5_000) return;
      last = Date.now();
      ctrl?.abort();
      ctrl = new AbortController();
      api<PlatformMe>("/api/platform/me", { signal: ctrl.signal }).then(applyMe, (e) => {
        if (e instanceof ApiError && e.status === 403) endSession(ACCESS_REMOVED_NOTE);
      });
    };
    const onVisible = () => {
      if (document.visibilityState === "visible") check();
    };
    check();
    const t = setInterval(check, ME_POLL_MS);
    window.addEventListener("focus", check);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      ctrl?.abort();
      clearInterval(t);
      window.removeEventListener("focus", check);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [token]);
}

/**
 * The private app: without a session, the sign-in screen renders in place (the URL is kept, so the page
 * appears after sign-in). With one, the console bar, the rail and the page.
 */
export function Console({ children }: { children: ReactNode }) {
  const hydrated = useHydrated();
  const session = useSession();
  const now = useNow(30_000);
  useMeCheck(session?.accessToken ?? null);

  const expired = !!session && now > 0 && new Date(session.expiresAt).getTime() <= now;
  useEffect(() => {
    if (expired) endSession(SESSION_ENDED_NOTE);
  }, [expired]);

  if (!hydrated) return <Loading label="Opening the console…" />;
  if (!session || expired) return <SignIn />;

  return (
    <SessionContext.Provider value={session}>
      <ToastProvider>
        <QueueProvider>
          <Shell session={session} now={now}>
            {children}
          </Shell>
        </QueueProvider>
      </ToastProvider>
    </SessionContext.Provider>
  );
}

/** Wrap a page: renders "You need the X role" instead of the page when the role can't use it; 403s say the same. */
export function RequireArea({ area, children }: { area: Area; children: ReactNode }) {
  const { user } = useAdmin();
  return <AreaProvider value={area}>{can(user.platformRole, area) ? children : <NoAccess area={area} />}</AreaProvider>;
}

// ---------- Chrome ----------

function ConsoleBar({ session, now }: { session: AdminSession; now: number }) {
  const left = now > 0 ? new Date(session.expiresAt).getTime() - now : null;
  const role = session.user.platformRole;
  const [signingOut, setSigningOut] = useState(false);

  /** End the panel session on the server too; a failure there still signs this tab out. */
  async function signOut() {
    setSigningOut(true);
    try {
      await api("/api/platform/auth/logout", { method: "POST" });
    } catch {
      /* ignored: the local session ends either way */
    }
    endSession(null);
  }

  return (
    <div className="fixed inset-x-0 top-0 z-40 flex h-9 items-center gap-3 bg-caution pr-2 text-sm text-graphite">
      <span className="caution-stripe h-full w-8 shrink-0 sm:w-10" aria-hidden="true" />
      <span className="font-semibold whitespace-nowrap">Admin console</span>
      <span className="ml-auto flex min-w-0 items-center gap-3">
        <Link href="/account" className="hidden min-w-0 truncate underline-offset-2 hover:underline sm:inline" title="Your account">
          {session.user.fullName}
        </Link>
        {role && <span className="rounded-control bg-graphite px-1.5 py-0.5 text-xs font-semibold whitespace-nowrap text-caution">{ROLE_LABEL[role]}</span>}
        {left !== null && (
          <span className="tnum text-xs font-medium" title="Session time left. Sign in again when it runs out.">
            <span className="sr-only">Session time left: </span>
            {timeLeft(left)}
          </span>
        )}
        <button
          type="button"
          onClick={signOut}
          disabled={signingOut}
          aria-busy={signingOut || undefined}
          className="inline-flex h-8 items-center gap-1.5 rounded-control px-2 text-xs font-semibold hover:bg-graphite/10 disabled:opacity-60"
        >
          <LogoutIcon className="h-4 w-4" />
          <span className="hidden sm:inline">Sign out</span>
          <span className="sr-only sm:hidden">Sign out</span>
        </button>
      </span>
    </div>
  );
}

function useVisibleNav() {
  const { user } = useAdmin();
  return NAV.map((g) => ({ ...g, items: g.items.filter((i) => can(user.platformRole, i.area)) })).filter((g) => g.items.length > 0);
}

function QueueCount({ item, compact }: { item: NavItem; compact?: boolean }) {
  const { queues } = useQueues();
  if (!item.queue || !queues) return null;
  const n = queues[item.queue];
  if (!n) return null;
  return (
    <span
      className={cx(
        "tnum rounded-control bg-caution px-1.5 text-xs leading-5 font-semibold text-graphite",
        compact ? "absolute top-1 right-1 min-w-4 px-1 text-center text-[0.6875rem] leading-4" : "ml-auto",
      )}
    >
      {n > 99 ? "99+" : n}
      <span className="sr-only"> waiting</span>
    </span>
  );
}

function Rail() {
  const pathname = usePathname();
  const groups = useVisibleNav();
  return (
    <aside className="fixed top-9 bottom-0 left-0 z-30 hidden w-16 flex-col overflow-y-auto bg-graphite text-on-graphite md:flex xl:w-[232px]">
      <nav aria-label="Console" className="flex-1 px-2 py-3">
        {groups.map((g, gi) => (
          <div key={gi} className={gi > 0 ? "mt-3 border-t border-graphite-3 pt-3" : ""}>
            {g.label && <p className="mb-1 hidden px-3 text-xs font-medium tracking-wide text-on-graphite/70 uppercase xl:block">{g.label}</p>}
            <ul className="space-y-0.5">
              {g.items.map((item) => {
                const active = isActive(pathname, item);
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      aria-current={active ? "page" : undefined}
                      title={item.label}
                      className={cx(
                        "relative flex min-h-10 items-center justify-center gap-3 rounded-control px-3 text-sm font-medium xl:justify-start",
                        active ? "bg-graphite-3 text-white" : "hover:bg-graphite-2 hover:text-white",
                      )}
                    >
                      {active && <span className="absolute inset-y-2 left-0 w-[3px] rounded-full bg-caution" aria-hidden="true" />}
                      <item.icon className="h-5 w-5" />
                      <span className="sr-only xl:not-sr-only">{item.label}</span>
                      <span className="hidden xl:contents">
                        <QueueCount item={item} />
                      </span>
                      <span className="xl:hidden">
                        <QueueCount item={item} compact />
                      </span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>
    </aside>
  );
}

/** Phones: a bottom bar with the current page and a Menu button that opens the nav as a bottom sheet. */
function PhoneNav() {
  const pathname = usePathname();
  const groups = useVisibleNav();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDialogElement>(null);
  const current = groups.flatMap((g) => g.items).find((i) => isActive(pathname, i));

  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);

  return (
    <>
      <div className="fixed inset-x-0 bottom-0 z-30 flex h-14 items-center gap-2 border-t border-graphite-3 bg-graphite px-3 text-white md:hidden">
        <span className="min-w-0 flex-1 truncate text-sm font-medium">{current?.label ?? "Admin console"}</span>
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex h-10 items-center gap-2 rounded-control px-3 text-sm font-semibold hover:bg-graphite-2"
          aria-haspopup="dialog"
        >
          <MenuIcon className="h-5 w-5" /> Menu
        </button>
      </div>
      <dialog
        ref={ref}
        aria-label="Menu"
        onCancel={(e) => {
          e.preventDefault();
          setOpen(false);
        }}
        className="fixed inset-x-0 top-auto bottom-0 m-0 max-h-[85dvh] w-full max-w-none overflow-y-auto rounded-t-panel bg-graphite p-0 text-on-graphite open:animate-sheet-in md:hidden"
      >
        {open && (
          <nav aria-label="Console" className="px-3 pt-2 pb-6">
            <div className="flex items-center justify-between py-1">
              <span className="px-2 text-sm font-semibold text-white">Menu</span>
              <button type="button" onClick={() => setOpen(false)} className="inline-flex h-10 w-10 items-center justify-center rounded-control hover:bg-graphite-2" aria-label="Close menu">
                <CloseIcon className="h-5 w-5" />
              </button>
            </div>
            {groups.map((g, gi) => (
              <div key={gi} className="mt-2 border-t border-graphite-3 pt-2">
                {g.label && <p className="px-3 py-1 text-xs font-medium tracking-wide uppercase">{g.label}</p>}
                <ul className="grid grid-cols-2 gap-1">
                  {g.items.map((item) => {
                    const active = isActive(pathname, item);
                    return (
                      <li key={item.href}>
                        <Link
                          href={item.href}
                          onClick={() => setOpen(false)}
                          aria-current={active ? "page" : undefined}
                          className={cx("flex min-h-11 items-center gap-2.5 rounded-control px-3 text-sm font-medium", active ? "bg-graphite-3 text-white" : "hover:bg-graphite-2")}
                        >
                          <item.icon className="h-5 w-5" />
                          <span className="min-w-0 truncate">{item.label}</span>
                          <QueueCount item={item} />
                        </Link>
                      </li>
                    );
                  })}
                </ul>
              </div>
            ))}
          </nav>
        )}
      </dialog>
    </>
  );
}

function Shell({ session, now, children }: { session: AdminSession; now: number; children: ReactNode }) {
  return (
    <div className="min-h-dvh">
      <a href="#main" className="sr-only z-50 bg-surface px-3 py-2 focus:not-sr-only focus:fixed focus:top-10 focus:left-2">
        Skip to content
      </a>
      <ConsoleBar session={session} now={now} />
      <Rail />
      <main id="main" className="min-w-0 px-4 pt-[3.25rem] pb-20 md:ml-16 md:px-6 md:pb-10 xl:ml-[232px]">
        <div className="mx-auto max-w-[1400px]">{children}</div>
      </main>
      <PhoneNav />
    </div>
  );
}

