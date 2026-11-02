"use client";

import Link from "next/link";
import { firstName, useAuth } from "@/lib/auth";
import { formatCount, formatDay } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { StudioCourseSummaryDto, StudioDashboardDto } from "@/lib/types";
import { CopyButton } from "@/components/copy-button";
import { BookIcon, PlusIcon } from "@/components/icons";
import { ApiErrorView, Badge, Card, EmptyState, PageHeader, PageLoader, SectionTitle, SummaryStrip, buttonClass } from "@/components/ui";

export default function StudioDashboard() {
  const auth = useAuth();
  const canMoney = useCan("earnings.view");
  const canCreate = useCan("courses.edit");
  const { data, error, reload } = useApi<StudioDashboardDto>("/api/studio/dashboard");
  const courses = useApi<StudioCourseSummaryDto[]>("/api/studio/courses");
  const academyUrl = typeof window !== "undefined" && auth?.tenant ? `${window.location.origin}/a/${auth.tenant.slug}` : "";

  return (
    <div className="space-y-6">
      <PageHeader title={`Hello, ${firstName(auth?.user.fullName)}`} subtitle={auth?.tenant?.name} />

      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}

      {data && (
        <>
          <SummaryStrip
            label="This month"
            cols={4}
            items={[
              { label: "Students", value: formatCount(data.studentsTotal), sub: `${formatCount(data.enrolmentsThisMonth)} new this month`, href: "/studio/students" },
              ...(canMoney
                ? [
                    { label: "Revenue this month", value: formatMoney(data.revenueThisMonthMinor, data.currency), sub: "Your share, after fees", href: "/studio/earnings" },
                    {
                      label: "Available to pay out",
                      value: formatMoney(data.availableMinor, data.currency),
                      sub: `${formatMoney(data.pendingMinor, data.currency)} pending`,
                      tone: "green" as const,
                      href: "/studio/earnings",
                    },
                  ]
                : []),
              { label: "Average rating", value: data.ratingAverage ? `${data.ratingAverage.toFixed(1)} of 5` : "No ratings", href: "/studio/reviews" },
            ]}
          />

          <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
            <Card>
              <SectionTitle>Recent enrolments</SectionTitle>
              {data.recentEnrolments.length === 0 ? (
                <p className="text-sm text-muted">No enrolments yet. Share your academy link to bring in your first learners.</p>
              ) : (
                <ul className="divide-y divide-line-soft">
                  {data.recentEnrolments.map((e, i) => (
                    <li key={`${e.enrolledAt}-${i}`} className="flex items-center justify-between gap-3 py-3">
                      <div className="min-w-0">
                        <p className="truncate font-medium text-ink">{e.studentName}</p>
                        <p className="truncate text-sm text-muted">{e.courseTitle}</p>
                      </div>
                      <div className="shrink-0 text-right">
                        <p className="tnum text-sm font-semibold text-ink">{e.amountMinor > 0 ? formatMoney(e.amountMinor, data.currency) : "Free"}</p>
                        <p className="flex items-center justify-end gap-1.5 text-xs text-muted">
                          {e.channel && (
                            <Badge className={e.channel === "Academy" ? "bg-lagoon-wash text-lagoon-text" : "bg-mist-2 text-slate-700"}>{e.channel}</Badge>
                          )}
                          {formatDay(e.enrolledAt)}
                        </p>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Card>

            <Card>
              <SectionTitle>Your academy link</SectionTitle>
              <p className="text-sm text-muted">Put it in your bio, WhatsApp status or Telegram channel. Sales through it carry the academy fee, not the marketplace fee.</p>
              {academyUrl && <p className="mt-3 rounded-control bg-mist px-3 py-2 font-mono text-sm break-all text-ink">{academyUrl}</p>}
              <div className="mt-3 flex flex-wrap gap-2">
                {academyUrl && <CopyButton text={academyUrl} label="Copy link" />}
                <a href={academyUrl || "#"} target="_blank" rel="noopener noreferrer" className={buttonClass("ghost")}>
                  Open
                </a>
              </div>
            </Card>
          </div>
        </>
      )}

      {courses.data && courses.data.length === 0 && canCreate && (
        <EmptyState
          icon={<BookIcon />}
          title="Build your first course"
          text="Add a title, then sections and lessons: videos, readings, downloads and quizzes. Publish when one lesson is ready."
          action={
            <Link href="/studio/courses/new" className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> New course
            </Link>
          }
        />
      )}
    </div>
  );
}
