"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { api } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { portalHref, usePatientApi, usePortal } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalNoticeDto } from "@/lib/types/portal";
import { BellIcon, badgeLabel } from "./notification-bell";
import { Button, EmptyState, ErrorBox, PageLoader, cx, linkClass } from "./ui";

// The portal's notices (Task 15 carry): the per-patient feed `GET /api/care-portal/patients/{id}/notifications`, scoped by account, clinic
// and patient, never the shared `/api/notifications` feed (which a customer token would see across clinics and products). It holds the
// newest 50; there is no unread-count route, so the badge counts the unread ones in it. Items are marked read one by one through the
// shared `POST /api/me/notifications/{id}/read` (the shared "read all" would also mark other clinics' and products' notices).

const POLL_MS = 60_000;

/** The chosen patient's feed, refreshed every minute and when the window regains focus. */
export function usePortalNotices() {
  const path = usePatientApi("/notifications");
  const feed = useApi<PortalNoticeDto[]>(path);
  const { reload } = feed;
  useEffect(() => {
    if (!path) return;
    const timer = setInterval(reload, POLL_MS);
    window.addEventListener("focus", reload);
    return () => {
      clearInterval(timer);
      window.removeEventListener("focus", reload);
    };
  }, [path, reload]);
  return feed;
}

async function markRead(id: string) {
  try {
    await api(`/api/me/notifications/${id}/read`, { method: "POST" });
  } catch {
    /* opening the notice matters more than the read mark */
  }
}

function useOpen(onRead: (id: string) => void) {
  const router = useRouter();
  return async (n: PortalNoticeDto) => {
    if (!n.readAt) {
      await markRead(n.id);
      onRead(n.id);
    }
    // Notice links are this site's portal pages ("/portal/bills?patient={id}"); anything else is ignored.
    if (n.link && n.link.startsWith("/portal")) router.push(n.link);
  };
}

function Item({ n, onOpen, compact }: { n: PortalNoticeDto; onOpen: () => void; compact?: boolean }) {
  const unread = !n.readAt;
  return (
    <button type="button" onClick={onOpen} className={cx("flex w-full items-start gap-3 text-left hover:bg-slate-50", compact ? "px-4 py-3" : "case-pad py-3")}>
      <span aria-hidden="true" className={cx("mt-1.5 h-2 w-2 shrink-0 rounded-full", unread ? "bg-scrub" : "bg-transparent")} />
      <span className="min-w-0 flex-1">
        <span className={cx("block text-sm text-ink", unread ? "font-bold" : "font-medium")}>
          {unread && <span className="sr-only">Unread: </span>}
          {n.title}
        </span>
        {n.body && <span className={cx("mt-0.5 block text-sm text-slate-700", compact && "line-clamp-2")}>{n.body}</span>}
        <span className="tnum mt-1 block text-xs text-muted">{formatStamp(n.createdAt)}</span>
      </span>
    </button>
  );
}

function markLocal(list: PortalNoticeDto[] | undefined, ids: string[]): PortalNoticeDto[] {
  const now = new Date().toISOString();
  return (list ?? []).map((n) => (ids.includes(n.id) && !n.readAt ? { ...n, readAt: now } : n));
}

