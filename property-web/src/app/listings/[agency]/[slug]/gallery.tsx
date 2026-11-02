"use client";

import { useCallback, useEffect, useRef, useState, type TouchEvent } from "react";
import { createPortal } from "react-dom";
import type { Photo } from "@/lib/types";
import { ChevronLeftIcon, ChevronRightIcon, CloseIcon, ExpandIcon, ImageIcon } from "@/components/icons";
import { cx } from "@/components/ui";

/** Gallery first: one large photo plus a strip; tapping opens a full-screen viewer with swipe and arrow keys. */
export function Gallery({ photos, title }: { photos: Photo[]; title: string }) {
  const [index, setIndex] = useState(0);
  const [open, setOpen] = useState(false);
  const count = photos.length;
  const go = useCallback((d: number) => setIndex((i) => (count ? (i + d + count) % count : 0)), [count]);

  if (count === 0) {
    return (
      <div className="flex aspect-[16/9] items-center justify-center bg-plaster-2 text-faint sm:rounded-panel">
        <ImageIcon className="h-10 w-10" />
        <span className="sr-only">No photos yet</span>
      </div>
    );
  }

  const main = photos[index];
  return (
    <div>
      <div className="relative overflow-hidden bg-asphalt sm:rounded-panel">
        <button type="button" onClick={() => setOpen(true)} className="block w-full" aria-label={`Open photo ${index + 1} of ${count} full screen`}>
          <img
            src={main.cardUrl}
            srcSet={`${main.cardUrl} 960w, ${main.fullUrl} 1920w`}
            sizes="(min-width: 1024px) 66vw, 100vw"
            alt={`${title}, photo ${index + 1} of ${count}`}
            className="aspect-[4/3] w-full object-cover sm:aspect-[16/9] lg:aspect-auto lg:h-[min(32rem,60vh)]"
          />
        </button>
        {count > 1 && (
          <>
            <button
              type="button"
              onClick={() => go(-1)}
              className="absolute top-1/2 left-2 flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-surface/90 text-asphalt hover:bg-surface"
              aria-label="Previous photo"
            >
              <ChevronLeftIcon />
            </button>
            <button
              type="button"
              onClick={() => go(1)}
              className="absolute top-1/2 right-2 flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-surface/90 text-asphalt hover:bg-surface"
              aria-label="Next photo"
            >
              <ChevronRightIcon />
            </button>
          </>
        )}
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="absolute right-2 bottom-2 inline-flex min-h-11 items-center gap-1.5 rounded-control bg-asphalt/80 px-3 text-sm font-semibold text-white"
        >
          <ExpandIcon className="h-4 w-4" />
          <span className="tnum">
            {index + 1} / {count}
          </span>
        </button>
      </div>
      {count > 1 && (
        <ul className="no-scrollbar mt-2 flex gap-2 overflow-x-auto px-4 sm:px-0" aria-label="Photos">
          {photos.map((p, i) => (
            <li key={p.thumbUrl + i} className="shrink-0">
              <button
                type="button"
                onClick={() => setIndex(i)}
                aria-label={`Show photo ${i + 1}`}
                aria-current={i === index}
                className={cx("block overflow-hidden rounded-[4px] border-2", i === index ? "border-adire" : "border-transparent opacity-80 hover:opacity-100")}
              >
                <img src={p.thumbUrl} alt="" loading="lazy" className="h-14 w-20 object-cover sm:h-16 sm:w-24" />
              </button>
            </li>
          ))}
        </ul>
      )}
      {open && <Viewer photos={photos} title={title} index={index} onIndex={setIndex} onClose={() => setOpen(false)} />}
    </div>
  );
}

function Viewer({ photos, title, index, onIndex, onClose }: { photos: Photo[]; title: string; index: number; onIndex: (i: number) => void; onClose: () => void }) {
  const count = photos.length;
  const touch = useRef<{ x: number; y: number } | null>(null);
  const [dx, setDx] = useState(0);
  const closeRef = useRef<HTMLButtonElement>(null);
  const go = useCallback((d: number) => onIndex((index + d + count) % count), [index, count, onIndex]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowLeft") go(-1);
      if (e.key === "ArrowRight") go(1);
    };
    document.addEventListener("keydown", onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = prev;
    };
  }, [go, onClose]);

  const start = (e: TouchEvent) => (touch.current = { x: e.touches[0].clientX, y: e.touches[0].clientY });
  const move = (e: TouchEvent) => {
    if (!touch.current) return;
    setDx(e.touches[0].clientX - touch.current.x);
  };
  const end = () => {
    if (Math.abs(dx) > 60) go(dx < 0 ? 1 : -1);
    setDx(0);
    touch.current = null;
  };

  const p = photos[index];
  const reduce = typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  return createPortal(
    <div role="dialog" aria-modal="true" aria-label={`Photos of ${title}`} className="on-asphalt fixed inset-0 z-50 flex flex-col bg-asphalt text-white">
      <div className="flex items-center justify-between px-3 py-2">
        <p className="tnum text-sm text-on-asphalt" aria-live="polite">
          Photo {index + 1} of {count}
        </p>
        <button ref={closeRef} type="button" onClick={onClose} className="flex h-11 w-11 items-center justify-center rounded-control hover:bg-white/10" aria-label="Close photos">
          <CloseIcon />
        </button>
      </div>
      <div className="relative flex min-h-0 flex-1 items-center justify-center overflow-hidden" onTouchStart={start} onTouchMove={move} onTouchEnd={end}>
        <img
          key={p.fullUrl}
          src={p.fullUrl}
          alt={`${title}, photo ${index + 1} of ${count}`}
          className="max-h-full max-w-full object-contain select-none"
          style={{ transform: dx ? `translateX(${dx}px)` : undefined, transition: dx || reduce ? "none" : "transform 200ms ease-out" }}
          draggable={false}
        />
        {count > 1 && (
          <>
            <button type="button" onClick={() => go(-1)} className="absolute left-2 hidden h-12 w-12 items-center justify-center rounded-full bg-white/10 hover:bg-white/20 sm:flex" aria-label="Previous photo">
              <ChevronLeftIcon />
            </button>
            <button type="button" onClick={() => go(1)} className="absolute right-2 hidden h-12 w-12 items-center justify-center rounded-full bg-white/10 hover:bg-white/20 sm:flex" aria-label="Next photo">
              <ChevronRightIcon />
            </button>
          </>
        )}
      </div>
      <p className="px-4 py-3 text-center text-xs text-on-asphalt">Swipe or use the arrow keys. Photos carry the agency&apos;s watermark.</p>
    </div>,
    document.body,
  );
}
