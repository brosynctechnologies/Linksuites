"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalAnnouncementDto } from "@/lib/types/communication";
import { AnnouncementList } from "../../parent/_shared";

export default function StudentAnnouncementsPage() {
  const { data, error, loading, reload } = useApi<PortalAnnouncementDto[]>("/api/student/announcements");
  return (
    <div>
      <PageHeader title="Announcements" subtitle="News the school posted for you." />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && !data && <PageLoader />}
      {data && <AnnouncementList items={data} />}
    </div>
  );
}
