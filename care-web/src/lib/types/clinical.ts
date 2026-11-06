// Types for the clinical screens (Task 18), mirroring api/src/Saas.Application/Care: VisitService, Vitals, AppointmentService,
// ConsultationService, PrescriptionService, TestService, RecordTimelineService and CareReportService (unsigned drafts).
// Enums travel as strings. Dates are "YYYY-MM-DD"; instants are ISO 8601. Money is in kobo.

import type { PatientDto, PatientSex } from "../types";

// ---- Visits and the queue (demographics only: every member may see these) ----
export type VisitStage = "WaitingVitals" | "WaitingDoctor" | "WithDoctor" | "WaitingPayment" | "Done";
export type Urgency = "Routine" | "Urgent" | "Emergency";

export interface VisitDto {
  id: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  sex: PatientSex;
  ageYears: number;
  stage: VisitStage;
  urgency: Urgency;
  doctorUserId: string | null;
  doctorName: string | null;
  appointmentId: string | null;
  billId: string | null;
  arrivedAt: string;
  minutesWaited: number;
}
export interface QueueStageDto {
  stage: VisitStage;
  visits: VisitDto[];
}
/** `GET /api/care/queue?date=&mine=`: one Lagos day, every stage present, Emergency first then longest waiting. */
export interface QueueDto {
  date: string;
  mine: boolean;
  stages: QueueStageDto[];
}
export interface CheckInRequest {
  patientId: string;
  appointmentId: string | null;
}

// ---- Vital signs (clinical) ----
export interface VitalsInput {
  temperatureC: number | null;
  systolic: number | null;
  diastolic: number | null;
  pulse: number | null;
  respRate: number | null;
  spO2: number | null;
  weightKg: number | null;
  heightCm: number | null;
}
export interface TriageRequest {
  vitals: VitalsInput;
  urgency: Urgency;
  doctorUserId: string | null;
}
export interface VitalSignsDto {
  id: string;
  patientId: string;
  visitId: string | null;
  takenBy: string;
  takenByName: string;
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
  /** "High temperature", "Critical SpO2"… */
  flags: string[];
  critical: boolean;
}
export interface TriageResultDto {
  visit: VisitDto;
  vitals: VitalSignsDto;
}

// ---- Doctors, schedules and appointments ----
export type Weekday = "Sunday" | "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday";
export interface DoctorDto {
  userId: string;
  name: string;
}
export interface ScheduleBlockDto {
  weekday: Weekday;
  /** Lagos wall clock, "HH:mm:ss". */
  start: string;
  end: string;
  slotMinutes: number;
}
export interface DoctorScheduleDto {
  doctorUserId: string;
  doctorName: string;
  blocks: ScheduleBlockDto[];
}
export interface SlotDto {
  start: string;
  end: string;
}
export type AppointmentStatus = "Booked" | "CheckedIn" | "Done" | "Cancelled" | "NoShow";
export interface AppointmentDto {
  id: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  doctorUserId: string;
  doctorName: string;
  start: string;
  end: string;
  /** A short, non-clinical booking reason. */
  reason: string | null;
  status: AppointmentStatus;
  source: "Desk" | "Portal";
}

// ---- Consultations, diagnoses and addenda (clinical) ----
export type NoteStatus = "Draft" | "Signed";
export type NoteKind = "Consultation" | "WardRound" | "Discharge";
export interface DiagnosisInput {
  text: string | null;
  code: string | null;
  primary: boolean;
}
export interface SaveConsultationRequest {
  complaint: string | null;
  history: string | null;
  examination: string | null;
  plan: string | null;
  diagnoses: DiagnosisInput[] | null;
}
export interface DiagnosisDto {
  id: string;
  patientId: string;
  consultationId: string | null;
  admissionId: string | null;
  text: string;
  code: string | null;
  primary: boolean;
  createdAt: string;
}
export interface AddendumDto {
  id: string;
  noteKind: NoteKind;
  noteId: string;
  authorUserId: string;
  authorName: string;
  text: string;
  at: string;
}
export interface ConsultationDto {
  id: string;
  visitId: string;
  patientId: string;
  doctorUserId: string;
  doctorName: string;
  complaint: string | null;
  history: string | null;
  examination: string | null;
  plan: string | null;
  status: NoteStatus;
  signedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  diagnoses: DiagnosisDto[];
  addenda: AddendumDto[];
}
/** `GET /api/care/diagnosis-codes?q=`: the built-in list. */
export interface DiagnosisCode {
  code: string;
  name: string;
}

// ---- Prescriptions (clinical) ----
export type DoseFrequency = "OD" | "BD" | "TDS" | "QDS" | "Q4H" | "Q6H" | "Q8H" | "Nocte" | "Stat" | "PRN";
export interface PrescriptionItemInput {
  drug: string;
  strength: string | null;
  dose: string;
  frequency: DoseFrequency | null;
  durationDays: number | null;
  quantity: number | null;
  route: string | null;
  instructions: string | null;
  priceItemId: string | null;
}
export interface CreatePrescriptionRequest {
  patientId: string;
  consultationId: string | null;
  admissionId: string | null;
  allergyAcknowledged: boolean;
  items: PrescriptionItemInput[];
}
export interface PrescriptionItemDto {
  id: string;
  drug: string;
  strength: string | null;
  dose: string;
  frequency: DoseFrequency;
  durationDays: number | null;
  quantity: number | null;
  route: string | null;
  instructions: string | null;
  priceItemId: string | null;
}
export interface PrescriptionDto {
  id: string;
  patientId: string;
  consultationId: string | null;
  admissionId: string | null;
  doctorUserId: string;
  doctorName: string;
  allergyAcknowledged: boolean;
  overriddenAllergens: string[];
  createdAt: string;
  items: PrescriptionItemDto[];
}

