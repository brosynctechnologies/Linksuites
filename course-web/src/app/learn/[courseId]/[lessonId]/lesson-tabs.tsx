"use client";

import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { api, apiUrl, asList, toApiError, type ApiError } from "@/lib/api";
import { formatBytes, formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { LessonPlaybackDto, NoteDto, QuestionDto } from "@/lib/types";
import { DownloadIcon, FileIcon } from "@/components/icons";
import { QuestionThread } from "@/components/question-thread";
import { Markdown } from "@/components/markdown";
import { Button, ErrorBox, PageLoader, Tabs, Textarea, cx, inputClass } from "@/components/ui";

type TabKey = "overview" | "resources" | "qa" | "notes";

/** Tabs under the lesson: Overview (the lesson's notes), Resources, Q&A and the learner's own Notes. */
export function LessonTabs({ lesson }: { lesson: LessonPlaybackDto }) {
  const hasOverview = lesson.type !== "Text" && !!lesson.content?.trim();
  const items: { value: TabKey; label: ReactNode }[] = [
    ...(hasOverview ? [{ value: "overview" as const, label: "Overview" }] : []),
    {
      value: "resources",
      label: (
        <>
          Resources{lesson.resources.length > 0 && <span className="tnum ml-1 text-muted">({lesson.resources.length})</span>}
        </>
      ),
    },
    { value: "qa", label: "Q&A" },
    { value: "notes", label: "Notes" },
  ];
  const [tab, setTab] = useState<TabKey>(items[0].value);
  const id = `lesson-${lesson.id}`;

  return (
    <div>
      <Tabs items={items} value={tab} onChange={setTab} label="Lesson details" idBase={id} />
      <div role="tabpanel" id={`${id}-panel-${tab}`} aria-labelledby={`${id}-tab-${tab}`} className="pt-5">
        {tab === "overview" && <Markdown source={lesson.content} variant="lesson" />}
        {tab === "resources" && <Resources resources={lesson.resources} />}
        {tab === "qa" && <QuestionsPanel lessonId={lesson.id} />}
        {tab === "notes" && <NotesPanel lessonId={lesson.id} />}
      </div>
    </div>
  );
}

export function Resources({ resources }: { resources: LessonPlaybackDto["resources"] }) {
  if (!resources.length) return <p className="text-sm text-muted">No downloads for this lesson.</p>;
  return (
    <ul className="max-w-2xl divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
      {resources.map((r, i) => (
        <li key={`${r.url}-${i}`}>
          <a href={apiUrl(r.url)} target="_blank" rel="noopener noreferrer" className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50">
            <FileIcon className="h-5 w-5 shrink-0 text-muted" />
            <span className="min-w-0 flex-1">
              <span className="block truncate font-medium text-ink">{r.title}</span>
              <span className="tnum block text-xs text-muted">{formatBytes(r.sizeBytes)}</span>
            </span>
            <DownloadIcon className="h-4 w-4 shrink-0 text-lagoon-text" />
            <span className="sr-only">Download</span>
          </a>
        </li>
      ))}
    </ul>
  );
}

function QuestionsPanel({ lessonId }: { lessonId: string }) {
  const { data, error, reload, setData } = useApi<QuestionDto[]>(`/api/learn/lessons/${lessonId}/questions`);
  const list = asList(data);
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  const [askError, setAskError] = useState<ApiError | null>(null);

  async function ask(e: FormEvent) {
    e.preventDefault();
    if (!body.trim()) return;
    setBusy(true);
    setAskError(null);
    try {
      const q = await api<QuestionDto>(`/api/learn/lessons/${lessonId}/questions`, { method: "POST", body: { body: body.trim() } });
      setData((xs) => [q, ...asList(xs)]);
      setBody("");
    } catch (err) {
      setAskError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="max-w-2xl space-y-5">
      <form onSubmit={ask} className="space-y-2">
        <Textarea label="Ask the tutor a question" value={body} onChange={(e) => setBody(e.target.value)} rows={3} placeholder="What's unclear in this lesson?" hint="Other learners on this course can see questions and answers." />
        {askError && <ErrorBox error={askError} />}
        <Button type="submit" loading={busy} disabled={!body.trim()}>
          Post question
        </Button>
      </form>
      {!data && error && <ErrorBox error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && <p className="text-sm text-muted">No questions on this lesson yet.</p>}
      {list.length > 0 && (
        <ul className="space-y-3">
          {list.map((q) => (
            <QuestionThread key={q.id} question={q} onChange={(nq) => setData((xs) => asList(xs).map((x) => (x.id === nq.id ? nq : x)))} answerPath={`/api/learn/questions/${q.id}/answers`} />
          ))}
        </ul>
      )}
    </div>
  );
}

/** Private notes for this lesson, saved a second after typing stops. */
function NotesPanel({ lessonId }: { lessonId: string }) {
  const { data, error, reload } = useApi<NoteDto>(`/api/learn/lessons/${lessonId}/note`);
  if (!data && error && error.status !== 404) return <ErrorBox error={error} onRetry={reload} />;
  if (!data && !error) return <PageLoader />;
  return <NoteEditor key={lessonId} lessonId={lessonId} initial={data ?? { body: "", updatedAt: null }} />;
}

function NoteEditor({ lessonId, initial }: { lessonId: string; initial: NoteDto }) {
  const [body, setBody] = useState(initial.body ?? "");
  const [saved, setSaved] = useState<{ body: string; at: string | null }>({ body: initial.body ?? "", at: initial.updatedAt });
  const [state, setState] = useState<"idle" | "saving" | "error">("idle");

  useEffect(() => {
    if (body === saved.body) return;
    const t = setTimeout(async () => {
      setState("saving");
      try {
        const res = await api<NoteDto>(`/api/learn/lessons/${lessonId}/note`, { method: "PUT", body: { body } });
        setSaved({ body, at: res?.updatedAt ?? new Date().toISOString() });
        setState("idle");
      } catch {
        setState("error");
      }
    }, 1000);
    return () => clearTimeout(t);
  }, [body, saved.body, lessonId]);

  const dirty = body !== saved.body;
  return (
    <div className="max-w-2xl">
      <label htmlFor={`note-${lessonId}`} className="text-sm font-medium text-slate-700">
        Your notes on this lesson
      </label>
      <textarea
        id={`note-${lessonId}`}
        rows={8}
        className={cx(inputClass, "mt-1.5 font-serif text-[1rem] leading-relaxed sm:text-[1rem]")}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        placeholder="Only you can see these."
      />
      <p className="mt-1.5 text-xs text-muted" role="status" aria-live="polite">
        {state === "saving"
          ? "Saving…"
          : state === "error"
            ? "Not saved. Check your connection; we'll try again when you keep typing."
            : dirty
              ? "Unsaved changes"
              : saved.at
                ? `Saved ${formatStamp(saved.at)}`
                : "Notes save as you type."}
      </p>
    </div>
  );
}
