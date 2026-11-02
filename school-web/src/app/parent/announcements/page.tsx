"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalAnnouncementDto } from "@/lib/types/communication";
import { AnnouncementList, ChildGate, useChildPath } from "../_shared";

export default function ParentAnnouncementsPage() {
  return (
    <ChildGate>
      <Announcements />
    </ChildGate>
  );
}

function Announcements() {
  const { data, error, loading, reload } = useApi<PortalAnnouncementDto[]>(useChildPath("/announcements"));
  return (
    <div>
      <PageHeader title="Announcements" subtitle="News the school posted for this child." />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && data && <AnnouncementList items={data} />}
    </div>
  );
}
