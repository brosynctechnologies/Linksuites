"use client";

import { NotificationFeed } from "@/components/notification-bell";
import { PageHeader } from "@/components/ui";

export default function StudentNotificationsPage() {
  return (
    <div>
      <PageHeader title="Notifications" subtitle="Results and announcements for you, newest first." />
      <NotificationFeed />
    </div>
  );
}
