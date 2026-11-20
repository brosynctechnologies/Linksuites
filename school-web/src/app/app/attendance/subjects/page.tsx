"use client";

import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { SubjectAttendanceReportDto } from "@/lib/types/academic";
import { ApiErrorView, EmptyState, Notice, PageHeader, PageLoader, RuledSheet, ruledRowClass } from "@/components/ui";
import { AcademicNoAccess } from "../../scores/academic-shared";
import { SubjectTermBar, useSubjectTerm } from "../../teaching-shared";
import { AttendanceNav } from "../attendance-nav";

export default function SubjectAttendanceReportPage() {
  const role = useRole();
  const state = useSubjectTerm();
  if (role === "Accountant") return <AcademicNoAccess what="Attendance" />;

  const error = state.academic.error ?? state.terms.error;
  const loaded = state.academic.loaded && state.terms.loaded;
  return (
    <div className="min-w-0 space-y-4">
      <PageHeader title="Attendance" subtitle="Each student's lessons in a subject this term: present, absent and late." />
      <AttendanceNav />
      {error && (
        <ApiErrorView
          error={error}
          onRetry={() => {
            state.academic.reload();
            state.terms.reload();
          }}
        />
      )}
      {!loaded && !error && <PageLoader />}
      {loaded && state.classes.length === 0 && (
        <EmptyState
          title="No subjects to show"
          text={
            state.academic.leadership
              ? "This session has no classes yet. Add them in Setup."
              : "You don't teach a subject in any class this session. Ask the principal to assign you."
          }
        />
      )}
      {loaded && state.classes.length > 0 && (
        <>
          <SubjectTermBar state={state} />
          <ApiErrorView error={state.subjectsError} onRetry={state.reloadSubjects} />
          {!state.classId && <p className="text-sm text-muted">Choose a class to see attendance by subject.</p>}
          {state.classId && !state.subjectsLoading && state.subjects.length === 0 && <Notice>This class has no subjects you teach.</Notice>}
          {state.classSubjectId && state.termId && <Report classSubjectId={state.classSubjectId} termId={state.termId} />}
        </>
      )}
    </div>
  );
}

function Report({ classSubjectId, termId }: { classSubjectId: string; termId: string }) {
  const report = useApi<SubjectAttendanceReportDto>(`/api/school/subject-attendance/report${qs({ classSubjectId, termId })}`);
  if (report.error) return <ApiErrorView error={report.error} onRetry={report.reload} />;
  if (!report.data) return <PageLoader />;
  const data = report.data;
  if (data.lessons === 0) {
    return <EmptyState title="No lesson registers yet" text={`No register has been taken for ${data.subject} in ${data.className} this term.`} />;
  }
  return (
    <>
      <p className="tnum text-sm text-muted">
        {data.subject}, {data.className}: {data.lessons} {data.lessons === 1 ? "lesson" : "lessons"} taken in {data.termName}
      </p>
      <RuledSheet
        label="Students"
        busy={report.loading}
        template="minmax(0,1fr) 5rem 5rem 5rem 5rem"
        head={[{ label: "Student" }, { label: "Present", align: "right" }, { label: "Absent", align: "right" }, { label: "Late", align: "right" }, { label: "Share", align: "right" }]}
      >
        {data.students.map((s) => (
          <li key={s.enrolmentId} className={ruledRowClass}>
            <span className="min-w-0">
              <span className="block font-bold break-words text-ink">{s.name}</span>
              <span className="tnum block text-xs text-muted">{s.admissionNo}</span>
            </span>
            <span className="tnum hidden text-right text-sm md:block">{s.present}</span>
            <span className="tnum hidden text-right text-sm md:block">{s.absent}</span>
            <span className="tnum hidden text-right text-sm md:block">{s.late}</span>
            <span className="tnum text-sm text-muted md:text-right md:text-ink">
              {s.percent === null ? "No lessons" : `${s.percent}%`}
              <span className="md:hidden">{s.lessons > 0 ? ` · ${s.absent} absent, ${s.late} late` : ""}</span>
            </span>
          </li>
        ))}
      </RuledSheet>
      <p className="text-xs text-muted">Share is the lessons the student was present or late for, out of the lessons they were marked in.</p>
    </>
  );
}
