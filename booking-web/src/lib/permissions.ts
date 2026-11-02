// Role permissions, mirroring the roles paragraph in docs/BOOKINGS_API.md:
// Owner/Admin manage settings, services, staff, team; Staff create and manage bookings and customers;
// Accountant reads and sees reports. The server is the source of truth (it returns 403);
// the UI only hides what a role can't do.

import { useAuth } from "./auth";
import type { Role } from "./types";

export type Action =
  /** Create, reschedule, change status of bookings; record payments; edit customers. */
  | "bookings.edit"
  /** Refund an online deposit through Paystack. */
  | "bookings.refund"
  /** Reports page. */
  | "reports.view"
  /** See the settings page (read-only for Accountant). */
  | "settings.view"
  /** Business settings, hours, booking rules, Paystack. */
  | "settings.edit"
  /** Services and staff (create, edit, schedules, time off). */
  | "catalog.edit"
  /** Team: invite, change roles, remove. */
  | "team.manage"
  /** Billing upgrade. */
  | "billing.manage";

const TABLE: Record<Action, readonly Role[]> = {
  "bookings.edit": ["Owner", "Admin", "Staff"],
  "bookings.refund": ["Owner", "Admin"],
  "reports.view": ["Owner", "Admin", "Accountant"],
  "settings.view": ["Owner", "Admin", "Accountant"],
  "settings.edit": ["Owner", "Admin"],
  "catalog.edit": ["Owner", "Admin"],
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

/** `const allowed = useCan("bookings.edit")` */
export function useCan(action: Action): boolean {
  return can(useAuth()?.tenant.role, action);
}

export const ROLE_DESCRIPTIONS: Record<Role, string> = {
  Owner: "Full access, including billing.",
  Admin: "Everything except billing: settings, services, staff, team, reports and all bookings.",
  Staff: "Takes bookings and looks after customers. No settings, reports or team.",
  Accountant: "Sees everything without changing it, plus reports.",
};
