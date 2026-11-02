"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState, useSyncExternalStore, type SVGProps } from "react";
import { createPortal } from "react-dom";
import { api, toApiError, type ApiError } from "@/lib/api";
import { getAuth, useAuth } from "@/lib/auth";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { NotificationPage, UnreadCountDto, UserNotificationDto } from "@/lib/types";
import { Button, EmptyState, ErrorBox, PageLoader, Spinner, Tabs, cx, linkClass } from "./ui";

// The in-app notifications feed (`/api/me/notifications`). It works with whichever token is stored: a staff token sees this clinic's
// notifications. (The patient portal reads its own per-patient feed, not this one.) A support view reads but never marks anything read.

const POLL_MS = 60_000;
/** The dropdown shows the latest ten; the API pages by twenty. */
const DROPDOWN_SIZE = 10;

// ---------- Icons (kept here so the bell has no dependency on another area's files) ----------

type P = SVGProps<SVGSVGElement>;
function Svg({ children, ...props }: P) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      width={20}
      height={20}
      aria-hidden="true"
      {...props}
    >
      {children}
    </svg>
  );
}
export const BellIcon = (p: P) => (
  <Svg {...p}>
    <path d="M6 9a6 6 0 1 1 12 0c0 5 2 6.5 2 6.5H4S6 14 6 9z" />
    <path d="M10 19.5a2 2 0 0 0 4 0" />
  </Svg>
);

// ---------- Shared unread count (one poll however many bells are mounted) ----------

let unread = 0;
const listeners = new Set<() => void>();
let timer: ReturnType<typeof setInterval> | null = null;
let inflight: Promise<void> | null = null;

function emit() {
  listeners.forEach((l) => l());
}

function setUnread(n: number) {
  if (n === unread) return;
  unread = n;
  emit();
}

/** Fetch the unread count now. Failures keep the last count: the bell is never the place to show a network error. */
export function refreshNotifications(): Promise<void> {
  if (typeof window === "undefined" || !getAuth()) return Promise.resolve();
  inflight ??= api<UnreadCountDto>("/api/me/notifications/unread-count")
    .then(
      (r) => setUnread(r?.count ?? 0),
      () => {},
    )
    .finally(() => {
      inflight = null;
    });
  return inflight;
}

function onFocus() {
  void refreshNotifications();
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  if (listeners.size === 1) {
    unread = 0;
    void refreshNotifications();
    timer = setInterval(() => void refreshNotifications(), POLL_MS);
    window.addEventListener("focus", onFocus);
  }
  return () => {
    listeners.delete(cb);
    if (listeners.size === 0) {
      if (timer) clearInterval(timer);
      timer = null;
      window.removeEventListener("focus", onFocus);
    }
  };
}

/** The signed-in user's unread count, refreshed every 60 seconds and when the window regains focus. */
export function useUnreadCount(): number {
  return useSyncExternalStore(
    subscribe,
    () => unread,
    () => 0,
  );
}

/** "9+" above nine. */
export function badgeLabel(count: number): string {
  return count > 9 ? "9+" : String(count);
}

// ---------- Actions ----------

async function markRead(n: UserNotificationDto) {
  if (n.readAt || getAuth()?.support) return;
  try {
    await api(`/api/me/notifications/${n.id}/read`, { method: "POST" });
  } catch {
    /* opening the link matters more than the read mark */
  }
  void refreshNotifications();
}

