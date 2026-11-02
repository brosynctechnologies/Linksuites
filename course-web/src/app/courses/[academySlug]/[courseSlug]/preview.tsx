"use client";

import { useEffect, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import type { LessonPlaybackDto } from "@/lib/types";
import { Markdown } from "@/components/markdown";
import { ErrorBox, Modal, PageLoader } from "@/components/ui";
import { PlayIcon } from "@/components/icons";
import { VideoPlayer } from "@/components/video-player";

/** "Preview" on a free lesson in the course path; plays it in a modal without signing in. */
export function PreviewButton({ lessonId, title }: { lessonId: string; title: string }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="mt-0.5 inline-flex shrink-0 items-center gap-1 rounded-full border border-lagoon-line px-2.5 py-1 text-xs font-semibold text-lagoon-text hover:bg-lagoon-wash"
      >
        <PlayIcon className="h-3 w-3" />
        Preview<span className="sr-only">: {title}</span>
      </button>
      <Modal open={open} title={title} onClose={() => setOpen(false)} wide dark>
        {open && <PreviewBody lessonId={lessonId} />}
      </Modal>
    </>
  );
}

function PreviewBody({ lessonId }: { lessonId: string }) {
  const [lesson, setLesson] = useState<LessonPlaybackDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    api<LessonPlaybackDto>(`/api/catalog/lessons/${lessonId}/preview`, { auth: false, signal: ctrl.signal }).then(setLesson, (e) => {
      if (!ctrl.signal.aborted) setError(toApiError(e));
    });
    return () => ctrl.abort();
  }, [lessonId]);

  if (error) {
    return (
      <div className="p-5">
        <ErrorBox error={error.status === 404 ? "This preview isn't available any more." : error} />
      </div>
    );
  }
  if (!lesson) {
    return (
      <div className="text-on-ink">
        <PageLoader label="Loading preview…" />
      </div>
    );
  }
  const video = lesson.video;
  return (
    <div>
      {video?.url && video.status === "Ready" ? (
        <VideoPlayer src={video.url} format={video.format} poster={video.thumbnailUrl} title={lesson.title} />
      ) : lesson.type === "Video" ? (
        <p className="p-5 text-sm text-on-ink">This video is still being prepared. Try again in a few minutes.</p>
      ) : null}
      {lesson.content && (
        <div className="max-h-[50dvh] overflow-y-auto bg-surface px-5 py-5 sm:px-8">
          <Markdown source={lesson.content} variant="lesson" />
        </div>
      )}
    </div>
  );
}
