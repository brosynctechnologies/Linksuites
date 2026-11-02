// Status colour is always paired with the word. Tones stay on the LearnLink palette:
// neutral (draft, not listed), lagoon (in progress: in review, processing, requested, pending),
// lime (done: published, approved, ready, paid, available), brick (rejected, failed, reversed).

import type { CourseStatus, EarningStatus, MarketplaceStatus, PayoutStatus, VideoStatus } from "./types";

export type Tone = "neutral" | "lagoon" | "lime" | "brick" | "coral";

export const COURSE_STATUS: Record<CourseStatus, { label: string; tone: Tone }> = {
  Draft: { label: "Draft", tone: "neutral" },
  Published: { label: "Published", tone: "lime" },
  Archived: { label: "Archived", tone: "neutral" },
};

export const MARKETPLACE_STATUS: Record<MarketplaceStatus, { label: string; tone: Tone }> = {
  NotListed: { label: "Not on marketplace", tone: "neutral" },
  InReview: { label: "In review", tone: "lagoon" },
  Approved: { label: "On marketplace", tone: "lime" },
  Rejected: { label: "Rejected", tone: "brick" },
};

export const VIDEO_STATUS: Record<VideoStatus, { label: string; tone: Tone }> = {
  None: { label: "No video", tone: "neutral" },
  Processing: { label: "Processing", tone: "lagoon" },
  Ready: { label: "Ready", tone: "lime" },
  Failed: { label: "Failed", tone: "brick" },
};

export const PAYOUT_STATUS: Record<PayoutStatus, { label: string; tone: Tone }> = {
  Requested: { label: "Requested", tone: "lagoon" },
  Paid: { label: "Paid", tone: "lime" },
  Rejected: { label: "Rejected", tone: "brick" },
};

export const EARNING_STATUS: Record<EarningStatus, { label: string; tone: Tone }> = {
  Pending: { label: "Pending", tone: "lagoon" },
  Available: { label: "Available", tone: "lime" },
  PaidOut: { label: "Paid out", tone: "neutral" },
  Reversed: { label: "Refunded", tone: "brick" },
};

export function statusOf<K extends string>(map: Record<K, { label: string; tone: Tone }>, key: string | null | undefined) {
  return (key && map[key as K]) || { label: key ?? "—", tone: "neutral" as Tone };
}
