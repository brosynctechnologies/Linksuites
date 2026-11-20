"use client";

import { SubNav } from "@/components/sub-nav";

/** Daily registers, lesson registers and the subject reports. The lesson pages explain themselves when the school has them off. */
export function AttendanceNav() {
  return (
    <SubNav
      label="Attendance"
      items={[
        { href: "/app/attendance", label: "Daily registers", exact: true },
        { href: "/app/attendance/lessons", label: "Lessons" },
        { href: "/app/attendance/subjects", label: "By subject" },
      ]}
    />
  );
}
