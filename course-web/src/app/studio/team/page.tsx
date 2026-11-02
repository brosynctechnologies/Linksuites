"use client";

import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { ROLE_DESCRIPTIONS, useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { InviteDto, InviteRole, MemberDto, TeamDto } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  StatusBadge,
  UpgradePrompt,
  UsageMeter,
  cx,
  inputClass,
  type Tone,
} from "@/components/ui";
import { PlusIcon, TrashIcon } from "@/components/icons";

const INVITE_ROLES: InviteRole[] = ["Admin", "Staff", "Accountant"];

const roleTone: Record<string, Tone> = {
  Owner: "lagoon",
  Admin: "lagoon",
  Staff: "neutral",
  Accountant: "lime",
};

export default function TeamPage() {
  const canManage = useCan("team.manage");
  const { data, error, reload, setData } = useApi<TeamDto>(canManage ? "/api/team" : null);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

  if (!canManage) {
    return (
      <div>
        <PageHeader title="Team" back="/studio/more" />
        <NoAccess text="Only the owner and admins can manage the team." />
      </div>
    );
  }

  async function act(key: string, fn: () => Promise<void>) {
    setBusy(key);
    setActionError(null);
    setBanner(null);
    try {
      await fn();
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const changeRole = (m: MemberDto, role: InviteRole) =>
    act(`role-${m.id}`, async () => {
      const updated = await api<MemberDto>(`/api/team/members/${m.id}`, { method: "PATCH", body: { role } });
      setData((t) => (t ? { ...t, members: t.members.map((x) => (x.id === updated.id ? updated : x)) } : t!));
      setBanner(`${m.fullName || m.email} is now ${updated.role}.`);
    });

  const removeMember = (m: MemberDto) => {
    if (!window.confirm(`Remove ${m.fullName || m.email} from this academy? They lose access straight away.`)) return;
    act(`rm-${m.id}`, async () => {
      await api(`/api/team/members/${m.id}`, { method: "DELETE" });
      reload();
    });
  };

  const resend = (inv: InviteDto) =>
    act(`resend-${inv.id}`, async () => {
      const updated = await api<InviteDto>(`/api/team/invites/${inv.id}/resend`, { method: "POST" });
      setData((t) => (t ? { ...t, invites: t.invites.map((x) => (x.id === updated.id ? updated : x)) } : t!));
      setBanner(`Invite re-sent to ${inv.email}.`);
    });

  const cancelInvite = (inv: InviteDto) => {
    if (!window.confirm(`Cancel the invite for ${inv.email}?`)) return;
    act(`cancel-${inv.id}`, async () => {
      await api(`/api/team/invites/${inv.id}`, { method: "DELETE" });
      reload();
    });
  };

  if (!data) {
    return (
      <div>
        <PageHeader title="Team" back="/studio/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const { members, invites, seats } = data;
  const full = seats.limit !== -1 && seats.used >= seats.limit;

  return (
    <div className="space-y-4">
      <PageHeader
        title="Team"
        back="/studio/more"
        subtitle="Give co-tutors, an assistant and your accountant their own login."
        action={
          <Button onClick={() => setInviteOpen(true)}>
            <PlusIcon className="h-4 w-4" /> Invite
          </Button>
        }
      />

      <Card>
        <UsageMeter label="Seats used" used={seats.used} limit={seats.limit} />
        <p className="mt-2 text-xs text-slate-500">Members and pending invites both use a seat.</p>
        {full && (
          <div className="mt-3">
            <UpgradePrompt
             
              message={
                seats.limit <= 1
                  ? "Team seats come with paid plans: Pro has 3 and Business 10."
                  : "All seats are in use. Remove a member or cancel an invite to free one up."
              }
            />
          </div>
        )}
      </Card>

      {banner && <Notice tone="success">{banner}</Notice>}
      {actionError && <ApiErrorView error={actionError} />}

      <Card>
        <SectionTitle>Members</SectionTitle>
        <ul className="divide-y divide-line-soft">
          {members.map((m) => {
            const isOwner = m.role === "Owner";
            const editable = !isOwner && !m.isYou;
            return (
              <li key={m.id} className="flex flex-wrap items-center gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 font-medium text-slate-900">
                    <span className="truncate">{m.fullName || m.email}</span>
                    {m.isYou && <Badge className="bg-mist text-slate-700">You</Badge>}
                  </p>
                  <p className="truncate text-xs text-muted">{m.email}</p>
                  {m.joinedAt && <p className="text-xs text-muted">Joined {formatDay(m.joinedAt)}</p>}
                </div>
                {editable ? (
                  <div className="flex items-center gap-2">
                    <label htmlFor={`role-${m.id}`} className="sr-only">
                      Role for {m.fullName || m.email}
                    </label>
                    <select
                      id={`role-${m.id}`}
                      className={cx(inputClass, "w-36 py-1.5")}
                      value={m.role}
                      disabled={busy === `role-${m.id}`}
                      onChange={(e) => changeRole(m, e.target.value as InviteRole)}
                    >
                      {INVITE_ROLES.map((r) => (
                        <option key={r} value={r}>
                          {r}
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      className="flex h-9 w-9 items-center justify-center rounded-control text-brick hover:bg-brick-wash disabled:opacity-50"
                      aria-label={`Remove ${m.fullName || m.email}`}
                      disabled={busy === `rm-${m.id}`}
                      onClick={() => removeMember(m)}
                    >
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  </div>
                ) : (
                  <StatusBadge tone={roleTone[m.role] ?? "neutral"}>{m.role}</StatusBadge>
                )}
              </li>
            );
          })}
        </ul>
      </Card>

      <Card>
        <SectionTitle>Pending invites</SectionTitle>
        {invites.length === 0 ? (
          <p className="text-sm text-muted">No pending invites. People you invite appear here until they join.</p>
        ) : (
          <ul className="divide-y divide-line-soft">
            {invites.map((inv) => (
              <li key={inv.id} className="flex flex-wrap items-center gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 font-medium text-slate-900">
                    <span className="truncate">{inv.email}</span>
                    <StatusBadge tone={roleTone[inv.role] ?? "neutral"}>{inv.role}</StatusBadge>
                  </p>
                  <p className="text-xs text-muted">
                    Invited {formatDay(inv.invitedAt)}. Expires {formatDay(inv.expiresAt)}.
                  </p>
                </div>
                <div className="flex gap-2">
                  <Button variant="secondary" className="min-h-8 px-3 py-1" loading={busy === `resend-${inv.id}`} onClick={() => resend(inv)}>
                    Resend
                  </Button>
                  <Button variant="danger" className="min-h-8 px-3 py-1" loading={busy === `cancel-${inv.id}`} onClick={() => cancelInvite(inv)}>
                    Cancel
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Modal open={inviteOpen} title="Invite a team member" onClose={() => setInviteOpen(false)}>
        {inviteOpen && (
          <InviteForm
            onClose={() => setInviteOpen(false)}
            onInvited={(inv) => {
              setInviteOpen(false);
              setBanner(`Invite sent to ${inv.email}.`);
              reload();
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function InviteForm({ onClose, onInvited }: { onClose: () => void; onInvited: (inv: InviteDto) => void }) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<InviteRole>("Staff");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onInvited(await api<InviteDto>("/api/team/invites", { method: "POST", body: { email: email.trim(), role } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Email" type="email" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} placeholder="name@example.com" />
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium text-slate-700">Role</legend>
        {INVITE_ROLES.map((r) => (
          <label
            key={r}
            className={cx(
              "flex cursor-pointer items-start gap-3 rounded-control border p-3",
              role === r ? "border-lagoon bg-lagoon-wash" : "border-line hover:bg-slate-50",
            )}
          >
            <input type="radio" name="role" value={r} checked={role === r} onChange={() => setRole(r)} className="mt-1 accent-[var(--lagoon)]" />
            <span>
              <span className="block text-sm font-semibold text-slate-900">{r}</span>
              <span className="block text-xs text-slate-600">{ROLE_DESCRIPTIONS[r]}</span>
            </span>
          </label>
        ))}
      </fieldset>
      <p className="text-xs text-slate-500">They&apos;ll get an email with a link to join. Invites expire after 7 days.</p>
      {error && (error.isPlanLimit ? <UpgradePrompt message={error.detail} /> : <ErrorBox error={error} />)}
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button type="submit" loading={busy} disabled={!email.trim()}>
          Send invite
        </Button>
      </div>
    </form>
  );
}