function useOpenNotification() {
  const router = useRouter();
  return async (n: UserNotificationDto, onRead?: (id: string) => void) => {
    const wasUnread = !n.readAt;
    await markRead(n);
    if (wasUnread) onRead?.(n.id);
    if (!n.link) return;
    if (/^https?:\/\//i.test(n.link)) window.location.assign(n.link);
    else router.push(n.link.startsWith("/") ? n.link : `/${n.link}`);
  };
}

// ---------- Pieces ----------

function NotificationItem({ n, onOpen, compact }: { n: UserNotificationDto; onOpen: () => void; compact?: boolean }) {
  const isUnread = !n.readAt;
  return (
    <button
      type="button"
      onClick={onOpen}
      className={cx("flex w-full items-start gap-3 text-left hover:bg-slate-50", compact ? "px-4 py-3" : "case-pad py-3")}
    >
      <span aria-hidden="true" className={cx("mt-1.5 h-2 w-2 shrink-0 rounded-full", isUnread ? "bg-scrub" : "bg-transparent")} />
      <span className="min-w-0 flex-1">
        <span className={cx("block text-sm text-ink", isUnread ? "font-bold" : "font-medium")}>
          {isUnread && <span className="sr-only">Unread: </span>}
          {n.title}
        </span>
        {n.body && <span className={cx("mt-0.5 block text-sm text-slate-700", compact && "line-clamp-2")}>{n.body}</span>}
        <span className="tnum mt-1 block text-xs text-muted">{formatStamp(n.createdAt)}</span>
      </span>
    </button>
  );
}

interface Pos {
  top: number;
  left: number;
  width: number;
  maxHeight: number;
}

/**
 * The bell: an unread badge (capped at 9+), a dropdown of the latest ten with "Mark all as read", and a link to the full page.
 * `href` is the full notifications page (staff `/app/notifications`).
 * `tone="dark"` sits on the Scrub deep bar and rail; `"light"` on a white or Gauze header.
 */
export function NotificationBell({
  href,
  tone = "dark",
  align = "end",
  className,
}: {
  href: string;
  tone?: "dark" | "light";
  /** Which edge of the button the dropdown lines up with. */
  align?: "start" | "end";
  className?: string;
}) {
  const count = useUnreadCount();
  const support = !!useAuth()?.support;
  const open = useOpenNotification();
  const [pos, setPos] = useState<Pos | null>(null);
  const [list, setList] = useState<UserNotificationDto[] | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const markAll = useAction();
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const loadCtrl = useRef<AbortController | null>(null);
  const panelId = useId();
  const titleId = useId();
  const isOpen = pos !== null;

  function place(): Pos | null {
    const el = button.current;
    if (!el) return null;
    const r = el.getBoundingClientRect();
    const width = Math.min(360, window.innerWidth - 16);
    const left = Math.max(8, Math.min(align === "end" ? r.right - width : r.left, window.innerWidth - width - 8));
    const top = r.bottom + 8;
    return { top, left, width, maxHeight: Math.max(240, window.innerHeight - top - 16) };
  }

  function load() {
    loadCtrl.current?.abort();
    const ctrl = new AbortController();
    loadCtrl.current = ctrl;
    setError(null);
    api<NotificationPage>(`/api/me/notifications${qs({ page: 1 })}`, { signal: ctrl.signal }).then(
      (p) => setList((p?.items ?? []).slice(0, DROPDOWN_SIZE)),
      (e) => {
        if (!ctrl.signal.aborted) setError(toApiError(e));
      },
    );
  }

  function close(returnFocus = false) {
    loadCtrl.current?.abort();
    setPos(null);
    if (returnFocus) button.current?.focus();
  }

  function toggle() {
    if (isOpen) return close();
    setPos(place());
    load();
    void refreshNotifications();
  }

  useEffect(() => {
    if (!isOpen) return;
    const onPointer = (e: PointerEvent) => {
      const t = e.target as Node;
      if (panel.current?.contains(t) || button.current?.contains(t)) return;
      loadCtrl.current?.abort();
      setPos(null);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      loadCtrl.current?.abort();
      setPos(null);
      button.current?.focus();
    };
    const onResize = () => {
      loadCtrl.current?.abort();
      setPos(null);
    };
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    window.addEventListener("resize", onResize);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("resize", onResize);
    };
  }, [isOpen]);

  useEffect(() => () => loadCtrl.current?.abort(), []);

  async function readAll() {
    const res = await markAll.run(() => api("/api/me/notifications/read-all", { method: "POST" }));
    if (!res.ok) return;
    const now = new Date().toISOString();
    setList((l) => l?.map((n) => (n.readAt ? n : { ...n, readAt: now })) ?? l);
    setUnread(0);
    void refreshNotifications();
  }

  const label = count > 0 ? `Notifications, ${count > 9 ? "more than 9" : count} unread` : "Notifications";
  const hasUnread = count > 0 || !!list?.some((n) => !n.readAt);

  return (
    <>
      <button
        ref={button}
        type="button"
        onClick={toggle}
        aria-label={label}
        aria-expanded={isOpen}
        aria-controls={isOpen ? panelId : undefined}
        aria-haspopup="dialog"
        className={cx(
          "relative flex h-11 w-11 shrink-0 items-center justify-center rounded-control",
          tone === "dark" ? "text-on-scrub hover:bg-scrub-deep-2 hover:text-white" : "text-slate-700 hover:bg-ink/[0.06] hover:text-ink",
          isOpen && (tone === "dark" ? "bg-scrub-deep-2 text-white" : "bg-ink/[0.06] text-ink"),
          className,
        )}
      >
        <BellIcon className="h-5 w-5" />
        {count > 0 && (
          <span
            aria-hidden="true"
            className={cx(
              "tnum absolute top-1 right-1 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-arterial px-1 text-[11px] leading-none font-bold text-white",
              tone === "dark" ? "ring-2 ring-scrub-deep" : "ring-2 ring-surface",
            )}
          >
            {badgeLabel(count)}
          </span>
        )}
      </button>

      {isOpen &&
        pos &&
        createPortal(
          <div
            ref={panel}
            id={panelId}
            role="dialog"
            aria-labelledby={titleId}
            className="fixed z-50 flex flex-col overflow-hidden rounded-panel border border-rule bg-surface text-ink shadow-float"
            style={{ top: pos.top, left: pos.left, width: pos.width, maxHeight: Math.min(pos.maxHeight, 560) }}
          >
            <div className="flex items-center justify-between gap-3 border-b border-rule-soft px-4 py-2">
              <h2 id={titleId} className="display text-lg font-bold text-ink">
                Notifications
              </h2>
              {!support && (
                <Button variant="ghost" className="-mr-2 px-2 text-sm" onClick={readAll} loading={markAll.busy} disabled={!hasUnread}>
                  Mark all as read
                </Button>
              )}
            </div>
            {markAll.error && (
              <div className="px-4 pt-3">
                <ErrorBox error={markAll.error} />
              </div>
            )}
            <div className="min-h-0 flex-1 overflow-y-auto">
              {error ? (
                <div className="p-4">
                  <ErrorBox error={error} onRetry={load} />
                </div>
              ) : !list ? (
                <div className="flex items-center gap-2 px-4 py-6 text-sm text-muted" role="status">
                  <Spinner className="h-4 w-4" /> Loading…
                </div>
              ) : list.length === 0 ? (
                <p className="px-4 py-6 text-sm text-muted">Nothing yet. Patients waiting, alerts and imports will show here.</p>
              ) : (
                <ul className="divide-y divide-rule-soft">
                  {list.map((n) => (
                    <li key={n.id}>
                      <NotificationItem
                        n={n}
                        compact
                        onOpen={() => {
                          close();
                          void open(n);
                        }}
                      />
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <div className="border-t border-rule-soft px-4 py-2.5">
              <Link href={href} onClick={() => close()} className={cx(linkClass, "inline-flex min-h-9 items-center text-sm")}>
                See all notifications
              </Link>
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}

// ---------- Full page ----------

type Filter = "all" | "unread";

/**
 * The full notifications list, twenty a page, newest first, with an All / Unread filter and "Mark all as read".
 * Used by the staff page `/app/notifications`; staff only.
 */
export function NotificationFeed() {
  const support = !!useAuth()?.support;
  const [filter, setFilter] = useState<Filter>("all");
  const [page, setPage] = useState(1);
  const feed = useApi<NotificationPage>(`/api/me/notifications${qs({ unread: filter === "unread" ? true : undefined, page })}`);
  const open = useOpenNotification();
  const markAll = useAction();
  const count = useUnreadCount();

  const data = feed.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  function markLocal(id: string) {
    const now = new Date().toISOString();
    feed.setData((d) => (d ? { ...d, items: d.items.map((n) => (n.id === id ? { ...n, readAt: now } : n)) } : (d as unknown as NotificationPage)));
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <Tabs
          label="Show"
          value={filter}
          onChange={(v) => {
            setFilter(v);
            setPage(1);
          }}
          items={[
            { value: "all", label: "All" },
            { value: "unread", label: count > 0 ? `Unread (${count > 9 ? "9+" : count})` : "Unread" },
          ]}
          className="flex-1"
        />
        {!support && (
          <Button
            variant="secondary"
            loading={markAll.busy}
            disabled={count === 0 && !data?.items.some((n) => !n.readAt)}
            onClick={async () => {
              if ((await markAll.run(() => api("/api/me/notifications/read-all", { method: "POST" }))).ok) {
                setUnread(0);
                void refreshNotifications();
                feed.reload();
              }
            }}
          >
            Mark all as read
          </Button>
        )}
      </div>
      <ErrorBox error={markAll.error} />
      {feed.error && <ErrorBox error={feed.error} onRetry={feed.reload} />}
      {!data && !feed.error && <PageLoader />}
      {data && data.items.length === 0 && (
        <EmptyState
          icon={<BellIcon />}
          title={filter === "unread" ? "You're all caught up" : "No notifications yet"}
          text={
            filter === "unread"
              ? "Nothing unread. New alerts will show here."
              : "Patients waiting, alerts and finished imports will show here as they happen."
          }
        />
      )}
      {data && data.items.length > 0 && (
        <div className={cx("case-sheet", feed.loading && "opacity-60 transition-opacity")}>
          <ul className="case-rows" aria-label="Notifications">
            {data.items.map((n) => (
              <li key={n.id}>
                <NotificationItem n={n} onOpen={() => void open(n, markLocal)} />
              </li>
            ))}
          </ul>
        </div>
      )}
      {data && pages > 1 && (
        <div className="flex items-center justify-between gap-3">
          <Button variant="secondary" disabled={page <= 1 || feed.loading} onClick={() => setPage((p) => Math.max(1, p - 1))}>
            Newer
          </Button>
          <span className="tnum text-sm text-muted">
            Page {page} of {pages}
          </span>
          <Button variant="secondary" disabled={page >= pages || feed.loading} onClick={() => setPage((p) => p + 1)}>
            Older
          </Button>
        </div>
      )}
      {support && <p className="text-xs text-muted">This is a read-only support view, so nothing here is marked as read.</p>}
    </div>
  );
}
