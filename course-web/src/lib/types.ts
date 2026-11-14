import type { BankTransfer, PendingTransfer } from "@/lib/plan-payment";

// Types mirroring docs/COURSES_API.md (and the shared auth/team/billing parts of docs/API.md and
// docs/INVOICES_PHASE2_API.md). Money fields ending in `Minor` are integers in minor units.
// Instants are ISO 8601 with an offset; dates are "YYYY-MM-DD".

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
/** Tutor tokens carry a tenant; learner tokens have `tenant: null`. */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant | null;
}
export interface TutorSignupRequest {
  fullName: string;
  email: string;
  password: string;
  country: string;
  product: "courses";
  businessName: string;
}

// ---- Shared enums ----
export type Level = "Beginner" | "Intermediate" | "Advanced" | "AllLevels";
export type CourseStatus = "Draft" | "Published" | "Archived";
export type MarketplaceStatus = "NotListed" | "InReview" | "Approved" | "Rejected";
export type LessonType = "Video" | "Text" | "File" | "Quiz";
export type VideoStatus = "None" | "Processing" | "Ready" | "Failed";
export type QuestionType = "SingleChoice" | "MultipleChoice" | "TrueFalse" | "ShortAnswer";
export type Channel = "Marketplace" | "Academy";
/** How a learner got into a course (server EnrollmentSource). */
export type EnrollmentSource = "Purchase" | "Free" | "Coupon";
export type PayoutStatus = "Requested" | "Paid" | "Rejected";
export type EarningStatus = "Pending" | "Available" | "PaidOut" | "Reversed";
export type DiscountType = "Percent" | "Fixed";

// ---- Academy ----
export interface PayoutAccount {
  bankName: string;
  accountNumber: string;
  accountName: string;
}
export interface AcademyDto {
  name: string;
  slug: string;
  headline: string | null;
  bio: string | null;
  logoUrl: string | null;
  brandColor: string | null;
  currency: string;
  payoutAccount: PayoutAccount | null;
  academyUrl: string;
}
export interface UpdateAcademyRequest {
  name: string;
  slug: string;
  headline: string | null;
  bio: string | null;
  brandColor: string | null;
}

// ---- Studio: courses ----
export interface SaveCourseRequest {
  title: string;
  subtitle: string | null;
  description: string | null;
  categoryId: string | null;
  level: Level;
  language: string;
  priceMinor: number;
  isFree: boolean;
  outcomes: string[];
  requirements: string[];
  listOnMarketplace: boolean;
}
export interface StudioCourseSummaryDto {
  id: string;
  title: string;
  slug: string;
  thumbnailUrl: string | null;
  status: CourseStatus;
  marketplaceStatus: MarketplaceStatus;
  priceMinor: number;
  isFree: boolean;
  lessonsCount: number;
  studentsCount: number;
  ratingAverage: number | null;
  ratingCount: number;
  revenueMinor: number;
  updatedAt: string;
}
export interface LessonSummaryDto {
  id: string;
  title: string;
  type: LessonType;
  position: number;
  isPreview: boolean;
  durationSec: number | null;
  videoStatus: VideoStatus;
}
export interface StudioSectionDto {
  id: string;
  title: string;
  position: number;
  lessons: LessonSummaryDto[];
}
export interface StudioCourseDto extends SaveCourseRequest {
  id: string;
  slug: string;
  thumbnailUrl: string | null;
  status: CourseStatus;
  marketplaceStatus: MarketplaceStatus;
  marketplaceNote: string | null;
  publishedAt: string | null;
  courseUrl: string;
  sections: StudioSectionDto[];
}

// ---- Studio: lessons ----
export interface SaveLessonRequest {
  title: string;
  type: LessonType;
  isPreview: boolean;
  content: string | null;
}
export interface VideoDto {
  status: VideoStatus;
  format: "hls" | "mp4" | string | null;
  durationSec: number | null;
  thumbnailUrl: string | null;
  error: string | null;
  renditions: string[];
}
export interface ResourceDto {
  id: string;
  title: string;
  fileName: string;
  sizeBytes: number;
  url: string;
}
export interface QuizOption {
  id: string | null;
  text: string;
  isCorrect: boolean;
}
export interface QuizQuestion {
  id: string | null;
  type: QuestionType;
  prompt: string;
  points: number;
  explanation: string | null;
  options: QuizOption[];
  acceptedAnswers: string[];
}
export interface SaveQuizRequest {
  timeLimitMinutes: number | null;
  passMarkPercent: number;
  maxAttempts: number | null;
  shuffle: boolean;
  questions: QuizQuestion[];
}
export type QuizDto = SaveQuizRequest;
export interface StudioLessonDto extends SaveLessonRequest {
  id: string;
  sectionId: string;
  courseId: string;
  durationSec: number | null;
  video: VideoDto | null;
  resources: ResourceDto[];
  quiz: QuizDto | null;
}