// ---- Tests (clinical) ----
export type TestKind = "Lab" | "Imaging" | "Other";
export type TestStatus = "Requested" | "Resulted";
export interface CreateTestRequest {
  patientId: string;
  consultationId: string | null;
  admissionId: string | null;
  kind: TestKind;
  description: string;
  priceItemId: string | null;
}
export interface TestRequestDto {
  id: string;
  patientId: string;
  consultationId: string | null;
  admissionId: string | null;
  requestedBy: string;
  requestedByName: string;
  kind: TestKind;
  description: string;
  priceItemId: string | null;
  status: TestStatus;
  resultText: string | null;
  hasFile: boolean;
  resultedBy: string | null;
  resultedByName: string | null;
  resultedAt: string | null;
  createdAt: string;
  /** Post-build: a doctor's review releases the result to the portal. Null by with a time: reviewed before reviews existed. */
  reviewedBy: string | null;
  reviewedByName: string | null;
  reviewedAt: string | null;
}
/** `POST /api/care/tests/{id}/review`: who reviewed the result and when (idempotent: a second review answers with the first). */
export interface TestReviewDto {
  id: string;
  reviewedBy: string | null;
  reviewedByName: string | null;
  reviewedAt: string;
}
/** `GET /api/care/tests/awaiting-review`: a result waiting for a doctor's review. Never the test's description or its result. */
export interface AwaitingReviewDto {
  id: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  kind: TestKind;
  consultationId: string | null;
  admissionId: string | null;
  recordedByName: string | null;
  recordedAt: string;
}
/** `POST /api/care/tests/{id}/result`: the result just recorded (never the request's description). */
export interface TestResultDto {
  id: string;
  status: TestStatus;
  resultText: string | null;
  hasFile: boolean;
  resultedBy: string | null;
  resultedAt: string | null;
}
/** `GET /api/care/tests/{id}/file`: a two-minute signed link. */
export interface TestFileLinkDto {
  url: string;
  expiresAt: string;
  contentType: string;
}

// ---- The record timeline (clinical) ----
export interface RecordVisitDto {
  id: string;
  arrivedAt: string;
  stage: VisitStage;
  urgency: Urgency;
  doctorUserId: string | null;
  doctorName: string | null;
  doneAt: string | null;
  consultationId: string | null;
  billId: string | null;
}
export type DischargeType = "Home" | "AgainstAdvice" | "TransferredOut" | "Died";
/** The fields of an admission the timeline shows (Task 19 owns the full admission screens). */
export interface RecordAdmissionDto {
  id: string;
  status: "Admitted" | "Discharged";
  wardName: string | null;
  bedLabel: string | null;
  doctorName: string | null;
  admittedAt: string;
  reason: string;
  admittingDiagnosis: string;
  dischargeType: DischargeType | null;
  dischargedAt: string | null;
}
export interface RecordNursingNoteDto {
  id: string;
  admissionId: string;
  authorName: string;
  note: string;
  at: string;
}
export interface RecordWardRoundDto {
  id: string;
  admissionId: string;
  doctorUserId: string;
  doctorName: string;
  note: string | null;
  status: NoteStatus;
  signedAt: string | null;
  createdAt: string;
  addenda: AddendumDto[];
}
export interface RecordMedicationSummaryDto {
  admissionId: string;
  prescriptionItemId: string;
  drug: string;
  dose: string;
  frequency: DoseFrequency;
  given: number;
  lastGivenAt: string | null;
}
export interface RecordDischargeSummaryDto {
  id: string;
  admissionId: string;
  doctorName: string;
  diagnoses: string | null;
  treatment: string | null;
  condition: string | null;
  takeHome: string | null;
  followUp: string | null;
  status: NoteStatus;
  signedAt: string | null;
  createdAt: string;
  addenda: AddendumDto[];
}
export type BillStatus = "Unpaid" | "PartPaid" | "Paid" | "Waived" | "InCredit";
export interface RecordBillDto {
  id: string;
  number: string;
  visitId: string | null;
  admissionId: string | null;
  status: BillStatus;
  totalMinor: number;
  balanceMinor: number;
  createdAt: string;
}
/** `GET /api/care/patients/{id}/record`: the whole record, opened through the record guard (logged). */
export interface PatientRecordDto {
  patient: PatientDto;
  visits: RecordVisitDto[];
  consultations: ConsultationDto[];
  /** Oldest first, for the chart. */
  vitals: VitalSignsDto[];
  diagnoses: DiagnosisDto[];
  prescriptions: PrescriptionDto[];
  results: TestRequestDto[];
  admissions: RecordAdmissionDto[];
  nursingNotes: RecordNursingNoteDto[];
  wardRounds: RecordWardRoundDto[];
  medicationSummary: RecordMedicationSummaryDto[];
  dischargeSummaries: RecordDischargeSummaryDto[];
  bills: RecordBillDto[];
}

// ---- The director's unsigned drafts (no note text, no patient name) ----
export interface UnsignedDraftDto {
  kind: "consultation" | "ward-round" | "discharge-summary" | string;
  id: string;
  patientId: string;
  patientNumber: string;
  doctorUserId: string;
  doctorName: string;
  visitId: string | null;
  admissionId: string | null;
  date: string;
  ageDays: number;
}
