// Studio role permissions for an agency (tenant). The server is the source of truth (it returns 403);
// the UI only hides what a role can't do. Contract: reads are open to any member; writes are Owner/Admin/Staff
// unless noted; money is Owner/Admin; payouts and the payout account are Owner only.

import { useAuth } from "./auth";
import type { Role } from "./types";

export type Action =
  /** Create, edit, submit, renew and delete listings; photos; short-let calendars. */
  | "listings.edit"
  /** Enquiry status, viewing times and weekly windows, agent profile. */
  | "leads.manage"
  /** Buy boosts, cancel stays as host. */
  | "money.manage"
  /** Request payouts and set the payout account. */
  | "payouts.manage"
  /** Agency profile and verification documents. */
  | "agency.edit"
  /** Team: invite, change roles, remove. */
  | "team.manage"
  /** Plan upgrades. */
  | "billing.manage";

const TABLE: Record<Action, readonly Role[]> = {
  "listings.edit": ["Owner", "Admin", "Staff"],
  "leads.manage": ["Owner", "Admin", "Staff"],
  "money.manage": ["Owner", "Admin"],
  "payouts.manage": ["Owner"],
  "agency.edit": ["Owner", "Admin"],
  "team.manage": ["Owner", "Admin"],
  "billing.manage": ["Owner"],
};

export const ROLES: readonly Role[] = ["Owner", "Admin", "Staff", "Accountant"];

/** Normalise whatever the server sent (e.g. "owner") to a known role; unknown roles get no extra rights. */
export function toRole(role: string | null | undefined): Role | null {
  if (!role) return null;
  const hit = ROLES.find((r) => r.toLowerCase() === role.toLowerCase());
  return hit ?? null;
}

export function can(role: string | null | undefined, action: Action): boolean {
  const r = toRole(role);
  return r !== null && TABLE[action].includes(r);
}

/** The signed-in user's role in the current agency (null for seeker sessions). */
export function useRole(): Role | null {
  return toRole(useAuth()?.tenant?.role);
}

/** `const allowed = useCan("listings.edit")` */
export function useCan(action: Action): boolean {
  return can(useAuth()?.tenant?.role, action);
}

export const ROLE_DESCRIPTIONS: Record<Role, string> = {
  Owner: "Full access, including the plan, payouts and the payout account.",
  Admin: "Everything except billing and payouts: listings, leads, boosts, verification and team.",
  Staff: "Posts and edits listings, answers enquiries and runs viewings. No money, verification or team.",
  Accountant: "Sees listings, leads, stays and earnings without changing them.",
};
