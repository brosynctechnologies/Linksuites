"use client";

import { useAuth } from "@/lib/auth";
import { useCare } from "@/lib/care-me";
import { careRoleLabel } from "@/lib/roles";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { Card, PageHeader, SectionTitle } from "@/components/ui";

export default function AccountPage() {
  const auth = useAuth();
  const { me, access } = useCare();
  return (
    <div className="space-y-5">
      <PageHeader title="Account" />
      <Card>
        <SectionTitle>You</SectionTitle>
        <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[auto_minmax(0,1fr)]">
          <dt className="text-muted">Name</dt>
          <dd className="font-bold text-ink">{me.fullName}</dd>
          <dt className="text-muted">Email</dt>
          <dd className="break-all text-ink">{auth?.user.email}</dd>
          <dt className="text-muted">Clinic</dt>
          <dd className="text-ink">{auth?.tenant?.name}</dd>
          <dt className="text-muted">Role</dt>
          <dd className="text-ink">{careRoleLabel(me.memberRole, me.clinicalRole)}</dd>
          {me.title && (
            <>
              <dt className="text-muted">Title</dt>
              <dd className="text-ink">{me.title}</dd>
            </>
          )}
          {me.licenceNumber && (
            <>
              <dt className="text-muted">Licence number</dt>
              <dd className="tnum text-ink">{me.licenceNumber}</dd>
            </>
          )}
        </dl>
      </Card>
      {!access.readOnly && <ChangePassword />}
      {access.isDirector && <AccountActivity />}
    </div>
  );
}
