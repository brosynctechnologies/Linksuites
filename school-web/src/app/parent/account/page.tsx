"use client";

import { ChangePassword } from "@/components/change-password";
import { Card, PageHeader, SectionTitle } from "@/components/ui";
import { useAuth } from "@/lib/auth";
import { useChild } from "../_shared";

export default function ParentAccountPage() {
  const auth = useAuth();
  const { list, child } = useChild();
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
        </dl>
      </Card>
      <Card>
        <SectionTitle>Children</SectionTitle>
        {list.length === 0 ? (
          <p className="text-sm text-muted">No children are linked to this account. Ask the school office to send you an invite.</p>
        ) : (
          <ul className="space-y-2 text-sm">
            {list.map((c) => (
              <li key={c.studentId} className={c.studentId === child?.studentId ? "font-bold text-ink" : "text-ink"}>
                {c.name}
                {c.className ? ` · ${c.className}` : ""}
                <span className="block text-xs font-medium text-muted">{c.schoolName}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
      <ChangePassword />
    </div>
  );
}
