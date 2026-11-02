"use client";

import { NotificationFeed } from "@/components/notification-bell";
import { PageHeader } from "@/components/ui";

export default function NotificationsPage() {
  return (
    <div>
      <PageHeader title="Notifications" subtitle="Alerts for you at this clinic, newest first." />
      <NotificationFeed />
    </div>
  );
}
