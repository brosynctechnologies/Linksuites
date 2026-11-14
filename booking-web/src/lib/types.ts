import type { BankTransfer, PendingTransfer } from "@/lib/plan-payment";

// Types mirroring docs/BOOKINGS_API.md (and the shared auth/team/billing parts of docs/API.md and
// docs/INVOICES_PHASE2_API.md). Money fields ending in `Minor` are integers in minor units.
// Instants are ISO 8601 with an offset; dates are "YYYY-MM-DD"; clock times are "HH:mm";
// weekdays are numbers, 0 = Sunday … 6 = Saturday.

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

// ---- Auth ----
export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
}
export type Role = "Owner" | "Admin" | "Staff" | "Accountant";
export interface AuthTenant {
  id: string;
  name: string;
  slug: string;
  role: Role | string;
  product?: string;
}
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant;
}
export interface SignupRequest {
  fullName: string;
  email: string;
  password: string;
  country: string;
  product: "bookings";
  businessName: string;
}

// ---- Shared enums ----
export type Weekday = 0 | 1 | 2 | 3 | 4 | 5 | 6;
export type BusinessCategory = "Salon" | "Barber" | "Spa" | "MakeUp" | "Photography" | "Clinic" | "Fitness" | "Other";
export type DepositType = "None" | "Fixed" | "Percent";
export type BookingStatus = "Pending" | "Confirmed" | "Completed" | "Cancelled" | "NoShow" | "Expired";
export type BookingSource = "WalkIn" | "Phone" | "Online";
export type PaymentType = "Deposit" | "Balance" | "Refund";
export type PaymentMethod = "Online" | "Cash" | "Transfer" | "Card";

export interface OpeningHours {
  weekday: Weekday;
  open: string;
  close: string;
  closed: boolean;
}

// ---- Business settings ----
export interface BookingBusinessDto {
  name: string;
  slug: string;
  category: BusinessCategory;
  description: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  address: string | null;
  phone: string | null;
  whatsappNumber: string | null;
  email: string | null;
  timezone: string;
  currency: string;
  country: string;
  brandColor: string | null;
  policies: string | null;
  cancellationHours: number;
  slotIntervalMinutes: number;
  minNoticeMinutes: number;
  maxDaysAhead: number;
  bookingEnabled: boolean;
  paystackConnected: boolean;
  /** Customers are actually asked for deposits: Paystack is connected and the plan includes deposits. */
  depositsEnabled: boolean;
  /** The plan includes deposits (whether or not Paystack is connected). */
  depositsInPlan: boolean;
  bookingUrl: string;
}
export type UpdateBookingBusinessRequest = Omit<
  BookingBusinessDto,
  "logoUrl" | "coverUrl" | "currency" | "country" | "paystackConnected" | "depositsEnabled" | "depositsInPlan" | "bookingUrl"
>;

// ---- Services ----
export interface SaveServiceRequest {
  name: string;
  description: string | null;
  durationMinutes: number;
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
  priceMinor: number;
  depositType: DepositType;
  depositValue: number;
  isActive: boolean;
  position: number;
  staffIds: string[];
}
export interface ServiceDto extends SaveServiceRequest {
  id: string;
  depositMinor: number;
}

// ---- Staff ----
export interface ScheduleInterval {
  weekday: Weekday;
  start: string;
  end: string;
}
export interface SaveStaffRequest {
  displayName: string;
  bio: string | null;
  email: string | null;
  color: string;
  isBookable: boolean;
  position: number;
  serviceIds: string[];
  userId: string | null;
}
export interface StaffDto extends SaveStaffRequest {
  id: string;
  photoUrl: string | null;
  schedule: ScheduleInterval[];
  calendarFeedUrl: string | null;
}
export interface TimeOffDto {
  id: string;
  staffId: string;
  startsAt: string;
  endsAt: string;
  reason: string | null;
}

// ---- Availability ----
export interface SlotDto {
  startsAt: string;
  endsAt: string;
  staffIds: string[];
}
export interface AvailabilityDto {
  date: string;
  timezone: string;
  slots: SlotDto[];
}
export interface DayAvailability {
  date: string;
  available: boolean;
}

// ---- Bookings ----
export interface BookingSummaryDto {
  id: string;
  code: string;
  startsAt: string;
  endsAt: string;
  status: BookingStatus;
  customerName: string;
  customerPhone: string;
  serviceName: string;
  staffId: string;
  staffName: string;
  staffColor: string | null;
  priceMinor: number;
  depositMinor: number;
  paidMinor: number;
  source: BookingSource;
}
export interface PaymentDto {
  id: string;
  amountMinor: number;
  type: PaymentType;
  method: PaymentMethod;
  reference: string | null;
  online: boolean;
  createdAt: string;
}
export interface HistoryEntry {
  status: BookingStatus;
  note: string | null;
  createdAt: string;
}
export interface ReminderDto {
  channel: string;
  sendAt: string;
  sentAt: string | null;
  status: string;
}
export interface ReviewDto {
  rating: number;
  comment: string | null;
  createdAt: string;
}
export interface BookingDto extends BookingSummaryDto {
  customer: { id: string; name: string; phone: string; email: string | null; noShowCount: number };
  serviceId: string;
  notes: string | null;
  holdExpiresAt: string | null;
  manageUrl: string | null;
  whatsappReminderUrl: string | null;
  payments: PaymentDto[];
  history: HistoryEntry[];
  reminders: ReminderDto[];
  review: ReviewDto | null;
  /** What Paystack can still send back, across every online payment. */
  refundableMinor: number;
}
export interface CreateBookingRequest {
  serviceId: string;
  staffId: string;
  startsAt: string;
  customer: { name: string; phone: string; email: string | null };
  source: BookingSource;
  notes: string | null;
  force: boolean;
}
export interface CalendarDto {
  timezone: string;
  staff: { id: string; name: string; color: string | null }[];
  bookings: BookingSummaryDto[];
  timeOff: TimeOffDto[];
  hours: OpeningHours[];
}