// ---- Studio: learners, Q&A, reviews, coupons, money ----
export interface StudioDashboardDto {
  currency: string;
  studentsTotal: number;
  enrolmentsThisMonth: number;
  revenueThisMonthMinor: number;
  availableMinor: number;
  pendingMinor: number;
  ratingAverage: number | null;
  recentEnrolments: { studentName: string; courseTitle: string; enrolledAt: string; amountMinor: number; channel: Channel | null }[];
}
export interface StudentDto {
  enrolmentId: string;
  userId: string;
  name: string;
  email: string;
  courseId: string;
  courseTitle: string;
  enrolledAt: string;
  progressPercent: number;
  completedAt: string | null;
  /** How the learner got in. */
  source: EnrollmentSource;
  /** Where the sale was made; null for free courses and 100% coupons (no order). */
  channel: Channel | null;
}
export interface AnswerDto {
  id: string;
  authorName: string;
  isTutor: boolean;
  body: string;
  createdAt: string;
}
export interface QuestionDto {
  id: string;
  courseId: string;
  courseTitle: string;
  lessonId: string | null;
  lessonTitle: string | null;
  authorName: string;
  body: string;
  createdAt: string;
  answers: AnswerDto[];
}
export interface StudioReviewDto {
  id: string;
  courseTitle: string;
  studentName: string;
  rating: number;
  comment: string | null;
  createdAt: string;
}
export interface CouponDto {
  id: string;
  code: string;
  courseId: string | null;
  discountType: DiscountType;
  value: number;
  maxUses: number | null;
  uses: number;
  expiresAt: string | null;
  isActive: boolean;
}
export interface EarningEntry {
  id: string;
  /** The course order this sale came from; refunds post to it. */
  orderId: string;
  courseTitle: string;
  studentName: string;
  channel: Channel;
  grossMinor: number;
  feeMinor: number;
  netMinor: number;
  availableAt: string;
  createdAt: string;
  status: EarningStatus;
  /** Still inside the refund window (the server decides). */
  refundable: boolean;
}
export interface EarningsDto {
  currency: string;
  availableMinor: number;
  pendingMinor: number;
  paidOutMinor: number;
  entries: Paged<EarningEntry>;
}
export interface PayoutDto {
  id: string;
  amountMinor: number;
  status: PayoutStatus;
  bankName: string;
  accountNumber: string;
  accountName: string;
  reference: string | null;
  requestedAt: string;
  paidAt: string | null;
  note: string | null;
}

// ---- Public catalogue ----
export interface CategoryDto {
  id: string;
  name: string;
  slug: string;
  parentId: string | null;
}
export interface CourseCardDto {
  id: string;
  slug: string;
  academySlug: string;
  academyName: string;
  title: string;
  subtitle: string | null;
  thumbnailUrl: string | null;
  priceMinor: number;
  currency: string;
  isFree: boolean;
  level: Level;
  ratingAverage: number | null;
  ratingCount: number;
  studentsCount: number;
  lessonsCount: number;
  durationSec: number | null;
  categoryName: string | null;
}
export interface AcademyPageDto {
  name: string;
  slug: string;
  headline: string | null;
  bio: string | null;
  logoUrl: string | null;
  brandColor: string | null;
  showBranding: boolean;
  courses: CourseCardDto[];
}
export interface CourseDetailDto extends CourseCardDto {
  description: string | null;
  language: string | null;
  outcomes: string[];
  requirements: string[];
  updatedAt: string;
  tutor: { name: string; headline: string | null; bio: string | null; logoUrl: string | null };
  brandColor: string | null;
  sections: { id: string; title: string; lessons: { id: string; title: string; type: LessonType; durationSec: number | null; isPreview: boolean }[] }[];
  enrolled: boolean;
}
export interface PublicReviewDto {
  studentName: string;
  rating: number;
  comment: string | null;
  createdAt: string;
}
export interface CertificateDto {
  code: string;
  studentName: string;
  courseTitle: string;
  tutorName: string;
  academyName: string;
  issuedAt: string;
}

