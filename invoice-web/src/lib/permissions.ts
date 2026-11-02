// Role permissions, mirroring the table in docs/INVOICES_PHASE2_API.md.
// The server is the source of truth (it returns 403); the UI only hides what a role can't do.

import { useAuth } from "./auth";
import type { Role } from "./types";

export type Action =
  /** Create/edit/send invoices, quotes, clients, items, recurring; record/delete payments. */
  | "docs.edit"
  /** Add/edit expenses. */
  | "expenses.edit"
  /** Profit report and CSV exports. */
  | "reports.view"
  /** See the settings page (read-only for Accountant; UI-only, not in the contract table). */
  | "settings.view"
  /** Business settings, tax rates, reminders, templates. */
  | "settings.edit"
  /** Team: invite, change roles, remove. */
  | "team.manage"
  /** Billing upgrade, Paystack key. */
  | "billing.manage";

const TABLE: Record<Action, readonly Role[]> = {
  "docs.edit": ["Owner", "Admin", "Staff"],
  "expenses.edit": ["Owner", "Admin", "Staff"],
  "reports.view": ["Owner", "Admin", "Accountant"],
  "settings.view": ["Owner", "Admin", "Accountant"],
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

/** The signed-in user's role in the current business. */
export function useRole(): Role | null {
  return toRole(useAuth()?.tenant.role);
}

/** `const allowed = useCan("docs.edit")` */
export function useCan(action: Action): boolean {
  return can(useAuth()?.tenant.role, action);
}

export const ROLE_DESCRIPTIONS: Record<Role, string> = {
  Owner: "Full access, including billing.",
  Admin: "Everything except billing: settings, team, reports and all documents.",
  Staff: "Creates and sends invoices, quotes and expenses. No settings, reports or team.",
  Accountant: "Read-only access to everything, plus the profit report and CSV exports.",
};
