// Types for attendance, scores, results, report cards and the timetable, mirroring
// api/src/Saas.Application/School/{AttendanceService,ScoreService,ResultService,ReportCardService,TimetableService,StaffService}.cs.
// Enums travel as strings. Dates are "YYYY-MM-DD"; instants are ISO 8601 with an offset.

// ---- My classes (GET /api/school/my-classes) ----
export interface MyClassDto {
  classId: string;
  className: string;
}
export interface MyClassSubjectDto {
  classSubjectId: string;
  classId: string;
  className: string;
  subjectId: string;
  subjectName: string;
  subjectCode: string;
}
export interface MyClassesDto {
  classTeacherOf: MyClassDto[];
  subjects: MyClassSubjectDto[];
}

// ---- Attendance ----
export type AttendanceStatus = "Present" | "Absent" | "Late" | "Excused";
export const ATTENDANCE_STATUSES: AttendanceStatus[] = ["Present", "Absent", "Late", "Excused"];

export interface RegisterMarkDto {
  enrolmentId: string;
  studentId: string;
  studentName: string;
  admissionNo: string;
  status: AttendanceStatus;
  note: string | null;
}
/** A student newly marked absent, with a ready WhatsApp message. Only filled on a save. */
export interface MessageParentDto {
  studentName: string;
  guardianName: string;
  whatsAppUrl: string;
}
/** `taken` is false for a register nobody has saved yet (every mark is then Present). */
export interface RegisterDto {
  classId: string;
  className: string;
  date: string;
  taken: boolean;
  clientId: string | null;
  marks: RegisterMarkDto[];
  messageParents: MessageParentDto[];
}
export interface RegisterMarkInput {
  enrolmentId: string;
  status: AttendanceStatus;
  note: string | null;
}
/** PUT /api/school/attendance/{classId}/{date}. The client id (at most 80 characters) makes a resend of the same save harmless. */
export interface SaveRegisterRequest {
  clientId: string;
  marks: RegisterMarkInput[];
  /** The stored register's client id the marks were based on ("" when it hadn't been taken). A newer stored register gives 409 register_changed. */
  baseClientId?: string;
  /** An unsent save on this phone that this one replaces. */
  priorClientId?: string | null;
}
export interface ClassAttendanceDto {
  classId: string;
  className: string;
  taken: boolean;
  present: number;
  absent: number;
  late: number;
  excused: number;
  /** Share present or late of those marked; null when the register wasn't taken. */
  percent: number | null;
}

// ---- Scores ----
export interface ScoreComponentDto {
  id: string;
  name: string;
  maxScore: number;
}
export interface ScoreStudentDto {
  enrolmentId: string;
  name: string;
  admissionNo: string;
  /** Keyed by component id; null is a score not entered yet. */
  values: Record<string, number | null>;
}
export interface ScoreGridDto {
  classId: string;
  classSubjectId: string;
  termId: string;
  locked: boolean;
  components: ScoreComponentDto[];
  students: ScoreStudentDto[];
}
/** One cell. `componentId` is the component's id (the API matches components by id); a null value clears the score. */
export interface ScoreEntryInput {
  enrolmentId: string;
  componentId: string;
  value: number | null;
}
export interface SaveScoresRequest {
  classSubjectId: string;
  termId: string;
  entries: ScoreEntryInput[];
}
export interface ResultLockDto {
  classId: string;
  termId: string;
  locked: boolean;
}

// ---- Results ----
export interface ResultSubjectDto {
  classSubjectId: string;
  subject: string;
  subjectCode: string;
  total: number;
  grade: string;
  /** Null when the subject has no comment. */
  remark: string | null;
  classAverage: number;
  position: number;
  incomplete: boolean;
}
/** One developmental area on the class grid. A numeric class sends an empty list. */
export interface ResultDevelopmentalDto {
  area: string;
  scale: string | null;
  comment: string | null;
}
export interface ResultStudentDto {
  enrolmentId: string;
  studentId: string;
  name: string;
  admissionNo: string;
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
  incomplete: boolean;
  subjects: ResultSubjectDto[];
  developmental: ResultDevelopmentalDto[];
  /** The mode this row was computed in, so a level that changed mode later does not rewrite it. */
  mode: "Numeric" | "Developmental";
}
export interface ResultsDto {
  classId: string;
  className: string;
  termId: string;
  termName: string;
  locked: boolean;
  publishedAt: string | null;
  /** Students in the class now who have no stored result row (for example, joined after publishing). */
  pendingStudents: number;
  students: ResultStudentDto[];
  /** The mode of the stored rows, or the level's current mode when there are none yet. */
  mode: "Numeric" | "Developmental";
}
/** Null fields are left as they are; an empty string clears the remark. */
export interface SetRemarksRequest {
  enrolmentId: string;
  teacherRemark: string | null;
  principalRemark: string | null;
}
/** An empty comment clears the subject comment. */
export interface SetSubjectCommentRequest {
  enrolmentId: string;
  classSubjectId: string;
  comment: string | null;
}
export interface SubjectCommentDto {
  enrolmentId: string;
  classSubjectId: string;
  comment: string | null;
}

