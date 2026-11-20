"use client";

import { useRef, useState, type FormEvent } from "react";
import { postForm } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { ParentAssignmentDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, PageHeader, PageLoader, Textarea, linkClass, panelClass } from "@/components/ui";
import { ChildGate, score, useChild, useChildPath } from "../_shared";

const MAX_BODY = 5000;
const MAX_FILE_BYTES = 5 * 1024 * 1024;
const FILE_TYPES = "application/pdf,image/jpeg,image/png,image/webp";

export default function ParentAssignmentsPage() {
  return (
    <ChildGate>
      <Homework />
    </ChildGate>
  );
}

function Homework() {
  const { child } = useChild();
  const path = useChildPath("/assignments");
  const list = useApi<ParentAssignmentDto[]>(path);
  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Homework"
        subtitle={child ? `${child.name}${child.className ? ` · ${child.className}` : ""}` : undefined}
      />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {list.loading && !list.data && <PageLoader />}
      {list.data && list.data.length === 0 && (
        <EmptyState title="No homework yet" text="When a teacher sets work for this class, it shows here with the mark and the teacher's comment." />
      )}
      {list.data && list.data.length > 0 && child && (
        <ul className="min-w-0 space-y-4" aria-label="Homework">
          {list.data.map((item) => (
            <li key={item.id}>
              <HomeworkCard
                item={item}
                studentId={child.studentId}
                onSaved={(saved) => list.setData((current) => (current ?? []).map((row) => (row.id === saved.id ? saved : row)))}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function HomeworkCard({ item, studentId, onSaved }: { item: ParentAssignmentDto; studentId: string; onSaved: (saved: ParentAssignmentDto) => void }) {
  return (
    <article className={`${panelClass} w-full min-w-0 space-y-3 p-4`}>
      <div className="min-w-0">
        <h2 className="font-bold break-words text-ink">{item.title}</h2>
        <p className="mt-0.5 text-xs text-muted">
          {item.subject && <span>{item.subject} · </span>}
          Due <span className="tnum">{formatStamp(item.dueAt)}</span>
          {item.maxMarks !== null && <span> · Out of {score(item.maxMarks)}</span>}
        </p>
      </div>
      <div className="flex flex-wrap gap-1.5">
        <Badge tone={item.submitted ? "blazer" : "neutral"}>{item.submitted ? "Submitted" : "Not submitted"}</Badge>
        {item.late && <Badge tone="crest">Late</Badge>}
        {item.mark !== null && <Badge tone="pass">Mark {score(item.mark)}</Badge>}
      </div>
      <p className="text-sm break-words whitespace-pre-line text-slate-800">{item.instructions}</p>
      {item.submittedAt && (
        <p className="text-xs text-muted">
          Sent {formatStamp(item.submittedAt)}
          {item.sentByParent ? " by you" : ""}
        </p>
      )}
      {item.body && <p className="text-sm break-words whitespace-pre-line text-slate-700">{item.body}</p>}
      {item.fileUrl && (
        <a href={item.fileUrl} target="_blank" rel="noopener noreferrer" className={`${linkClass} inline-flex min-h-9 items-center text-sm`}>
          {item.fileName ?? "The file sent"}
        </a>
      )}
      {item.feedback && (
        <p className="text-sm break-words text-slate-700">
          <span className="font-semibold text-ink">Teacher&apos;s comment:</span> {item.feedback}
        </p>
      )}
      {item.mark === null && !item.submitted && !item.canSubmit && <p className="text-xs text-muted">Your child sends this work from their own student account.</p>}
      {item.canSubmit && <SendWork item={item} studentId={studentId} onSaved={onSaved} />}
    </article>
  );
}

/** For a child without a student login: the parent sends a written answer, a photo or PDF of the work, or both. */
function SendWork({ item, studentId, onSaved }: { item: ParentAssignmentDto; studentId: string; onSaved: (saved: ParentAssignmentDto) => void }) {
  const [body, setBody] = useState(item.body ?? "");
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const send = useAction();

  function pick(next: File | undefined) {
    setFileError(null);
    if (!next) {
      setFile(null);
      return;
    }
    if (next.size > MAX_FILE_BYTES) {
      setFile(null);
      setFileError("Attachments must be 5 MB or smaller. Choose a smaller file.");
      return;
    }
    setFile(next);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const fd = new FormData();
    const text = body.trim();
    if (text) fd.append("body", text);
    if (file) fd.append("file", file);
    const res = await send.run(() => postForm<ParentAssignmentDto>(`/api/parent/children/${studentId}/assignments/${item.id}/submit`, fd));
    if (res.ok) {
      onSaved(res.data);
      setFile(null);
      setBody(res.data.body ?? "");
      if (fileInput.current) fileInput.current.value = "";
    }
  }

  return (
    <form className="w-full min-w-0 space-y-3 border-t border-rule-soft pt-3" onSubmit={submit}>
      <ErrorBox error={fileError ?? send.error} />
      <Textarea
        label={item.submitted ? "Update the answer" : "Your child's answer"}
        value={body}
        maxLength={MAX_BODY}
        onChange={(e) => setBody(e.target.value)}
        hint="Type the answer, attach a photo or PDF of the work, or both. A PDF, JPEG, PNG or WebP up to 5 MB."
      />
      <label className="block min-w-0 text-sm">
        <span className="font-medium text-slate-800">Photo or PDF of the work</span>
        <input type="file" accept={FILE_TYPES} className="mt-1.5 block w-full min-w-0 text-sm" ref={fileInput} onChange={(e) => pick(e.target.files?.[0])} />
      </label>
      <Button type="submit" loading={send.busy} disabled={!body.trim() && !file}>
        {item.submitted ? "Send again" : "Send to the teacher"}
      </Button>
    </form>
  );
}
