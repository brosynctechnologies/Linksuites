// Status colour is always paired with the word. Tones stay on the PropertyLink palette:
// neutral (draft, closed), adire (in progress you act on), seal (live, approved, confirmed: verified-style good news),
// ochre (waiting: pending review, held, expiring), brick (rejected, failed, hidden, cancelled).

import { formatDay } from "./format";

export type Tone ="neutral" | "adire" | "seal" | "ochre" | "brick";
type Map = Record<string, { label: string; tone: Tone }>;

export const LISTING_STATUS: Map = {
  Draft: { label: "Draft", tone: "neutral" },
  Pending: { label: "Pending review", tone: "ochre" },
  Active: { label: "Live", tone: "seal" },
  Expired: { label: "Expired", tone: "ochre" },
  Rented: { label: "Rented", tone: "neutral" },
  Sold: { label: "Sold", tone: "neutral" },
  Rejected: { label: "Rejected", tone: "brick" },
  Hidden: { label: "Hidden (reported)", tone: "brick" },
};

export const ENQUIRY_STATUS: Map = {
  New: { label: "New", tone: "adire" },
  Replied: { label: "Replied", tone: "seal" },
  Closed: { label: "Closed", tone: "neutral" },
};

export const INSPECTION_STATUS: Map = {
  Requested: { label: "Requested", tone: "ochre" },
  Confirmed: { label: "Confirmed", tone: "seal" },
  Done: { label: "Done", tone: "neutral" },
  Cancelled: { label: "Cancelled", tone: "brick" },
  NoShow: { label: "No-show", tone: "brick" },
};

export const STAY_STATUS: Map = {
  Pending: { label: "Held, awaiting payment", tone: "ochre" },
  Confirmed: { label: "Confirmed", tone: "seal" },
  Completed: { label: "Completed", tone: "neutral" },
  Cancelled: { label: "Cancelled", tone: "brick" },
  Expired: { label: "Hold expired", tone: "neutral" },
};

export const VERIFICATION_STATUS: Map = {
  Pending: { label: "Pending review", tone: "ochre" },
  Approved: { label: "Approved", tone: "seal" },
  Rejected: { label: "Rejected", tone: "brick" },
};

export const BOOST_STATUS: Map = {
  Pending: { label: "Awaiting payment", tone: "ochre" },
  Active: { label: "Active", tone: "seal" },
  Expired: { label: "Ended", tone: "neutral" },
};

export const PAYOUT_STATUS: Map = {
  Requested: { label: "Requested", tone: "ochre" },
  Paid: { label: "Paid", tone: "seal" },
  Rejected: { label: "Rejected", tone: "brick" },
};

export const EARNING_STATUS: Map = {
  Pending: { label: "Pending", tone: "ochre" },
  Available: { label: "Available", tone: "seal" },
  PaidOut: { label: "Paid out", tone: "neutral" },
  Reversed: { label: "Refunded", tone: "brick" },
};

export const MEDIA_STATUS: Map = {
  Processing: { label: "Processing", tone: "ochre" },
  Ready: { label: "Ready", tone: "seal" },
  Failed: { label: "Failed", tone: "brick" },
};

/**
 * What renewing actually did, from the listing the API returns: a relisted listing from an unverified agency (or one with
 * open reports or a flagged photo) goes to review rather than live, and only a live listing has a term.
 */
export function renewNotice(fresh: { title: string; status: string; expiresAt: string | null }, named = false) {
  const what = named ? `"${fresh.title}"` : "The listing";
  if (fresh.status === "Pending") return `${what} was sent for review. It goes live, with a fresh term, once a moderator approves it.`;
  if (fresh.status === "Active") return fresh.expiresAt ? `${what} is live until ${formatDay(fresh.expiresAt)}.` : `${what} is live.`;
  return `${what} is now ${statusOf(LISTING_STATUS, fresh.status).label.toLowerCase()}.`;
}

export function statusOf(map: Map, key: string | null | undefined) {
  return (key && map[key]) || { label: key ?? "—", tone: "neutral" as Tone };
}
