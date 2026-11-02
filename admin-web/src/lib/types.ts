/** DTOs from docs/PLATFORM_API.md (plus the admin sections of COURSES_API.md and PROPERTY_API.md). */

export type PlatformRole = "SuperAdmin" | "Moderator" | "Finance" | "Support";

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

export interface AdminUser {
  id: string;
  fullName: string;
  email: string;
  isPlatformAdmin: boolean;
  platformRole: PlatformRole | null;
}

/** GET /api/platform/me: the signed-in admin, checked against the server while the console is open. */
export interface PlatformMe {
  id: string;
  fullName: string;
  email: string;
  platformRole: PlatformRole;
  sessionExpiresAt: string | null;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AdminUser;
  tenant: null;
}

// Overview and health

export interface JobRunDto {
  name: string;
  lastStartedAt: string | null;
  lastSucceededAt: string | null;
  lastError: string | null;
}

export interface OverviewDto {
  products: { product: string; businesses: number; newThisMonth: number; paying: number; suspended: number }[];
  /** The NGN figure. From the price each business paid, not list price. */
  mrrMinor: number;
  mrrByCurrency: { currency: string; monthlyMinor: number }[];
  fees: { courseFeesMinor: number; shortLetCommissionMinor: number; boostsMinor: number; totalMinor: number };
  queues: { listings: number; courses: number; verifications: number; reports: number; reviews: number; payouts: number; payoutsMinor: number };
  failedEmails24h: number;
  jobs: JobRunDto[];
}

export interface HealthDto {
  jobs: JobRunDto[];
  outbox: { pending: number; sent: number; failed: number };
  /** `recipient` is masked ("a…@example.com") for roles other than Support and Super admin. */
  failures: { id: string; templateKey: string; recipient: string; error: string | null; attempts: number; createdAt: string }[];
}

// Businesses

export type BusinessStatus = "Active" | "Suspended" | "Closed";

export interface BusinessRow {
  id: string;
  name: string;
  slug: string;
  product: string;
  status: BusinessStatus;
  planName: string | null;
  /** null for roles other than Support and Super admin. */
  ownerEmail: string | null;
  createdAt: string;
}

export interface AuditRowDto {
  id: string;
  tenantId: string | null;
  businessName: string | null;
  product: string | null;
  actorId: string | null;
  actorName: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  beforeJson: string | null;
  afterJson: string | null;
  ip: string | null;
  createdAt: string;
}

export interface BusinessDetailDto {
  id: string;
  name: string;
  slug: string;
  product: string;
  status: BusinessStatus;
  country: string;
  currency: string;
  createdAt: string;
  planName: string | null;
  subscription: {
    id: string;
    planCode: string;
    planName: string;
    priceMinor: number;
    status: string;
    currentPeriodStart: string | null;
    currentPeriodEnd: string | null;
    provider: string;
  } | null;
  usage: { key: string; label: string; used: number; limit: number }[];
  /** `email` is null for roles other than Support and Super admin. */
  members: { userId: string; name: string; email: string | null; role: string; status: string; lastLoginAt: string | null }[];
  webUrl: string | null;
  recentAudit: AuditRowDto[];
  payments: { id: string; amountMinor: number; currency: string; status: string; reference: string | null; createdAt: string }[];
  payouts: { id: string; amountMinor: number; status: string; reference: string | null; createdAt: string; paidAt: string | null }[];
}

export interface ViewAsResponse {
  token: string;
  expiresAt: string;
  product: string;
  webUrl: string;
  sessionId: string;
}

// Users

export interface UserRow {
  id: string;
  name: string;
  email: string;
  createdAt: string;
  lastLoginAt: string | null;
  businesses: number;
}

export interface UserDetailDto {
  id: string;
  name: string;
  email: string;
  phone: string | null;
  emailConfirmed: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  lockedOut: boolean;
  lockoutEnd: string | null;
  adminRole: PlatformRole | null;
  memberships: { tenantId: string; name: string; product: string; role: string; status: string; businessStatus: string }[];
  activity: { courses: number; stays: number; enquiries: number; viewings: number; savedSearches: number };
}

