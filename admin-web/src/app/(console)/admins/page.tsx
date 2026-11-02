"use client";

import { useState, type FormEvent } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea, useAdmin } from "@/components/console";
import { PlusIcon, TrashIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Input, Loading, PageHeader, Panel, Select, TableScroll, inputClass, cx } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { day } from "@/lib/format";
import { ROLE_LABEL, ROLES } from "@/lib/roles";
import type { AdminRow, PlatformRole } from "@/lib/types";
import { useApi } from "@/lib/use-api";

const ROLE_HELP: Record<PlatformRole, string> = {
  SuperAdmin: "everything, including admins",
  Moderator: "moderation queues, locations and categories",
  Finance: "payments, payouts, plans and comps",
  Support: "businesses, users, view as and the audit log",
};

const LAST_SUPER = "The last super admin can't be demoted or removed. Make someone else a super admin first.";

export default function AdminsPage() {
  return (
    <RequireArea area="admins">
      <Admins />
    </RequireArea>
  );
}

function Admins() {
  const { user } = useAdmin();
  const toast = useToast();
  const { data, error, reload, setData } = useApi<AdminRow[]>("/api/platform/admins");
  const [roleChange, setRoleChange] = useState<{ admin: AdminRow; role: PlatformRole } | null>(null);
  const [removing, setRemoving] = useState<AdminRow | null>(null);
  const admins = data ?? [];
  const superCount = admins.filter((a) => a.role === "SuperAdmin").length;
  const isLastSuper = (a: AdminRow) => a.role === "SuperAdmin" && superCount <= 1;

  return (
    <>
      <PageHeader title="Admins" subtitle="People who can use this console, and what their role lets them do." />
      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,2fr)_minmax(300px,1fr)]">
        <Panel flush>
          {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
          {!data && !error && <Loading />}
          {data && admins.length === 0 && <EmptyState title="No admins" />}
          {data && admins.length > 0 && (
            <TableScroll label="Admins">
              <table className="data-table min-w-[720px]">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Role</th>
                    <th>Added</th>
                    <th className="w-px">
                      <span className="sr-only">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {admins.map((a) => {
                    const locked = isLastSuper(a);
                    const me = a.userId === user.id;
                    return (
                      <tr key={a.id}>
                        <td>
                          <span className="block font-medium">
                            {a.name}
                            {me && <span className="ml-1 text-xs font-normal text-muted">(you)</span>}
                          </span>
                          <span className="text-sm text-muted">{a.email}</span>
                          {locked && <span className="mt-1 block max-w-80 text-xs text-muted">{LAST_SUPER}</span>}
                        </td>
                        <td>
                          <label htmlFor={`role-${a.id}`} className="sr-only">
                            Role of {a.name}
                          </label>
                          <select
                            id={`role-${a.id}`}
                            value={a.role}
                            disabled={locked}
                            title={locked ? LAST_SUPER : undefined}
                            onChange={(e) => setRoleChange({ admin: a, role: e.target.value as PlatformRole })}
                            className={cx(inputClass, "w-40 pr-8")}
                          >
                            {ROLES.map((r) => (
                              <option key={r} value={r}>
                                {ROLE_LABEL[r]}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <span className="tnum block">{day(a.addedAt)}</span>
                          {a.addedByName && <span className="text-xs text-muted">by {a.addedByName}</span>}
                        </td>
                        <td>
                          <Button variant="secondary" disabled={locked} title={locked ? LAST_SUPER : undefined} onClick={() => setRemoving(a)} aria-label={`Remove ${a.name}`}>
                            <TrashIcon className="h-4 w-4" /> <span className="hidden sm:inline">Remove</span>
                          </Button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </TableScroll>
          )}
        </Panel>

        <div className="space-y-4">
          <AddAdmin
            onAdded={(a) => {
              setData((xs) => [...(xs ?? []), a]);
              reload();
            }}
          />
          <Panel title="Roles">
            <dl className="space-y-2 text-sm">
              {ROLES.map((r) => (
                <div key={r}>
                  <dt className="font-medium">{ROLE_LABEL[r]}</dt>
                  <dd className="text-muted">{ROLE_HELP[r]}</dd>
                </div>
              ))}
            </dl>
          </Panel>
        </div>
      </div>

      <ConfirmDialog
        open={!!roleChange}
        onClose={() => setRoleChange(null)}
        customer={false}
        title="Change role"
        effect={
          roleChange && (
            <>
              Makes {roleChange.admin.name} a {ROLE_LABEL[roleChange.role]} instead of a {ROLE_LABEL[roleChange.admin.role]}. They can then use {ROLE_HELP[roleChange.role]}, from their
              next request.
            </>
          )
        }
        confirmLabel="Change role"
        onConfirm={async () => {
          if (!roleChange) return;
          const updated = await api<AdminRow>(`/api/platform/admins/${roleChange.admin.id}`, { method: "PUT", body: { role: roleChange.role } });
          setData((xs) => (xs ?? []).map((x) => (x.id === roleChange.admin.id ? { ...x, ...(updated ?? {}), role: roleChange.role } : x)));
          setRoleChange(null);
          toast("Role changed");
        }}
      />
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        customer={false}
        title="Remove admin"
        effect={
          removing && (
            <>
              Removes {removing.name} ({removing.email}) from the console. Their next request is refused; their customer account stays.
              {removing.userId === user.id && " This is you: you lose access at once."}
            </>
          )
        }
        confirmLabel="Remove admin"
        danger
        onConfirm={async () => {
          if (!removing) return;
          await api(`/api/platform/admins/${removing.id}`, { method: "DELETE" });
          setData((xs) => (xs ?? []).filter((x) => x.id !== removing.id));
          setRemoving(null);
          toast("Admin removed");
        }}
      />
    </>
  );
}

function AddAdmin({ onAdded }: { onAdded: (a: AdminRow) => void }) {
  const toast = useToast();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<PlatformRole>("Support");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const added = await api<AdminRow>("/api/platform/admins", { method: "POST", body: { email: email.trim(), role } });
      onAdded(added);
      setEmail("");
      toast("Admin added");
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  // 404 (no account) and 409 (already an admin) are about the email address.
  const emailError = error?.field("email") ?? (error && (error.status === 404 || error.status === 409) ? error.message : undefined);
  const otherError = error && !emailError ? error : null;

  return (
    <Panel title="Add an admin">
      <form onSubmit={submit} className="space-y-3" noValidate>
        <Input
          label="Email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={emailError}
          hint="They need an account first (any product, or as a learner or seeker). They are emailed."
          required
        />
        <Select label="Role" value={role} onChange={(e) => setRole(e.target.value as PlatformRole)} error={error?.field("role")}>
          {ROLES.map((r) => (
            <option key={r} value={r}>
              {ROLE_LABEL[r]}
            </option>
          ))}
        </Select>
        {otherError && !otherError.field("role") && <ErrorPanel error={otherError} />}
        <Button type="submit" loading={busy} disabled={!email.trim()} className="w-full">
          <PlusIcon className="h-4 w-4" /> Add admin
        </Button>
      </form>
    </Panel>
  );
}
