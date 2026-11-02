// Studio role permissions for an academy (tenant). The server is the source of truth (it returns 403);
// the UI only hides what a role can't do. Same four roles as the other products:
// Owner/Admin run the academy; Staff build courses and look after learners; Accountant reads money.

import { useAuth } from "./auth";
import type { Role } from "./types";

export type Action =
  /** Create and edit courses, lessons, quizzes; publish; submit to the marketplace. */
  | "courses.edit"
  /** Answer learner questions. */
  | "questions.answer"
  /** Students list and reviews. */
  | "learners.view"
  /** Coupons. */
  | "coupons.manage"
  /** Earnings, ledger, payouts list. */
  | "earnings.view"
  /** Request payouts, set the payout account, refund orders. */
  | "money.manage"
  /** Academy profile, slug, logo, brand colour. */
  | "settings.edit"
  /** Team: invite, change roles, remove. */
  | "team.manage"
  /** Plan upgrades. */
  | "billing.manage";

const TABLE: Record<Action, readonly Role[]> = {
  "courses.edit": ["Owner", "Admin", "Staff"],
  "questions.answer": ["Owner", "Admin", "Staff"],
  "learners.view": ["Owner", "Admin", "Staff", "Accountant"],
  "coupons.manage": ["Owner", "Admin"],
  "earnings.view": ["Owner", "Admin", "Accountant"],
  "money.manage": ["Owner", "Admin"],
  "settings.edit": ["Owner", "Admin"],
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

/** The signed-in user's role in the current academy (null for learner sessions). */
export function useRole(): Role | null {
  return toRole(useAuth()?.tenant?.role);
}

/** `const allowed = useCan("courses.edit")` */
export function useCan(action: Action): boolean {
  return can(useAuth()?.tenant?.role, action);
}

export const ROLE_DESCRIPTIONS: Record<Role, string> = {
  Owner: "Full access, including the plan and billing.",
  Admin: "Everything except billing: courses, learners, money, settings and team.",
  Staff: "Builds courses and answers learners. No money, settings or team.",
  Accountant: "Sees earnings, payouts and learners without changing courses.",
};