// Money

export interface PaymentRow {
  id: string;
  kind: "Subscription" | "Course" | "Stay" | "Boost";
  product: string;
  businessId: string;
  businessName: string;
  /** What was charged (or asked for, while unpaid). */
  amountMinor: number;
  refundedMinor: number;
  /** The one "collected" rule for every source: money actually received, less refunds. */
  collectedMinor: number;
  currency: string;
  status: string;
  reference: string | null;
  createdAt: string;
  paidAt: string | null;
}

export type PayoutStatus = "Requested" | "Paid" | "Rejected";

export interface PayoutRow {
  id: string;
  amountMinor: number;
  status: PayoutStatus;
  bankName: string | null;
  accountNumber: string | null;
  accountName: string | null;
  reference: string | null;
  requestedAt: string;
  paidAt: string | null;
  note: string | null;
  product: string;
  businessName: string;
  currency: string;
  academyName?: string | null;
}

export interface PlanDto {
  id: string;
  product: string;
  code: string;
  name: string;
  priceMinor: number;
  currency: string;
  interval: string;
  limits: Record<string, number>;
  features: string[];
  isDefault: boolean;
  isHidden: boolean;
  sortOrder: number;
  /** Running paid subscriptions; comps are counted apart. */
  activeSubscribers: number;
  compSubscribers: number;
}

export interface SavePlanRequest {
  product: string;
  code: string;
  name: string;
  priceMinor: number;
  isHidden: boolean;
  sortOrder: number;
  limits: Record<string, number>;
  features: string[];
}

// Admins

export interface AdminRow {
  id: string;
  userId: string;
  name: string;
  email: string;
  role: PlatformRole;
  addedAt: string;
  addedByName: string | null;
}

// Moderation: courses

export interface CourseReviewItem {
  courseId: string;
  title: string;
  academyName: string;
  submittedAt: string | null;
  lessonsCount: number;
  priceMinor: number;
  courseUrl: string | null;
}

export interface CategoryDto {
  id: string;
  name: string;
  slug: string;
  parentId: string | null;
}

// Moderation: property

export interface ModerationItemDto {
  listingId: string;
  title: string;
  status: string;
  note: string | null;
  agencyName: string;
  agencySlug: string;
  agencyVerified: boolean;
  hostCancellations: number;
  openReports: number;
  coverUrl: string | null;
  /** Every ready photo URL, in listing order. */
  photos: string[];
  /** The listing's public page; it only resolves once the listing is live. */
  publicUrl: string;
  priceMinor: number;
  pricePeriod: string;
  areaName: string | null;
  duplicates: { myPhotoUrl: string; theirPhotoUrl: string; theirListingId: string; theirListingTitle: string; theirAgency: string }[];
  submittedAt: string | null;
}

export interface VerificationDocument {
  index: number;
  label: string;
  fileName: string;
  contentType: string;
}

export type VerificationStatus = "Pending" | "Approved" | "Rejected";

export interface AdminVerificationDto {
  id: string;
  tenantId: string;
  agencyName: string;
  agencySlug: string;
  kind: string;
  status: VerificationStatus;
  licenceNo: string | null;
  note: string | null;
  submittedByName: string;
  submittedByEmail: string;
  documents: VerificationDocument[];
  createdAt: string;
}

export interface ReportRow {
  id: string;
  listingId: string;
  listingTitle: string;
  listingStatus: string;
  agencyName: string;
  reason: string;
  details: string | null;
  status: string;
  createdAt: string;
}

export interface HiddenReviewRow {
  id: string;
  agentName: string;
  agentSlug: string;
  userName: string;
  rating: number;
  comment: string | null;
  reportCount: number;
  status: string;
  createdAt: string;
}

export type LocationKind = "State" | "City" | "Area";

export interface AdminLocationDto {
  id: string;
  parentId: string | null;
  kind: LocationKind;
  name: string;
  slug: string;
  lat: number | null;
  lng: number | null;
  position: number;
}
