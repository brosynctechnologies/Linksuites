"use client";

// Staff-side portal access on the patient page (Task 19 carry from Task 17): front desk and leadership invite a family member or the
// patient to the CareLink portal (a 14-day single-use link, emailed when there is an address and offered on WhatsApp), see and cancel
// pending invites, and see and revoke linked accounts. Doctors, nurses, the cashier and the support view never get this panel (the API
// refuses them too). The link is shown once to copy or send and is never stored by the web. Final review C1: front desk invites only the
// email on the patient's file (and the API refuses a clerk who changed that email in the last 24 hours); link-only invitations and other
// addresses are the medical director's or an admin's.

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { PendingInviteDto, PatientInviteDto, PortalLinkDto } from "@/lib/types/billing";
import { CopyIcon } from "./icons";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, Modal, Notice, SectionTitle, WhatsAppLink } from "./ui";

/**
 * `leadership`: the medical director or an admin, who may also invite another address or make a link-only invitation (no email). Front desk
 * sends emailed invitations to the email on the patient's file only (final review C1 ruling); the API refuses anything else.
 */
export function PortalAccess({
  patientId,
  patientName,
  deceased,
  email,
  phone,
  leadership,
}: {
  patientId: string;
  patientName: string;
  deceased: boolean;
  email: string | null;
  phone: string | null;
  leadership: boolean;
}) {
  const pending = useApi<PendingInviteDto[]>(`/api/care/patients/${patientId}/invites`);
  const links = useApi<PortalLinkDto[]>(`/api/care/patients/${patientId}/portal-links`);
  const [inviting, setInviting] = useState(false);
  const act = useAction();

  async function cancel(i: PendingInviteDto) {
    if (!window.confirm("Cancel this invitation? The link stops working.")) return;
    const res = await act.run(() => api(`/api/care/patients/${patientId}/invites/${i.id}/cancel`, { method: "POST" }));
    if (res.ok) pending.reload();
  }
  async function revoke(l: PortalLinkDto) {
    if (!window.confirm(`Remove ${l.name}'s portal access to ${patientName}? Any pending invitations for this patient are cancelled too.`)) return;
    const res = await act.run(() => api(`/api/care/patients/${patientId}/portal-links/${l.id}`, { method: "DELETE" }));
    if (res.ok) {
      links.reload();
      pending.reload();
    }
  }

  return (
    <Card className="space-y-4">
      <SectionTitle
        action={
          !deceased && (
            <Button variant="secondary" onClick={() => setInviting(true)}>
              Invite to the portal
            </Button>
          )
        }
      >
        Patient portal
      </SectionTitle>
      <p className="-mt-2 text-sm text-muted">The patient or a parent or guardian sees signed notes, results, bills and appointments in their own account.</p>

      <div className="space-y-2">
        <h3 className="text-sm font-bold text-ink">Linked accounts</h3>
        {links.error && <ApiErrorView error={links.error} onRetry={links.reload} />}
        {links.data && links.data.length === 0 && <p className="text-sm text-muted">No one is linked yet.</p>}
        {links.data && links.data.length > 0 && (
          <ul className="divide-y divide-rule-soft text-sm">
            {links.data.map((l) => (
              <li key={l.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
                <span className="font-bold text-ink">{l.name}</span>
                <Badge>{l.relationship === "Self" ? "The patient" : l.relationship}</Badge>
                {!l.active && <Badge tone="iodine">Revoked</Badge>}
                <span className="text-xs text-muted">
                  {l.email ?? ""} {l.active ? `linked ${formatStamp(l.createdAt)}` : l.revokedAt ? `revoked ${formatStamp(l.revokedAt)}` : ""}
                </span>
                {l.active && (
                  <Button variant="ghost" className="ml-auto min-h-9 px-2.5 text-arterial" onClick={() => revoke(l)}>
                    Revoke
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="space-y-2">
        <h3 className="text-sm font-bold text-ink">Pending invitations</h3>
        {pending.error && <ApiErrorView error={pending.error} onRetry={pending.reload} />}
        {pending.data && pending.data.length === 0 && <p className="text-sm text-muted">None.</p>}
        {pending.data && pending.data.length > 0 && (
          <ul className="divide-y divide-rule-soft text-sm">
            {pending.data.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
                <span className="text-ink">{[i.email, i.phone].filter(Boolean).join(", ") || "Link only"}</span>
                <span className="text-xs text-muted">
                  Sent {formatStamp(i.createdAt)}
                  {i.invitedByName && ` by ${i.invitedByName}`}, expires {formatStamp(i.expiresAt)}
                </span>
                <Button variant="ghost" className="ml-auto min-h-9 px-2.5" onClick={() => cancel(i)}>
                  Cancel
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>
      <ErrorBox error={act.error} />
      {inviting && (
        <InviteDialog
          patientId={patientId}
          email={email}
          leadership={leadership}
          phone={phone}
          onClose={() => setInviting(false)}
          onSent={() => pending.reload()}
        />
      )}
    </Card>
  );
}

function InviteDialog({
  patientId,
  email,
  phone,
  leadership,
  onClose,
  onSent,
}: {
  patientId: string;
  email: string | null;
  phone: string | null;
  leadership: boolean;
  onClose: () => void;
  onSent: () => void;
}) {
  const [to, setTo] = useState(email ?? "");
  const [tel, setTel] = useState(phone ?? "");
  const [sent, setSent] = useState<PatientInviteDto | null>(null);
  const [copied, setCopied] = useState(false);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => api<PatientInviteDto>(`/api/care/patients/${patientId}/invites`, { method: "POST", body: { email: to.trim() || null, phone: tel.trim() || null } }));
    if (res.ok) {
      setSent(res.data);
      onSent();
    }
  }

  return (
    <Modal open title="Invite to the portal" onClose={onClose}>
      {sent ? (
        <div className="space-y-4">
          <Notice tone="success">{to.trim() ? `The invitation was emailed to ${to.trim()}.` : "The invitation is ready."} It works once and lasts until {formatStamp(sent.expiresAt)}.</Notice>
          <Input label="Invitation link" readOnly value={sent.link} onFocus={(e) => e.currentTarget.select()} />
          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              onClick={async () => {
                try {
                  await navigator.clipboard.writeText(sent.link);
                  setCopied(true);
                } catch {
                  setCopied(false);
                }
              }}
            >
              <CopyIcon className="h-4 w-4" /> {copied ? "Copied" : "Copy the link"}
            </Button>
            {sent.whatsAppUrl && <WhatsAppLink href={sent.whatsAppUrl}>Send on WhatsApp</WhatsAppLink>}
          </div>
          <p className="text-xs text-muted">The message names the clinic and the patient&apos;s first name only. Whoever accepts chooses whether they are the patient, a parent or a guardian.</p>
          <div className="flex justify-end">
            <Button onClick={onClose}>Done</Button>
          </div>
        </div>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          {leadership ? (
            <Input
              label="Email"
              type="email"
              value={to}
              maxLength={200}
              onChange={(e) => setTo(e.target.value)}
              error={error?.field("email")}
              hint="If given, only an account with this email can accept. Left empty, anyone holding the link can accept it."
            />
          ) : email ? (
            // Front desk: the email on the patient's file, as is (final review C1). A different address is leadership's to send.
            <Input label="Email" type="email" value={email} readOnly hint="The email on the patient's file. Only an account with this email can accept." />
          ) : (
            <Notice tone="warning">
              Add the patient&apos;s email on this page to invite them. Only the medical director or an admin can create an invitation without an email.
            </Notice>
          )}
          <Input label="WhatsApp number" type="tel" value={tel} onChange={(e) => setTel(e.target.value)} error={error?.field("phone")} hint="Optional. You send the WhatsApp message yourself." />
          <ErrorBox error={error && !error.field("email") && !error.field("phone") ? error : null} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={busy} disabled={!leadership && !email}>
              Create the invitation
            </Button>
          </div>
        </form>
      )}
    </Modal>
  );
}
