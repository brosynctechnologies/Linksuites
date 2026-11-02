"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api, apiUrl, toApiError, type ApiError } from "@/lib/api";
import { AwardIcon, DownloadIcon, StarIcon } from "@/components/icons";
import { Button, ErrorBox, Modal, Notice, buttonClass, cx, inputClass } from "@/components/ui";

/** The certificate moment: shown once when the last lesson or quiz completes the course. */
export function CertificateMoment({
  open,
  onClose,
  courseTitle,
  certificateCode,
  onRate,
}: {
  open: boolean;
  onClose: () => void;
  courseTitle: string;
  certificateCode: string | null;
  onRate: () => void;
}) {
  return (
    <Modal open={open} title="Course complete" onClose={onClose}>
      <div className="animate-cert-in py-2 text-center">
        <span className="mx-auto flex h-16 w-16 animate-node-fill items-center justify-center rounded-full bg-lime text-white" aria-hidden="true">
          <AwardIcon className="h-8 w-8" />
        </span>
        <p className="mt-5 text-sm text-muted">You finished</p>
        <p className="mt-1 font-serif text-xl leading-snug font-semibold text-ink">{courseTitle}</p>
        {certificateCode ? (
          <>
            <p className="mt-3 text-slate-700">Your certificate is ready. Anyone can check it on its verify page.</p>
            <div className="mt-5 flex flex-wrap justify-center gap-2">
              <a href={apiUrl(`/api/certificates/${encodeURIComponent(certificateCode)}/pdf`)} target="_blank" rel="noopener noreferrer" className={buttonClass("primary")}>
                <DownloadIcon className="h-4 w-4" /> Download certificate
              </a>
              <Link href={`/certificates/${encodeURIComponent(certificateCode)}`} className={buttonClass("secondary")}>
                Verify page
              </Link>
            </div>
          </>
        ) : (
          <p className="mt-3 text-slate-700">Every lesson done and every quiz passed. That&apos;s the whole path.</p>
        )}
        <button type="button" onClick={onRate} className="mt-5 text-sm font-semibold text-lagoon-text hover:underline">
          Rate this course
        </button>
      </div>
    </Modal>
  );
}

/** Rate the course (POST the first time, PUT to change it). */
export function ReviewModal({ open, onClose, courseId }: { open: boolean; onClose: () => void; courseId: string }) {
  return (
    <Modal open={open} title="Rate this course" onClose={onClose}>
      {open && <ReviewForm courseId={courseId} onDone={onClose} />}
    </Modal>
  );
}

function ReviewForm({ courseId, onDone }: { courseId: string; onDone: () => void }) {
  const [rating, setRating] = useState(0);
  const [hover, setHover] = useState(0);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [done, setDone] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!rating) return;
    setBusy(true);
    setError(null);
    const body = { rating, comment: comment.trim() || null };
    try {
      try {
        await api(`/api/learn/courses/${courseId}/review`, { method: "POST", body });
      } catch (err) {
        const e2 = toApiError(err);
        if (e2.status !== 409) throw e2;
        await api(`/api/learn/courses/${courseId}/review`, { method: "PUT", body });
      }
      setDone(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <div className="space-y-4">
        <Notice tone="success">Thanks. Your review helps other learners choose.</Notice>
        <Button className="w-full" onClick={onDone}>
          Close
        </Button>
      </div>
    );
  }

  const shown = hover || rating;
  return (
    <form onSubmit={submit} className="space-y-4">
      <fieldset>
        <legend className="text-sm font-medium text-slate-700">Your rating</legend>
        <div className="mt-2 flex gap-1" onMouseLeave={() => setHover(0)}>
          {[1, 2, 3, 4, 5].map((n) => (
            <label key={n} className="cursor-pointer rounded-control p-1 has-[:focus-visible]:outline-[3px] has-[:focus-visible]:outline-lagoon has-[:focus-visible]:outline-solid" onMouseEnter={() => setHover(n)}>
              <input type="radio" name="rating" value={n} className="sr-only" checked={rating === n} onChange={() => setRating(n)} />
              <StarIcon filled={n <= shown} className={cx("h-8 w-8", n <= shown ? "text-ink" : "text-slate-300")} />
              <span className="sr-only">
                {n} star{n === 1 ? "" : "s"}
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      <div>
        <label htmlFor="review-comment" className="text-sm font-medium text-slate-700">
          What would you tell another learner? (optional)
        </label>
        <textarea id="review-comment" rows={4} className={cx(inputClass, "mt-1.5 leading-normal")} value={comment} onChange={(e) => setComment(e.target.value)} />
      </div>
      {error && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy} disabled={!rating}>
        Post review
      </Button>
    </form>
  );
}
