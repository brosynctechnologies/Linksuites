import type { BankTransfer, PendingTransfer } from "@/lib/plan-payment";

// Types mirroring the CareLink API (api/src/Saas.Application/Care/*) and the shared auth, team, billing and notification parts.
// Enums travel as strings. Dates are "YYYY-MM-DD"; instants are ISO 8601 with an offset. Money is in kobo.

import type { ImportPreviewRow } from "./types/imports";

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;
  /** Import confirm (409 import_errors): the rows that still have errors. */
  rows?: ImportPreviewRow[];
}

// ---- Auth ----
export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
  isPlatformAdmin?: boolean;
}
export type Role = "Owner" | "Admin" | "Staff" | "Accountant";
export interface AuthTenant {
  id: string;
  name: string;
  slug: string;
  role: Role | string;
  product?: string;
}
/** Staff tokens carry the clinic (tenant); patient portal tokens have `tenant: null`. */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant | null;
}
export interface SupportViewDto {
  sessionId: string;
  adminName: string;
  reason: string;
  startedAt: string;
  endsAt: string;
  endedAt: string | null;
}
/** `GET /api/public/invites/{token}`. It doesn't carry the invite's clinical role. */
export interface PublicInviteDto {
  businessName: string;
  email: string;
  role: Role;
  hasAccount: boolean;
  expired: boolean;
  /** The clinic role the invite is for ("Nurse", "Front desk", "Admin (doctor)"); null outside CareLink. */
  roleLabel?: string | null;
}

// ---- Team (shared) ----
export type ClinicalRole = "None" | "Doctor" | "Nurse" | "FrontDesk";
export interface MemberDto {
  /** The membership id: PATCH and DELETE /api/team/members/{id} take it. */
  id: string;
  userId: string;
  fullName: string;
  email: string;
  role: Role;
  isYou: boolean;
  joinedAt: string;
}
export interface TeamInviteDto {
  id: string;
  email: string;
  role: Role;
  invitedAt: string;
  expiresAt: string;
  clinicalRole?: ClinicalRole | null;
}
export interface TeamDto {
  members: MemberDto[];
  invites: TeamInviteDto[];
  seats: { used: number; limit: number };
}

// ---- Billing (shared) ----
export interface PlanDto {
  code: string;
  name: string;
  priceMinor: number;
  currency: string;
  interval: string;
  limits: Record<string, number>;
  features: string[];
}
export interface BillingDto {
  currentPlan: PlanDto;
  subscription: { status: string; currentPeriodEnd: string | null; complimentary: boolean } | null;
  plans: PlanDto[];
  usage: { items: { key: string; label: string; used: number; limit: number }[] };
  paymentsEnabled: boolean;
  /** The company account to pay a plan into by bank transfer: only while online payment is off. */
  bankTransfer?: BankTransfer | null;
  /** A bank transfer this business said it sent, waiting for confirmation. */
  pendingTransfer?: PendingTransfer | null;
}

// ---- Notifications (shared) ----
export interface UserNotificationDto {
  id: string;
  product: string;
  kind: string;
  title: string;
  body: string;
  link: string;
  tenantId: string | null;
  createdAt: string;
  readAt: string | null;
}
/** `GET /api/me/notifications?unread=&page=`: newest first, 20 a page. */
export interface NotificationPage {
  items: UserNotificationDto[];
  page: number;
  pageSize: number;
  total: number;
}
/** `GET /api/me/notifications/unread-count`. */
export interface UnreadCountDto {
  count: number;
}

// ---- Care: who you are ----
/** `GET /api/care/staff/me`. The flags come from the server's own CareCaller (support view: never clinical). */
export interface CareMeDto {
  userId: string;
  fullName: string;
  memberRole: Role;
  clinicalRole: ClinicalRole;
  title: string | null;
  licenceNumber: string | null;
  isClinical: boolean;
  isLeadership: boolean;
  isCashier: boolean;
}

