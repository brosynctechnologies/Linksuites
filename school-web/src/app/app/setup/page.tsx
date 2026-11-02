"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ApplyTemplateResult, ClassDto, LevelDto, SubjectDto } from "@/lib/types";
import { ApiErrorView, Button, Card, Checkbox, ErrorBox, Notice, SectionTitle, linkClass } from "@/components/ui";

/** The setup overview: the Nigerian template, and where each part of the setup lives. */
export default function SetupOverviewPage() {
  const canManage = useCan("school.manage");
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const subjects = useApi<SubjectDto[]>("/api/school/subjects");
  const classes = useApi<ClassDto[]>("/api/school/classes");
  const [useLevels, setUseLevels] = useState(true);
  const [useSubjects, setUseSubjects] = useState(true);
  const [result, setResult] = useState<ApplyTemplateResult | null>(null);
  const { busy, error, run } = useAction();

  async function apply() {
    const res = await run(() => api<ApplyTemplateResult>("/api/school/setup/apply-template", { method: "POST", body: { levels: useLevels, subjects: useSubjects } }));
    if (res.ok) {
      setResult(res.data);
      levels.reload();
      subjects.reload();
    }
  }

  const loadError = levels.error ?? subjects.error ?? classes.error;
  const rows = [
    { href: "/app/setup/levels", title: "Levels", text: levels.data ? plural(levels.data.length, "level") : "…" },
    { href: "/app/setup/classes", title: "Classes", text: classes.data ? plural(classes.data.length, "class", "classes") + " this session" : "…" },
    { href: "/app/setup/subjects", title: "Subjects", text: subjects.data ? plural(subjects.data.length, "subject") : "…" },
    { href: "/app/setup/assessment", title: "Assessment", text: "How each term's score is made up (CA1, CA2, exam)" },
    { href: "/app/setup/grading", title: "Grading", text: "Grade bands from A1 to F9" },
  ];

  return (
    <div className="space-y-5">
      {loadError && <ApiErrorView error={loadError} 
          onRetry={() => {
            levels.reload();
            subjects.reload();
            classes.reload();
          }}
        />}
      {canManage && (
        <Card>
          <SectionTitle>Start from the Nigerian template</SectionTitle>
          <p className="-mt-1 mb-4 max-w-prose text-sm text-slate-700">
            Adds Creche, Nursery 1 and 2, Primary 1 to 6, JSS 1 to 3 and SSS 1 to 3 (with student logins for Primary, JSS and SSS), and the usual subjects.
            Anything you already have is kept. You can rename or delete any of it afterwards.
          </p>
          <div className="mb-4 space-y-2">
            <Checkbox checked={useLevels} onChange={setUseLevels} label="Levels, Creche to SSS 3" />
            <Checkbox checked={useSubjects} onChange={setUseSubjects} label="Subjects" />
          </div>
          <ErrorBox error={error} />
          {result && (
            <Notice tone="success" className="mb-3">
              Added {plural(result.levelsAdded, "level")} and {plural(result.subjectsAdded, "subject")}.{" "}
              {result.levelsAdded > 0 && (
                <Link href="/app/setup/classes" className={linkClass}>
                  Now add your classes
                </Link>
              )}
            </Notice>
          )}
          <Button onClick={apply} loading={busy} disabled={!useLevels && !useSubjects} variant={levels.data?.length ? "secondary" : "primary"}>
            Apply the template
          </Button>
        </Card>
      )}
      <ul className="ruled-sheet ruled-rows">
        {rows.map((r) => (
          <li key={r.href}>
            <Link href={r.href} className="ruled-pad flex min-h-14 items-center justify-between gap-4 py-3 hover:bg-slate-50">
              <span>
                <span className="block font-bold text-ink">{r.title}</span>
                <span className="block text-sm text-muted">{r.text}</span>
              </span>
              <span aria-hidden="true" className="text-faint">
                ›
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
