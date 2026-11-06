"use client";

import { PortalNotificationFeed } from "@/components/portal-bell";
import { PageHeader } from "@/components/ui";
import { usePortal } from "@/lib/portal";

/** The chosen patient's notices at their clinic (the per-patient feed, never the shared one). */
export default function PortalNotificationsPage() {
  const { patient } = usePortal();
  return (
    <div>
      <PageHeader title="Notifications" subtitle={patient ? `For ${patient.firstName} at ${patient.clinicName}.` : undefined} />
      <PortalNotificationFeed />
    </div>
  );
}