/** The portal bell: the chosen patient's unread count and the latest ten. */
export function PortalNotificationBell({ className }: { className?: string }) {
  const { patient } = usePortal();
  const feed = usePortalNotices();
  const open = useOpen((id) => feed.setData((l) => markLocal(l, [id])));
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null);
  const [marking, setMarking] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const titleId = useId();
  const items = feed.data ?? [];
  const unread = items.filter((n) => !n.readAt);
  const count = unread.length;

  useEffect(() => {
    if (!pos) return;
    const onPointer = (e: PointerEvent) => {
      const t = e.target as Node;
      if (panel.current?.contains(t) || button.current?.contains(t)) return;
      setPos(null);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setPos(null);
      button.current?.focus();
    };
    const onResize = () => setPos(null);
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    window.addEventListener("resize", onResize);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("resize", onResize);
    };
  }, [pos]);

  function toggle() {
    if (pos) return setPos(null);
    const r = button.current?.getBoundingClientRect();
    if (!r) return;
    const width = Math.min(360, window.innerWidth - 16);
    setPos({ top: r.bottom + 8, left: Math.max(8, Math.min(r.right - width, window.innerWidth - width - 8)), width });
    feed.reload();
  }

  async function readAll() {
    const ids = unread.map((n) => n.id);
    setMarking(true);
    await Promise.all(ids.map(markRead));
    feed.setData((l) => markLocal(l, ids));
    setMarking(false);
  }

  if (!patient) return null;
  const label = count > 0 ? `Notifications, ${count > 9 ? "more than 9" : count} unread` : "Notifications";

  return (
    <>
      <button
        ref={button}
        type="button"
        onClick={toggle}
        aria-label={label}
        aria-expanded={!!pos}
        aria-controls={pos ? panelId : undefined}
        aria-haspopup="dialog"
        className={cx(
          "relative flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-slate-700 hover:bg-ink/[0.06] hover:text-ink",
          pos && "bg-ink/[0.06] text-ink",
          className,
        )}
      >
        <BellIcon className="h-5 w-5" />
        {count > 0 && (
          <span
            aria-hidden="true"
            className="tnum absolute top-1 right-1 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-arterial px-1 text-[11px] leading-none font-bold text-white ring-2 ring-surface"
          >
            {badgeLabel(count)}
          </span>
        )}
      </button>
      {pos &&
        createPortal(
          <div
            ref={panel}
            id={panelId}
            role="dialog"
            aria-labelledby={titleId}
            className="fixed z-50 flex max-h-[min(560px,calc(100vh-6rem))] flex-col overflow-hidden rounded-panel border border-rule bg-surface text-ink shadow-float"
            style={{ top: pos.top, left: pos.left, width: pos.width }}
          >
            <div className="flex items-center justify-between gap-3 border-b border-rule-soft px-4 py-2">
              <h2 id={titleId} className="display text-lg font-bold text-ink">
                Notifications
              </h2>
              <Button variant="ghost" className="-mr-2 px-2 text-sm" onClick={readAll} loading={marking} disabled={count === 0}>
                Mark all as read
              </Button>
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto">
              {feed.error ? (
                <div className="p-4">
                  <ErrorBox error={feed.error} onRetry={feed.reload} />
                </div>
              ) : !feed.data ? (
                <p className="px-4 py-6 text-sm text-muted" role="status">
                  Loading…
                </p>
              ) : items.length === 0 ? (
                <p className="px-4 py-6 text-sm text-muted">Nothing yet. Bookings, results and bills will show here.</p>
              ) : (
                <ul className="divide-y divide-rule-soft">
                  {items.slice(0, 10).map((n) => (
                    <li key={n.id}>
                      <Item
                        n={n}
                        compact
                        onOpen={() => {
                          setPos(null);
                          void open(n);
                        }}
                      />
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <div className="border-t border-rule-soft px-4 py-2.5">
              <Link
                href={portalHref("/portal/notifications", patient.id)}
                onClick={() => setPos(null)}
                className={cx(linkClass, "inline-flex min-h-9 items-center text-sm")}
              >
                See all notifications
              </Link>
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}

/** The full portal notices page for the chosen patient (the newest 50). */
export function PortalNotificationFeed() {
  const feed = usePortalNotices();
  const open = useOpen((id) => feed.setData((l) => markLocal(l, [id])));
  const [marking, setMarking] = useState(false);
  const items = feed.data ?? [];
  const unread = items.filter((n) => !n.readAt);

  async function readAll() {
    const ids = unread.map((n) => n.id);
    setMarking(true);
    await Promise.all(ids.map(markRead));
    feed.setData((l) => markLocal(l, ids));
    setMarking(false);
  }

  if (feed.error && !feed.data) return <ErrorBox error={feed.error} onRetry={feed.reload} />;
  if (!feed.data) return <PageLoader />;
  return (
    <div className="space-y-4">
      {items.length > 0 && (
        <div className="flex justify-end">
          <Button variant="secondary" loading={marking} disabled={unread.length === 0} onClick={readAll}>
            Mark all as read
          </Button>
        </div>
      )}
      {items.length === 0 ? (
        <EmptyState icon={<BellIcon />} title="No notifications yet" text="Bookings, reminders, results and bills will show here." />
      ) : (
        <div className="case-sheet">
          <ul className="case-rows" aria-label="Notifications">
            {items.map((n) => (
              <li key={n.id}>
                <Item n={n} onOpen={() => void open(n)} />
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
