"use client";

import { useAuth } from "@/lib/auth";
import { ChangePassword } from "@/components/change-password";
import { Card, PageHeader, SectionTitle } from "@/components/ui";

/** The learner's own account: who they're signed in as, and their password. The /learn layout guards the session. */
export default function LearnerAccountPage() {
  const auth = useAuth();
  if (!auth) return null;

  return (
    <div>
      <PageHeader title="Account" back="/learn" />
      <div className="space-y-6">
        <Card>
          <SectionTitle>Signed in as</SectionTitle>
          <dl className="space-y-3 text-sm">
            <div>
              <dt className="text-muted">Name</dt>
              <dd className="font-medium text-ink">{auth.user.fullName}</dd>
            </div>
            <div>
              <dt className="text-muted">Email</dt>
              <dd className="font-medium break-all text-ink">{auth.user.email}</dd>
            </div>
          </dl>
        </Card>
        <ChangePassword />
      </div>
    </div>
  );
}
