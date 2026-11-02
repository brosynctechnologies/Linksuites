"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { MessageIcon } from "@/components/notification-bell";
import { ApiErrorView, Badge, EmptyState, PageHeader, PageLoader, RuledSheet, Tabs, cx } from "@/components/ui";
import { formatStamp } from "@/lib/format";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ThreadDto } from "@/lib/types/communication";
import { POLL_MS, threadTo } from "./messages-shared";

/** The API returns at most this many threads, latest first. */
const LIST_CAP = 200;

type View = "all" | "mine";

/**
 * The staff inbox. Leadership sees every thread ("Office and mine" narrows it to the office's and their own); the bursar the office's;
 * a teacher the threads sent to them ("Sent to me") or also those of the classes they are class teacher of ("My classes").
 */
export default function MessagesPage() {
  const role = useRole();
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const isBursar = role === "Accountant";
  const isLeadership = role === "Owner" || role === "Admin";
  const view: View = !isBursar && params.get("mine") === "true" ? "mine" : "all";
  const threads = useApi<ThreadDto[]>(`/api/school/threads${qs({ mine: view === "mine" ? true : undefined })}`);
  const { reload } = threads;

  useEffect(() => {
    const t = setInterval(reload, POLL_MS);
    window.addEventListener("focus", reload);
    return () => {
      clearInterval(t);
      window.removeEventListener("focus", reload);
    };
  }, [reload]);

  function setView(v: View) {
    router.replace(`${pathname}${qs({ mine: v === "mine" ? true : undefined })}`);
  }

  const list = threads.data;
  const subtitle = isBursar
    ? "Messages parents send to the school office."
    : isLeadership
      ? "Every conversation between parents and the school."
      : "Messages from the parents of your classes.";

  return (
    <div>
      <PageHeader title="Messages" subtitle={subtitle} />
      {!isBursar && (
        <Tabs
          label="Show"
          value={view}
          onChange={setView}
          className="mb-4"
          items={
            isLeadership
              ? [
                  { value: "all", label: "All conversations" },
                  { value: "mine", label: "Office and mine" },
                ]
              : [
                  { value: "all", label: "My classes" },
                  { value: "mine", label: "Sent to me" },
                ]
          }
        />
      )}
      {threads.error && <ApiErrorView error={threads.error} onRetry={reload} />}
      {!list && !threads.error && <PageLoader />}
      {list && list.length === 0 && (
        <EmptyState
          icon={<MessageIcon />}
          title="No messages yet"
          text={
            view === "mine"
              ? "When a parent writes to you, the conversation shows here."
              : "Parents start a conversation from their portal. It shows here when they do."
          }
        />
      )}
      {list && list.length > 0 && (
        <>
          <RuledSheet label="Conversations" busy={threads.loading}>
            {list.map((t) => (
              <li key={t.id}>
                <Link href={`/app/messages/${t.id}`} className="ruled-pad flex items-start gap-3 py-3 hover:bg-slate-50">
                  <span className="min-w-0 flex-1">
                    <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                      <span className={cx("text-ink", t.unread > 0 ? "font-bold" : "font-medium")}>{t.guardianName}</span>
                      <span className="text-sm text-muted">about {t.studentName}</span>
                    </span>
                    <span className="mt-0.5 block text-xs text-muted">To {threadTo(t)}</span>
                    <span className={cx("mt-1 block truncate text-sm", t.unread > 0 ? "text-ink" : "text-slate-700")}>
                      {t.lastMessage ? t.lastMessage : t.lastMessage === "" ? "Sent an image." : "No messages yet."}
                    </span>
                  </span>
                  <span className="flex shrink-0 flex-col items-end gap-1">
                    <span className="tnum text-xs text-muted">{formatStamp(t.lastMessageAt)}</span>
                    {t.unread > 0 && <Badge tone="blazer">{t.unread > 9 ? "9+ new" : `${t.unread} new`}</Badge>}
                  </span>
                </Link>
              </li>
            ))}
          </RuledSheet>
          {list.length >= LIST_CAP && (
            <p className="mt-3 text-xs text-muted">Showing the {LIST_CAP} most recent conversations.</p>
          )}
        </>
      )}
    </div>
  );
}
