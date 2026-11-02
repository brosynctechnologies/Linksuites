"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { phoneDigits } from "@/lib/format";
import type { AgentReviewDto } from "@/lib/types";
import { PhoneIcon, StarIcon, WhatsAppIcon } from "./icons";
import { Button, ErrorBox, Notice, Textarea, buttonClass, cx, linkClass } from "./ui";

/** Header contact buttons on agent and agency pages (no listing, so no lead events). */
export function ProfileContact({ phone, whatsApp, name }: { phone: string | null; whatsApp?: string | null; name: string }) {
  if (!phone && !whatsApp) return null;
  return (
    <div className="flex flex-wrap gap-2">
      {whatsApp && (
        <a
          href={`https://wa.me/${phoneDigits(whatsApp)}?text=${encodeURIComponent(`Hello ${name}, I found you on PropertyLink.`)}`}
          target="_blank"
          rel="noopener noreferrer"
          className={buttonClass("whatsapp")}
        >
          <WhatsAppIcon className="h-5 w-5" /> Chat on WhatsApp
        </a>
      )}
      {phone && (
        <a href={`tel:+${phoneDigits(phone)}`} className={buttonClass("secondary")}>
          <PhoneIcon className="h-4 w-4" /> Call {phone}
        </a>
      )}
    </div>
  );
}

/** "Report review": hides it until a moderator decides. */
export function ReportReviewButton({ reviewId }: { reviewId: string }) {
  const auth = useAuth();
  const [state, setState] = useState<"idle" | "busy" | "done" | "error">("idle");
  if (!auth) return null;
  if (state === "done") return <span className="text-xs text-muted">Reported. A moderator will check it.</span>;
  return (
    <button
      type="button"
      disabled={state === "busy"}
      className="min-h-11 text-xs font-medium text-muted hover:text-brick hover:underline"
      onClick={async () => {
        if (!window.confirm("Report this review? It will be hidden until a moderator checks it.")) return;
        setState("busy");
        try {
          await api(`/api/reviews/${reviewId}/report`, { method: "POST" });
          setState("done");
        } catch {
          setState("error");
        }
      }}
    >
      {state === "error" ? "Couldn't report. Try again" : "Report"}
    </button>
  );
}

/** Leave a review: allowed after an enquiry 24 h old, a done viewing or a completed stay (the API checks). */
export function ReviewForm({ agentSlug, agentName }: { agentSlug: string; agentName: string }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const pathname = usePathname();
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [done, setDone] = useState<AgentReviewDto | null>(null);

  if (!hydrated) return null;
  if (!auth) {
    return (
      <p className="text-sm text-slate-700">
        Dealt with {agentName}?{" "}
        <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={linkClass}>
          Log in to leave a review
        </Link>
        .
      </p>
    );
  }
  if (done) return <Notice tone="success">Thanks. Your review is live.</Notice>;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!rating) return;
    setBusy(true);
    setError(null);
    try {
      setDone(await api<AgentReviewDto>(`/api/agents/${agentSlug}/reviews`, { method: "POST", body: { rating, comment: comment.trim() || null } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-panel border border-line bg-surface p-4">
      <fieldset>
        <legend className="text-sm font-medium text-slate-700">Your rating for {agentName}</legend>
        <div className="mt-1 flex gap-1">
          {[1, 2, 3, 4, 5].map((n) => (
            <button
              key={n}
              type="button"
              onClick={() => setRating(n)}
              aria-pressed={rating === n}
              aria-label={`${n} star${n === 1 ? "" : "s"}`}
              className="flex h-11 w-11 items-center justify-center rounded-control hover:bg-plaster-2"
            >
              <StarIcon filled={n <= rating} className={cx("h-6 w-6", n <= rating ? "text-asphalt" : "text-slate-300")} />
            </button>
          ))}
        </div>
      </fieldset>
      <Textarea label="What was it like? (optional)" rows={3} value={comment} onChange={(e) => setComment(e.target.value)} error={error?.field("comment")} />
      {error?.status === 403 ? (
        <Notice tone="warning">You can review an agent 24 hours after an enquiry, or after a viewing or stay with them.</Notice>
      ) : error?.status === 409 ? (
        <Notice tone="info">You have already reviewed {agentName}.</Notice>
      ) : (
        <ErrorBox error={error} />
      )}
      <Button type="submit" loading={busy} disabled={!rating}>
        Post review
      </Button>
    </form>
  );
}
