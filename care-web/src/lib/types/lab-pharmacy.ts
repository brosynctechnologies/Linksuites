// Wave 4 types for the lab and the pharmacy, mirroring api/src/Saas.Application/Care/LabService.cs and PharmacyService.cs.
// Enums travel as strings. Dates are "YYYY-MM-DD"; instants are ISO 8601. Money is in kobo.

import type { PatientSex } from "../types";
import type { DoseFrequency, TestStatus } from "./clinical";

// ---- Lab catalogue (every member reads it: catalogue data, no patient) ----
export interface LabRangeDto {
  sex: PatientSex | null;
  minAgeYears: number | null;
  maxAgeYears: number | null;
  low: number | null;
  high: number | null;
  criticalLow: number | null;
  criticalHigh: number | null;
}
export interface LabAnalyteDto {
  id: string;
  name: string;
  unit: string | null;
  ranges: LabRangeDto[];
}
export interface LabTestDto {
  id: string;
  name: string;
  sampleType: string;
  priceItemId: string | null;
  priceItemName: string | null;
  priceMinor: number | null;
  turnaroundHours: number;
  active: boolean;
  analytes: LabAnalyteDto[];
}

// ---- Lab orders (the lab only) ----
export type LabStage = "AwaitingSample" | "AwaitingResult" | "AwaitingReview" | "Done";
export type LabFlag = "None" | "Normal" | "Low" | "High" | "CriticalLow" | "CriticalHigh";

export interface LabWorklistRowDto {
  id: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  sex: PatientSex;
  ageYears: number;
  labTestId: string | null;
  testName: string | null;
  sampleType: string | null;
  sampleNumber: string | null;
  stage: LabStage;
  requestedByName: string;
  orderedAt: string;
  collectedAt: string | null;
  reportedAt: string | null;
  dueBy: string | null;
  overdue: boolean;
}
export interface LabOrderAnalyteDto {
  id: string;
  name: string;
  unit: string | null;
  low: number | null;
  high: number | null;
  criticalLow: number | null;
  criticalHigh: number | null;
  rangeText: string;
}
export interface LabValueDto {
  id: string;
  analyteId: string | null;
  name: string;
  unit: string | null;
  value: string;
  numericValue: number | null;
  flag: LabFlag;
  low: number | null;
  high: number | null;
  rangeText: string;
  enteredAt: string;
  superseded: boolean;
}
export interface LabOrderDto {
  id: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  sex: PatientSex;
  ageYears: number;
  description: string;
  labTestId: string | null;
  testName: string | null;
  sampleType: string | null;
  turnaroundHours: number | null;
  stage: LabStage;
  status: TestStatus;
  sampleNumber: string | null;
  collectedByName: string | null;
  collectedAt: string | null;
  requestedByName: string;
  orderedAt: string;
  analytes: LabOrderAnalyteDto[];
  values: LabValueDto[];
  resultText: string | null;
  hasFile: boolean;
  resultedByName: string | null;
  resultedAt: string | null;
  reviewedAt: string | null;
}
export interface LabSampleDto {
  id: string;
  sampleNumber: string;
  collectedAt: string;
  collectedByName: string;
}
export interface LabResultEntryDto {
  id: string;
  status: TestStatus;
  values: LabValueDto[];
  resultText: string;
  anyCritical: boolean;
  resultedAt: string;
}
export interface LabTurnaroundRowDto {
  labTestId: string | null;
  name: string;
  targetHours: number;
  ordered: number;
  collected: number;
  reported: number;
  reviewed: number;
  avgOrderToCollectMinutes: number | null;
  avgCollectToReportMinutes: number | null;
  avgReportToReviewMinutes: number | null;
  avgOrderToReportMinutes: number | null;
  withinTarget: number;
  late: number;
}
export interface LabTurnaroundDto {
  from: string;
  to: string;
  rows: LabTurnaroundRowDto[];
  total: LabTurnaroundRowDto;
}

// ---- Pharmacy stock (leadership and pharmacists: stock, never a patient) ----
export interface StockItemDto {
  id: string;
  name: string;
  form: string | null;
  unit: string;
  priceItemId: string | null;
  priceItemName: string | null;
  reorderLevel: number;
  active: boolean;
  onHand: number;
  expiredOnHand: number;
  nextExpiry: string | null;
  low: boolean;
  valueMinor: number;
}
export interface StockBatchDto {
  id: string;
  itemId: string;
  itemName: string;
  unit: string;
  batchNumber: string;
  expiresOn: string;
  received: number;
  onHand: number;
  unitCostMinor: number;
  expired: boolean;
  daysToExpiry: number;
  receivedAt: string;
}
export interface StockReceiptDto {
  id: string;
  supplier: string | null;
  reference: string | null;
  receivedOn: string;
  receivedByName: string;
  totalCostMinor: number;
  batches: StockBatchDto[];
}
export type StockMovementKind = "Receipt" | "Dispense" | "Adjustment";
export interface StockMovementDto {
  id: string;
  itemId: string;
  itemName: string;
  batchId: string;
  batchNumber: string;
  kind: StockMovementKind;
  quantity: number;
  balanceAfter: number;
  reason: string | null;
  byName: string;
  at: string;
  receiptId: string | null;
  dispensingId: string | null;
}
export interface StockCountResultDto {
  adjusted: number;
  movements: StockMovementDto[];
}
export interface StockAlertsDto {
  days: number;
  lowStock: StockItemDto[];
  nearExpiry: StockBatchDto[];
  expired: StockBatchDto[];
}

// ---- Dispensing (pharmacists and clinical staff) ----
export interface DispenseQueueRowDto {
  prescriptionId: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  doctorName: string;
  writtenAt: string;
  items: number;
  itemsDone: number;
  inpatient: boolean;
}
export interface DispenseItemDto {
  prescriptionItemId: string;
  drug: string;
  strength: string | null;
  dose: string;
  frequency: DoseFrequency;
  durationDays: number | null;
  quantity: number | null;
  dispensed: number;
  remaining: number | null;
  stockItemId: string | null;
  stockItemName: string | null;
  stockOnHand: number | null;
}
export interface DispensedLineDto {
  prescriptionItemId: string;
  itemId: string;
  itemName: string;
  batchId: string;
  batchNumber: string;
  expiresOn: string;
  quantity: number;
}
export interface DispensingDto {
  id: string;
  dispensedByName: string;
  at: string;
  lines: DispensedLineDto[];
}
export interface DispensePrescriptionDto {
  prescriptionId: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  doctorName: string;
  writtenAt: string;
  items: DispenseItemDto[];
  dispensings: DispensingDto[];
}
