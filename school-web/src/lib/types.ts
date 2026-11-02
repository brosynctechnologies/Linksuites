// Types mirroring the SchoolLink API (api/src/Saas.Application/School/*) and the shared auth/team parts.
// Enums travel as strings. Dates are "YYYY-MM-DD"; instants are ISO 8601 with an offset.

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;
  /** Import confirm (409 import_errors): the rows that still have errors. */
  rows?: ImportRowDto[];
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
/** Staff tokens carry the school (tenant); parent and student tokens have `tenant: null`. */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant | null;
}
export interface SupportViewDto {
  sessionId: string;
  adminName: string;
  reason: string;
  startedAt: string;
  endsAt: string;
  endedAt: string | null;
}
export interface PublicInviteDto {
  businessName: string;
  email: string;
  role: Role;
  hasAccount: boolean;
  expired: boolean;
}

// ---- Team ----
export interface MemberDto {
  id: string;
  userId: string;
  fullName: string;
  email: string;
  role: Role;
  isYou: boolean;
  joinedAt: string;
}
export interface TeamInviteDto {
  id: string;
  email: string;
  role: Role;
  invitedAt: string;
  expiresAt: string;
}
export interface TeamDto {
  members: MemberDto[];
  invites: TeamInviteDto[];
  seats: { used: number; limit: number };
}

// ---- School setup ----
export interface SchoolDto {
  name: string;
  slug: string;
  address: string | null;
  motto: string | null;
  hasLogo: boolean;
}
export interface TermDto {
  id: string;
  number: number;
  name: string;
  startDate: string;
  endDate: string;
  nextTermStarts: string | null;
  isCurrent: boolean;
}
export interface SessionDto {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  isCurrent: boolean;
  terms: TermDto[];
}
export interface ChecklistItemDto {
  key: "levels-and-classes" | "subjects" | "students" | "staff" | string;
  label: string;
  done: boolean;
}
export interface SchoolSetupDto {
  school: SchoolDto;
  sessions: SessionDto[];
  checklist: ChecklistItemDto[];
}
/** GET/PUT /api/school/settings/profile, PUT /settings/logo and /settings/signature (Owner and Admin). */
export interface SchoolBrandingDto {
  name: string;
  motto: string | null;
  address: string | null;
  /** A short-lived signed link to the logo, or null when there is none. */
  logoUrl: string | null;
  /** The signature is private: it is only printed on report cards, never sent to the browser. */
  hasSignature: boolean;
}
/** Leave a field out to keep it; an empty motto or address clears it; the name can't be empty. */
export interface SaveSchoolProfileRequest {
  name?: string;
  motto?: string;
  address?: string;
}
export interface SchoolPublicDto {
  name: string;
  logoUrl: string | null;
  motto: string | null;
}

// ---- Academic ----
export type Stage = "Creche" | "Nursery" | "Primary" | "Jss" | "Sss";
/** Creche and nursery are developmental; every other stage is numeric unless a level says otherwise. */
export type AssessmentMode = "Numeric" | "Developmental";
export interface LevelDto {
  id: string;
  name: string;
  stage: Stage;
  order: number;
  hasStudentLogins: boolean;
  assessmentMode: AssessmentMode;
}
export interface ClassDto {
  id: string;
  sessionId: string;
  levelId: string;
  levelName: string;
  arm: string;
  name: string;
  classTeacherStaffId: string | null;
}
export interface SubjectDto {
  id: string;
  name: string;
  code: string;
}
export interface ClassSubjectDto {
  id: string;
  classId: string;
  subjectId: string;
  subjectName: string;
  subjectCode: string;
  teacherStaffId: string | null;
}
export interface ApplyTemplateResult {
  levelsAdded: number;
  subjectsAdded: number;
}
export interface ComponentDto {
  id: string;
  name: string;
  maxScore: number;
  order: number;
}
export interface AssessmentDto {
  levelId: string | null;
  components: ComponentDto[];
}
export interface BandDto {
  min: number;
  max: number;
  grade: string;
  remark: string;
}
export interface GradingDto {
  levelId: string | null;
  bands: BandDto[];
}

