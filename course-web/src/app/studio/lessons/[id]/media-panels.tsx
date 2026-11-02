"use client";

import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { api, apiUrl, toApiError, uploadWithProgress, type ApiError } from "@/lib/api";
import { formatBytes, formatLength } from "@/lib/format";
import { VIDEO_STATUS, statusOf } from "@/lib/status";
import type { StudioLessonDto, VideoDto } from "@/lib/types";
import { IconButton } from "@/components/editors";
import { FileIcon, TrashIcon, UploadIcon } from "@/components/icons";
import { Button, Card, ErrorBox, Notice, SectionTitle, StatusBadge, cx, inputClass } from "@/components/ui";

const POLL_MS = 5000;
const MAX_VIDEO_BYTES = 2 * 1024 ** 3;
const MAX_RESOURCE_BYTES = 50 * 1024 ** 2;

function ProgressLine({ fraction, label }: { fraction: number; label: string }) {
  const pct = Math.round(fraction * 100);
  return (
    <div>
      <div className="flex justify-between text-sm">
        <span className="text-slate-700">{label}</span>
        <span className="tnum font-semibold text-ink">{pct}%</span>
      </div>
      <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-mist-2" role="progressbar" aria-label={label} aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
        <div className="h-full rounded-full bg-lagoon transition-[width] duration-200" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

/**
 * Video upload with a real progress bar (XMLHttpRequest), then polling every 5 s while the server
 * prepares it, until Ready or Failed.
 */
export function VideoPanel({ lesson, onChange, canEdit }: { lesson: StudioLessonDto; onChange: (l: StudioLessonDto) => void; canEdit: boolean }) {
  const [video, setVideo] = useState<VideoDto | null>(lesson.video);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const abort = useRef<AbortController | null>(null);
  const status = video?.status ?? "None";

  useEffect(() => {
    if (status !== "Processing") return;
    const t = setInterval(async () => {
      try {
        const v = await api<VideoDto>(`/api/studio/lessons/${lesson.id}/video`);
        setVideo(v);
        if (v.status !== "Processing") {
          onChange({ ...lesson, video: v, durationSec: v.durationSec ?? lesson.durationSec });
        }
      } catch {
        /* keep polling; a blip shouldn't stop it */
      }
    }, POLL_MS);
    return () => clearInterval(t);
  }, [status, lesson, onChange]);

  useEffect(() => () => abort.current?.abort(), []);

  async function pick(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    if (file.size > MAX_VIDEO_BYTES) {
      setError(toApiError(new Error("That file is over 2 GB. Export it smaller (720p is plenty for most lessons) and try again.")));
      return;
    }
    setError(null);
    setProgress(0);
    abort.current = new AbortController();
    try {
      const res = await uploadWithProgress<StudioLessonDto>(`/api/studio/lessons/${lesson.id}/video`, file, setProgress, { signal: abort.current.signal });
      setVideo(res.video ?? { status: "Processing", format: null, durationSec: null, thumbnailUrl: null, error: null, renditions: [] });
      onChange(res);
    } catch (err) {
      if (!(err instanceof DOMException && err.name === "AbortError")) setError(toApiError(err));
    } finally {
      setProgress(null);
      abort.current = null;
    }
  }

  const st = statusOf(VIDEO_STATUS, status);

  return (
    <Card className="space-y-4">
      <SectionTitle action={<StatusBadge tone={st.tone}>{st.label}</StatusBadge>}>Video</SectionTitle>

      {video?.thumbnailUrl && status === "Ready" && <img src={video.thumbnailUrl} alt="" className="aspect-video w-full max-w-sm rounded-[10px] object-cover" />}

      {status === "None" && progress === null && <p className="text-sm text-slate-700">Upload an MP4, MOV, WebM or MKV file up to 2 GB.</p>}
      {status === "Processing" && (
        <Notice tone="info">Preparing the video for streaming. This page checks every few seconds; you can leave and come back.</Notice>
      )}
      {status === "Failed" && <Notice tone="error">The video couldn&apos;t be processed{video?.error ? `: ${video.error}` : "."} Upload it again, or try another export.</Notice>}

      {status === "Ready" && video && (
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm sm:grid-cols-3">
          <div>
            <dt className="text-xs text-muted">Length</dt>
            <dd className="tnum font-semibold text-ink">{formatLength(video.durationSec) || "—"}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted">Format</dt>
            <dd className="font-semibold text-ink">{video.format === "hls" ? "Adaptive (HLS)" : (video.format ?? "—").toUpperCase()}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted">Qualities</dt>
            <dd className="font-semibold text-ink">{video.renditions?.length ? video.renditions.join(", ") : "Original only"}</dd>
          </div>
        </dl>
      )}
      {status === "Ready" && video?.format === "mp4" && (
        <p className="text-sm text-muted">
          Learners get the single MP4 file: adaptive streaming (lower quality on slow connections) is off on this server. Keep videos at 720p or below to spare
          learners&apos; data.
        </p>
      )}

      {progress !== null && (
        <div className="space-y-2">
          <ProgressLine fraction={progress} label={progress >= 1 ? "Upload finished, starting processing…" : "Uploading"} />
          <Button variant="ghost" onClick={() => abort.current?.abort()}>
            Cancel upload
          </Button>
        </div>
      )}
      {error && <ErrorBox error={error} />}

      {canEdit && progress === null && (
        <>
          <input ref={input} type="file" accept="video/mp4,video/quicktime,video/webm,video/x-matroska,.mp4,.mov,.webm,.mkv" className="sr-only" onChange={pick} aria-label="Choose a video file" />
          <Button variant={status === "None" ? "primary" : "secondary"} onClick={() => input.current?.click()} disabled={status === "Processing"}>
            <UploadIcon className="h-4 w-4" /> {status === "None" ? "Upload video" : "Replace video"}
          </Button>
        </>
      )}
    </Card>
  );
}

/** Downloadable files for the lesson (≤ 50 MB each). */
export function ResourcesPanel({ lesson, onChange, canEdit }: { lesson: StudioLessonDto; onChange: (l: StudioLessonDto) => void; canEdit: boolean }) {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const [progress, setProgress] = useState<number | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  async function add(e: FormEvent) {
    e.preventDefault();
    if (!file) return;
    if (file.size > MAX_RESOURCE_BYTES) {
      setError(toApiError(new Error("Files can be up to 50 MB.")));
      return;
    }
    setError(null);
    setProgress(0);
    try {
      onChange(await uploadWithProgress<StudioLessonDto>(`/api/studio/lessons/${lesson.id}/resources`, file, setProgress, { fields: { title: title.trim() || file.name } }));
      setFile(null);
      setTitle("");
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setProgress(null);
    }
  }

  async function remove(id: string, name: string) {
    if (!window.confirm(`Remove "${name}"?`)) return;
    setBusy(id);
    setError(null);
    try {
      onChange(await api<StudioLessonDto>(`/api/studio/lessons/${lesson.id}/resources/${id}`, { method: "DELETE" }));
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card className="space-y-4">
      <SectionTitle>{lesson.type === "File" ? "Files to download" : "Resources"}</SectionTitle>
      {lesson.resources.length === 0 ? (
        <p className="text-sm text-muted">{lesson.type === "File" ? "Add the files learners download in this lesson." : "Optional: worksheets, slides or past questions that go with this lesson."}</p>
      ) : (
        <ul className="divide-y divide-line-soft rounded-control border border-line">
          {lesson.resources.map((r) => (
            <li key={r.id} className="flex items-center gap-3 px-3 py-2.5">
              <FileIcon className="h-5 w-5 shrink-0 text-muted" />
              <a href={apiUrl(r.url)} target="_blank" rel="noopener noreferrer" className="min-w-0 flex-1 hover:underline">
                <span className="block truncate font-medium text-ink">{r.title}</span>
                <span className="tnum block truncate text-xs text-muted">
                  {r.fileName}, {formatBytes(r.sizeBytes)}
                </span>
              </a>
              {canEdit && (
                <IconButton label={`Remove ${r.title}`} danger disabled={busy === r.id} onClick={() => remove(r.id, r.title)}>
                  <TrashIcon className="h-4 w-4" />
                </IconButton>
              )}
            </li>
          ))}
        </ul>
      )}

      {canEdit && (
        <form onSubmit={add} className="space-y-3 rounded-control border border-dashed border-slate-300 p-3">
          <div className="flex flex-wrap items-center gap-3">
            <input ref={fileInput} type="file" className="sr-only" aria-label="Choose a file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
            <Button variant="secondary" onClick={() => fileInput.current?.click()}>
              <UploadIcon className="h-4 w-4" /> {file ? "Choose another file" : "Choose a file"}
            </Button>
            {file && (
              <span className="tnum min-w-0 truncate text-sm text-slate-700">
                {file.name}, {formatBytes(file.size)}
              </span>
            )}
          </div>
          {file && (
            <>
              <div>
                <label htmlFor="res-title" className="text-sm font-medium text-slate-700">
                  Title learners see
                </label>
                <input id="res-title" className={cx(inputClass, "mt-1.5")} value={title} placeholder={file.name} onChange={(e) => setTitle(e.target.value)} />
              </div>
              {progress !== null && <ProgressLine fraction={progress} label="Uploading" />}
              <Button type="submit" loading={progress !== null}>
                Add file
              </Button>
            </>
          )}
        </form>
      )}
      {error && <ErrorBox error={error} />}
    </Card>
  );
}