/** Lines from the comment bank. Subject lines match one subject; the overall lists match the child's average, or the developmental scale. */
export interface CommentSuggestionsDto {
  subject: string[];
  teacherOverall: string[];
  principalOverall: string[];
}
export interface TraitRatingItemDto {
  criterionId: string;
  name: string;
  order: number;
  /** 1 to 5, or null when the item has not been rated. */
  value: number | null;
}
export interface DevelopmentalRatingItemDto {
  areaId: string;
  name: string;
  order: number;
  scaleId: string | null;
  scale: string | null;
  code: string | null;
  comment: string | null;
}
/**
 * One child for a term. `subjects` is every subject for leadership and the class teacher, and only the
 * subjects a subject teacher teaches. `suggestions` is set only when the request asks for them.
 */
export interface ChildResultDto {
  studentId: string;
  enrolmentId: string;
  name: string;
  admissionNo: string;
  classId: string;
  className: string;
  termId: string;
  termName: string;
  locked: boolean;
  publishedAt: string | null;
  daysPresent: number;
  /** Null on results published before late, absent and excused were counted. */
  daysAbsent: number | null;
  daysLate: number | null;
  daysExcused: number | null;
  daysOpen: number;
  subjects: ResultSubjectDto[];
  developmental: DevelopmentalRatingItemDto[];
  psychomotor: TraitRatingItemDto[];
  affective: TraitRatingItemDto[];
  teacherRemark: string | null;
  principalRemark: string | null;
  suggestions: CommentSuggestionsDto | null;
  mode: "Numeric" | "Developmental";
}
export interface PublishResultDto {
  classId: string;
  termId: string;
  published: boolean;
  publishedAt: string | null;
  emailed: number;
}

// ---- Report cards ----
export interface ReportCardBundleRequestedDto {
  id: string;
}
export type ReportCardBundleStatus = "Queued" | "Ready" | "Failed";
/** `url` is a short-lived signed link, only when Ready. */
export interface ReportCardBundleDto {
  status: ReportCardBundleStatus | string;
  url: string | null;
  classId: string;
  className: string;
  termId: string;
}

// ---- Timetable ----
export type SchoolDay = "Mon" | "Tue" | "Wed" | "Thu" | "Fri" | "Sat";
export const SCHOOL_DAYS: { value: SchoolDay; label: string }[] = [
  { value: "Mon", label: "Monday" },
  { value: "Tue", label: "Tuesday" },
  { value: "Wed", label: "Wednesday" },
  { value: "Thu", label: "Thursday" },
  { value: "Fri", label: "Friday" },
  { value: "Sat", label: "Saturday" },
];
export const MAX_PERIOD = 12;

/** One lesson as sent in the PUT body (a bare array of these). Start and end are "HH:mm". */
export interface TimetableSlotInput {
  day: SchoolDay;
  period: number;
  start: string;
  end: string;
  classSubjectId: string;
}
export interface TimetableSlotDto {
  id: string;
  day: SchoolDay;
  period: number;
  start: string;
  end: string;
  classSubjectId: string;
  subject: string;
  subjectCode: string;
  teacher: string | null;
}
/** The teacher of a lesson also teaches `otherClass` at the same day and period. */
export interface TimetableClashDto {
  teacher: string;
  day: SchoolDay;
  period: number;
  otherClass: string;
}
export interface TimetableDto {
  classId: string;
  className: string;
  slots: TimetableSlotDto[];
  warnings: TimetableClashDto[];
}

// ---- Assignments (AssignmentService) ----
export type AssignmentSubmissionStatus = "None" | "Received" | "Returned";
export interface AssignmentSubmissionDto {
  enrolmentId: string;
  name: string;
  admissionNo: string;
  status: AssignmentSubmissionStatus;
  submitted: boolean;
  late: boolean;
  body: string | null;
  submittedAt: string | null;
  mark: number | null;
  feedback: string | null;
  fileName: string | null;
  fileUrl: string | null;
}
export interface AssignmentDto {
  id: string;
  classSubjectId: string;
  termId: string;
  title: string;
  instructions: string;
  dueAt: string;
  maxMarks: number | null;
  assessmentComponentId: string | null;
  submissions: AssignmentSubmissionDto[];
}
export interface CreateAssignmentRequest {
  classSubjectId: string;
  termId: string;
  title: string;
  instructions: string;
  dueAt: string;
  maxMarks?: number | null;
  assessmentComponentId?: string | null;
}
export interface MarkAssignmentRequest {
  mark?: number | null;
  feedback?: string | null;
  reopen?: boolean | null;
  received?: boolean | null;
}
export interface StudentAssignmentDto {
  id: string;
  title: string;
  instructions: string;
  dueAt: string;
  maxMarks: number | null;
  submitted: boolean;
  late: boolean;
  body: string | null;
  mark: number | null;
  feedback: string | null;
  fileName: string | null;
  fileUrl: string | null;
}
/** Parent homework: title, due, submitted, and the teacher's mark. No instructions or files. */
export interface ParentAssignmentDto {
  id: string;
  title: string;
  dueAt: string;
  submitted: boolean;
  mark: number | null;
}

