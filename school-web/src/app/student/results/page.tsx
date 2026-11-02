"use client";

import { useState } from "react";
import { PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalOverviewDto, PortalResultsDto } from "@/lib/types/portals";
import { PortalBlocked, TermResults } from "../../parent/_shared";
import { useStudentMe } from "../student-shell";

export default function StudentResultsPage() {
  const me = useStudentMe();
  const overview = useApi<PortalOverviewDto>("/api/student/overview");
  const terms = overview.data?.results ?? [];
  const current = terms.find((t) => t.isCurrent) ?? terms[0];
  const [picked, setPicked] = useState("");
  const termId = picked || current?.termId || "";
  const term = terms.find((t) => t.termId === termId);
  const detailsPath = termId && term && !term.held && term.published ? `/api/student/results/${termId}` : null;
  const details = useApi<PortalResultsDto>(detailsPath);

  return (
    <div>
      <PageHeader title="Results" subtitle={me ? `${me.name}${me.className ? ` · ${me.className}` : ""}` : undefined} />
      {overview.error && <PortalBlocked error={overview.error} onRetry={overview.reload} />}
      {!overview.data && !overview.error && <PageLoader />}
      {overview.data && (
        <TermResults
          overview={overview.data}
          selectedId={termId}
          onSelect={setPicked}
          details={details.data}
          detailsError={details.error}
          detailsLoading={details.loading}
          onRetry={details.reload}
          pdfPath={(tid, classId) => `/api/student/report-card/${tid}.pdf${classId ? `?classId=${classId}` : ""}`}
        />
      )}
    </div>
  );
}
