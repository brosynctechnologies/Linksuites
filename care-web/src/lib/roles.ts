// CareLink roles. The server is the source of truth (it returns 403 and leaves clinical fields out); the UI hides what a role can't
// do and never renders clinical fields for a role without clinical access, even if they were present.
//
// Clinic role labels (global constraints):
//   Medical director = Owner (clinical role Doctor by default; always has clinical access)
//   Admin            = Admin (clinical role optional; only the medical director gives an admin one)
//   Doctor           = Staff + Doctor
//   Nurse            = Staff + Nurse
//   Front desk       = Staff + FrontDesk
//   Cashier          = Accountant (never a clinical role)
// "Clinical access" = Owner, or a clinical role of Doctor or Nurse (never the cashier, never a support view).

import type { CareMeDto, ClinicalRole, Role } from "./types";

export const CLINICAL_ROLES: { value: ClinicalRole; label: string }[] = [
  { value: "Doctor", label: "Doctor" },
  { value: "Nurse", label: "Nurse" },
  { value: "FrontDesk", label: "Front desk" },
  { value: "None", label: "None" },
];

export function clinicalRoleLabel(c: ClinicalRole | null | undefined): string {
  return CLINICAL_ROLES.find((r) => r.value === c)?.label ?? "None";
}

/** The clinic's word for a member: "Medical director", "Admin" (with "doctor" or "nurse" when they have one), "Doctor", "Cashier"... */
export function careRoleLabel(memberRole: Role | string | null | undefined, clinicalRole?: ClinicalRole | null): string {
  switch (memberRole) {
    case "Owner":
      return "Medical director";
    case "Admin":
      return clinicalRole === "Doctor" ? "Admin and doctor" : clinicalRole === "Nurse" ? "Admin and nurse" : "Admin";
    case "Accountant":
      return "Cashier";
    case "Staff":
      if (clinicalRole === "Doctor") return "Doctor";
      if (clinicalRole === "Nurse") return "Nurse";
      if (clinicalRole === "FrontDesk") return "Front desk";
      return clinicalRole ? "Staff, no clinic role" : "Staff";
    default:
      return "Staff";
  }
}

/** The clinic roles someone can be invited as or moved to, and the platform role plus clinical role each one stores. */
export type ClinicRoleKey = "Admin" | "Doctor" | "Nurse" | "FrontDesk" | "Cashier";
export const CLINIC_ROLES: { key: ClinicRoleKey; label: string; memberRole: Role; clinicalRole: ClinicalRole; text: string }[] = [
  { key: "Doctor", label: "Doctor", memberRole: "Staff", clinicalRole: "Doctor", text: "Consultations, diagnoses, prescriptions, tests, admissions and ward rounds." },
  { key: "Nurse", label: "Nurse", memberRole: "Staff", clinicalRole: "Nurse", text: "Vitals and triage, nursing notes, the fluid chart and doses." },
  { key: "FrontDesk", label: "Front desk", memberRole: "Staff", clinicalRole: "FrontDesk", text: "Registration, appointments, check-in, the queue and beds. No clinical notes." },
  { key: "Cashier", label: "Cashier", memberRole: "Accountant", clinicalRole: "None", text: "Bills, deposits, payments, receipts and reports. No clinical notes." },
  { key: "Admin", label: "Admin", memberRole: "Admin", clinicalRole: "None", text: "Staff, settings, wards, prices and reports. Clinical notes only with a clinical role." },
];

/** Which clinic role a member holds now (Owner is not in the list: the medical director's role never changes). */
export function clinicRoleKey(memberRole: Role, clinicalRole: ClinicalRole): ClinicRoleKey | null {
  if (memberRole === "Admin") return "Admin";
  if (memberRole === "Accountant") return "Cashier";
  if (memberRole === "Staff") {
    if (clinicalRole === "Doctor") return "Doctor";
    if (clinicalRole === "Nurse") return "Nurse";
    if (clinicalRole === "FrontDesk") return "FrontDesk";
  }
  return null;
}

/** What the signed-in member may do in the UI. Every flag comes from the server's own answer at `GET /api/care/staff/me`. */
export interface CareAccess {
  memberRole: Role;
  clinicalRole: ClinicalRole;
  /** Owner. */
  isDirector: boolean;
  /** Owner or Admin: staff, setup, settings, waivers. */
  isLeadership: boolean;
  isCashier: boolean;
  /** Clinical notes, allergies, conditions, blood group, genotype. Never true in a support view. */
  isClinical: boolean;
  /** Doctor or Nurse without leadership: they don't see money items (price list, bills, reports). */
  isClinicalOnly: boolean;
  /** Registers patients and edits demographics: everyone but the cashier. */
  canRegister: boolean;
  /** Price list, and later bills and reports: everyone but clinical-only staff. */
  canSeeMoney: boolean;
  /** Writes the price list: leadership or the cashier. */
  canEditPrices: boolean;
  /** A platform admin's read-only support view: no writes at all. */
  readOnly: boolean;
}

export function careAccess(me: CareMeDto, support: boolean): CareAccess {
  const isDirector = me.memberRole === "Owner";
  // The server's flags already account for the support view (never clinical); the extra && keeps the web strict either way.
  const isClinical = me.isClinical && !support;
  const isLeadership = me.isLeadership;
  const isCashier = me.isCashier;
  const clinicalStaff = me.clinicalRole === "Doctor" || me.clinicalRole === "Nurse";
  const isClinicalOnly = !isLeadership && !isCashier && clinicalStaff;
  return {
    memberRole: me.memberRole,
    clinicalRole: me.clinicalRole,
    isDirector,
    isLeadership,
    isCashier,
    isClinical,
    isClinicalOnly,
    canRegister: !isCashier && !support,
    canSeeMoney: !isClinicalOnly,
    canEditPrices: (isLeadership || isCashier) && !support,
    readOnly: support,
  };
}

/**
 * Navigation needs: an item is shown only when the member's access has this flag. "director" is the medical director (Owner), a support
 * view of the director included where the API allows it (the unsigned-notes list is readable in support view, Task 16 ruling).
 */
export type NavNeed = "leadership" | "clinical" | "money" | "desk" | "director" | "cashier" | "doctor";

/** Bills, payments and reports (Task 19): the cashier and leadership only. Front desk and clinical-only staff never get these screens. */
export function canTakeMoney(a: CareAccess): boolean {
  return a.isCashier || a.isLeadership;
}

/**
 * Post-build (user decision): doctors and the medical director review a test result before the patient's portal shows it. Never a nurse,
 * the front desk, the cashier, an admin without the doctor role, or the support view.
 */
export function canReviewResults(a: CareAccess): boolean {
  return !a.readOnly && a.isClinical && (a.isDirector || a.clinicalRole === "Doctor");
}

/** Front desk and leadership manage the patient's portal invites and links (Task 15 ruling); never the support view. */
export function canInvitePortal(a: CareAccess): boolean {
  return !a.readOnly && (a.isLeadership || (a.memberRole === "Staff" && a.clinicalRole === "FrontDesk"));
}

export function meets(a: CareAccess, need: NavNeed | undefined): boolean {
  switch (need) {
    case undefined:
      return true;
    case "leadership":
      return a.isLeadership;
    case "clinical":
      return a.isClinical;
    case "money":
      return a.canSeeMoney;
    case "desk":
      return !a.isCashier;
    case "director":
      return a.isDirector;
    case "cashier":
      return canTakeMoney(a);
    case "doctor":
      return a.isClinical && (a.isDirector || a.clinicalRole === "Doctor");
  }
}
