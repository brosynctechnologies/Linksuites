"use client";

import Link from "next/link";
import { AnnouncementIcon, MessageIcon } from "@/components/notification-bell";
import { ApiErrorView, Card, PageHeader, PageLoader, SectionTitle, buttonClass, linkClass } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalOverviewDto } from "@/lib/types/portals";
import {
  AttendanceIcon,
  ChildGate,
  FeesIcon,
  OverviewResults,
  ResultsIcon,
  ExamsIcon,
  HomeworkIcon,
  money,
  TimetableIcon,
  useChild,
  useChildPath,
} from "./_shared";

export default function ParentHomePage() {
  const { child } = useChild();
  return (
    <ChildGate>
      <Overview key={child?.studentId ?? "none"} />
    </ChildGate>
  );
}

function Overview() {
  const { child } = useChild();
  const path = useChildPath("/overview");
  const { data, error, loading, reload } = useApi<PortalOverviewDto>(path);
  if (!child) return null;

  return (
    <div className="space-y-5">
      <PageHeader
        title={child.name}
        subtitle={[child.className, !loading ? data?.child.admissionNo : undefined, child.schoolName].filter(Boolean).join(" · ")}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && !error && <PageLoader />}
      {!loading && data && (
        <>
          {data.fees && data.fees.balance > 0 && (
            <Card className="border-l-[3px] border-l-crest">
              <SectionTitle>Fees</SectionTitle>
              <p className="slab tnum text-2xl font-bold text-ink">{money(data.fees.balance)}</p>
              <p className="mt-1 text-sm text-muted">Outstanding across this child&apos;s invoices.</p>
              {/* "Pay now" only when an invoice can be paid online; otherwise the Fees page says to pay at the school office. */}
              {data.fees.invoices.some((i) => i.canPayOnline) ? (
                <Link href="/parent/fees" className={buttonClass("primary", "mt-3")}>
                  Pay now
                </Link>
              ) : (
                <Link href="/parent/fees" className={buttonClass("secondary", "mt-3")}>
                  See fees
                </Link>
              )}
            </Card>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <OverviewResults results={data.results} href="/parent/results" />
            <Card>
              <SectionTitle action={<Link href="/parent/attendance" className={linkClass}>Attendance</Link>}>
                Attendance
              </SectionTitle>
              {data.attendance ? (
                <p className="text-sm text-ink">
                  Present <span className="tnum font-bold">{data.attendance.present}</span> of{" "}
                  <span className="tnum">{data.attendance.open}</span> days in {data.attendance.className}, {data.attendance.termName}.
                </p>
              ) : (
                <p className="text-sm text-muted">No class this term, so there&apos;s no register yet.</p>
              )}
            </Card>
          </div>
          <ul className="grid gap-2 sm:grid-cols-2">
            {[
              { href: "/parent/results", label: "Results", icon: ResultsIcon },
              { href: "/parent/fees", label: "Fees", icon: FeesIcon },
              { href: "/parent/attendance", label: "Attendance", icon: AttendanceIcon },
              { href: "/parent/timetable", label: "Timetable", icon: TimetableIcon },
              { href: "/parent/exams", label: "Exams", icon: ExamsIcon },
              { href: "/parent/assignments", label: "Homework", icon: HomeworkIcon },
              { href: "/parent/announcements", label: "Announcements", icon: AnnouncementIcon },
              { href: "/parent/messages", label: "Messages", icon: MessageIcon },
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
