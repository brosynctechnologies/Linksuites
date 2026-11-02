"use client";

import { useCare } from "@/lib/care-me";
import { plural } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { CareSetupDto } from "@/lib/types";
import { ApiErrorView, NoAccess, Notice, PageHeader, PageLoader } from "@/components/ui";
import { SetupChecklist } from "./checklist";

/** The clinic's setup checklist (leadership). */
export default function SetupPage() {
  const { access } = useCare();
  const setup = useApi<CareSetupDto>(access.isLeadership ? "/api/care/setup" : null);
  if (!access.isLeadership) return <NoAccess />;

  const left = setup.data?.checklist.filter((i) => !i.done).length ?? 0;
  return (
    <div className="space-y-5">
      <PageHeader
        title="Setup"
        subtitle={setup.data ? (left === 0 ? "Everything is set up." : `${plural(left, "step")} left before the clinic is ready.`) : undefined}
      />
      {setup.error && <ApiErrorView error={setup.error} onRetry={setup.reload} />}
      {!setup.data && !setup.error && <PageLoader />}
      {setup.data && (
        <>
          <SetupChecklist items={setup.data.checklist} />
          {left === 0 && <Notice tone="success">Your clinic is set up. You can change any of it later from the menu.</Notice>}
        </>
      )}
    </div>
  );
}