// ---- Lesson plans (LessonPlanService) ----
export type LessonPlanStatus = "Draft" | "Submitted" | "Accepted" | "Returned";
export interface LessonPlanDto {
  id: string;
  classSubjectId: string;
  termId: string;
  week: number;
  date: string;
  topic: string;
  objectives: string;
  materials: string;
  methods: string;
  activities: string;
  previousKnowledge: string;
  introduction: string;
  presentation: string;
  conclusion: string;
  evaluation: string;
  assignmentId: string | null;
  teacherRemarks: string | null;
  status: LessonPlanStatus;
  reviewerNote: string | null;
  submittedAt: string | null;
  reviewedBy: string | null;
  createdBy: string;
}
export interface SaveLessonPlanRequest {
  classSubjectId: string;
  termId: string;
  week: number;
  date: string;
  topic: string;
  objectives: string;
  materials: string;
  methods: string;
  activities: string;
  previousKnowledge: string;
  introduction: string;
  presentation: string;
  conclusion: string;
  evaluation: string;
  assignmentId: string | null;
  teacherRemarks: string | null;
}
export interface LessonPlanSettingsDto {
  needReview: boolean;
}

// ---- Exam timetable (ExamTimetableService) ----
export type ExamPeriodKind = "Test" | "Examination";
export interface ExamWarningDto {
  sittingId: string;
  detail: string;
}
export interface ExamPeriodDto {
  id: string;
  termId: string;
  name: string;
  kind: ExamPeriodKind;
  start: string;
  end: string;
  publishedAt: string | null;
}
export interface ExamSittingDto {
  id: string;
  periodId: string;
  classSubjectId: string;
  subject: string;
  subjectCode: string;
  className: string;
  date: string;
  start: string;
  end: string;
  durationMinutes: number;
  venue: string;
  instructions: string;
  invigilatorStaffId: string | null;
  invigilator: string | null;
  warnings: ExamWarningDto[];
}
export interface ExamPeriodViewDto {
  id: string;
  termId: string;
  name: string;
  kind: ExamPeriodKind;
  start: string;
  end: string;
  publishedAt: string | null;
  sittings: ExamSittingDto[];
}
export interface SaveExamPeriodRequest {
  termId: string;
  name: string;
  kind: ExamPeriodKind;
  start: string;
  end: string;
}
export interface SaveExamSittingRequest {
  classSubjectId: string;
  date: string;
  start: string;
  end: string;
  durationMinutes: number;
  venue: string;
  instructions: string;
  invigilatorStaffId?: string | null;
}
/** GET /api/student/exam-timetable and /api/parent/children/{studentId}/exam-timetable: published sittings of the child's class. */
export interface PortalExamSittingDto {
  id: string;
  subject: string;
  subjectCode: string;
  date: string;
  start: string;
  end: string;
  durationMinutes: number;
  venue: string;
  instructions: string;
  invigilator: string | null;
}
export interface PortalExamPeriodDto {
  id: string;
  name: string;
  kind: ExamPeriodKind;
  start: string;
  end: string;
  sittings: PortalExamSittingDto[];
}
export interface PortalExamTimetableDto {
  periods: PortalExamPeriodDto[];
}

