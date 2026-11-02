"use client";

import { asList } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { StudioCourseSummaryDto } from "@/lib/types";
import { cx, inputClass } from "./styles";

/** "All courses" or one course, for studio lists. */
export function CourseFilter({ value, onChange, className, id = "course-filter" }: { value: string; onChange: (v: string) => void; className?: string; id?: string }) {
  const { data } = useApi<StudioCourseSummaryDto[]>("/api/studio/courses");
  const courses = asList(data);
  return (
    <div className={className}>
      <label htmlFor={id} className="sr-only">
        Course
      </label>
      <select id={id} className={cx(inputClass, "py-2")} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">All courses</option>
        {courses.map((c) => (
          <option key={c.id} value={c.id}>
            {c.title}
          </option>
        ))}
      </select>
    </div>
  );
}
