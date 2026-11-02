"use client";

import Link from "next/link";
import { AnnouncementIcon } from "@/components/notification-bell";
import { ApiErrorView, Card, PageHeader, PageLoader, SectionTitle, linkClass } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalOverviewDto } from "@/lib/types/portals";
import { ExamsIcon, HomeworkIcon, OverviewResults, PlayIcon, ResultsIcon, TestsIcon, TimetableIcon } from "../parent/_shared";
import { useStudentMe } from "./student-shell";

export default function StudentHomePage() {
  const me = useStudentMe();
  const { data, error, loading, reload } = useApi<PortalOverviewDto>("/api/student/overview");

  return (
    <div className="space-y-5">
      <PageHeader
        title={me?.name ?? "Overview"}
        subtitle={[me?.className, me?.admissionNo, me?.schoolName].filter(Boolean).join(" · ")}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {(loading || !data) && !error && <PageLoader />}
      {data && (
        <>
          <div className="grid gap-4 sm:grid-cols-2">
            <OverviewResults results={data.results} href="/student/results" />
            <Card>
              <SectionTitle action={<Link href="/student/timetable" className={linkClass}>Timetable</Link>}>
                This week
              </SectionTitle>
              {data.timetable && data.timetable.slots.length > 0 ? (
                <p className="text-sm text-ink">
                  {data.timetable.slots.length} lessons in {data.timetable.className}.
                </p>
              ) : (
                <p className="text-sm text-muted">No timetable yet for this class.</p>
              )}
            </Card>
          </div>
          <ul className="grid gap-2 sm:grid-cols-2">
            {[
              { href: "/student/results", label: "Results", icon: ResultsIcon },
              { href: "/student/timetable", label: "Timetable", icon: TimetableIcon },
              { href: "/student/exams", label: "Exams", icon: ExamsIcon },
              { href: "/student/assignments", label: "Homework", icon: HomeworkIcon },
              { href: "/student/cbt", label: "Tests", icon: TestsIcon },
              { href: "/student/play", label: "Play", icon: PlayIcon },
              { href: "/student/announcements", label: "Announcements", icon: AnnouncementIcon },
            ].map((item) => (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className="flex min-h-12 items-center gap-3 rounded-panel border border-rule bg-surface px-4 text-sm font-bold text-ink hover:bg-slate-50"
                >
                  <item.icon className="h-5 w-5 text-muted" />
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