// ---- Question bank and CBT (CbtService) ----
/** Staff only: `correctIndex` is the bank's answer and never reaches a student. */
export interface BankQuestionDto {
  id: string;
  subjectId: string;
  levelId: string | null;
  stem: string;
  options: string[];
  correctIndex: number;
  marks: number;
  archived: boolean;
  createdBy: string;
}
export interface SaveBankQuestionRequest {
  subjectId: string;
  levelId: string | null;
  stem: string;
  options: string[];
  correctIndex: number;
  marks: number;
}
/** `drawCount` is extra questions drawn from the bank at the start, on top of the picks. With `examSittingId`, the sitting's window is used. */
export interface CreateCbtPaperRequest {
  classSubjectId: string;
  termId: string;
  title: string;
  durationMinutes: number;
  examSittingId: string | null;
  openAt: string | null;
  closeAt: string | null;
  assessmentComponentId: string | null;
  drawCount: number;
  pickQuestionIds: string[];
}
export interface CbtPaperDto {
  id: string;
  classSubjectId: string;
  termId: string;
  title: string;
  durationMinutes: number;
  examSittingId: string | null;
  openAt: string;
  closeAt: string;
  assessmentComponentId: string | null;
  drawCount: number;
  pickQuestionIds: string[];
  publishedAt: string | null;
  createdBy: string;
  releasedKeyAt: string | null;
}
export type CbtAttemptStatus = "NotStarted" | "InProgress" | "Submitted";
/** GET /api/student/cbt/papers: published papers of the student's class this term, with the student's current attempt. No key. */
export interface StudentCbtPaperDto {
  id: string;
  title: string;
  subject: string;
  subjectCode: string;
  durationMinutes: number;
  openAt: string;
  closeAt: string;
  counted: boolean;
  status: CbtAttemptStatus;
  attemptId: string | null;
  deadlineAt: string | null;
  submittedAt: string | null;
  totalMarks: number | null;
}
/** Staff: one question of an attempt. `marksAwarded` is null until the attempt is handed in. */
export interface CbtStaffItemDto {
  order: number;
  stem: string;
  marks: number;
  marksAwarded: number | null;
}
export interface CbtStaffAttemptDto {
  attemptId: string;
  startedAt: string;
  deadlineAt: string;
  submittedAt: string | null;
  reopenedAt: string | null;
  totalMarks: number;
  paperMarks: number;
  items: CbtStaffItemDto[];
}
export interface CbtRosterRowDto {
  enrolmentId: string;
  name: string;
  admissionNo: string;
  /** Newest first. */
  attempts: CbtStaffAttemptDto[];
}
/** GET /api/school/cbt/papers/{id}/attempts: the class roster with each student's attempts. */
export interface CbtAttemptsDto {
  paperId: string;
  title: string;
  releasedKeyAt: string | null;
  students: CbtRosterRowDto[];
}
export interface CbtReleaseDto {
  id: string;
  releasedKeyAt: string;
}
export interface CbtReopenDto {
  id: string;
  reopenedAt: string;
}
/** A student item: the stem and the options in the order this student sees them. No answer key. */
export interface CbtStartItemDto {
  id: string;
  stem: string;
  options: string[];
}
export interface CbtStartDto {
  attemptId: string;
  deadlineAt: string;
  items: CbtStartItemDto[];
}
export interface SaveCbtAnswerRequest {
  itemId: string;
  chosenIndex: number;
}
export interface CbtOpenItemDto extends CbtStartItemDto {
  chosenIndex: number | null;
}
/** Before submit: the items and the saved choices. */
export interface CbtAttemptOpenDto {
  attemptId: string;
  deadlineAt: string;
  items: CbtOpenItemDto[];
}
/** After submit, before the key is released: the total only. */
export interface CbtAttemptTotalDto {
  attemptId: string;
  deadlineAt: string;
  submittedAt: string | null;
  totalMarks: number;
  autoMarked: boolean;
}
export interface CbtMarkedItemDto extends CbtOpenItemDto {
  correct: boolean;
  marksAwarded: number;
}
/** After the key is released (by the teacher, or when the window ends): each item's correctness. */
export interface CbtAttemptMarkedDto {
  attemptId: string;
  deadlineAt: string;
  submittedAt: string;
  totalMarks: number;
  autoMarked: boolean;
  items: CbtMarkedItemDto[];
}
/** GET /api/student/cbt/attempts/{id} and the submit response: one of the three shapes above. */
export type CbtAttemptViewDto = CbtAttemptOpenDto | CbtAttemptTotalDto | CbtAttemptMarkedDto;

// ---- Play (PlayService) ----
/** One list per scope: a class subject, a level, or the whole school (both null). Words are stored upper case. */
export interface SaveWordListRequest {
  classSubjectId: string | null;
  levelId: string | null;
  name: string;
  words: string[];
}
export interface WordListDto {
  id: string;
  classSubjectId: string | null;
  levelId: string | null;
  name: string;
  words: string[];
  createdBy: string;
}
export interface SaveBrainTeaserRequest {
  levelId: string | null;
  prompt: string;
  answer: string;
}
/** Staff only: the answer is never sent to a student. */
export interface BrainTeaserDto {
  id: string;
  levelId: string | null;
  prompt: string;
  answer: string;
  archived: boolean;
  createdBy: string;
}
export interface PlayTeaserDto {
  id: string;
  prompt: string;
}
/** A square grid of single A–Z letters. Each word reads once, left to right or top to bottom. */
export interface WordSearchDto {
  grid: string[][];
  words: string[];
}
export interface PlaySettingsRequest {
  enabled: boolean;
}
export interface PlaySettingsDto {
  enabled: boolean;
}
