"use client";

import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { ROLE_DESCRIPTIONS } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { InviteDto, InviteRole, MemberDto, TeamDto } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  Button,
  ErrorBox,
  Input,
  Notice,
  PageHeader,
  PageLoader,
  Panel,
  SectionHeading,
  Sheet,
  UpgradePrompt,
  cx,
  formatDate,
  inputClass,
  listClass,
} from "@/components/ui";
import { PlusIcon, TrashIcon } from "@/components/icons";

const INVITE_ROLES: InviteRole[] = ["Admin", "Staff"];

export default function TeamPage() {
  const readOnly = !!useAuth()?.support;
  const { data, error, reload, setData } = useApi<TeamDto>("/api/team");
  const [inviteOpen, setInviteOpen] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

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

  const who = (m: MemberDto) => m.fullName || m.email;

  const changeRole = (m: MemberDto, role: InviteRole) =>
    act(`role-${m.id}`, async () => {
      const updated = await api<MemberDto | undefined>(`/api/team/members/${m.id}`, { method: "PATCH", body: { role } });
      if (updated?.id) setData((t) => (t ? { ...t, members: t.members.map((x) => (x.id === updated.id ? updated : x)) } : (t as unknown as TeamDto)));
      else reload();
      setBanner(`${who(m)} is now ${role}.`);
    });

  const removeMember = (m: MemberDto) => {
    if (!window.confirm(`Remove ${who(m)} from this store? They lose access straight away.`)) return;
    act(`rm-${m.id}`, async () => {
      await api(`/api/team/members/${m.id}`, { method: "DELETE" });
      setBanner(`${who(m)} was removed.`);
      reload();
    });
  };

  const resend = (inv: InviteDto) =>
    act(`resend-${inv.id}`, async () => {
      const updated = await api<InviteDto | undefined>(`/api/team/invites/${inv.id}/resend`, { method: "POST" });
      if (updated?.id) setData((t) => (t ? { ...t, invites: t.invites.map((x) => (x.id === updated.id ? updated : x)) } : (t as unknown as TeamDto)));
      setBanner(`Invite sent again to ${inv.email}.`);
    });

  const cancelInvite = (inv: InviteDto) => {
    if (!window.confirm(`Cancel the invite for ${inv.email}? The link in their email stops working.`)) return;
    act(`cancel-${inv.id}`, async () => {
      await api(`/api/team/invites/${inv.id}`, { method: "DELETE" });
      setBanner(`Invite for ${inv.email} cancelled.`);
      reload();
    });
  };

  if (!data) {
    return (
      <div>
        <PageHeader title="Team" back="/dashboard/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader label="Loading your team…" rows={3} />}
      </div>
    );
  }

  const { members, invites, seats } = data;
  const unlimited = seats.limit === -1;
  const full = !unlimited && seats.used >= seats.limit;
  const pct = unlimited || seats.limit <= 0 ? 0 : Math.min(100, Math.round((seats.used / seats.limit) * 100));

  return (
    <div className="space-y-6">
      <PageHeader
        title="Team"
        back="/dashboard/more"
        subtitle="Give the people who help you their own login."
        action={
          !readOnly && (
            <Button onClick={() => setInviteOpen(true)} disabled={full}>
              <PlusIcon width={18} height={18} /> Invite
            </Button>
          )
        }
      />

      <Panel>
        <div className="flex justify-between text-sm">
          <span className="text-ink-2">Seats used</span>
          <span className="font-semibold text-ink num">
            {seats.used} / {unlimited ? "∞" : seats.limit}
          </span>
        </div>
        {!unlimited && (
          <div
            className="mt-2 h-2 overflow-hidden rounded-full bg-line"
            role="progressbar"
            aria-label="Seats used"
            aria-valuemin={0}
            aria-valuemax={seats.limit}
            aria-valuenow={seats.used}
          >
            <div className={cx("h-full rounded-full", full ? "bg-hibiscus" : "bg-ink")} style={{ width: `${pct}%` }} />
          </div>
        )}
        <p className="mt-2 text-xs text-subtle">You, your team and pending invites each use a seat.</p>
        {full && (
          <div className="mt-3">
            <UpgradePrompt
              message={
                seats.limit <= 1
                  ? "The Free plan is for one person. Pro adds a team of up to 3."
                  : "All seats are in use. Remove someone or cancel an invite to free one."
              }
            />
          </div>
        )}
      </Panel>

      {banner && <Notice tone="success">{banner}</Notice>}
      {actionError && <ApiErrorView error={actionError} />}

      <section aria-labelledby="members-heading">
        <SectionHeading id="members-heading" title="Members" />
        <ul className={listClass}>
          {members.map((m) => {
            const editable = m.role !== "Owner" && !m.isYou && !readOnly;
            return (
              <li key={m.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 font-medium text-ink">
                    <span className="truncate">{who(m)}</span>
                    {m.isYou && <Badge tone="ink">You</Badge>}
                  </p>
                  <p className="truncate text-sm text-muted">{m.email}</p>
                  {m.joinedAt && <p className="text-xs text-subtle">Joined {formatDate(m.joinedAt, false)}</p>}
                </div>
                {editable ? (
                  <div className="flex items-center gap-1">
                    <label htmlFor={`role-${m.id}`} className="sr-only">
                      Role for {who(m)}
                    </label>
                    <select
                      id={`role-${m.id}`}
                      className={cx(inputClass, "w-32")}
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
                      className="flex h-11 w-11 items-center justify-center rounded-control text-hibiscus-deep hover:bg-hibiscus-soft disabled:opacity-50"
                      aria-label={`Remove ${who(m)}`}
                      disabled={busy === `rm-${m.id}`}
                      onClick={() => removeMember(m)}
                    >
                      <TrashIcon />
                    </button>
                  </div>
                ) : (
                  <Badge tone={m.role === "Staff" ? "neutral" : "ink"}>{m.role}</Badge>
                )}
              </li>
            );
          })}
        </ul>
      </section>

      <section aria-labelledby="invites-heading">
        <SectionHeading id="invites-heading" title="Pending invites" />
        {invites.length === 0 ? (
          <p className="text-sm text-muted">No pending invites. People you invite show here until they join.</p>
        ) : (
          <ul className={listClass}>
            {invites.map((inv) => (
              <li key={inv.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 font-medium text-ink">
                    <span className="truncate">{inv.email}</span>
                    <Badge tone={inv.role === "Staff" ? "neutral" : "ink"}>{inv.role}</Badge>
                  </p>
                  <p className="text-xs text-subtle">
                    Invited {formatDate(inv.invitedAt, false)}. Expires {formatDate(inv.expiresAt, false)}.
                  </p>
                </div>
                {!readOnly && (
                  <div className="flex gap-2">
                    <Button variant="secondary" loading={busy === `resend-${inv.id}`} disabled={!!busy} onClick={() => resend(inv)}>
                      Resend
                    </Button>
                    <Button variant="danger" loading={busy === `cancel-${inv.id}`} disabled={!!busy} onClick={() => cancelInvite(inv)}>
                      Cancel invite
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <Sheet open={inviteOpen} title="Invite someone" onClose={() => setInviteOpen(false)}>
        {inviteOpen && (
          <InviteForm
            onClose={() => setInviteOpen(false)}
            onInvited={(email) => {
              setInviteOpen(false);
              setBanner(`Invite sent to ${email}.`);
              reload();
            }}
          />
        )}
      </Sheet>
    </div>
  );
}

function InviteForm({ onClose, onInvited }: { onClose: () => void; onInvited: (email: string) => void }) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<InviteRole>("Staff");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const value = email.trim();
    if (!value || !value.includes("@")) return setError("Enter their email address.");
    setBusy(true);
    setError(null);
    try {
      await api("/api/team/invites", { method: "POST", body: { email: value, role } });
      onInvited(value);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      {error && (typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />)}
      <Input label="Email" type="email" autoComplete="off" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} placeholder="name@example.com" />
      <fieldset className="space-y-2">
        <legend className="mb-1.5 text-sm font-medium text-ink">Role</legend>
        {INVITE_ROLES.map((r) => (
          <label
            key={r}
            className={cx(
              "flex min-h-11 cursor-pointer items-start gap-3 rounded-control border p-3",
              role === r ? "border-ink bg-ink-soft" : "border-line hover:bg-paper",
            )}
          >
            <input type="radio" name="role" value={r} checked={role === r} onChange={() => setRole(r)} className="mt-0.5 h-5 w-5 accent-ink" />
            <span>
              <span className="block text-sm font-semibold text-ink">{r}</span>
              <span className="block text-sm text-ink-2">{ROLE_DESCRIPTIONS[r]}</span>
            </span>
          </label>
        ))}
      </fieldset>
      <p className="text-sm text-muted">They get an email with a link to join. Invites expire after 7 days.</p>
      <div className="flex justify-end gap-2">
        <Button variant="ghost" onClick={onClose}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          Send invite
        </Button>
      </div>
    </form>
  );
}
