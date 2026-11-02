"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { timeAgo } from "@/lib/format";
import type { QuestionDto } from "@/lib/types";
import { Badge, Button, ErrorBox, cx, inputClass } from "./ui";

/** One question with its answers and a reply box (shared with the studio Q&A inbox). */
export function QuestionThread({
  question: q,
  onChange,
  answerPath,
  context,
}: {
  question: QuestionDto;
  onChange: (q: QuestionDto) => void;
  answerPath: string;
  context?: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const [reply, setReply] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function send(e: FormEvent) {
    e.preventDefault();
    if (!reply.trim()) return;
    setBusy(true);
    setError(null);
    try {
      onChange(await api<QuestionDto>(answerPath, { method: "POST", body: { body: reply.trim() } }));
      setReply("");
      setOpen(false);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <li className="rounded-panel border border-line bg-surface p-4">
      {context}
      <p className="text-sm">
        <span className="font-semibold text-ink">{q.authorName}</span> <span className="text-muted">{timeAgo(q.createdAt)}</span>
      </p>
      <p className="mt-1 text-[0.9375rem] whitespace-pre-line text-slate-800">{q.body}</p>
      {q.answers.length > 0 && (
        <ul className="mt-3 space-y-3 border-l-2 border-line pl-4">
          {q.answers.map((a) => (
            <li key={a.id}>
              <p className="flex flex-wrap items-center gap-2 text-sm">
                <span className="font-semibold text-ink">{a.authorName}</span>
                {a.isTutor && <Badge className="bg-lagoon-wash text-lagoon-text">Tutor</Badge>}
                <span className="text-muted">{timeAgo(a.createdAt)}</span>
              </p>
              <p className="mt-0.5 text-[0.9375rem] whitespace-pre-line text-slate-800">{a.body}</p>
            </li>
          ))}
        </ul>
      )}
      {open ? (
        <form onSubmit={send} className="mt-3 space-y-2">
          <label htmlFor={`reply-${q.id}`} className="sr-only">
            Your answer
          </label>
          <textarea id={`reply-${q.id}`} rows={3} autoFocus className={cx(inputClass, "leading-normal")} value={reply} onChange={(e) => setReply(e.target.value)} />
          {error && <ErrorBox error={error} />}
          <div className="flex gap-2">
            <Button type="submit" loading={busy} disabled={!reply.trim()}>
              Post answer
            </Button>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Cancel
            </Button>
          </div>
        </form>
      ) : (
        <button type="button" onClick={() => setOpen(true)} className="mt-3 text-sm font-semibold text-lagoon-text hover:underline">
          Answer
        </button>
      )}
    </li>
  );
}