// ---- Care: setup and settings ----
export interface ClinicDto {
  name: string;
  slug: string;
  address: string | null;
  phone: string | null;
  patientPrefix: string;
  hasLogo: boolean;
  hasPaystackKey: boolean;
  dispensesDrugs: boolean;
  minDepositMinor: number;
  lowDepositThresholdMinor: number;
  registrationPriceItemId: string | null;
  consultationPriceItemId: string | null;
}
export interface CareChecklistItemDto {
  key: "profile" | "price-list" | "wards" | "staff" | string;
  label: string;
  done: boolean;
}
export interface CareSetupDto {
  clinic: ClinicDto;
  checklist: CareChecklistItemDto[];
}
/**
 * PUT /api/care/settings/profile. A full replace: the price-item ids are set to whatever is sent (null clears them), so the form always
 * sends both.
 */
export interface SaveClinicProfileRequest {
  name: string;
  address: string | null;
  phone: string | null;
  patientPrefix: string;
  dispensesDrugs: boolean;
  minDepositMinor: number;
  lowDepositThresholdMinor: number;
  registrationPriceItemId: string | null;
  consultationPriceItemId: string | null;
}
export interface CarePaystackSettingsDto {
  connected: boolean;
  /** False when the plan doesn't include online payments. */
  available: boolean;
  webhookUrl: string;
}

// ---- Care: staff ----
export interface CareStaffDto {
  userId: string;
  membershipId: string;
  fullName: string;
  email: string;
  memberRole: Role;
  clinicalRole: ClinicalRole;
  title: string | null;
  licenceNumber: string | null;
  hasSignature: boolean;
  isClinical: boolean;
  isYou: boolean;
}

// ---- Care: price list ----
export type PriceCategory = "Consultation" | "Procedure" | "Test" | "Drug" | "Bed" | "Registration" | "Other";
export interface PriceItemDto {
  id: string;
  name: string;
  category: PriceCategory;
  priceMinor: number;
  active: boolean;
}

// ---- Care: patients ----
export type PatientSex = "Female" | "Male";
export type PatientStatus = "Active" | "Inactive" | "Deceased";
export interface PatientSummaryDto {
  id: string;
  number: string;
  fullName: string;
  sex: PatientSex;
  dateOfBirth: string;
  phone: string | null;
  status: PatientStatus;
}
/** Demographics: what every staff member may see. */
export interface PatientDemographics {
  id: string;
  number: string;
  firstName: string;
  middleName: string | null;
  lastName: string;
  fullName: string;
  sex: PatientSex;
  dateOfBirth: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  nextOfKinName: string | null;
  nextOfKinPhone: string | null;
  nextOfKinRelationship: string | null;
  status: PatientStatus;
  createdAt: string;
}
/** Clinical facts. The API leaves them out for non-clinical callers; the web never renders them for those roles either way. */
export interface PatientClinicalFacts {
  bloodGroup?: string | null;
  genotype?: string | null;
  allergies?: string[] | null;
  conditions?: string[] | null;
  /** Bumped by every clinical-facts save; send it back as `version` (final review I1). Clinical callers only. */
  factsVersion?: number | null;
}
export type PatientDto = PatientDemographics & PatientClinicalFacts;
export interface RegisterPatientResult {
  patient: PatientDemographics;
  possibleDuplicates: PatientSummaryDto[];
}
export interface SavePatientRequest {
  firstName: string;
  middleName: string | null;
  lastName: string;
  sex: PatientSex | null;
  dateOfBirth: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  nextOfKinName: string | null;
  nextOfKinPhone: string | null;
  nextOfKinRelationship: string | null;
}
export interface SaveClinicalFactsRequest {
  allergies: string[];
  conditions: string[];
  bloodGroup: string | null;
  genotype: string | null;
  /** The `factsVersion` loaded; a stale one gets 409 "These clinical details were changed by someone else. Reload and try again." */
  version: number;
}
/** `GET /api/care/patients/{id}/access-log` (the medical director only). */
export interface RecordAccessDto {
  id: string;
  staffUserId: string;
  staffName: string;
  role: string;
  area: string;
  reason: string | null;
  emergency: boolean;
  at: string;
  areaLabel: string;
  /** A read through the patient portal (final review C1): `staffName` is the account's short name, `relationship` its relationship. */
  portal: boolean;
  relationship: string | null;
  /** "Nkem W. (parent), patient portal", or "Ada Obi, Doctor" for staff. */
  who: string;
}
