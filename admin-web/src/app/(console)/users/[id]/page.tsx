"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea, useAdmin } from "@/components/console";
import { ChevronLeftIcon, KeyIcon, LogoutIcon, MailIcon, UnlockIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Facts, Loading, Mono, PageHeader, Panel, ProductChip, StatusBadge, TableScroll } from "@/components/ui";
import { api } from "@/lib/api";
import { num, stamp } from "@/lib/format";
import { can, ROLE_LABEL } from "@/lib/roles";
import type { UserDetailDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";

type Action = "unlock" | "code" | "reset" | "signout" | null;

export default function UserPage() {
  return (
    <RequireArea area="users">
      <User />
    </RequireArea>
  );
}

function User() {
  const { id } = useParams<{ id: string }>();
  const { user: me } = useAdmin();
  const toast = useToast();
  const { data: u, error, reload } = useApi<UserDetailDto>(`/api/platform/users/${id}`);
  const [action, setAction] = useState<Action>(null);
  const linkBusinesses = can(me.platformRole, "businesses");
  const canHelp = can(me.platformRole, "users");

  if (!u) {
    return (
      <>
        <BackLink />
        {error ? <ErrorPanel error={error} onRetry={reload} /> : <Loading />}
      </>
    );
  }

  const name = u.name || u.email;

  return (
    <>
      <BackLink />
      <PageHeader
        title={name}
        action={
          <>
            <Button variant="secondary" onClick={() => setAction("unlock")} disabled={!u.lockedOut} title={u.lockedOut ? undefined : "The account is not locked"}>
              <UnlockIcon className="h-4 w-4" /> Unlock
            </Button>
            <Button variant="secondary" onClick={() => setAction("code")}>
              <MailIcon className="h-4 w-4" /> Send sign-in code
            </Button>
            {canHelp && (
              <Button variant="secondary" onClick={() => setAction("reset")}>
                <KeyIcon className="h-4 w-4" /> Send password reset
              </Button>
            )}
            <Button variant="danger" onClick={() => setAction("signout")}>
              <LogoutIcon className="h-4 w-4" /> Sign out everywhere
            </Button>
          </>
        }
      >
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
          <span>{u.email}</span>
          {u.lockedOut ? <StatusBadge tone="stop">Locked{u.lockoutEnd ? ` until ${stamp(u.lockoutEnd)}` : ""}</StatusBadge> : <StatusBadge tone="ok">Not locked</StatusBadge>}
          {!u.emailConfirmed && <StatusBadge tone="wait">Email not confirmed</StatusBadge>}
          {u.adminRole && <StatusBadge tone="neutral">Admin: {ROLE_LABEL[u.adminRole]}</StatusBadge>}
        </div>
      </PageHeader>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <Panel title="Memberships" flush>
          {u.memberships.length === 0 ? (
            <EmptyState title="No businesses" text="This person is a customer only (learner, seeker or guest)." />
          ) : (
            <TableScroll label="Memberships">
              <table className="data-table min-w-[600px]">
                <thead>
                  <tr>
                    <th>Business</th>
                    <th>Product</th>
                    <th>Role</th>
                    <th>Member status</th>
                    <th>Business status</th>
                  </tr>
                </thead>
                <tbody>
                  {u.memberships.map((m) => (
                    <tr key={m.tenantId}>
                      <td>
                        {linkBusinesses ? (
                          <Link href={`/businesses/${m.tenantId}`} className="font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                            {m.name}
                          </Link>
                        ) : (
                          <span className="font-medium">{m.name}</span>
                        )}
                      </td>
                      <td>
                        <ProductChip product={m.product} />
                      </td>
                      <td>{m.role}</td>
                      <td>
                        <StatusBadge status={m.status} />
                      </td>
                      <td>
                        <StatusBadge status={m.businessStatus} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableScroll>
          )}
        </Panel>
        <div className="space-y-4">
          <Panel title="Account">
            <Facts
              items={[
                { label: "Phone", value: u.phone ?? "—" },
                { label: "Joined", value: stamp(u.createdAt) },
                { label: "Last sign-in", value: stamp(u.lastLoginAt) },
                { label: "User id", value: <Mono>{u.id}</Mono> },
              ]}
            />
          </Panel>
          <Panel title="Activity">
            <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3 lg:grid-cols-2">
              {(
                [
                  ["Courses", u.activity.courses],
                  ["Stays", u.activity.stays],
                  ["Enquiries", u.activity.enquiries],
                  ["Viewings", u.activity.viewings],
                  ["Saved searches", u.activity.savedSearches],
                ] as const
              ).map(([label, n]) => (
                <div key={label}>
                  <dt className="text-muted">{label}</dt>
                  <dd className="tnum text-lg font-semibold">{num(n)}</dd>
                </div>
              ))}
            </dl>
          </Panel>
        </div>
      </div>

      <ConfirmDialog
        open={action === "unlock"}
        onClose={() => setAction(null)}
        title={`Unlock ${name}`}
        effect={<>Lifts the sign-in lockout on {u.email} so they can try their password again now.</>}
        confirmLabel="Unlock account"
        onConfirm={async () => {
          await api(`/api/platform/users/${u.id}/unlock`, { method: "POST" });
          setAction(null);
          toast("Account unlocked");
          reload();
        }}
      />
      <ConfirmDialog
        open={action === "code"}
        onClose={() => setAction(null)}
        title={`Send a sign-in code to ${name}`}
        effect={<>Emails a one-time sign-in code to {u.email}. They use the code to get in without their password.</>}
        confirmLabel="Send sign-in code"
        onConfirm={async () => {
          await api(`/api/platform/users/${u.id}/send-login-code`, { method: "POST" });
          setAction(null);
          toast("Sign-in code sent");
        }}
      />
      <ConfirmDialog
        open={action === "reset"}
        onClose={() => setAction(null)}
        title={`Send a password reset to ${name}`}
        effect={
          <>
            Emails {u.email} a link to choose a new password, opening in the product they joined first. Their current password works until they use
            the link.
          </>
        }
        confirmLabel="Send password reset"
        onConfirm={async () => {
          await api(`/api/platform/users/${u.id}/send-password-reset`, { method: "POST" });
          setAction(null);
          toast("Password reset sent");
        }}
      />
      <ConfirmDialog
        open={action === "signout"}
        onClose={() => setAction(null)}
        title={`Sign ${name} out everywhere`}
        effect={<>Revokes every refresh token for {u.email}: each device signs out when its current access token runs out.</>}
        confirmLabel="Sign out everywhere"
        danger
        onConfirm={async () => {
          await api(`/api/platform/users/${u.id}/sign-out-everywhere`, { method: "POST" });
          setAction(null);
          toast("Signed out everywhere");
        }}
      />
    </>
  );
}

function BackLink() {
  return (
    <Link href="/users" className="mb-2 inline-flex min-h-10 items-center gap-1 text-sm text-muted hover:text-graphite">
      <ChevronLeftIcon className="h-4 w-4" /> Users
    </Link>
  );
}
