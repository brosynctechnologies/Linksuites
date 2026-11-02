"use client";

import Link from "next/link";
import { formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { StudentCbtPaperDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, EmptyState, PageHeader, PageLoader, RuledSheet } from "@/components/ui";
import { score } from "../../parent/_shared";
import { windowState } from "./cbt-shared";

function action(paper: StudentCbtPaperDto): { label: string; tone: "neutral" | "blazer" | "pass" | "crest" } {
  const state = windowState(paper);
  if (paper.status === "Submitted") return { label: `Handed in${paper.totalMarks !== null ? `, ${score(paper.totalMarks)} marks` : ""}`, tone: "pass" };
  if (paper.status === "InProgress") return { label: "In progress", tone: "crest" };
  if (state === "upcoming") return { label: "Not open yet", tone: "neutral" };
  if (state === "closed") return { label: "Closed", tone: "neutral" };
  return { label: "Open now", tone: "blazer" };
}

export default function StudentTestsPage() {
  const list = useApi<StudentCbtPaperDto[]>("/api/student/cbt/papers");
  return (
    <div className="min-w-0 space-y-4">
      <PageHeader title="Tests" subtitle="Tests your teacher sets on the computer this term. Each one opens and closes at a set time." />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && <EmptyState title="No tests yet" text="When your teacher publishes a test for your class, it shows here." />}
      {list.data && list.data.length > 0 && (
        <RuledSheet label="Tests" busy={list.loading}>
          {list.data.map((paper) => {
            const a = action(paper);
            return (
              <li key={paper.id}>
                <Link href={`/student/cbt/${paper.id}`} className="ruled-pad flex min-w-0 flex-wrap items-center justify-between gap-2 py-3 hover:bg-slate-50">
                  <span className="min-w-0">
                    <span className="block font-bold break-words text-ink">{paper.title}</span>
                    <span className="block text-xs text-muted">
                      {paper.subject} · {paper.durationMinutes} min{paper.counted ? "" : " · Practice"}
                    </span>
                    <span className="tnum block text-xs text-muted">
                      {formatStamp(paper.openAt)} to {formatStamp(paper.closeAt)}
                    </span>
                  </span>
                  <Badge tone={a.tone}>{a.label}</Badge>
                </Link>
              </li>
            );
          })}
        </RuledSheet>
      )}
    </div>
  );
}
