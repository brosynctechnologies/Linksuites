"use client";

import { useAuth } from "@/lib/auth";
import { ChangePassword } from "@/components/change-password";
import { Card, PageHeader, SectionTitle } from "@/components/ui";

/** The seeker's own account: who they're signed in as, and their password. The (seeker) layout guards the session. */
export default function SeekerAccountPage() {
  const auth = useAuth();
  if (!auth) return null;

  return (
    <div>
      <PageHeader title="Account" />
      <div className="max-w-2xl space-y-6">
        <Card>
          <SectionTitle>Signed in as</SectionTitle>
          <dl className="space-y-3 text-sm">
            <div>
              <dt className="text-muted">Name</dt>
              <dd className="font-medium text-asphalt">{auth.user.fullName}</dd>
            </div>
            <div>
              <dt className="text-muted">Email</dt>
              <dd className="font-medium break-all text-asphalt">{auth.user.email}</dd>
            </div>
          </dl>
        </Card>
        <ChangePassword />
      </div>
    </div>
  );
}
