"use client";

import { useAuth } from "@/lib/auth";
import { roleLabel } from "@/lib/roles";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { Card, PageHeader, SectionTitle } from "@/components/ui";

export default function AccountPage() {
  const auth = useAuth();
  const isOwner = auth?.tenant?.role === "Owner";
  return (
    <div className="space-y-5">
      <PageHeader title="Account" />
      <Card>
        <SectionTitle>You</SectionTitle>
        <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[auto_minmax(0,1fr)]">
          <dt className="text-muted">Name</dt>
          <dd className="font-bold text-ink">{auth?.user.fullName}</dd>
          <dt className="text-muted">Email</dt>
          <dd className="break-all text-ink">{auth?.user.email}</dd>
          <dt className="text-muted">School</dt>
          <dd className="text-ink">{auth?.tenant?.name}</dd>
          <dt className="text-muted">Role</dt>
          <dd className="text-ink">{roleLabel(auth?.tenant?.role)}</dd>
        </dl>
      </Card>
      <ChangePassword />
      {isOwner && <AccountActivity />}
    </div>
  );
}
