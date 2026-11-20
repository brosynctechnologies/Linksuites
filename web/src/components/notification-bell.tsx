"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState, useSyncExternalStore } from "react";
import { createPortal } from "react-dom";
import { api, toApiError, type ApiError } from "@/lib/api";
import { getAuth, useAuth } from "@/lib/auth";
import type { Paged, UserNotificationDto } from "@/lib/types";
import { Button, ErrorBox, Spinner, cx, formatDate } from "./ui";
import { BellIcon } from "./icons";

// The in-app notifications feed (`/api/me/notifications`): low stock, new team members and the like.
// A support view reads it but never marks anything read.

const POLL_MS = 60_000;
const DROPDOWN_SIZE = 10;

// ---------- Shared unread count (one poll however many bells are mounted) ----------

let unread = 0;
const listeners = new Set<() => void>();
let timer: ReturnType<typeof setInterval> | null = null;
let inflight: Promise<void> | null = null;

function setUnread(n: number) {
  if (n === unread) return;
  unread = n;
  listeners.forEach((l) => l());
}

/** Fetch the unread count now. Failures keep the last count: the bell is never the place to show a network error. */
function refreshUnread(): Promise<void> {
  if (typeof window === "undefined" || !getAuth()) return Promise.resolve();
  inflight ??= api<{ count: number }>("/api/me/notifications/unread-count")
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
  void refreshUnread();
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  if (listeners.size === 1) {
    void refreshUnread();
    timer = setInterval(() => void refreshUnread(), POLL_MS);
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

function useUnreadCount(): number {
  return useSyncExternalStore(
    subscribe,
    () => unread,
    () => 0,
  );
}

/** Notification links are written for the suite's `/app/...` paths; OrderLink's dashboard lives at `/dashboard/...`. */
export function dashboardLink(link: string): string {
  const path = link.startsWith("/") ? link : `/${link}`;
  if (path === "/app") return "/dashboard";
  return path.startsWith("/app/") || path.startsWith("/app?") ? `/dashboard${path.slice(4)}` : path;
}

interface Pos {
  top: number;
  left: number;
  width: number;
  maxHeight: number;
}

/**
 * The bell: an unread badge (9+ above nine) and a dropdown of the latest ten with "Mark all as read".
 * `tone="dark"` sits on the indigo rail; `"light"` on the white mobile header.
 */
export function NotificationBell({ tone = "dark", align = "end" }: { tone?: "dark" | "light"; align?: "start" | "end" }) {
  const router = useRouter();
  const count = useUnreadCount();
  const support = !!useAuth()?.support;
  const [pos, setPos] = useState<Pos | null>(null);
  const [list, setList] = useState<UserNotificationDto[] | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [markingAll, setMarkingAll] = useState(false);
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
    api<Paged<UserNotificationDto>>(`/api/me/notifications?page=1&pageSize=${DROPDOWN_SIZE}`, { signal: ctrl.signal }).then(
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
    void refreshUnread();
  }

  useEffect(() => {
    if (!isOpen) return;
    const dismiss = () => {
      loadCtrl.current?.abort();
      setPos(null);
    };
    const onPointer = (e: PointerEvent) => {
      const t = e.target as Node;
      if (panel.current?.contains(t) || button.current?.contains(t)) return;
      dismiss();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      dismiss();
      button.current?.focus();
    };
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    window.addEventListener("resize", dismiss);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("resize", dismiss);
    };
  }, [isOpen]);

  useEffect(() => () => loadCtrl.current?.abort(), []);

  async function openItem(n: UserNotificationDto) {
    close();
    if (!n.readAt && !support) {
      try {
        await api(`/api/me/notifications/${n.id}/read`, { method: "POST" });
      } catch {
        /* opening the link matters more than the read mark */
      }
      void refreshUnread();
    }
    if (!n.link) return;
    if (/^https?:\/\//i.test(n.link)) window.location.assign(n.link);
    else router.push(dashboardLink(n.link));
  }

  async function readAll() {
    setMarkingAll(true);
    setError(null);
    try {
      await api("/api/me/notifications/read-all", { method: "POST" });
      const now = new Date().toISOString();
      setList((l) => l?.map((n) => (n.readAt ? n : { ...n, readAt: now })) ?? l);
      setUnread(0);
      void refreshUnread();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setMarkingAll(false);
    }
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
          tone === "dark" ? "text-white/80 hover:bg-white/10 hover:text-white" : "text-ink-2 hover:bg-ink-soft hover:text-ink",
          isOpen && (tone === "dark" ? "bg-white/12 text-white" : "bg-ink-soft text-ink"),
        )}
      >
        <BellIcon />
        {count > 0 && (
          <span
            aria-hidden="true"
            className={cx(
              "absolute top-1 right-1 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-marigold px-1 text-[11px] leading-none font-bold text-ink num",
              tone === "dark" ? "ring-2 ring-ink" : "ring-2 ring-surface",
            )}
          >
            {count > 9 ? "9+" : count}
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
            className="fixed z-50 flex flex-col overflow-hidden rounded-panel border border-line bg-surface text-ink shadow-lift"
            style={{ top: pos.top, left: pos.left, width: pos.width, maxHeight: Math.min(pos.maxHeight, 560) }}
          >
            <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2">
              <h2 id={titleId} className="font-display text-lg font-bold text-ink">
                Notifications
              </h2>
              {!support && (
                <Button variant="ghost" className="-mr-2 px-2" onClick={readAll} loading={markingAll} disabled={!hasUnread}>
                  Mark all as read
                </Button>
              )}
            </div>
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
                <p className="px-4 py-6 text-sm text-muted">Nothing yet. Low stock and team news show here.</p>
              ) : (
                <ul className="divide-y divide-line">
                  {list.map((n) => {
                    const isUnread = !n.readAt;
                    return (
                      <li key={n.id}>
                        <button
                          type="button"
                          onClick={() => void openItem(n)}
                          className="flex min-h-11 w-full items-start gap-3 px-4 py-3 text-left hover:bg-paper focus-visible:-outline-offset-3"
                        >
                          <span aria-hidden="true" className={cx("mt-1.5 h-2 w-2 shrink-0 rounded-full", isUnread ? "bg-marigold" : "bg-transparent")} />
                          <span className="min-w-0 flex-1">
                            <span className={cx("block text-sm text-ink", isUnread ? "font-bold" : "font-medium")}>
                              {isUnread && <span className="sr-only">Unread: </span>}
                              {n.title}
                            </span>
                            {n.body && <span className="mt-0.5 line-clamp-2 block text-sm text-ink-2">{n.body}</span>}
                            <span className="mt-1 block text-xs text-subtle num">{formatDate(n.createdAt)}</span>
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}