// ---- Buying and learning ----
export interface CheckoutResponse {
  enrolled: boolean;
  authorizationUrl: string | null;
  orderId: string | null;
  priceMinor: number;
  discountMinor: number;
  totalMinor: number;
}
export interface QuoteResponse {
  priceMinor: number;
  discountMinor: number;
  totalMinor: number;
  couponMessage: string | null;
}
export interface MyCourseDto {
  courseId: string;
  slug: string;
  academySlug: string;
  title: string;
  thumbnailUrl: string | null;
  tutorName: string;
  progressPercent: number;
  lessonsDone: number;
  lessonsTotal: number;
  lastLessonId: string | null;
  completedAt: string | null;
  certificateCode: string | null;
}
export interface LearnLessonDto {
  id: string;
  title: string;
  type: LessonType;
  durationSec: number | null;
  completed: boolean;
  isQuiz: boolean;
  passed: boolean | null;
}
export interface LearnCourseDto {
  courseId: string;
  title: string;
  academySlug: string;
  slug: string;
  tutorName: string;
  progressPercent: number;
  certificateCode: string | null;
  brandColor: string | null;
  sections: { id: string; title: string; lessons: LearnLessonDto[] }[];
  resumeLessonId: string | null;
}
export interface LessonPlaybackDto {
  id: string;
  courseId: string;
  title: string;
  type: LessonType;
  content: string | null;
  durationSec: number | null;
  video: { status: VideoStatus; format: string | null; url: string | null; thumbnailUrl: string | null } | null;
  resources: { title: string; url: string; sizeBytes: number }[];
  quiz: {
    questionsCount: number;
    timeLimitMinutes: number | null;
    passMarkPercent: number;
    maxAttempts: number | null;
    attemptsUsed: number;
    bestScorePercent: number | null;
    passed: boolean;
  } | null;
  positionSec: number | null;
  completed: boolean;
  previousLessonId: string | null;
  nextLessonId: string | null;
  watermark: string | null;
}
export interface ProgressResponse {
  progressPercent: number;
  courseCompleted: boolean;
  certificateCode: string | null;
}
export interface NoteDto {
  body: string;
  updatedAt: string | null;
}
export interface AttemptDto {
  attemptId: string;
  startedAt: string;
  endsAt: string | null;
  questions: { id: string; type: QuestionType; prompt: string; points: number; options: { id: string; text: string }[] }[];
}
export interface QuizResultDto {
  attemptId: string;
  scorePercent: number;
  passed: boolean;
  submittedAt: string | null;
  questions: {
    id: string;
    prompt: string;
    correct: boolean;
    explanation: string | null;
    yourOptionIds: string[];
    yourText: string | null;
    correctOptionIds: string[];
  }[];
}
export interface MyCertificateDto {
  code: string;
  courseTitle: string;
  issuedAt: string;
  pdfUrl: string;
  verifyUrl: string;
}

// ---- Account activity ----
/** A read-only support view of this academy (`GET /api/account/activity`, last 90 days). */
export interface SupportViewDto {
  sessionId: string;
  adminName: string;
  reason: string;
  startedAt: string;
  endsAt: string;
  /** Set when the view was ended before it expired. */
  endedAt: string | null;
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
  /** Nothing renews automatically: the plan ends at currentPeriodEnd unless paid again. A comp can't be extended by paying. */
  subscription: { status: string; currentPeriodEnd: string | null; complimentary: boolean } | null;
  plans: PlanDto[];
  usage: { items?: UsageItem[] } & Record<string, unknown>;
  paymentsEnabled: boolean;
  /** The company account to pay a plan into by bank transfer: only while online payment is off. */
  bankTransfer?: BankTransfer | null;
  /** A bank transfer this business said it sent, waiting for confirmation. */
  pendingTransfer?: PendingTransfer | null;
}

// ---- Team & academies ----
export type InviteRole = Exclude<Role, "Owner">;
export interface MemberDto {
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
