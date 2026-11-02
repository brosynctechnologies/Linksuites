// Communication types: announcements, messages, in-app notifications, plus the student/guardian fields the communication pages read.
// Mirrors api/src/Saas.Application/School/{AnnouncementService,MessageService,StaffService,StudentService}.cs and
// api/src/Saas.Application/Common/InAppNotifications.cs. Enums travel as strings; instants are ISO 8601.

import type { Role, StudentDto } from "../types";

// ---- In-app notifications (/api/me/notifications) ----

/** One row of the bell. `link` is an app path ("/app/messages/…", "/parent/…", "/student/…"). */
export interface UserNotificationDto {
  id: string;
  product: string;
  kind: string;
  title: string;
  body: string;
  link: string;
  tenantId: string | null;
  createdAt: string;
  readAt: string | null;
}
/** `GET /api/me/notifications?unread=&page=`: newest first, 20 a page. */
export interface NotificationPage {
  items: UserNotificationDto[];
  page: number;
  pageSize: number;
  total: number;
}
/** `GET /api/me/notifications/unread-count`. The API stops counting at 99; the UI shows 9+. */
export interface UnreadCountDto {
  count: number;
}

// ---- Announcements ----

export type AnnouncementAudience = "School" | "Levels" | "Classes" | "Staff";
export interface PostAnnouncementRequest {
  title: string;
  body: string;
  audience: AnnouncementAudience;
  levelIds: string[];
  classIds: string[];
  emailGuardians: boolean;
}
/** `whatsAppText` is filled only in the response to posting. */
export interface AnnouncementDto {
  id: string;
  title: string;
  body: string;
  audience: AnnouncementAudience;
  levelIds: string[];
  classIds: string[];
  emailGuardians: boolean;
  postedBy: string;
  postedByName: string | null;
  postedAt: string;
  attachmentName: string | null;
  attachmentUrl: string | null;
  whatsAppText: string | null;
}
/** What parents and students see (`/api/parent/children/{id}/announcements`, `/api/student/announcements`). */
export interface PortalAnnouncementDto {
  id: string;
  title: string;
  body: string;
  postedAt: string;
  attachmentName: string | null;
  attachmentUrl: string | null;
}

// ---- Messages ----

export interface ThreadDto {
  id: string;
  studentId: string;
  studentName: string;
  guardianId: string;
  guardianName: string;
  staffId: string | null;
  staffName: string | null;
  isOffice: boolean;
  lastMessageAt: string;
  lastMessage: string | null;
  /** Messages from the other side not yet read (for staff: the parent's). */
  unread: number;
}
export interface MessageDto {
  id: string;
  threadId: string;
  senderUserId: string;
  senderName: string;
  fromParent: boolean;
  body: string;
  imageUrl: string | null;
  sentAt: string;
  readAt: string | null;
}
/** Parent side: `POST /api/parent/children/{id}/threads`. `to` is "class-teacher" or "office". */
export interface StartThreadRequest {
  to: "class-teacher" | "office";
  body: string;
}

// ---- Staff and students (carry items) ----

/** `GET /api/school/staff/me`: the signed-in member's role, job title and staff profile id (null without a profile). */
export interface StaffMeDto {
  role: Role;
  jobTitle: string;
  staffId: string | null;
}

/** `StudentDto` with `hasLogin`: true when the student has their own login. */
export type StudentWithLoginDto = StudentDto & { hasLogin: boolean };

export interface GuardianChildDto {
  studentId: string;
  name: string;
  className: string;
}
/** `GET /api/school/guardians?withoutEmail=&classId=`: guardians of a class's active students (leadership and the bursar). */
export interface ClassGuardianDto {
  guardianId: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  hasAccount: boolean;
  children: GuardianChildDto[];
}
