"use client";

import { useState } from "react";
import { PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalOverviewDto, PortalResultsDto } from "@/lib/types/portals";
import { ChildGate, PortalBlocked, TermResults, useChild, useChildPath } from "../_shared";

export default function ParentResultsPage() {
  const { child } = useChild();
  return (
    <ChildGate>
      <Results key={child?.studentId ?? "none"} />
    </ChildGate>
  );
}

function Results() {
  const { child } = useChild();
  const overview = useApi<PortalOverviewDto>(useChildPath("/overview"));
  const terms = overview.data?.results ?? [];
  const current = terms.find((t) => t.isCurrent) ?? terms[0];
  const [picked, setPicked] = useState("");
  const termId = picked || current?.termId || "";
  const term = terms.find((t) => t.termId === termId);
  const detailsPath = child && termId && term && !term.held && term.published ? `/api/parent/children/${child.studentId}/results/${termId}` : null;
  const details = useApi<PortalResultsDto>(detailsPath);
  if (!child) return null;

  return (
    <div>
      <PageHeader title="Results" subtitle={child ? `${child.name}${child.className ? ` · ${child.className}` : ""}` : undefined} />
      {overview.error && <PortalBlocked error={overview.error} onRetry={overview.reload} />}
      {overview.loading && <PageLoader />}
      {!overview.loading && overview.data && (
        <TermResults
          overview={overview.data}
          selectedId={termId}
          onSelect={setPicked}
          details={details.data}
          detailsError={details.error}
          detailsLoading={details.loading}
          onRetry={details.reload}
          pdfPath={(tid, classId) =>
            `/api/parent/children/${child.studentId}/report-card/${tid}.pdf${classId ? `?classId=${classId}` : ""}`
          }
        />
      )}
    </div>
  );
}
