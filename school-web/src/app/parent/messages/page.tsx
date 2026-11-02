"use client";

import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { MessageIcon } from "@/components/notification-bell";
import {
  ApiErrorView,
  Badge,
  Button,
  EmptyState,
  ErrorBox,
  Modal,
  PageHeader,
  PageLoader,
  RuledSheet,
  Textarea,
  cx,
} from "@/components/ui";
import { api } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { StartThreadRequest, ThreadDto } from "@/lib/types/communication";
import { ChildGate, useChild, useChildPath } from "../_shared";

const POLL_MS = 30_000;
const MAX_BODY = 2000;

export default function ParentMessagesPage() {
  const { child } = useChild();
  return (
    <ChildGate>
      <Inbox key={child?.studentId ?? "none"} />
    </ChildGate>
  );
}

function Inbox() {
  const { child } = useChild();
  const path = useChildPath("/threads");
  const threads = useApi<ThreadDto[]>(path);
  const { reload } = threads;
  const [compose, setCompose] = useState(false);

  useEffect(() => {
    const t = setInterval(reload, POLL_MS);
    window.addEventListener("focus", reload);
    return () => {
      clearInterval(t);
      window.removeEventListener("focus", reload);
    };
  }, [reload]);

  const list = threads.data;

  return (
    <div>
      <PageHeader
        title="Messages"
        subtitle={child ? `About ${child.name}` : undefined}
        action={
          <Button onClick={() => setCompose(true)} disabled={!child}>
            New message
          </Button>
        }
      />
      {threads.error && <ApiErrorView error={threads.error} onRetry={reload} />}
      {!list && !threads.error && <PageLoader />}
      {list && list.length === 0 && (
        <EmptyState
          icon={<MessageIcon />}
          title="No messages yet"
          text="Write to the class teacher or the school office about this child."
          action={<Button onClick={() => setCompose(true)}>New message</Button>}
        />
      )}
      {list && list.length > 0 && (
        <RuledSheet label="Conversations" busy={threads.loading}>
          {list.map((t) => (
            <li key={t.id}>
              <Link href={`/parent/messages/${t.id}`} className="ruled-pad flex items-start gap-3 py-3 hover:bg-slate-50">
                <span className="min-w-0 flex-1">
                  <span className={cx("text-ink", t.unread > 0 ? "font-bold" : "font-medium")}>{threadTo(t)}</span>
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
      )}
      {compose && child && (
        <ComposeModal
          studentId={child.studentId}
          onClose={() => setCompose(false)}
          onStarted={() => {
            setCompose(false);
            reload();
          }}
        />
      )}
    </div>
  );
}

function threadTo(t: ThreadDto): string {
  if (t.isOffice) return "School office";
  return t.staffName ?? "Class teacher";
}

function ComposeModal({ studentId, onClose, onStarted }: { studentId: string; onClose: () => void; onStarted: () => void }) {
  const [to, setTo] = useState<StartThreadRequest["to"]>("class-teacher");
  const [body, setBody] = useState("");
  const send = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await send.run(() =>
      api<ThreadDto>(`/api/parent/children/${studentId}/threads`, { method: "POST", body: { to, body: body.trim() } }),
    );
    if (res.ok) onStarted();
  }

  const toError = send.error?.field("to");
  const bodyError = send.error?.field("body");
  const other = send.error && !toError && !bodyError ? send.error : null;

  return (
    <Modal
      open
      title="New message"
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="parent-compose" loading={send.busy}>
            Send
          </Button>
        </>
      }
    >
      <form id="parent-compose" onSubmit={submit} className="space-y-4">
        <fieldset>
          <legend className="mb-1.5 block text-sm font-medium text-slate-700">To</legend>
          <div className="grid grid-cols-2 gap-2">
            {(
              [
                ["class-teacher", "Class teacher"],
                ["office", "School office"],
              ] as const
            ).map(([value, label]) => (
              <button
                key={value}
                type="button"
                onClick={() => setTo(value)}
                className={cx(
                  "min-h-11 rounded-control border px-3 text-sm font-semibold",
                  to === value ? "border-blazer bg-blazer-wash text-ink" : "border-rule bg-surface text-slate-700",
                )}
              >
                {label}
              </button>
            ))}
          </div>
          {toError && <p className="mt-1.5 text-xs font-medium text-brick">{toError}</p>}
        </fieldset>
        <Textarea
          label="Message"
          value={body}
          onChange={(e) => setBody(e.target.value)}
          maxLength={MAX_BODY}
          rows={5}
          required
          error={bodyError}
        />
        <ErrorBox error={other} />
        {send.error?.code === "no_class_teacher" && (
          <p className="text-sm text-slate-700">This class has no class teacher yet. Message the school office instead.</p>
        )}
      </form>
    </Modal>
  );
}
