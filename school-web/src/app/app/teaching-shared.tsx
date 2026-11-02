"use client";

import { useState } from "react";
import { Select } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { AssessmentMode, ClassDto, ClassSubjectDto, LevelDto } from "@/lib/types";
import { ClassPicker, TermPicker, useAcademicClasses, useSessionTerms } from "./scores/academic-shared";

/**
 * Class, subject and term for assignments and lesson plans.
 * A teacher only gets subjects they teach. Leadership gets every subject of the class.
 * Listing a subject they do not teach is refused by the API.
 */
export function useSubjectTerm() {
  const academic = useAcademicClasses();
  const terms = useSessionTerms();
  const [classPick, setClassPick] = useState("");
  const [subjectPick, setSubjectPick] = useState("");
  const [termPick, setTermPick] = useState("");

  const classes = academic.leadership
    ? academic.classes
    : academic.classes.filter((c) => academic.subjects.some((s) => s.classId === c.id));
  const classId = classes.some((c) => c.id === classPick) ? classPick : "";
  const termId = termPick || terms.current?.id || "";

  const classSubjects = useApi<ClassSubjectDto[]>(academic.leadership && classId ? `/api/school/classes/${classId}/subjects` : null);
  const subjects = academic.leadership
    ? (classSubjects.data ?? []).map((s) => ({ id: s.id, name: s.subjectName }))
    : academic.subjects.filter((s) => s.classId === classId).map((s) => ({ id: s.classSubjectId, name: s.subjectName }));
  const classSubjectId = subjects.some((s) => s.id === subjectPick) ? subjectPick : (subjects[0]?.id ?? "");

  const allClasses = useApi<ClassDto[]>("/api/school/classes");
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const levelId = allClasses.data?.find((c) => c.id === classId)?.levelId ?? null;
  const mode: AssessmentMode | undefined = levels.data?.find((l) => l.id === levelId)?.assessmentMode;
  const levelLoaded = !!allClasses.data && !!levels.data;
  const lookupError = allClasses.error ?? levels.error;

  return {
    academic,
    terms,
    classes,
    classId,
    setClassId: (id: string) => {
      setClassPick(id);
      setSubjectPick("");
    },
    subjects,
    classSubjectId,
    setClassSubjectId: setSubjectPick,
    subjectsLoading: classSubjects.loading,
    subjectsError: classSubjects.error,
    reloadSubjects: classSubjects.reload,
    termId,
    setTermId: setTermPick,
    levelId,
    developmental: mode === "Developmental",
    levelLoaded,
    lookupError,
    reloadLookups: () => {
      allClasses.reload();
      levels.reload();
    },
  };
}

export function SubjectTermBar({
  state,
}: {
  state: Pick<
    ReturnType<typeof useSubjectTerm>,
    "classes" | "classId" | "setClassId" | "subjects" | "classSubjectId" | "setClassSubjectId" | "subjectsLoading" | "terms" | "termId" | "setTermId"
  >;
}) {
  return (
    <div className="flex w-full min-w-0 flex-wrap items-end gap-2">
      <ClassPicker classes={state.classes} value={state.classId} onChange={state.setClassId} className="w-full min-w-0 sm:w-44" />
      {state.classId && (
        <div className="w-full min-w-0 sm:w-52">
          <Select label="Subject" value={state.classSubjectId} onChange={(e) => state.setClassSubjectId(e.target.value)}>
            {state.subjects.length === 0 && <option value="">{state.subjectsLoading ? "Loading…" : "No subjects yet"}</option>}
            {state.subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
        </div>
      )}
      <div className="w-full min-w-0 sm:w-44">
        <TermPicker terms={state.terms.terms} value={state.termId} onChange={state.setTermId} />
      </div>
    </div>
  );
}
