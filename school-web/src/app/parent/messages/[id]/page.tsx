"use client";

import { useParams } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { CloseIcon, UploadIcon } from "@/components/icons";
import { refreshNotifications } from "@/components/notification-bell";
import { Button, ErrorBox, PageHeader, PageLoader, Textarea, cx } from "@/components/ui";
import { api, upload } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { MessageDto, ThreadDto } from "@/lib/types/communication";
import { PortalBlocked, useChildPath } from "../../_shared";

const POLL_MS = 30_000;
const MAX_BODY = 2000;
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const IMAGE_TYPES = "image/jpeg,image/png,image/webp";

export default function ParentThreadPage() {
  const { id } = useParams<{ id: string }>();
  const threads = useApi<ThreadDto[]>(useChildPath("/threads"));
  const messages = useApi<MessageDto[]>(id ? `/api/parent/threads/${id}/messages` : null);
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

  const title = thread ? (thread.isOffice ? "School office" : (thread.staffName ?? "Class teacher")) : "Conversation";

  if (messages.error && !messages.data) {
    return (
      <div>
        <PageHeader title={title} back="/parent/messages" />
        <PortalBlocked error={messages.error} onRetry={reload} />
      </div>
    );
  }
  if (!messages.data) {
    return (
      <div>
        <PageHeader title={title} back="/parent/messages" />
        <PageLoader />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <PageHeader title={title} back="/parent/messages" />
      {messages.error && <ErrorBox error={messages.error} onRetry={reload} />}
      <ol className="space-y-3" aria-label="Messages">
        {messages.data.map((m) => (
          <li key={m.id} className={cx("flex", m.fromParent ? "justify-end" : "justify-start")}>
            <div
              className={cx(
                "max-w-[85%] rounded-panel border px-3.5 py-2.5 sm:max-w-[70%]",
                m.fromParent ? "border-blazer-line bg-blazer-wash" : "border-rule bg-surface",
              )}
            >
              <p className="text-xs font-bold text-ink-2">{m.senderName || (m.fromParent ? "You" : "School")}</p>
              {m.body && <p className="mt-1 text-sm break-words whitespace-pre-line text-ink">{m.body}</p>}
              {m.imageUrl && (
                <a href={m.imageUrl} target="_blank" rel="noopener noreferrer" className="mt-2 block">
                  <img src={m.imageUrl} alt={`Image from ${m.senderName || "the sender"}`} className="max-h-72 rounded-control border border-rule-soft object-contain" />
                </a>
              )}
              <p className="tnum mt-1.5 text-xs text-muted">{formatStamp(m.sentAt)}</p>
            </div>
          </li>
        ))}
      </ol>
      <div ref={end} />
      <ReplyForm threadId={id} onSent={(m) => setData((d) => [...(d ?? []), m])} />
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
        ? upload<MessageDto>(`/api/parent/threads/${threadId}/messages/image`, image, text ? { body: text } : {})
        : api<MessageDto>(`/api/parent/threads/${threadId}/messages`, { method: "POST", body: { body: text } }),
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
      <Textarea label="Reply" value={body} onChange={(e) => setBody(e.target.value)} maxLength={MAX_BODY} rows={3} error={bodyError} />
      <div className="flex flex-wrap items-center gap-2">
        <input
          ref={fileInput}
          type="file"
          accept={IMAGE_TYPES}
          className="sr-only"
          onChange={(e) => pick(e.target.files?.[0])}
        />
        <Button type="button" variant="secondary" onClick={() => fileInput.current?.click()}>
          <UploadIcon className="h-4 w-4" /> {image ? "Change image" : "Add an image"}
        </Button>
        {image && (
          <span className="flex items-center gap-1 text-sm text-slate-700">
            {image.name}
            <button type="button" onClick={clearImage} className="rounded-control p-1 text-muted hover:text-ink" aria-label="Remove image">
              <CloseIcon className="h-4 w-4" />
            </button>
          </span>
        )}
        <Button type="submit" className="ml-auto" loading={send.busy} disabled={!body.trim() && !image}>
          Send
        </Button>
      </div>
      {imageError && <p className="text-xs font-medium text-brick">{imageError}</p>}
      <ErrorBox error={otherError} />
    </form>
  );
}
