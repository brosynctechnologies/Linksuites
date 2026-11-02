/** Status words match the API; colour follows the design plan (Ok / Wait / Stop / neutral). */
export type Tone = "ok" | "wait" | "stop" | "neutral";

const OK = new Set(["active", "paid", "succeeded", "approved", "confirmed", "completed", "healthy", "restored", "sent", "upheld", "ready"]);
const WAIT = new Set(["pending", "requested", "inreview", "in review", "processing", "pastdue", "past due", "open", "late", "invited", "hidden"]);
const STOP = new Set(["suspended", "failed", "rejected", "cancelled", "closed", "refunded", "expired", "locked", "error", "removed", "dismissed", "reversed"]);

export function toneOf(status: string | null | undefined): Tone {
  const s = (status ?? "").toLowerCase();
  if (OK.has(s)) return "ok";
  if (WAIT.has(s)) return "wait";
  if (STOP.has(s)) return "stop";
  return "neutral";
}

/** "PastDue" → "Past due", "InReview" → "In review". Other API words are shown as-is. */
export function statusLabel(status: string | null | undefined): string {
  if (!status) return "—";
  return status.replace(/([a-z])([A-Z])/g, (_, a: string, b: string) => `${a} ${b.toLowerCase()}`);
}

/** Jobs: email/photos/videos run every few minutes; the others within 20 minutes. */
export function jobHealth(job: { name: string; lastSucceededAt: string | null; lastError: string | null; lastStartedAt: string | null }, now = Date.now()) {
  const fast = ["email", "photos", "videos"].includes(job.name.toLowerCase());
  const limitMin = fast ? 10 : 20;
  if (!job.lastSucceededAt) return { tone: "stop" as Tone, label: "Never succeeded" };
  const ageMin = (now - new Date(job.lastSucceededAt).getTime()) / 60_000;
  const errorAfterSuccess =
    !!job.lastError && !!job.lastStartedAt && new Date(job.lastStartedAt).getTime() > new Date(job.lastSucceededAt).getTime();
  if (ageMin > limitMin) return { tone: "wait" as Tone, label: "Late" };
  if (errorAfterSuccess) return { tone: "stop" as Tone, label: "Failing" };
  return { tone: "ok" as Tone, label: "Healthy" };
}
