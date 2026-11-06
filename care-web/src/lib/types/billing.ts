// Types for the bills, payments and reports screens (Task 19), mirroring api/src/Saas.Application/Care: BillingService, PaymentService,
// CareReportService and PatientInviteService. Money is in kobo; instants are ISO 8601; dates "YYYY-MM-DD".

import type { PriceCategory } from "../types";
import type { BillStatus } from "./clinical";
import type { PayMethod } from "./inpatient";

export type BillLineSource = "Auto" | "Manual";
export type AdjustmentKind = "Discount" | "Waiver";
export type CarePaymentStatus = "Succeeded" | "Pending" | "Mismatch" | "Reversed" | "Abandoned";

/** A bill line. `clinicalRef` reaches clinical callers only and is never shown on these screens. */
export interface BillLineDto {
  id: string;
  /** Null for the cashier, front desk and support (final review I2). */
  priceItemId: string | null;
  /** The item name for clinical staff and leadership; the price category ("Drug", "Test") for the cashier, front desk and support (I2). */
  description: string;
  quantity: number;
  unitPriceMinor: number;
  amountMinor: number;
  source: BillLineSource;
  reason: string | null;
  removedReason: string | null;
  removedAt: string | null;
  createdAt: string;
  /** Not on the DTO yet (Task 13 carry): bed lines show their description. */
  chargeDay?: string | null;
}
export interface CarePaymentDto {
  id: string;
  billId: string;
  patientId: string;
  amountMinor: number;
  method: PayMethod;
  reference: string | null;
  receiptNumber: string | null;
  isDeposit: boolean;
  recordedByUserId: string | null;
  paidAt: string;
  status: CarePaymentStatus;
  reversedAt: string | null;
  reversalReason: string | null;
}
export interface BillAdjustmentDto {
  id: string;
  kind: AdjustmentKind;
  amountMinor: number;
  reason: string;
  byUserId: string;
  at: string;
}
/** `GET /api/care/bills`: at most 200, newest first. */
export interface BillSummaryDto {
  id: string;
  number: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  visitId: string | null;
  admissionId: string | null;
  status: BillStatus;
  totalMinor: number;
  paidMinor: number;
  balanceMinor: number;
  createdAt: string;
  finalisedAt: string | null;
  creditMinor: number;
  needsReview: boolean;
}
/** `GET /api/care/bills/{id}`. */
export interface BillDto extends BillSummaryDto {
  adjustedMinor: number;
  lines: BillLineDto[];
  payments: CarePaymentDto[];
  adjustments: BillAdjustmentDto[];
}
export interface CareReversalResult {
  payment: CarePaymentDto;
  message: string;
}

// ---- Reports ----
export interface MethodTotalDto {
  method: PayMethod;
  count: number;
  amountMinor: number;
}
export interface TakingsDayDto {
  date: string;
  count: number;
  totalMinor: number;
  byMethod: MethodTotalDto[];
}
export interface TakingsReportDto {
  from: string;
  to: string;
  count: number;
  totalMinor: number;
  byMethod: MethodTotalDto[];
  days: TakingsDayDto[];
}
export interface CareDebtorDto {
  patientId: string;
  patientNumber: string;
  patientName: string;
  phone: string | null;
  bills: number;
  balanceMinor: number;
  oldestBillAt: string;
  whatsAppUrl: string | null;
}
export interface CareDebtorsReportDto {
  count: number;
  totalMinor: number;
  debtors: CareDebtorDto[];
}
export interface CategoryTotalDto {
  category: PriceCategory;
  lines: number;
  amountMinor: number;
}
export interface RevenueReportDto {
  from: string;
  to: string;
  byCategory: CategoryTotalDto[];
  chargedMinor: number;
  discountsMinor: number;
  waiversMinor: number;
  netMinor: number;
}
export interface DepositRowDto {
  admissionId: string;
  patientId: string;
  patientNumber: string;
  patientName: string;
  wardName: string | null;
  bedLabel: string | null;
  admittedAt: string;
  depositMinor: number;
  chargesMinor: number;
  paidMinor: number;
  balanceMinor: number;
  creditMinor: number;
}
export interface DepositsReportDto {
  count: number;
  depositsMinor: number;
  creditMinor: number;
  admissions: DepositRowDto[];
}

// ---- Portal invites (staff side) ----
export type PatientLinkRelationship = "Self" | "Parent" | "Guardian";
export interface PatientInviteDto {
  link: string;
  whatsAppUrl: string | null;
  expiresAt: string;
}
export interface PendingInviteDto {
  id: string;
  email: string | null;
  phone: string | null;
  invitedByName: string | null;
  createdAt: string;
  expiresAt: string;
}
export interface PortalLinkDto {
  id: string;
  name: string;
  email: string | null;
  relationship: PatientLinkRelationship;
  active: boolean;
  createdAt: string;
  revokedAt: string | null;
}