// ---- Customers ----
export interface BookingCustomerDto {
  id: string;
  name: string;
  phone: string;
  email: string | null;
  notes: string | null;
  bookingsCount: number;
  completedCount: number;
  noShowCount: number;
  totalSpentMinor: number;
  lastVisitAt: string | null;
}
export interface BookingCustomerDetailDto extends BookingCustomerDto {
  bookings: BookingSummaryDto[];
}

// ---- Dashboard and reports ----
export interface BookingDashboardDto {
  timezone: string;
  currency: string;
  today: BookingSummaryDto[];
  upcomingCount: number;
  pendingCount: number;
  revenueThisMonthMinor: number;
  bookingsThisMonth: number;
  /** -1 (or null) = unlimited. */
  bookingLimit: number | null;
  noShowRate: number;
}
export interface BookingReportDto {
  currency: string;
  from: string;
  to: string;
  bookings: number;
  completed: number;
  cancelled: number;
  noShows: number;
  noShowRate: number;
  revenueMinor: number;
  depositsMinor: number;
  byService: { name: string; bookings: number; revenueMinor: number }[];
  byStaff: { name: string; bookings: number; revenueMinor: number; noShows: number }[];
  byDay: { date: string; bookings: number; revenueMinor: number }[];
}

// ---- Public booking page ----
export interface PublicServiceDto {
  id: string;
  name: string;
  description: string | null;
  durationMinutes: number;
  priceMinor: number;
  depositMinor: number;
  staffIds: string[];
}
export interface PublicStaffDto {
  id: string;
  displayName: string;
  photoUrl: string | null;
  bio: string | null;
  serviceIds: string[];
}
export interface PublicBookingPageDto {
  name: string;
  slug: string;
  category: BusinessCategory;
  description: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  address: string | null;
  phone: string | null;
  whatsappNumber: string | null;
  timezone: string;
  currency: string;
  brandColor: string | null;
  policies: string | null;
  cancellationHours: number;
  maxDaysAhead: number;
  bookingEnabled: boolean;
  showBranding: boolean;
  rating: { average: number; count: number } | null;
  services: PublicServiceDto[];
  staff: PublicStaffDto[];
  hours: OpeningHours[];
}
export interface PublicBookRequest {
  serviceId: string;
  staffId: string | null;
  startsAt: string;
  customer: { name: string; phone: string; email: string | null };
  notes: string | null;
}
export interface PublicBookingDto {
  code: string;
  status: BookingStatus;
  startsAt: string;
  endsAt: string;
  timezone: string;
  service: { id: string; name: string; durationMinutes: number };
  staff: { id: string; displayName: string; photoUrl: string | null };
  customerName: string;
  priceMinor: number;
  depositMinor: number;
  paidMinor: number;
  balanceMinor: number;
  /** Left to pay towards the deposit while the booking waits for one; the /pay link charges exactly this. */
  depositDueMinor: number;
  currency: string;
  holdExpiresAt: string | null;
  canCancel: boolean;
  canReschedule: boolean;
  cancelDeadline: string | null;
  canReview: boolean;
  review: ReviewDto | null;
  business: {
    name: string;
    slug: string;
    address: string | null;
    phone: string | null;
    whatsappNumber: string | null;
    logoUrl: string | null;
    brandColor: string | null;
    policies: string | null;
  };
}
export interface PublicBookResponse {
  booking: PublicBookingDto;
  authorizationUrl: string | null;
  manageUrl: string;
}

// ---- Billing ----
export interface PlanDto {
  code: string;
  name: string;
  priceMinor: number;
  currency: string;
  interval: string;
  limits: Record<string, number>;
  features: string[];
}
export interface UsageItem {
  key: string;
  label: string;
  used: number;
  limit: number;
}
export interface BillingDto {
  currentPlan: PlanDto;
  subscription: { status: string; currentPeriodEnd: string | null; complimentary?: boolean } | null;
  plans: PlanDto[];
  usage: { items?: UsageItem[] } & Record<string, unknown>;
  paymentsEnabled: boolean;
  /** The company account to pay a plan into by bank transfer: only while online payment is off. */
  bankTransfer?: BankTransfer | null;
  /** A bank transfer this business said it sent, waiting for confirmation. */
  pendingTransfer?: PendingTransfer | null;
}

// ---- Account activity ----
/** A read-only support view of this business (`GET /api/account/activity`, last 90 days). */
export interface SupportViewDto {
  sessionId: string;
  adminName: string;
  reason: string;
  startedAt: string;
  endsAt: string;
  /** Set when the view was ended before it expired. */
  endedAt: string | null;
}

// ---- Team & businesses ----
export type InviteRole = Exclude<Role, "Owner">;
export interface MemberDto {
  /** Membership id. */
  id: string;
  userId: string;
  fullName: string;
  email: string;
  role: Role;
  isYou: boolean;
  joinedAt: string;
}
export interface InviteDto {
  id: string;
  email: string;
  role: InviteRole;
  invitedAt: string;
  expiresAt: string;
}
export interface TeamDto {
  members: MemberDto[];
  invites: InviteDto[];
  /** used = members + pending invites. */
  seats: { used: number; limit: number };
}
export interface PublicInviteDto {
  businessName: string;
  email: string;
  role: Role;
  hasAccount: boolean;
  expired: boolean;
}
export interface MyBusinessDto {
  tenantId: string;
  name: string;
  role: Role;
  current: boolean;
}
