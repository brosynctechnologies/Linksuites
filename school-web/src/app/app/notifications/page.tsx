"use client";

import { NotificationFeed } from "@/components/notification-bell";
import { PageHeader } from "@/components/ui";

export default function NotificationsPage() {
  return (
    <div>
      <PageHeader title="Notifications" subtitle="Results, fees, messages and announcements for you, newest first." />
      <NotificationFeed />
    </div>
  );
}
