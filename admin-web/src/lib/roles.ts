import type { PlatformRole } from "./types";

/** Who can use what (role column of each route in docs/PLATFORM_API.md). SuperAdmin can do everything. */
export type Area =
  | "overview"
  | "health"
  | "businesses"
  | "business.support" // suspend, reactivate, view as
  | "business.comp"
  | "contact" // see customer emails and phones, search businesses by them
  | "users" // includes unlock, sign-in code, password reset, sign out everywhere
  | "moderation"
  | "money"
  | "audit"
  | "admins"
  | "account";

export const AREA_ROLES: Record<Area, PlatformRole[] | "any"> = {
  overview: "any",
  health: "any",
  businesses: ["Support", "Finance"],
  "business.support": ["Support"],
  "business.comp": ["Finance"],
  contact: ["Support"],
  users: ["Support"],
  moderation: ["Moderator"],
  money: ["Finance"],
  audit: ["Support"],
  admins: ["SuperAdmin"],
  account: "any",
};

export function can(role: PlatformRole | null | undefined, area: Area): boolean {
  if (!role) return false;
  if (role === "SuperAdmin") return true;
  const allowed = AREA_ROLES[area];
  return allowed === "any" || allowed.includes(role);
}

export const ROLE_LABEL: Record<PlatformRole, string> = {
  SuperAdmin: "Super admin",
  Moderator: "Moderator",
  Finance: "Finance",
  Support: "Support",
};

export const ROLES: PlatformRole[] = ["SuperAdmin", "Moderator", "Finance", "Support"];

/** "You need the Finance role for this." / "You need the Support or Finance role for this." */
export function needRoleText(area: Area): string {
  const allowed = AREA_ROLES[area];
  if (allowed === "any") return "You do not have access to this.";
  const names = allowed.map((r) => ROLE_LABEL[r]);
  const list = names.length > 1 ? `${names.slice(0, -1).join(", ")} or ${names[names.length - 1]}` : names[0];
  return `You need the ${list} role for this.`;
}
