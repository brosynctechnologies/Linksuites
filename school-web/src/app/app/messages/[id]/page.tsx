"use client";

import { useParams } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, upload } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { MessageDto, ThreadDto } from "@/lib/types/communication";
import { ApiErrorView, Button, ErrorBox, Notice, PageHeader, PageLoader, Textarea, cx } from "@/components/ui";
import { CloseIcon, UploadIcon } from "@/components/icons";
import { refreshNotifications } from "@/components/notification-bell";
import { POLL_MS, threadTo } from "../messages-shared";

/** MessageService.MaxBodyLength. */
const MAX_BODY = 2000;
/** ImageUploads.MaxBytes. */
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const IMAGE_TYPES = "image/jpeg,image/png,image/webp";

export default function ThreadPage() {
  const { id } = useParams<{ id: string }>();
  const support = !!useAuth()?.support;
  // There is no single-thread route, so the header comes from the inbox (which includes every thread this member can open).
  const threads = useApi<ThreadDto[]>("/api/school/threads");
  const messages = useApi<MessageDto[]>(`/api/school/threads/${id}/messages`);
  const { reload, setData } = messages;
  const thread = threads.data?.find((t) => t.id === id);
  const end = useRef<HTMLDivElement>(null);
  const count = messages.data?.length ?? 0;

  useEffect(() => {
    const t = setInterval(reload, POLL_MS);
    window.addEventListener("focus", reload);
    return () => {
      clearInterval(t);
      window.removeEventListener("focus", reload);
    };
  }, [reload]);

  useEffect(() => {
    if (count > 0) end.current?.scrollIntoView({ block: "end" });
  }, [count]);

  // Loading the thread marks its bell notifications read on the server; update the bell's count to match.
  const loaded = !!messages.data;
  useEffect(() => {
    if (loaded) void refreshNotifications();
  }, [loaded, count]);

  const title = thread ? `${thread.guardianName} about ${thread.studentName}` : "Conversation";
  const subtitle = thread ? `To ${threadTo(thread)}` : undefined;

  if (!messages.data) {
    return (
      <div>
        <PageHeader title={title} subtitle={subtitle} back="/app/messages" />
        {messages.error ? <ApiErrorView error={messages.error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const list = messages.data;
  // Opening the thread marks the parent's messages read only for the person they were sent to.
  const leftUnread = !support && list.some((m) => m.fromParent && !m.readAt);

  return (
    <div className="space-y-4">
      <PageHeader title={title} subtitle={subtitle} back="/app/messages" />
      {leftUnread && (
        <Notice>
          {thread && !thread.isOffice && thread.staffName
            ? `This conversation was sent to ${thread.staffName}, so opening it doesn't mark it read for them.`
            : "This conversation was sent to someone else, so opening it doesn't mark it read for them."}
        </Notice>
      )}
      {messages.error && <ErrorBox error={messages.error} onRetry={reload} />}

      <ol className="space-y-3" aria-label="Messages">
        {list.map((m) => (
          <li key={m.id} className={cx("flex", m.fromParent ? "justify-start" : "justify-end")}>
            <div
              className={cx(
                "max-w-[85%] rounded-panel border px-3.5 py-2.5 sm:max-w-[70%]",
                m.fromParent ? "border-rule bg-surface" : "border-blazer-rule bg-blazer-wash",
              )}
            >
              <p className="text-xs font-bold text-ink-2">{m.senderName || (m.fromParent ? "Parent" : "School")}</p>
              {m.body && <p className="mt-1 text-sm break-words whitespace-pre-line text-ink">{m.body}</p>}
              {m.imageUrl && (
                <a href={m.imageUrl} target="_blank" rel="noopener noreferrer" className="mt-2 block">
                  <img src={m.imageUrl} alt={`Image from ${m.senderName || "the sender"}`} className="max-h-72 rounded-control border border-rule-soft object-contain" />
                </a>
              )}
              <p className="tnum mt-1.5 text-xs text-muted">
                {formatStamp(m.sentAt)}
                {!m.fromParent && <span> · {m.readAt ? "Read" : "Sent"}</span>}
              </p>
            </div>
          </li>
        ))}
      </ol>
      <div ref={end} />

      {support ? (
        <p className="text-xs text-muted">This is a read-only support view, so you can&apos;t reply here.</p>
      ) : (
        <ReplyForm threadId={id} onSent={(m) => setData((d) => [...(d ?? []), m])} />
      )}
    </div>
  );
}

function ReplyForm({ threadId, onSent }: { threadId: string; onSent: (m: MessageDto) => void }) {
  const [body, setBody] = useState("");
  const [image, setImage] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const send = useAction();

  function pick(file: File | undefined) {
    setFileError(null);
    if (!file) return;
    if (file.size > MAX_IMAGE_BYTES) {
      setFileError("Images must be 5 MB or smaller. Choose a smaller one.");
      return;
    }
    setImage(file);
  }

  function clearImage() {
    setImage(null);
    if (fileInput.current) fileInput.current.value = "";
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const text = body.trim();
    if (!text && !image) return;
    const res = await send.run(() =>
      image
        ? upload<MessageDto>(`/api/school/threads/${threadId}/messages/image`, image, text ? { body: text } : {})
        : api<MessageDto>(`/api/school/threads/${threadId}/messages`, { method: "POST", body: { body: text } }),
    );
    if (!res.ok) return;
    onSent(res.data);
    setBody("");
    clearImage();
  }

  const bodyError = send.error?.field("body");
  const imageError = fileError ?? send.error?.field("file");
  const otherError = send.error && !bodyError && !send.error.field("file") ? send.error : null;

  return (
    <form onSubmit={submit} className="space-y-3 rounded-panel border border-rule bg-surface p-4">
      <Textarea
        label="Reply"
        value={body}
        onChange={(e) => setBody(e.target.value)}
        maxLength={MAX_BODY}
        rows={3}
        error={bodyError}
        hint={body.length > MAX_BODY - 200 ? `${MAX_BODY - body.length} characters left` : "The parent gets your reply in the app and by email."}
      />
      <div className="flex flex-wrap items-center gap-2">
        <input
          ref={fileInput}
          id={`reply-image-${threadId}`}
          type="file"
          accept={IMAGE_TYPES}
          className="sr-only"
          onChange={(e) => pick(e.target.files?.[0])}
        />
        {image ? (
          <span className="inline-flex min-h-11 items-center gap-2 rounded-control border border-rule px-3 text-sm text-ink">
            <span className="max-w-[14rem] truncate">{image.name}</span>
            <button
              type="button"
              onClick={clearImage}
              className="-mr-1 flex h-8 w-8 items-center justify-center rounded-control text-muted hover:bg-ink/[0.06] hover:text-ink"
              aria-label="Remove the image"
            >
              <CloseIcon className="h-4 w-4" />
            </button>
          </span>
        ) : (
          <label htmlFor={`reply-image-${threadId}`} className="inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-control border border-rule px-4 text-sm font-semibold text-ink hover:bg-slate-50">
            <UploadIcon className="h-4 w-4" /> Add an image
          </label>
        )}
        <Button type="submit" loading={send.busy} disabled={!body.trim() && !image} className="ml-auto">
          Send
        </Button>
      </div>
      {imageError && <p className="text-xs font-medium text-brick">{imageError}</p>}
      {!imageError && !image && <p className="text-xs text-muted">One JPEG, PNG or WebP image, up to 5 MB. Only the parent and the school can see it.</p>}
      <ErrorBox error={otherError} />
    </form>
  );
}
