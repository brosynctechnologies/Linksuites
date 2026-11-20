import { useAuth } from "./auth";
import type { InviteRole, Role } from "./types";

/**
 * Who may do what in the dashboard. The API enforces every rule (403); these only shape the UI.
 * - Owner: everything.
 * - Admin: everything except billing and payment settings (the billing page is read only).
 * - Staff: orders and products. No settings.
 */

export const ROLE_DESCRIPTIONS: Record<InviteRole, string> = {
  Admin: "Everything except billing and payment settings",
  Staff: "Orders and products. No settings",
};

export function toRole(value: string | null | undefined): Role {
  return value === "Owner" || value === "Admin" ? value : "Staff";
}

/** The signed-in role for this store. Null before hydration or when signed out. */
export function useRole(): Role | null {
  const auth = useAuth();
  return auth ? toRole(auth.tenant.role) : null;
}

/** Owner or Admin: may open the store's settings pages. */
export function canManageSettings(role: Role | null): boolean {
  return role === "Owner" || role === "Admin";
}

/** Dashboard pages that are settings: Staff sees "Ask the store owner" instead of the page. */
export const SETTINGS_PATHS = [
  "/dashboard/delivery",
  "/dashboard/discounts",
  "/dashboard/settings",
  "/dashboard/billing",
  "/dashboard/team",
  "/dashboard/payments",
];

export function isSettingsPath(pathname: string): boolean {
  return SETTINGS_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`));
}
