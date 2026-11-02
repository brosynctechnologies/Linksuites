// Fee types mirroring api/src/Saas.Application/School/{FeeService,FeePaymentService,FinanceService,ExpenseService}.cs.
// Money is in kobo (minor units). Dates are "YYYY-MM-DD"; instants are ISO 8601.

export type InvoiceStatus = "Unpaid" | "PartlyPaid" | "Paid" | "Cancelled";
export type FeePaymentMethod = "Online" | "Cash" | "Transfer" | "Pos";
/** Pending checkouts are never returned to staff; Mismatch and Abandoned are online charges that did not count. */
export type FeePaymentStatus = "Pending" | "Succeeded" | "Mismatch" | "Abandoned";
export type DiscountKind = "Percent" | "Amount";

// ---- Fee items and structure ----
export interface FeeItemDto {
  id: string;
  name: string;
  isOptional: boolean;
}
export interface SaveFeeItemRequest {
  name: string;
  isOptional: boolean;
}
export interface FeeLineInput {
  feeItemId: string;
  amountMinor: number;
}
export interface FeeStructureLineDto {
  feeItemId: string;
  feeItemName: string;
  isOptional: boolean;
  amountMinor: number;
}
export interface FeeStructureDto {
  termId: string;
  levelId: string;
  lines: FeeStructureLineDto[];
}
export interface FeeStructureCopyDto {
  termId: string;
  /** Levels copied (levels that already had lines in this term are left alone). */
  levels: number;
  lines: FeeStructureLineDto[];
}

// ---- Invoices ----
export interface GenerateInvoicesResult {
  created: number;
  refreshed: number;
  skipped: number;
  /** Refreshed invoices now flagged for review: a discount no longer fits, the instalments were cleared, or the level has no fees. */
  needsReview: string[];
  /** Invoices left alone because they have payments, listed only when their lines no longer match the fee structure. Not flagged. */
  withPayments: string[];
  /** Number and student for every id in `needsReview` and `withPayments`. */
  invoices: GeneratedInvoiceRefDto[];
}
export interface GeneratedInvoiceRefDto {
  id: string;
  number: string;
  studentName: string;
}
export interface InvoiceLineDto {
  id: string;
  /** Null for a discount line (its amount is negative). */
  feeItemId: string | null;
  description: string;
  amountMinor: number;
}
export interface InstalmentDto {
  id: string;
  dueDate: string;
  amountMinor: number;
}
export interface InstalmentInput {
  dueDate: string;
  amountMinor: number;
}
export interface InvoiceSummaryDto {
  id: string;
  number: string;
  enrolmentId: string;
  studentId: string;
  studentName: string;
  admissionNo: string;
  classId: string;
  className: string;
  termId: string;
  total: number;
  paid: number;
  balance: number;
  status: InvoiceStatus;
  dueDate: string;
}
export interface FeePaymentDto {
  id: string;
  invoiceId: string;
  amountMinor: number;
  currency: string;
  method: FeePaymentMethod;
  status: FeePaymentStatus;
  reference: string | null;
  paidOn: string;
  receiptNumber: string | null;
  reversedAt: string | null;
  reversalReason: string | null;
}
export interface InvoiceDto extends InvoiceSummaryDto {
  lines: InvoiceLineDto[];
  instalments: InstalmentDto[];
  needsReview: boolean;
  payments: FeePaymentDto[];
}
export interface DiscountRequest {
  kind: DiscountKind;
  /** Percent: 0–100 of the tuition lines. Amount: kobo, a whole number. */
  value: number;
  reason: string;
}
export interface RecordPaymentRequest {
  amountMinor: number;
  method: Exclude<FeePaymentMethod, "Online">;
  reference: string | null;
  paidOn: string;
}
export interface ReversePaymentResult {
  payment: FeePaymentDto;
  message: string;
}

// ---- Debtors and reports ----
export interface DebtorDto {
  invoiceId: string;
  invoiceNumber: string;
  studentId: string;
  studentName: string;
  admissionNo: string;
  classId: string;
  className: string;
  total: number;
  paid: number;
  balance: number;
  daysOverdue: number;
  primaryGuardian: string | null;
  guardianPhone: string | null;
}
export interface DebtorsDto {
  termId: string;
  count: number;
  totalBalance: number;
  debtors: DebtorDto[];
}
export interface DebtorWhatsAppDto {
  studentName: string;
  guardianName: string;
  balance: number;
  whatsAppUrl: string;
}
export interface FinanceSummaryDto {
  termId: string;
  expected: number;
  collected: number;
  outstanding: number;
  expenses: number;
}
export type CollectionsBy = "class" | "item" | "method";
export interface CollectionRowDto {
  key: string;
  label: string;
  amountMinor: number;
}
export interface CollectionsDto {
  termId: string;
  by: CollectionsBy;
  total: number;
  rows: CollectionRowDto[];
}

// ---- Settings ----
export interface FinanceSettingsDto {
  resultsHoldForUnpaidFees: boolean;
  reminderDaysBefore: number;
  reminderEveryDays: number;
}
export interface PaystackSettingsDto {
  connected: boolean;
  webhookUrl: string;
}

// ---- Expenses ----
export interface ExpenseDto {
  id: string;
  category: string;
  amountMinor: number;
  date: string;
  note: string | null;
  hasReceipt: boolean;
  /** A short-lived signed link to the receipt photo. */
  receiptUrl: string | null;
  recordedBy: string | null;
  createdAt: string;
}
export interface SaveExpenseRequest {
  category: string;
  amountMinor: number;
  date: string;
  note: string | null;
}
