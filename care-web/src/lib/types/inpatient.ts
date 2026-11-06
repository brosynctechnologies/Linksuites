// Types for the inpatient screens (Task 19), mirroring api/src/Saas.Application/Care: WardService, AdmissionService, WardCareService and
// DischargeService. Enums travel as strings; instants are ISO 8601; dates "YYYY-MM-DD"; money in kobo.
// Demographic shapes (wards, beds, the bed board, admission summaries) are for every member; the rest is clinical.

import type { PatientSex } from "../types";
import type { AddendumDto, DischargeType, DoseFrequency, NoteStatus, PrescriptionItemDto } from "./clinical";
import type { CarePaymentDto } from "./billing";

export type WardKind = "General" | "Male" | "Female" | "Maternity" | "Paediatric" | "Surgical" | "Intensive" | "Private" | "Other";
export type BedStatus = "Free" | "Occupied" | "Cleaning";
export type AdmissionStatus = "Admitted" | "Discharged";
export type PayMethod = "Cash" | "Pos" | "Transfer" | "Online";

/** `GET /api/care/wards`. `nurseUserIds` empty means every active nurse. */
export interface WardDto {
  id: string;
  name: string;
  kind: WardKind;
  bedPriceItemId: string;
  bedPriceItemName: string | null;
  minDepositMinor: number | null;
  nurseUserIds: string[];
  activeBeds: number;
  freeBeds: number;
  occupiedBeds: number;
}
export interface SaveWardRequest {
  name: string;
  kind: WardKind;
  bedPriceItemId: string | null;
  minDepositMinor: number | null;
  nurseUserIds: string[];
}
export interface BedDto {
  id: string;
  wardId: string;
  label: string;
  status: BedStatus;
  active: boolean;
}

/** An admission as every member sees it: demographics, bed and status. */
export interface AdmissionSummaryDto {
  id: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  sex: PatientSex;
  ageYears: number;
  status: AdmissionStatus;
  bedId: string | null;
  bedLabel: string | null;
  wardId: string | null;
  wardName: string | null;
  admittingDoctorUserId: string;
  doctorName: string | null;
  admittedAt: string;
  billId: string | null;
}
export interface BedBoardBedDto {
  id: string;
  label: string;
  status: BedStatus;
  patient: AdmissionSummaryDto | null;
}
export interface BedBoardWardDto {
  id: string;
  name: string;
  kind: WardKind;
  beds: BedBoardBedDto[];
}
/** `GET /api/care/bed-board`. */
export interface BedBoardDto {
  wards: BedBoardWardDto[];
  awaitingBed: AdmissionSummaryDto[];
}
/** `GET /api/care/admissions`: `dischargeType` and `dischargeSummaryStatus` arrive for clinical callers only. */
export interface AdmissionListItemDto extends AdmissionSummaryDto {
  dischargedAt: string | null;
  dischargeType?: DischargeType;
  dischargeSummaryStatus?: NoteStatus;
}

// ---- Clinical ----

export interface AdmitRequest {
  patientId: string;
  consultationId: string | null;
  reason: string;
  admittingDiagnosis: string;
}
/** `GET /api/care/admissions/{id}` (a logged open). */
export interface AdmissionDto extends AdmissionSummaryDto {
  consultationId: string | null;
  reason: string;
  admittingDiagnosis: string;
  depositOverrideBy: string | null;
  dischargeType: DischargeType | null;
  dischargedAt: string | null;
  amaReason: string | null;
  deathTime: string | null;
  deathCause: string | null;
  dischargedByUserId: string | null;
}
export interface AssignBedRequest {
  bedId: string;
  depositMinor: number;
  method: PayMethod | null;
  reference: string | null;
  overrideByOwner: boolean;
}
export interface BedAssignedDto {
  admission: AdmissionSummaryDto;
  deposit: CarePaymentDto | null;
}
export type CareAssignmentKind = "VisitDoctor" | "TriageNurse" | "AdmittingDoctor" | "WardNurse" | "CoveringDoctor";
export interface CareTeamMemberDto {
  userId: string;
  name: string;
  kind: CareAssignmentKind;
  since: string;
  endsAt: string | null;
}

export interface NursingNoteDto {
  id: string;
  admissionId: string;
  authorUserId: string;
  authorName: string;
  note: string;
  at: string;
}
export type FluidDirection = "In" | "Out";
export type FluidKind = "Oral" | "IV" | "Blood" | "NG" | "Urine" | "Vomit" | "Drain" | "Stool" | "Other";
export interface FluidEntryDto {
  id: string;
  admissionId: string;
  direction: FluidDirection;
  kind: FluidKind;
  ml: number;
  at: string;
  recordedByUserId: string;
  recordedByName: string;
}
export interface FluidChartDto {
  admissionId: string;
  date: string;
  entries: FluidEntryDto[];
  inMl: number;
  outMl: number;
  balanceMl: number;
}
export interface WardRoundDto {
  id: string;
  admissionId: string;
  patientId: string;
  doctorUserId: string;
  doctorName: string;
  note: string | null;
  status: NoteStatus;
  signedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  addenda: AddendumDto[];
}
export type DoseStatus = "Due" | "Given" | "Held" | "Refused" | "Missed" | "Stopped";
export interface MedicationDoseDto {
  id: string;
  prescriptionId: string;
  prescriptionItemId: string;
  drug: string;
  strength: string | null;
  dose: string;
  frequency: DoseFrequency;
  route: string | null;
  instructions: string | null;
  dueAt: string;
  status: DoseStatus;
  recordedByUserId: string | null;
  recordedByName: string | null;
  recordedAt: string | null;
  note: string | null;
}
export interface MedicationChartDto {
  admissionId: string;
  date: string;
  doses: MedicationDoseDto[];
  asNeeded: PrescriptionItemDto[];
}
export interface DoseRecordedDto {
  id: string;
  status: DoseStatus;
  recordedByUserId: string;
  recordedAt: string;
  note: string | null;
}

export interface SaveDischargeSummaryRequest {
  diagnoses: string | null;
  treatment: string | null;
  condition: string | null;
  takeHome: string | null;
  followUp: string | null;
}
export interface DischargeSummaryDto {
  id: string;
  admissionId: string;
  patientId: string;
  doctorUserId: string;
  doctorName: string;
  diagnoses: string | null;
  treatment: string | null;
  condition: string | null;
  takeHome: string | null;
  followUp: string | null;
  status: NoteStatus;
  signedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  addenda: AddendumDto[];
}
export interface DischargeRequest {
  type: DischargeType;
  amaReason: string | null;
  deathTime: string | null;
  deathCause: string | null;
  acknowledgeUnsignedNotes: boolean;
}
/** One unsigned ward-round draft named in the discharge 409 (`unsignedWardRounds`): never its text. */
export interface UnsignedRoundDto {
  id: string;
  doctorUserId: string;
  doctorName: string;
  createdAt: string;
}
export interface DischargedDto {
  admission: AdmissionSummaryDto;
  type: DischargeType;
  dischargedAt: string;
  billId: string;
  balanceMinor: number;
  stoppedDoses: number;
  missedDoses: number;
  unsignedRoundsAcknowledged: number;
}
