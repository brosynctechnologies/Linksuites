// CareLink lists shared by several screens.

import type { PatientSex, PriceCategory } from "./types";

export const PRICE_CATEGORIES: { value: PriceCategory; label: string }[] = [
  { value: "Consultation", label: "Consultation" },
  { value: "Registration", label: "Registration" },
  { value: "Procedure", label: "Procedure" },
  { value: "Test", label: "Test" },
  { value: "Drug", label: "Drug" },
  { value: "Bed", label: "Bed" },
  { value: "Other", label: "Other" },
];

export const SEXES: PatientSex[] = ["Female", "Male"];

/** The API's lists (PatientService.BloodGroups and Genotypes). */
export const BLOOD_GROUPS = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
export const GENOTYPES = ["AA", "AS", "AC", "SS", "SC", "CC"];

/** Next of kin relationships offered (free text is accepted too). */
export const RELATIONSHIPS = ["Spouse", "Parent", "Child", "Sibling", "Guardian", "Relative", "Friend"];

/** The header that carries an emergency-access reason (RecordGuard.ReasonHeader). Percent-encoded UTF-8; never in the address. */
export const REASON_HEADER = "X-Care-Reason";
export function reasonHeaders(reason: string | null | undefined): Record<string, string> | undefined {
  return reason ? { [REASON_HEADER]: encodeURIComponent(reason) } : undefined;
}
