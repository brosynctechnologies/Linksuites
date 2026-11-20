"use client";

import { NotificationFeed } from "@/components/notification-bell";
import { PushNotifications } from "@/components/pwa";
import { PageHeader } from "@/components/ui";

export default function StudentNotificationsPage() {
  return (
    <div>
      <PageHeader title="Notifications" subtitle="Results and announcements for you, newest first." />
      <NotificationFeed />
      <div className="mt-5">
        <PushNotifications />
      </div>
    </div>
  );
}
