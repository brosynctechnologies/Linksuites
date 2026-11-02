"use client";

import { useEffect, useRef, useState } from "react";
import { cx } from "./styles";

type HlsInstance = { destroy(): void; loadSource(src: string): void; attachMedia(el: HTMLMediaElement): void; on(ev: string, cb: (e: unknown, data: { fatal?: boolean }) => void): void };

export interface VideoPlayerProps {
  src: string;
  /** "hls" plays the .m3u8 through hls.js (Safari natively); anything else is a plain file. */
  format: string | null | undefined;
  poster?: string | null;
  /** Resume position in seconds. */
  startAt?: number | null;
  /** The learner's email, drawn faintly over the picture. */
  watermark?: string | null;
  onTime?: (sec: number, duration: number) => void;
  onPause?: (sec: number) => void;
  onEnded?: (sec: number) => void;
  className?: string;
  title?: string;
}

/**
 * Square-edged video player. Native controls (light, accessible, familiar on phones) with fullscreen
 * applied to the frame instead of the bare <video>, so the watermark stays on screen in fullscreen.
 */
export function VideoPlayer({ src, format, poster, startAt, watermark, onTime, onPause, onEnded, className, title }: VideoPlayerProps) {
  const frameRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState<{ src: string; message: string } | null>(null);
  const handlers = useRef({ onTime, onPause, onEnded });
  useEffect(() => {
    handlers.current = { onTime, onPause, onEnded };
  });

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    let hls: HlsInstance | null = null;
    let cancelled = false;
    const seek = () => {
      if (startAt && startAt > 3 && (!video.duration || startAt < video.duration - 5)) video.currentTime = startAt;
    };
    video.addEventListener("loadedmetadata", seek, { once: true });

    const isHls = (format ?? "").toLowerCase() === "hls" || /\.m3u8(\?|$)/i.test(src);
    if (isHls && !video.canPlayType("application/vnd.apple.mpegurl")) {
      import("hls.js").then(({ default: Hls }) => {
        if (cancelled) return;
        if (!Hls.isSupported()) {
          setError({ src, message: "This browser can't play this video. Try Chrome, Safari or Firefox." });
          return;
        }
        const instance = new Hls({ capLevelToPlayerSize: true, maxBufferLength: 20 }) as unknown as HlsInstance;
        hls = instance;
        instance.on(Hls.Events.ERROR, (_e, data) => {
          if (data?.fatal) setError({ src, message: "The video stopped loading. Check your connection, then reload the lesson." });
        });
        instance.loadSource(src);
        instance.attachMedia(video);
      });
    } else {
      video.src = src;
    }
    return () => {
      cancelled = true;
      video.removeEventListener("loadedmetadata", seek);
      hls?.destroy();
      video.removeAttribute("src");
      video.load();
    };
    // Re-initialise only when the source changes; startAt is read once per source.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [src, format]);

  function toggleFullscreen() {
    const frame = frameRef.current;
    if (!frame) return;
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    } else if (frame.requestFullscreen) {
      void frame.requestFullscreen();
    } else {
      // iPhone Safari: only the video element can go fullscreen.
      (videoRef.current as HTMLVideoElement & { webkitEnterFullscreen?: () => void })?.webkitEnterFullscreen?.();
    }
  }

  const shownError = error && error.src === src ? error.message : null;

  return (
    <div ref={frameRef} className={cx("group relative aspect-video w-full overflow-hidden bg-black", className)}>
      <video
        ref={videoRef}
        className="h-full w-full bg-black"
        controls
        playsInline
        preload="metadata"
        poster={poster ?? undefined}
        controlsList="nodownload nofullscreen noremoteplayback"
        disablePictureInPicture
        aria-label={title}
        onContextMenu={(e) => e.preventDefault()}
        onTimeUpdate={(e) => handlers.current.onTime?.(e.currentTarget.currentTime, e.currentTarget.duration || 0)}
        onPause={(e) => {
          if (!e.currentTarget.ended) handlers.current.onPause?.(e.currentTarget.currentTime);
        }}
        onEnded={(e) => handlers.current.onEnded?.(e.currentTarget.currentTime)}
        onError={() => setError({ src, message: "The video couldn't be loaded. Its link may have expired: reload the lesson." })}
      />
      {watermark && (
        <span
          aria-hidden="true"
          className="pointer-events-none absolute animate-drift text-[clamp(0.65rem,1.6vw,0.85rem)] font-medium whitespace-nowrap text-white/25 select-none [text-shadow:0_1px_2px_rgb(0_0_0/0.35)]"
        >
          {watermark}
        </span>
      )}
      <button
        type="button"
        onClick={toggleFullscreen}
        className="absolute top-2 right-2 rounded-[6px] bg-black/55 px-2.5 py-1 text-xs font-semibold text-white opacity-80 hover:opacity-100 focus-visible:opacity-100 sm:opacity-0 sm:group-hover:opacity-100"
      >
        Full screen
      </button>
      {shownError && (
        <div className="absolute inset-x-0 bottom-0 bg-ink/90 px-4 py-3 text-sm text-white" role="alert">
          {shownError}
        </div>
      )}
    </div>
  );
}
