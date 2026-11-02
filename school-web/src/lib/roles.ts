// School roles. The server is the source of truth (it returns 403); the UI only hides what a role can't do.
// Staff are the shared team roles, labelled in the school's own words: Owner "Proprietor", Admin "Principal" or "School admin"
// (their job title), Accountant "Bursar", Staff "Teacher".

import { useAuth } from "./auth";
import type { Role } from "./types";

export const ROLES: readonly Role[] = ["Owner", "Admin", "Accountant", "Staff"];

export function toRole(role: string | null | undefined): Role | null {
  if (!role) return null;
  return ROLES.find((r) => r.toLowerCase() === role.toLowerCase()) ?? null;
}

/** The label a school uses for a role. Admins show their job title when it is one of the admin titles. */
export function roleLabel(role: string | null | undefined, jobTitle?: string | null): string {
  switch (toRole(role)) {
    case "Owner":
      return "Proprietor";
    case "Admin":
      return jobTitle && jobTitle.trim() ? jobTitle.trim() : "School admin";
    case "Accountant":
      return "Bursar";
    case "Staff":
      return "Teacher";
    default:
      return "Staff";
  }
}

/** Roles staff can be invited as, with what each can do. */
export const INVITE_ROLES: { role: Role; label: string; text: string }[] = [
  { role: "Admin", label: "Principal or school admin", text: "Runs setup, students, staff, results and fees. No billing." },
  { role: "Accountant", label: "Bursar", text: "Fees, payments and expenses. Reads the student list." },
  { role: "Staff", label: "Teacher", text: "Takes the register and enters scores for their own classes." },
];

export type Action =
  /** Levels, classes, subjects, assessment, grading, students, import, invites, staff, promotion. */
  | "school.manage"
  /** ID cards. */
  | "idcards.print"
  /** Read the student list. */
  | "students.read"
  /** Fees: items, structure, invoices, payments, debtors, expenses, reports (the API's fee policy). */
  | "fees.manage"
  /** Results hold and the fee reminder schedule. */
  | "fees.settings"
  /** The school's own Paystack key. */
  | "fees.paystack"
  /** Attendance, scores, results, the timetable, assignments, lesson plans and exams (leadership and teachers; not the bursar). */
  | "academic.read";

const TABLE: Record<Action, readonly Role[]> = {
  "school.manage": ["Owner", "Admin"],
  "idcards.print": ["Owner", "Admin", "Accountant"],
  "students.read": ["Owner", "Admin", "Accountant", "Staff"],
  "fees.manage": ["Owner", "Admin", "Accountant"],
  "fees.settings": ["Owner", "Admin"],
  "fees.paystack": ["Owner"],
  "academic.read": ["Owner", "Admin", "Staff"],
};

export function can(role: string | null | undefined, action: Action): boolean {
  const r = toRole(role);
  return r !== null && TABLE[action].includes(r);
}

export function useRole(): Role | null {
  return toRole(useAuth()?.tenant?.role);
}

export function useCan(action: Action): boolean {
  const auth = useAuth();
  // A support view is read only.
  if (auth?.support && action !== "students.read" && action !== "idcards.print") return false;
  return can(auth?.tenant?.role, action);
}
