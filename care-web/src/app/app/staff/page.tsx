"use client";

import { useState, type FormEvent } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { CLINIC_ROLES, careRoleLabel, clinicRoleKey, clinicalRoleLabel, type ClinicRoleKey } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { CareStaffDto, ClinicalRole, TeamDto, TeamInviteDto } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  CaseSheet,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  Select,
  UsageMeter,
} from "@/components/ui";

const ADMIN_CLINICAL: { value: ClinicalRole; label: string }[] = [
  { value: "None", label: "No clinical role" },
  { value: "Doctor", label: "Doctor" },
  { value: "Nurse", label: "Nurse" },
];

/**
 * Staff and their clinic roles. The rules mirror the API's (CareStaff, TeamService):
 * - only the medical director gives or changes an admin's clinical role (an admin can't grant clinical access to an admin);
 * - nobody but the medical director changes their own role or clinical role;
 * - a cashier never has a clinical role, and moving someone to cashier clears it;
 * - when an admin (not the director) makes someone an admin, their clinical role is cleared.
 */
export default function StaffPage() {
  const { access } = useCare();
  const team = useApi<TeamDto>(access.isLeadership ? "/api/team" : null);
  const staff = useApi<CareStaffDto[]>(access.isLeadership ? "/api/care/staff" : null);
  const [inviting, setInviting] = useState(false);
  const [editing, setEditing] = useState<CareStaffDto | null>(null);
  const action = useAction();

  if (!access.isLeadership) return <NoAccess />;

  const reload = () => {
    team.reload();
    staff.reload();
  };
  const full = !!team.data && team.data.seats.limit >= 0 && team.data.seats.used >= team.data.seats.limit;
  const canWrite = !access.readOnly;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Staff"
        subtitle="Each person signs in with their own email. Their clinic role decides what they see: front desk and cashiers never see clinical notes."
        action={
          canWrite && (
            <div className="flex flex-wrap items-start gap-2">
              <ImportDialog kind="care-staff" title="Import staff" onDone={reload} />
              <Button onClick={() => setInviting(true)}>Invite staff</Button>
            </div>
          )
        }
      />
      {team.data && (
        <Card>
          <UsageMeter label="Staff logins on your plan" used={team.data.seats.used} limit={team.data.seats.limit} />
          {full && <p className="mt-2 text-sm text-iodine">Every login is taken. Upgrade your plan to invite more staff.</p>}
        </Card>
      )}
      <ErrorBox error={action.error} />
      {(team.error || staff.error) && <ApiErrorView error={team.error ?? staff.error} onRetry={reload} />}
      {(!team.data || !staff.data) && !team.error && !staff.error && <PageLoader />}

      {staff.data && (
        <CaseSheet label="Staff">
          {staff.data.map((s) => {
            const editable = canWrite && (s.memberRole !== "Owner" || s.isYou) && (!s.isYou || access.isDirector);
            return (
              <li key={s.userId} className="case-pad flex flex-wrap items-start justify-between gap-x-4 gap-y-2 py-3">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-bold text-ink">
                    {s.fullName} {s.isYou && <Badge>You</Badge>}
                    {s.isClinical && <Badge tone="scrub">Clinical access</Badge>}
                  </p>
                  <p className="text-sm text-slate-700">
                    {careRoleLabel(s.memberRole, s.clinicalRole)}
                    {s.title ? `, ${s.title}` : ""}
                    {s.licenceNumber ? `, licence ${s.licenceNumber}` : ""}
                  </p>
                  <p className="text-sm break-all text-muted">{s.email}</p>
                </div>
                <div className="flex flex-wrap gap-1">
                  {editable && (
                    <Button variant="ghost" className="px-2.5" onClick={() => setEditing(s)}>
                      Edit<span className="sr-only"> {s.fullName}</span>
                    </Button>
                  )}
                  {canWrite && !s.isYou && s.memberRole !== "Owner" && (
                    <Button
                      variant="ghost"
                      className="px-2.5 text-arterial"
                      onClick={async () => {
                        if (!window.confirm(`Remove ${s.fullName} from the clinic? They lose access straight away.`)) return;
                        if ((await action.run(() => api(`/api/team/members/${s.membershipId}`, { method: "DELETE" }))).ok) reload();
                      }}
                    >
                      Remove<span className="sr-only"> {s.fullName}</span>
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </CaseSheet>
      )}

      {team.data && team.data.invites.length > 0 && (
        <section aria-labelledby="pending" className="space-y-3">
          <h2 id="pending" className="display text-xl font-bold text-ink">
            Waiting to join
          </h2>
          <CaseSheet label="Pending invitations">
            {team.data.invites.map((i) => (
              <li key={i.id} className="case-pad flex flex-wrap items-center justify-between gap-x-4 gap-y-2 py-3">
                <div className="min-w-0">
                  <p className="font-bold break-all text-ink">{i.email}</p>
                  <p className="text-sm text-muted">
                    {inviteLabel(i)}, invited {formatStamp(i.invitedAt)}
                  </p>
                </div>
                {canWrite && (
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
                      className="px-2.5 text-arterial"
                      onClick={async () => {
                        if ((await action.run(() => api(`/api/team/invites/${i.id}`, { method: "DELETE" }))).ok) reload();
                      }}
                    >
                      Cancel
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </CaseSheet>
        </section>
      )}

      {inviting && (
        <InviteForm
          isDirector={access.isDirector}
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
          isDirector={access.isDirector}
          onClose={() => setEditing(null)}
          onChanged={reload}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function inviteLabel(i: TeamInviteDto) {
  return careRoleLabel(i.role, i.clinicalRole ?? (i.role === "Staff" ? null : "None"));
}

function InviteForm({ isDirector, onClose, onSaved }: { isDirector: boolean; onClose: () => void; onSaved: () => void }) {
  const [email, setEmail] = useState("");
  const [key, setKey] = useState<ClinicRoleKey>("Doctor");
  const [adminClinical, setAdminClinical] = useState<ClinicalRole>("None");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const role = CLINIC_ROLES.find((r) => r.key === key)!;
    // Only the medical director invites an admin with a clinical role (the API refuses it from an admin).
    const clinicalRole = role.key === "Admin" ? (isDirector ? adminClinical : "None") : role.key === "Cashier" ? null : role.clinicalRole;
    if ((await run(() => api("/api/team/invites", { method: "POST", body: { email: email.trim(), role: role.memberRole, clinicalRole } }))).ok) onSaved();
  }

  return (
    <Modal open title="Invite staff" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Email" type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} error={error?.field("email")} />
        <fieldset>
          <legend className="text-sm font-medium text-slate-700">Clinic role</legend>
          <div className="mt-1.5 grid gap-2">
            {CLINIC_ROLES.map((r) => (
              <label
                key={r.key}
                className="flex min-h-11 cursor-pointer items-start gap-3 rounded-control border border-rule p-3 has-[:checked]:border-scrub has-[:checked]:bg-scrub-wash"
              >
                <input type="radio" name="role" checked={key === r.key} onChange={() => setKey(r.key)} className="mt-1 accent-[#0f766e]" />
                <span>
                  <span className="block text-sm font-bold text-ink">{r.label}</span>
                  <span className="block text-xs text-slate-600">{r.text}</span>
                </span>
              </label>
            ))}
          </div>
        </fieldset>
        {key === "Admin" &&
          (isDirector ? (
            <Select
              label="Clinical role for this admin"
              value={adminClinical}
              onChange={(e) => setAdminClinical(e.target.value as ClinicalRole)}
              hint="An admin who is also a doctor or nurse can read clinical notes."
              error={error?.field("clinicalRole")}
            >
              {ADMIN_CLINICAL.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </Select>
          ) : (
            <Notice>The admin joins without a clinical role. Only the medical director can give an admin one.</Notice>
          ))}
        <Notice>They get an email with a link to join. You can add their title and licence number once they&apos;ve joined.</Notice>
        <ErrorBox error={error && !error.field("email") && !error.field("clinicalRole") ? error : null} />
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

function EditStaff({
  staff,
  isDirector,
  onClose,
  onSaved,
  onChanged,
}: {
  staff: CareStaffDto;
  isDirector: boolean;
  onClose: () => void;
  onSaved: () => void;
  /** Reload the list without closing (a call failed, or only the first of the two calls went through). */
  onChanged: () => void;
}) {
  const current = clinicRoleKey(staff.memberRole, staff.clinicalRole);
  const isOwner = staff.memberRole === "Owner";
  const [key, setKey] = useState<ClinicRoleKey | null>(current);
  // Starts at their current Doctor or Nurse role, so a director promoting a doctor to admin keeps it unless they change it.
  const [adminClinical, setAdminClinical] = useState<ClinicalRole>(staff.clinicalRole === "Doctor" || staff.clinicalRole === "Nurse" ? staff.clinicalRole : "None");
  const [title, setTitle] = useState(staff.title ?? "");
  const [licence, setLicence] = useState(staff.licenceNumber ?? "");
  const { busy, error, run } = useAction();

  // The medical director's own role never changes; nobody else edits their own row (the page doesn't offer it).
  const canChangeRole = !isOwner && !staff.isYou;
  const target = key ? CLINIC_ROLES.find((r) => r.key === key)! : null;
  const becomesAdmin = target?.key === "Admin";
  const wasAdmin = staff.memberRole === "Admin";
  // An admin's clinical role is the medical director's to set. A non-director moving someone to admin clears it (the API does).
  const adminClinicalEditable = becomesAdmin && isDirector;

  function clinicalFor(): ClinicalRole {
    if (isOwner) return staff.clinicalRole;
    if (!target) return staff.clinicalRole;
    if (target.key === "Admin") {
      if (isDirector) return adminClinical;
      // Unchanged admin: keep what they have (the API refuses a change). Newly made admin by an admin: cleared to None.
      return wasAdmin ? staff.clinicalRole : "None";
    }
    return target.clinicalRole;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    // Two calls: the member role (PATCH), then the clinical role, title and licence (PUT). The list is reloaded after each, whether it
    // worked or not, so a half-applied change (PATCH done, PUT refused) is never hidden behind the old row. The error stays shown here.
    if (canChangeRole && target && target.memberRole !== staff.memberRole) {
      const moved = await run(() => api(`/api/team/members/${staff.membershipId}`, { method: "PATCH", body: { role: target.memberRole } }));
      onChanged();
      if (!moved.ok) return;
    }
    const body = { clinicalRole: clinicalFor(), title: title.trim() || null, licenceNumber: licence.trim() || null };
    const saved = await run(() => api(`/api/care/staff/${staff.userId}`, { method: "PUT", body }));
    if (saved.ok) onSaved();
    else onChanged();
  }

  return (
    <Modal open title={`Edit ${staff.fullName}`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        {isOwner ? (
          <p className="text-sm text-slate-700">
            <strong>Medical director.</strong> Always has clinical access and every other right in the clinic.
          </p>
        ) : canChangeRole ? (
          <Select label="Clinic role" value={key ?? ""} onChange={(e) => setKey(e.target.value as ClinicRoleKey)}>
            {!current && (
              <option value="" disabled>
                {careRoleLabel(staff.memberRole, staff.clinicalRole)}
              </option>
            )}
            {CLINIC_ROLES.map((r) => (
              <option key={r.key} value={r.key}>
                {r.label}
              </option>
            ))}
          </Select>
        ) : null}
        {target && !isOwner && <p className="-mt-2 text-xs text-muted">{target.text}</p>}

        {becomesAdmin &&
          (adminClinicalEditable ? (
            <Select
              label="Clinical role for this admin"
              value={adminClinical}
              onChange={(e) => setAdminClinical(e.target.value as ClinicalRole)}
              hint="An admin who is also a doctor or nurse can read clinical notes."
              error={error?.field("clinicalRole")}
            >
              {ADMIN_CLINICAL.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </Select>
          ) : (
            <Notice>
              {wasAdmin
                ? `Clinical role: ${clinicalRoleLabel(staff.clinicalRole).toLowerCase()}. Only the medical director can change an admin's clinical role.`
                : "As an admin they won't have a clinical role. Only the medical director can give an admin one."}
            </Notice>
          ))}
        {target?.key === "Cashier" && current !== "Cashier" && <Notice tone="warning">A cashier can&apos;t have a clinical role, so they lose access to clinical notes.</Notice>}

        <Input label="Title" value={title} onChange={(e) => setTitle(e.target.value)} error={error?.field("title")} placeholder="e.g. Consultant physician, Matron" />
        <Input label="Licence number" value={licence} onChange={(e) => setLicence(e.target.value)} error={error?.field("licenceNumber")} hint="Optional. Printed on prescriptions and summaries." />
        <ErrorBox error={error && !["title", "licenceNumber", "clinicalRole"].some((f) => error.field(f)) ? error : null} />
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
