"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { INVITE_ROLES, roleLabel, useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { Role, StaffDto, TeamDto } from "@/lib/types";
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
  RuledSheet,
  Select,
  UsageMeter,
} from "@/components/ui";

/** Job titles offered for each role; the title is what the school calls the person. */
const TITLES: Record<Role, string[]> = {
  Owner: ["Proprietor", "Proprietress", "Director"],
  Admin: ["Principal", "Head teacher", "Vice principal", "School admin"],
  Accountant: ["Bursar", "Accountant"],
  Staff: ["Teacher", "Class teacher", "Subject teacher"],
};

export default function StaffPage() {
  const canManage = useCan("school.manage");
  const team = useApi<TeamDto>(canManage ? "/api/team" : null);
  const staff = useApi<StaffDto[]>(canManage ? "/api/school/staff" : null);
  const [inviting, setInviting] = useState(false);
  const [editing, setEditing] = useState<StaffDto | null>(null);
  const action = useAction();

  if (!canManage) return <NoAccess />;

  const reload = () => {
    team.reload();
    staff.reload();
  };
  const memberId = (s: StaffDto) => team.data?.members.find((m) => m.userId === s.userId)?.id;
  const full = !!team.data && team.data.seats.limit >= 0 && team.data.seats.used >= team.data.seats.limit;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Staff"
        subtitle="Proprietor, principal, bursar and teachers. Each person signs in with their own email."
        action={
          <div className="flex flex-wrap items-start gap-2">
            <ImportDialog kind="staff" title="Import staff" onDone={reload} />
            <Button onClick={() => setInviting(true)}>Invite staff</Button>
          </div>
        }
      />
      {team.data && (
        <Card>
          <UsageMeter label="Staff seats on your plan" used={team.data.seats.used} limit={team.data.seats.limit} />
          {full && (
            <p className="mt-2 text-sm text-crest">
              Every seat is taken. Upgrade your plan to invite more staff.{" "}
              <Link href="/app/billing" className="font-bold underline underline-offset-2">
                See plans
              </Link>
            </p>
          )}
        </Card>
      )}
      <ErrorBox error={action.error} />
      {(team.error || staff.error) && <ApiErrorView error={team.error ?? staff.error} onRetry={reload} />}
      {(!team.data || !staff.data) && !team.error && !staff.error && <PageLoader />}

      {staff.data && (
        <RuledSheet label="Staff">
          {staff.data.map((s) => {
            const id = memberId(s);
            return (
              <li key={s.staffId} className="ruled-pad flex flex-wrap items-start justify-between gap-x-4 gap-y-2 py-3">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-bold text-ink">
                    {s.fullName} {s.isYou && <Badge>You</Badge>}
                  </p>
                  <p className="text-sm text-slate-700">{roleLabel(s.role, s.jobTitle) === s.jobTitle ? s.jobTitle : `${s.jobTitle || roleLabel(s.role)}, with ${roleLabel(s.role).toLowerCase()} access`}</p>
                  <p className="text-sm break-all text-muted">
                    {s.email}
                    {s.phone ? `, ${s.phone}` : ""}
                    {s.staffNo ? `, staff no. ${s.staffNo}` : ""}
                  </p>
                </div>
                <div className="flex flex-wrap gap-1">
                  <Button variant="ghost" className="px-2.5" onClick={() => setEditing(s)}>
                    Edit
                  </Button>
                  {!s.isYou && s.role !== "Owner" && id && (
                    <Button
                      variant="ghost"
                      className="px-2.5 text-brick"
                      onClick={async () => {
                        if (!window.confirm(`Remove ${s.fullName} from the school? They lose access straight away.`)) return;
                        if ((await action.run(() => api(`/api/team/members/${id}`, { method: "DELETE" }))).ok) reload();
                      }}
                    >
                      Remove
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </RuledSheet>
      )}

      {team.data && team.data.invites.length > 0 && (
        <section aria-labelledby="pending" className="space-y-3">
          <h2 id="pending" className="slab text-xl font-bold text-ink">
            Waiting to join
          </h2>
          <RuledSheet label="Pending invitations">
            {team.data.invites.map((i) => (
              <li key={i.id} className="ruled-pad flex flex-wrap items-center justify-between gap-x-4 gap-y-2 py-3">
                <div className="min-w-0">
                  <p className="font-bold break-all text-ink">{i.email}</p>
                  <p className="text-sm text-muted">
                    {roleLabel(i.role)}, invited {formatStamp(i.invitedAt)}
                  </p>
                </div>
                <div className="flex gap-1">
                  <Button
                    variant="ghost"
                    className="px-2.5"
                    onClick={async () => {
                      if ((await action.run(() => api(`/api/team/invites/${i.id}/resend`, { method: "POST" }))).ok) reload();
                    }}
                  >
                    Resend
                  </Button>
                  <Button
                    variant="ghost"
                    className="px-2.5 text-brick"
                    onClick={async () => {
                      if ((await action.run(() => api(`/api/team/invites/${i.id}`, { method: "DELETE" }))).ok) reload();
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </li>
            ))}
          </RuledSheet>
        </section>
      )}

      {inviting && (
        <InviteForm
          onClose={() => setInviting(false)}
          onSaved={() => {
            setInviting(false);
            reload();
          }}
        />
      )}
      {editing && (
        <EditStaff
          staff={editing}
          memberId={memberId(editing)}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function InviteForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Staff");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    if ((await run(() => api("/api/team/invites", { method: "POST", body: { email: email.trim(), role } }))).ok) onSaved();
  }

  return (
    <Modal open title="Invite staff" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Email" type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} error={error?.field("email")} />
        <fieldset>
          <legend className="text-sm font-medium text-slate-700">Role</legend>
          <div className="mt-1.5 grid gap-2">
            {INVITE_ROLES.map((r) => (
              <label
                key={r.role}
                className="flex min-h-11 cursor-pointer items-start gap-3 rounded-control border border-rule p-3 has-[:checked]:border-blazer has-[:checked]:bg-blazer-wash"
              >
                <input type="radio" name="role" checked={role === r.role} onChange={() => setRole(r.role)} className="mt-1 accent-[#6b1d33]" />
                <span>
                  <span className="block text-sm font-bold text-ink">{r.label}</span>
                  <span className="block text-xs text-slate-600">{r.text}</span>
                </span>
              </label>
            ))}
          </div>
        </fieldset>
        <Notice>They get an email with a link to join. Set their job title once they&apos;ve joined.</Notice>
        <ErrorBox error={error && !error.field("email") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Send invitation
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function EditStaff({ staff, memberId, onClose, onSaved }: { staff: StaffDto; memberId?: string; onClose: () => void; onSaved: () => void }) {
  const [jobTitle, setJobTitle] = useState(staff.jobTitle);
  const [staffNo, setStaffNo] = useState(staff.staffNo ?? "");
  const [phone, setPhone] = useState(staff.phone ?? "");
  const [role, setRole] = useState<Role>(staff.role);
  const { busy, error, run } = useAction();
  const canChangeRole = !staff.isYou && staff.role !== "Owner" && !!memberId;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (canChangeRole && role !== staff.role) {
      if (!(await run(() => api(`/api/team/members/${memberId}`, { method: "PATCH", body: { role } }))).ok) return;
    }
    const body = { jobTitle: jobTitle.trim(), staffNo: staffNo.trim() || null, phone: phone.trim() || null };
    if ((await run(() => api(`/api/school/staff/${staff.membershipId}`, { method: "PUT", body }))).ok) onSaved();
  }

  const titles = [...new Set([...TITLES[role], jobTitle].filter(Boolean))];
  return (
    <Modal open title={`Edit ${staff.fullName}`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        {canChangeRole && (
          <Select
            label="Access"
            value={role}
            onChange={(e) => {
              const r = e.target.value as Role;
              setRole(r);
              setJobTitle(TITLES[r][0]);
            }}
          >
            {INVITE_ROLES.map((r) => (
              <option key={r.role} value={r.role}>
                {r.label}
              </option>
            ))}
          </Select>
        )}
        <Select label="Job title" value={jobTitle} onChange={(e) => setJobTitle(e.target.value)} error={error?.field("jobTitle")} hint="Shown on report cards and to parents.">
          {titles.map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </Select>
        <Input label="Staff number" value={staffNo} onChange={(e) => setStaffNo(e.target.value)} error={error?.field("staffNo")} />
        <Input label="Phone number" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} error={error?.field("phone")} />
        <ErrorBox error={error && !["jobTitle", "staffNo", "phone"].some((f) => error.field(f)) ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Save
          </Button>
        </div>
      </form>
    </Modal>
  );
}
