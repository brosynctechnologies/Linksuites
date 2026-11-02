// Parent and student portal types, mirroring api/src/Saas.Application/School/{PortalService,FeePaymentService,TimetableService}.cs.
// Money on invoices is kobo even when the field is not named *Minor (total, paid, balance). Dates are "YYYY-MM-DD".

import type { TimetableSlotDto } from "./academic";
import type { FeePaymentDto, InstalmentDto, InvoiceLineDto, InvoiceStatus } from "./fees";

export type { ChildRef, StudentMe } from "../types";

/** `GET /api/parent/children/{id}/overview` and `GET /api/student/overview`. `child` has no schoolSlug. */
export interface PortalChildDto {
  studentId: string;
  name: string;
  admissionNo: string;
  className: string;
  schoolName: string;
}

export interface PortalResultClassDto {
  classId: string;
  className: string;
  total: number;
  average: number;
  position: number;
  classSize: number;
}

/** One term on the overview. `classes` is empty while unpublished or held. */
export interface PortalTermDto {
  termId: string;
  termName: string;
  sessionName: string;
  isCurrent: boolean;
  published: boolean;
  held: boolean;
  classes: PortalResultClassDto[];
}

export interface PortalAttendanceDto {
  termId: string;
  termName: string;
  classId: string;
  className: string;
  present: number;
  absent: number;
  late: number;
  excused: number;
  open: number;
}

/** Parent invoices as the portal lists them. `total` / `paid` / `balance` are kobo. */
export interface ParentInvoiceDto {
  id: string;
  number: string;
  termId: string;
  termName: string;
  total: number;
  paid: number;
  balance: number;
  status: InvoiceStatus | string;
  dueDate: string;
  canPayOnline: boolean;
  nextInstalmentMinor: number | null;
  minimumPartPaymentMinor: number;
  lines: InvoiceLineDto[];
  instalments: InstalmentDto[];
  /** Succeeded payments only. Abandoned checkouts are not listed here. */
  payments: FeePaymentDto[];
}

export interface PortalFeesDto {
  balance: number;
  invoices: ParentInvoiceDto[];
}

export interface PortalTimetableDto {
  classId: string;
  className: string;
  slots: TimetableSlotDto[];
}

/** `fees` is null in the student portal. `attendance` and `timetable` are null without a class this term. */
export interface PortalOverviewDto {
  child: PortalChildDto;
  results: PortalTermDto[];
  attendance: PortalAttendanceDto | null;
  fees: PortalFeesDto | null;
  timetable: PortalTimetableDto | null;
}

export interface PortalSubjectDto {
  subject: string;
  subjectCode: string;
  total: number;
  grade: string;
  /** Null when the subject has no comment. */
  remark: string | null;
  classAverage: number;
  position: number;
}

export interface PortalResultEntryDto {
  classId: string;
  className: string;
  total: number;
  average: number;
  position: number;
  classSize: number;
  daysPresent: number;
  /** Null on results published before late, absent and excused were counted. */
  daysAbsent: number | null;
  daysLate: number | null;
  daysExcused: number | null;
  daysOpen: number;
  teacherRemark: string | null;
  principalRemark: string | null;
  subjects: PortalSubjectDto[];
  mode: "Numeric" | "Developmental";
}

export interface PortalResultsDto {
  termId: string;
  termName: string;
  sessionName: string;
  published: boolean;
  entries: PortalResultEntryDto[];
}

export interface PayOnlineRequest {
  amountMinor: number;
}

export interface PayOnlineResult {
  authorizationUrl: string;
  reference: string;
}

/** `POST /api/parent/payments/{reference}/verify`. `status` is a FeePaymentStatus string. */
export interface PaymentStatusDto {
  id: string;
  reference: string;
  studentId: string;
  invoiceId: string;
  amountMinor: number;
  status: string;
  receiptNumber: string | null;
  invoiceBalance: number;
}
