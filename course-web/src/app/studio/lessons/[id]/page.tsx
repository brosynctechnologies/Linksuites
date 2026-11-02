"use client";

import { useParams, useRouter } from "next/navigation";
import { useCallback, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { LessonType, StudioLessonDto } from "@/lib/types";
import { MarkdownField } from "@/components/editors";
import { ApiErrorView, Button, Card, Input, Notice, PageHeader, PageLoader, Select, Toggle } from "@/components/ui";
import { ResourcesPanel, VideoPanel } from "./media-panels";
import { QuizBuilder } from "./quiz-builder";

const TYPE_OPTIONS: { value: LessonType; label: string }[] = [
  { value: "Video", label: "Video" },
  { value: "Text", label: "Reading" },
  { value: "File", label: "Download" },
  { value: "Quiz", label: "Quiz" },
];

const CONTENT_LABEL: Record<LessonType, { label: string; hint?: string }> = {
  Video: { label: "Notes under the video", hint: "Optional. Shown in the lesson's Overview tab: key points, formulas, links." },
  Text: { label: "Lesson text", hint: "This is the lesson. Write it like a chapter: short paragraphs, headings, examples." },
  File: { label: "What's in the files", hint: "Optional. Tell learners what to do with the downloads." },
  Quiz: { label: "Instructions", hint: "Optional. Shown before the learner starts the quiz." },
};

export default function LessonEditorPage() {
  const { id } = useParams<{ id: string }>();
  const { data, error, reload, setData } = useApi<StudioLessonDto>(`/api/studio/lessons/${id}`);
  const canEdit = useCan("courses.edit");
  const onChange = useCallback((l: StudioLessonDto) => setData(l), [setData]);

  if (!data) {
    return (
      <div>
        <PageHeader title="Lesson" back="/studio/courses" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader title={<span className="font-serif">{data.title}</span>} back={`/studio/courses/${data.courseId}`} subtitle={TYPE_OPTIONS.find((t) => t.value === data.type)?.label} />
      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:items-start">
        <LessonForm key={`${data.id}-${data.type}`} lesson={data} onSaved={onChange} canEdit={canEdit} />
        <div className="space-y-5">
          {data.type === "Video" && <VideoPanel key={data.id} lesson={data} onChange={onChange} canEdit={canEdit} />}
          <ResourcesPanel lesson={data} onChange={onChange} canEdit={canEdit} />
        </div>
      </div>
      {data.type === "Quiz" && <QuizBuilder key={data.id} lesson={data} onSaved={onChange} canEdit={canEdit} />}
    </div>
  );
}

function LessonForm({ lesson, onSaved, canEdit }: { lesson: StudioLessonDto; onSaved: (l: StudioLessonDto) => void; canEdit: boolean }) {
  const router = useRouter();
  const [title, setTitle] = useState(lesson.title);
  const [type, setType] = useState<LessonType>(lesson.type);
  const [isPreview, setIsPreview] = useState(lesson.isPreview);
  const [content, setContent] = useState(lesson.content ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const touch = () => setSaved(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (type !== lesson.type && lesson.type === "Video" && lesson.video && !window.confirm("Change the lesson type? The uploaded video won't be shown for other lesson types.")) return;
    setBusy(true);
    setError(null);
    try {
      const res = await api<StudioLessonDto>(`/api/studio/lessons/${lesson.id}`, {
        method: "PUT",
        body: { title: title.trim(), type, isPreview, content: content.trim() ? content : null },
      });
      onSaved(res);
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!window.confirm(`Delete the lesson "${lesson.title}"? Its video, files and quiz go too.`)) return;
    setBusy(true);
    try {
      await api(`/api/studio/lessons/${lesson.id}`, { method: "DELETE" });
      router.replace(`/studio/courses/${lesson.courseId}`);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  const c = CONTENT_LABEL[type];
  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <fieldset disabled={!canEdit} className="space-y-4">
          <Input
            label="Lesson title"
            required
            value={title}
            onChange={(e) => {
              setTitle(e.target.value);
              touch();
            }}
          />
          <Select
            label="Type"
            value={type}
            onChange={(e) => {
              setType(e.target.value as LessonType);
              touch();
            }}
          >
            {TYPE_OPTIONS.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </Select>
          <Toggle
            checked={isPreview}
            onChange={(v) => {
              setIsPreview(v);
              touch();
            }}
            label="Free preview"
            description="Anyone can watch or read this lesson from the course page, without enrolling."
          />
          <MarkdownField
            label={c.label}
            hint={c.hint}
            lesson
            rows={type === "Text" ? 18 : 8}
            value={content}
            onChange={(v) => {
              setContent(v);
              touch();
            }}
          />
        </fieldset>
        {error && <ApiErrorView error={error} />}
        {saved && <Notice tone="success">Lesson saved.</Notice>}
        {canEdit && (
          <div className="flex flex-wrap items-center justify-between gap-2">
            <Button variant="danger" onClick={remove} disabled={busy}>
              Delete lesson
            </Button>
            <Button type="submit" loading={busy} disabled={!title.trim()}>
              Save lesson
            </Button>
          </div>
        )}
      </form>
    </Card>
  );
}
