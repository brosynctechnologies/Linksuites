"use client";

import Link from "next/link";
import { firstName, useAuth } from "@/lib/auth";
import { formatDay } from "@/lib/format";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ChecklistItemDto, SchoolSetupDto } from "@/lib/types";
import { CheckIcon } from "@/components/icons";
import { ApiErrorView, Badge, Card, PageHeader, PageLoader, SectionTitle, buttonClass, cx, linkClass } from "@/components/ui";

const LINKS: Record<string, { href: string; text: string; action: string }> = {
  "levels-and-classes": { href: "/app/setup/classes", text: "Start from the Nigerian template, then add the arms you run (JSS 1A, JSS 1B).", action: "Set up classes" },
  subjects: { href: "/app/setup/subjects", text: "The template lists the usual subjects. Keep the ones you teach.", action: "Add subjects" },
  students: { href: "/app/students/import", text: "Import them from Excel or CSV, or add them one at a time.", action: "Add students" },
  staff: { href: "/app/staff", text: "Invite your principal, bursar and teachers by email.", action: "Invite staff" },
};

/** The setup checklist, drawn as a register column. The first undone item carries the page's one primary button. */
function Checklist({ items }: { items: ChecklistItemDto[] }) {
  const next = items.find((i) => !i.done);
  const done = items.filter((i) => i.done).length;
  return (
    <section aria-labelledby="checklist">
      <div className="mb-3 flex items-baseline justify-between gap-3">
        <h2 id="checklist" className="slab text-xl font-bold text-ink">
          Set up your school
        </h2>
        <span className="tnum text-sm text-muted">
          {done} of {items.length} done
        </span>
      </div>
      <ol className="ruled-sheet ruled-rows">
        {items.map((item) => {
          const link = LINKS[item.key];
          const isNext = item === next;
          return (
            <li key={item.key} className="ruled-pad flex items-start gap-3 py-3.5">
              <span
                aria-hidden="true"
                className={cx(
                  "mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-[4px] border-2",
                  item.done ? "border-pass bg-pass text-white" : "border-slate-300 bg-surface",
                )}
              >
                {item.done && <CheckIcon className="h-4 w-4" strokeWidth={3} />}
              </span>
              <div className="min-w-0 flex-1">
                <p className={cx("font-bold", item.done ? "text-muted" : "text-ink")}>
                  {item.label}
                  <span className="sr-only">{item.done ? ", done" : ", to do"}</span>
                </p>
                {!item.done && link && <p className="mt-0.5 text-sm text-slate-600">{link.text}</p>}
                {isNext && link && (
                  <Link href={link.href} className={buttonClass("primary", "mt-3")}>
                    {link.action}
                  </Link>
                )}
              </div>
              {!isNext && link && (
                <Link href={link.href} className={cx(linkClass, "inline-flex min-h-11 items-center text-sm")}>
                  {item.done ? "Edit" : "Open"}
                </Link>
              )}
            </li>
          );
        })}
      </ol>
    </section>
  );
}

export default function DashboardPage() {
  const auth = useAuth();
  const canManage = useCan("school.manage");
  const { data, error, reload } = useApi<SchoolSetupDto>("/api/school/setup");

  const session = data?.sessions.find((s) => s.isCurrent) ?? data?.sessions[0];
  const term = session?.terms.find((t) => t.isCurrent);
  const allDone = data?.checklist.every((i) => i.done);

  return (
    <div className="space-y-6">
      <PageHeader title={`Good day, ${firstName(auth?.user.fullName)}`} subtitle={data?.school.name ?? auth?.tenant?.name} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && canManage && !allDone && <Checklist items={data.checklist} />}
      {data && (
        <Card>
          <SectionTitle>{session ? `${session.name} session` : "Session"}</SectionTitle>
          {session ? (
            <ul className="space-y-2 text-sm">
              {session.terms.map((t) => (
                <li key={t.id} className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-0.5">
                  <span className="flex items-center gap-2 font-bold text-ink">
                    {t.name} {t.isCurrent && <Badge tone="blazer">Current</Badge>}
                  </span>
                  <span className="tnum text-muted">
                    {formatDay(t.startDate)} to {formatDay(t.endDate)}
                  </span>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-muted">No session yet.</p>
          )}
          {term?.nextTermStarts && <p className="mt-3 text-sm text-muted">Next term starts {formatDay(term.nextTermStarts)}.</p>}
        </Card>
      )}
      {data && (
        <Card>
          <SectionTitle>Your school&apos;s sign-in page</SectionTitle>
          <p className="text-sm text-slate-700">Parents, students and staff sign in here. Share it with parents along with their invitation.</p>
          <Link href={`/${data.school.slug}`} className={cx(linkClass, "mt-2 inline-flex min-h-11 items-center break-all")}>
            {typeof window === "undefined" ? `/${data.school.slug}` : `${window.location.host}/${data.school.slug}`}
          </Link>
        </Card>
      )}
    </div>
  );
}