/** Psychomotor and affective lists. A level with its own rows overrides the school default for that kind. */
export type TraitKind = "Psychomotor" | "Affective";
export interface TraitCriterionDto {
  id: string;
  name: string;
  order: number;
}
export interface TraitCriteriaDto {
  levelId: string | null;
  kind: TraitKind;
  criteria: TraitCriterionDto[];
}

/** Developmental areas and the scale a creche or nursery class is rated on. */
export interface DevelopmentalAreaDto {
  id: string;
  name: string;
  order: number;
}
export interface DevelopmentalAreasDto {
  levelId: string | null;
  areas: DevelopmentalAreaDto[];
}
export interface DevelopmentalScaleItemDto {
  id: string;
  code: string;
  label: string;
  order: number;
}
export interface DevelopmentalScaleDto {
  levelId: string | null;
  scale: DevelopmentalScaleItemDto[];
}

/** Comment bank lines. Subject and overall lines match a grade; developmental lines match a scale label. */
export type CommentScope = "Subject" | "TeacherOverall" | "PrincipalOverall" | "Developmental";
export interface CommentBankEntryDto {
  id: string;
  scope: CommentScope;
  matchKey: string;
  text: string;
  order: number;
}
export interface CommentBankDto {
  entries: CommentBankEntryDto[];
}

// ---- Staff ----
export interface StaffDto {
  staffId: string;
  membershipId: string;
  userId: string;
  fullName: string;
  email: string;
  role: Role;
  jobTitle: string;
  staffNo: string | null;
  phone: string | null;
  isYou: boolean;
}

// ---- Students ----
export type Gender = "Female" | "Male";
export type StudentStatus = "Active" | "Graduated" | "Withdrawn" | "Transferred";
export interface StudentGuardianDto {
  guardianId: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  relationship: string;
  isPrimary: boolean;
  hasAccount: boolean;
}
export interface StudentDto {
  id: string;
  admissionNo: string;
  firstName: string;
  middleName: string | null;
  lastName: string;
  fullName: string;
  gender: Gender;
  dateOfBirth: string | null;
  status: StudentStatus;
  statusDate: string | null;
  statusReason: string | null;
  admittedAt: string;
  photoUrl: string | null;
  classId: string | null;
  className: string | null;
  guardians: StudentGuardianDto[] | null;
}
export interface GuardianInviteDto {
  link: string;
  whatsAppUrl: string | null;
}

// ---- Import ----
export type ImportRowStatus = "New" | "Update" | "Error";
export interface ImportRowDto {
  row: number;
  status: ImportRowStatus;
  admissionNo: string;
  name: string;
  className: string;
  message: string | null;
}
export interface ImportPreviewDto {
  token: string;
  rows: ImportRowDto[];
  newCount: number;
  updateCount: number;
  errorCount: number;
  fitsPlan: boolean;
  /** Students the plan still has room for; null when unlimited. */
  room: number | null;
}
export interface ImportResultDto {
  created: number;
  updated: number;
}

// ---- Promotion ----
export type PromotionAction = "Promote" | "Repeat" | "Graduate";
export interface PromotionStudentDto {
  studentId: string;
  name: string;
  action: PromotionAction;
}
export interface PromotionClassDto {
  fromClassId: string;
  fromClassName: string;
  toClassName: string;
  students: PromotionStudentDto[];
}
export interface PromotionPreviewDto {
  classes: PromotionClassDto[];
  unplaced: { studentId: string; name: string }[];
}
export interface PromotionResult {
  sessionId: string;
  sessionName: string;
  promoted: number;
  repeated: number;
  graduated: number;
}

// ---- Parents and students ----
export interface GuardianInvitePreview {
  schoolName: string;
  guardianName: string;
  children: string[];
}
export interface ChildRef {
  studentId: string;
  tenantId: string;
  schoolName: string;
  schoolSlug: string;
  name: string;
  className: string;
}
export interface StudentMe {
  name: string;
  admissionNo: string;
  className: string;
  schoolName: string;
}
