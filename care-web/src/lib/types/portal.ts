// Types for the patient portal (Task 20), mirroring api/src/Saas.Application/Care: PortalService, PatientPortalAccess,
// PatientInviteService and OnlinePaymentService. Money is in kobo; instants are ISO 8601 (UTC); dates "YYYY-MM-DD" (Lagos).

import type { PatientSex, PatientStatus } from "../types";
import type { CarePaymentStatus, PatientLinkRelationship } from "./billing";
import type { AppointmentStatus, BillStatus, DischargeType, DoseFrequency, TestKind } from "./clinical";
import type { AdmissionStatus, PayMethod } from "./inpatient";

/** `GET /api/care-portal/patients`: the account's linked patients across clinics (suspended clinics left out). */
export interface PortalPatientRef {
  id: string;
  clinicId: string;
  clinicName: string;
  clinicSlug: string;
  number: string;
  firstName: string;
  lastName: string;
  fullName: string;
  relationship: PatientLinkRelationship;
  status: PatientStatus;
}

/** `GET /api/care-portal/invites/{token}` (anonymous). */
export interface PatientInvitePreview {
  clinicName: string;
  patientFirstName: string;
  expiresAt: string;
}

// ---------- The record ----------

export interface PortalProfileDto {
  id: string;
  number: string;
  firstName: string;
  middleName: string | null;
  lastName: string;
  sex: PatientSex;
  dateOfBirth: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  nextOfKinName: string | null;
  nextOfKinPhone: string | null;
  nextOfKinRelationship: string | null;
  status: PatientStatus;
  bloodGroup: string | null;
  genotype: string | null;
  allergies: string[];
  conditions: string[];
}
export interface PortalDiagnosisDto {
  text: string;
  code: string | null;
  primary: boolean;
}
export interface PortalAddendumDto {
  authorName: string;
  text: string;
  at: string;
}
export interface PortalConsultationDto {
  id: string;
  doctorName: string;
  signedAt: string;
  complaint: string | null;
  history: string | null;
  examination: string | null;
  plan: string | null;
  diagnoses: PortalDiagnosisDto[];
  addenda: PortalAddendumDto[];
}
export interface PortalPrescriptionItemDto {
  drug: string;
  strength: string | null;
  dose: string;
  frequency: DoseFrequency;
  durationDays: number | null;
  quantity: number | null;
  route: string | null;
  instructions: string | null;
}
export interface PortalPrescriptionDto {
  id: string;
  doctorName: string;
  createdAt: string;
  items: PortalPrescriptionItemDto[];
}
export interface PortalResultDto {
  id: string;
  kind: TestKind;
  description: string;
  resultText: string | null;
  hasFile: boolean;
  resultedAt: string | null;
}
export interface PortalVitalsDto {
  takenAt: string;
  temperatureC: number | null;
  systolic: number | null;
  diastolic: number | null;
  pulse: number | null;
  respRate: number | null;
  spO2: number | null;
  weightKg: number | null;
  heightCm: number | null;
  bmi: number | null;
  flags: string[];
}
export interface PortalAdmissionDto {
  id: string;
  admittedAt: string;
  wardName: string | null;
  doctorName: string | null;
  reason: string;
  admittingDiagnosis: string;
  status: AdmissionStatus;
  dischargeType: DischargeType | null;
  dischargedAt: string | null;
}
export interface PortalWardRoundDto {
  id: string;
  admissionId: string;
  doctorName: string;
  signedAt: string;
  note: string | null;
  addenda: PortalAddendumDto[];
}
export interface PortalDischargeSummaryDto {
  id: string;
  admissionId: string;
  doctorName: string;
  signedAt: string;
  diagnoses: string | null;
  treatment: string | null;
  condition: string | null;
  takeHome: string | null;
  followUp: string | null;
  addenda: PortalAddendumDto[];
}
/** `GET .../patients/{id}/record`: signed notes and their addenda only, as the API returns them. */
export interface PortalRecordDto {
  patient: PortalProfileDto;
  consultations: PortalConsultationDto[];
  prescriptions: PortalPrescriptionDto[];
  results: PortalResultDto[];
  vitals: PortalVitalsDto[];
  admissions: PortalAdmissionDto[];
  wardRounds: PortalWardRoundDto[];
  dischargeSummaries: PortalDischargeSummaryDto[];
}

/**
 * `GET .../patients/{id}/access-log`: name, clinic role, what was opened (the API's area label) and when. A portal read (final review C1)
 * has `portal` set, the account's short name and its relationship as `role`; `who` reads "Nkem W. (parent), patient portal".
 */
export interface PortalAccessDto {
  staffName: string;
  role: string;
  what: string;
  at: string;
  portal: boolean;
  who: string;
}

// ---------- Bills and payments ----------

export interface PortalBillLineDto {
  description: string;
  quantity: number;
  unitPriceMinor: number;
  amountMinor: number;
}
export interface PortalPaymentDto {
  id: string;
  amountMinor: number;
  method: PayMethod;
  receiptNumber: string | null;
  paidAt: string;
  status: CarePaymentStatus;
}
export interface PortalBillDto {
  id: string;
  number: string;
  status: BillStatus;
  totalMinor: number;
  adjustedMinor: number;
  paidMinor: number;
  balanceMinor: number;
  creditMinor: number;
  createdAt: string;
  finalisedAt: string | null;
  lines: PortalBillLineDto[];
  payments: PortalPaymentDto[];
}
/** `POST .../bills/{billId}/pay`: Paystack's checkout page and the reference it returns with. */
export interface CareCheckoutResult {
  authorizationUrl: string;
  reference: string;
}
/** `POST /api/care-portal/payments/{reference}/verify`. */
export interface CareOnlineStatusDto {
  id: string;
  reference: string;
  billId: string;
  amountMinor: number;
  status: CarePaymentStatus;
  receiptNumber: string | null;
  billBalanceMinor: number;
}

// ---------- Appointments ----------

export interface PortalAppointmentDto {
  id: string;
  doctorUserId: string;
  doctorName: string;
  start: string;
  end: string;
  reason: string | null;
  status: AppointmentStatus;
  source: "Desk" | "Portal";
  canCancel: boolean;
}
export interface PortalAppointmentsDto {
  reasonLabel: string;
  appointments: PortalAppointmentDto[];
}

// ---------- Contact and notices ----------

export interface PortalContactDto {
  phone: string | null;
  email: string | null;
  address: string | null;
}
export interface PortalNoticeDto {
  id: string;
  kind: string;
  title: string;
  body: string;
  link: string;
  createdAt: string;
  readAt: string | null;
}
