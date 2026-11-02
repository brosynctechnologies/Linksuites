"use client";

import type { SVGProps } from "react";
import { useAuth } from "@/lib/auth";
import { can, useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ClassDto, SchoolSetupDto, TermDto } from "@/lib/types";
import type { MyClassesDto, MyClassSubjectDto } from "@/lib/types/academic";
import { NoAccess, Select } from "@/components/ui";

// Helpers shared by the attendance, scores, results and timetable pages.

function Glyph({ children, ...p }: SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      width={20}
      height={20}
      aria-hidden="true"
      {...p}
    >
      {children}
    </svg>
  );
}

/** A register page with ticks: the Attendance nav icon. */
export function AttendanceIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <rect x="4" y="3" width="16" height="18" rx="2" />
      <path d="m8 9 1.5 1.5L12 8M8 15l1.5 1.5L12 14M15 9h2M15 15h2" />
    </Glyph>
  );
}

/** A pencil on a line: the Scores nav icon. */
export function ScoresIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <path d="M4 20h16" />
      <path d="m14.5 4.5 3 3L9 16H6v-3z" />
    </Glyph>
  );
}

/** A report card: the Results nav icon. */
export function ResultsIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <path d="M6 3h9l3 3v15H6z" />
      <path d="M9 10h6M9 14h6M9 18h3" />
    </Glyph>
  );
}

/** A week grid: the Timetable nav icon. */
export function TimetableIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <rect x="3" y="4" width="18" height="17" rx="2" />
      <path d="M3 9h18M8 4v17M3 15h18" />
    </Glyph>
  );
}

/** A clipboard: the Assignments nav icon. */
export function AssignmentsIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <rect x="6" y="3" width="12" height="18" rx="2" />
      <path d="M9 3.5h6V6H9zM8 11h8M8 15h5" />
    </Glyph>
  );
}

/** A page of a plan: the Lesson plans nav icon. */
export function LessonPlansIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <path d="M6 3h8l4 4v14H6z" />
      <path d="M14 3v4h4M9 12h6M9 16h4" />
    </Glyph>
  );
}

/** A calendar: the Exams nav icon. */
export function ExamsIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M3 10h18M8 3v4M16 3v4M8 15l2 2 4-4" />
    </Glyph>
  );
}

/** A question mark in a bubble: the Questions nav icon. */
export function QuestionsIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <path d="M4 5h16v11H9l-5 4z" />
      <path d="M10 9a2 2 0 1 1 2.5 1.9c-.4.2-.5.5-.5.9M12 14h.01" />
    </Glyph>
  );
}

/** A screen with ticked lines: the CBT nav icon. */
export function CbtIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <rect x="3" y="4" width="18" height="13" rx="2" />
      <path d="M8 21h8M12 17v4M7 9l1.5 1.5L11 8M13 9.5h4" />
    </Glyph>
  );
}

/** A puzzle piece: the Play nav icon. */
export function PlayIcon(p: SVGProps<SVGSVGElement>) {
  return (
    <Glyph {...p}>
      <path d="M10 4a2 2 0 1 1 4 0v2h4v4h-2a2 2 0 1 0 0 4h2v4h-4v-2a2 2 0 1 0-4 0v2H6v-4h2a2 2 0 1 0 0-4H6V6h4z" />
    </Glyph>
  );
}

/** Proprietor and principal: they see every class (the API's leadership). */
export function useLeadership(): boolean {
  return can(useRole(), "school.manage");
}

/** A support view reads but never writes. */
export function useReadOnly(): boolean {
  return !!useAuth()?.support;
}

export function AcademicNoAccess({ what }: { what: string }) {
  return <NoAccess text={`${what} is for the principal and the teachers.`} />;
}

export interface ClassOption {
  id: string;
  name: string;
}

/**
 * The classes of the current session the signed-in member can work with: every class for leadership, else the classes they are
 * class teacher of or teach a subject in (`/my-classes`).
 */
export function useAcademicClasses() {
  const leadership = useLeadership();
  const all = useApi<ClassDto[]>(leadership ? "/api/school/classes" : null);
  const mine = useApi<MyClassesDto>("/api/school/my-classes");
  const classTeacherOf = new Set((mine.data?.classTeacherOf ?? []).map((c) => c.classId));
  const subjects: MyClassSubjectDto[] = mine.data?.subjects ?? [];

  let classes: ClassOption[] = [];
  if (leadership) {
    classes = (all.data ?? []).map((c) => ({ id: c.id, name: c.name }));
  } else if (mine.data) {
    const seen = new Map<string, string>();
    for (const c of mine.data.classTeacherOf) seen.set(c.classId, c.className);
    for (const s of subjects) if (!seen.has(s.classId)) seen.set(s.classId, s.className);
    classes = [...seen].map(([id, name]) => ({ id, name }));
  }
  const loaded = leadership ? !!all.data : !!mine.data;
  const error = (leadership ? all.error : undefined) ?? mine.error;
  const reload = () => {
    all.reload();
    mine.reload();
  };
  return { classes, classTeacherOf, subjects, loaded, error, reload, leadership };
}

/** The terms of the current session (the session the class lists show), with the current term first picked. */
export function useSessionTerms() {
  const { data, error, reload } = useApi<SchoolSetupDto>("/api/school/setup");
  const session = data?.sessions.find((s) => s.isCurrent);
  const terms: TermDto[] = [...(session?.terms ?? [])].sort((a, b) => a.number - b.number);
  const current = terms.find((t) => t.isCurrent) ?? terms[terms.length - 1];
  return { terms, current, sessionName: session?.name ?? null, loaded: !!data, error, reload };
}

export function TermPicker({ terms, value, onChange }: { terms: TermDto[]; value: string; onChange: (id: string) => void }) {
  return (
    <div className="w-44">
      <Select label="Term" value={value} onChange={(e) => onChange(e.target.value)}>
        {terms.length === 0 && <option value="">No terms yet</option>}
        {terms.map((t) => (
          <option key={t.id} value={t.id}>
            {t.name}
            {t.isCurrent ? " (current)" : ""}
          </option>
        ))}
      </Select>
    </div>
  );
}

export function ClassPicker({ classes, value, onChange, className = "w-44" }: { classes: ClassOption[]; value: string; onChange: (id: string) => void; className?: string }) {
  return (
    <div className={className}>
      <Select label="Class" value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">Choose a class</option>
        {classes.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name}
          </option>
        ))}
      </Select>
    </div>
  );
}

/** "1.5" stays, "12" stays; trailing zeros go. */
export function score(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1).replace(/\.0$/, "");
}

/** 1st, 2nd, 3rd, 11th, 21st. */
export function ordinal(n: number): string {
  const lastTwo = Math.abs(n) % 100;
  const suffix = lastTwo >= 11 && lastTwo <= 13 ? "th" : ({ 1: "st", 2: "nd", 3: "rd" } as Record<number, string>)[Math.abs(n) % 10] ?? "th";
  return `${n}${suffix}`;
}
