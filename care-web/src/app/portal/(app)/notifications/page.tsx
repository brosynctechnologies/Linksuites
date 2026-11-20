"use client";

import { PortalNotificationFeed } from "@/components/portal-bell";
import { PushNotifications } from "@/components/pwa";
import { PageHeader } from "@/components/ui";
import { usePortal } from "@/lib/portal";

/** The chosen patient's notices at their clinic (the per-patient feed, never the shared one). */
export default function PortalNotificationsPage() {
  const { patient } = usePortal();
  return (
    <div>
      <PageHeader title="Notifications" subtitle={patient ? `For ${patient.firstName} at ${patient.clinicName}.` : undefined} />
      <PortalNotificationFeed />
      <div className="mt-5">
        <PushNotifications description="Get a notification on this phone when the clinic has something for you. It only says to open CareLink, so no health details show on your lock screen." />
      </div>
    </div>
  );
}
