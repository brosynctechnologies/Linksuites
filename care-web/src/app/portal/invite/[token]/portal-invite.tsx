"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { PatientSignInForm, PatientSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { Button, ErrorBox, Notice, PageLoader, Segmented, buttonClass, cx } from "@/components/ui";
import { api, logout, toApiError, type ApiError } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { isPortalUnavailable, portalHref } from "@/lib/portal";
import type { PatientLinkRelationship } from "@/lib/types/billing";
import type { PatientInvitePreview, PortalPatientRef } from "@/lib/types/portal";

type Preview = { invite: PatientInvitePreview } | { error: ApiError };

const RELATIONSHIPS: { value: PatientLinkRelationship; label: string; text: (first: string) => string }[] = [
  { value: "Self", label: "It's me", text: (first) => `I am ${first}.` },
  { value: "Parent", label: "I'm their parent", text: (first) => `${first} is my child.` },
  { value: "Guardian", label: "I'm their guardian", text: (first) => `I look after ${first}.` },
];

/**
 * The portal invitation (`/portal/invite/{token}`, the link the clinic emails or sends on WhatsApp). The preview is public (the clinic and
 * the patient's first name). Accepting needs a signed-in portal account and the relationship, and shows the API's sentence when it is
 * refused: a staff account of the clinic, an invite sent to another email, an expired or used invite, a deceased patient, or a session
 * from before the email was confirmed ("Please sign in again.", which also signs this browser out).
 */
export function PortalInvite() {
  const { token } = useParams<{ token: string }>();
  const [preview, setPreview] = useState<Preview | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    api<PatientInvitePreview>(`/api/care-portal/invites/${encodeURIComponent(token)}`, { auth: false, signal: ctrl.signal }).then(
      (invite) => setPreview({ invite }),
      (e) => {
        if (!ctrl.signal.aborted) setPreview({ error: toApiError(e) });
      },
    );
    return () => ctrl.abort();
  }, [token]);

  if (!preview) return <PageLoader />;
  if ("error" in preview) {
    const e = preview.error;
    return (
      <AuthShell title={e.status === 404 ? "This invitation link isn't valid" : "This invitation can't be used"}>
        <p className="text-sm text-slate-700">
          {e.status === 404 ? "Check that the whole link was copied, or ask the clinic to send you a new one." : e.message}
        </p>
        <Link href="/portal" className={buttonClass("secondary", "mt-5 w-full")}>
          Open the patient portal
        </Link>
      </AuthShell>
    );
  }
  return <Accept token={token} invite={preview.invite} />;
}

function Accept({ token, invite }: { token: string; invite: PatientInvitePreview }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const router = useRouter();
  const [relationship, setRelationship] = useState<PatientLinkRelationship | null>(null);
  const [mode, setMode] = useState<"in" | "new">("in");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const first = invite.patientFirstName;
  const here = `/portal/invite/${encodeURIComponent(token)}`;
  const subtitle = (
    <>
      {invite.clinicName} has invited you to the CareLink patient portal for <strong>{first}</strong>.
    </>
  );

  async function accept() {
    if (!relationship) return;
    setBusy(true);
    setError(null);
    try {
      const patients = await api<PortalPatientRef[]>(`/api/care-portal/invites/${encodeURIComponent(token)}/accept`, {
        method: "POST",
        body: { relationship },
        stayOnPage: true,
      });
      const linked = (patients ?? []).find((p) => p.firstName === first && p.clinicName === invite.clinicName);
      router.replace(portalHref("/portal", linked?.id));
    } catch (e) {
      setError(toApiError(e));
      setBusy(false);
    }
  }

  async function signOut() {
    await logout();
    setError(null);
  }

  if (!hydrated) return <PageLoader />;

  // A staff session (or a support view): staff accounts can't be linked to patients, so this account can't accept.
  if (auth?.tenant) {
    return (
      <AuthShell title={`Join ${invite.clinicName}'s patient portal`} subtitle={subtitle}>
        <Notice tone="warning">
          You&apos;re signed in as staff at <strong>{auth.tenant.name}</strong>. Staff accounts can&apos;t be linked to patients. Sign out, then sign
          in or create an account with your personal email.
        </Notice>
        {!auth.support && (
          <Button className="mt-4 w-full" onClick={signOut}>
            Sign out
          </Button>
        )}
      </AuthShell>
    );
  }

  const choose = (
    <fieldset className="space-y-2">
      <legend className="mb-1 text-sm font-medium text-slate-700">Who is {first} to you?</legend>
      {RELATIONSHIPS.map((r) => (
        <label
          key={r.value}
          className={cx(
            "flex min-h-12 cursor-pointer items-center gap-3 rounded-control border px-3 py-2",
            relationship === r.value ? "border-scrub bg-scrub-wash" : "border-rule bg-surface hover:border-scrub-line",
          )}
        >
          <input
            type="radio"
            name="relationship"
            value={r.value}
            checked={relationship === r.value}
            onChange={() => setRelationship(r.value)}
            className="h-4 w-4 accent-[var(--color-scrub)]"
          />
          <span>
            <span className="block text-sm font-bold text-ink">{r.label}</span>
            <span className="block text-xs text-muted">{r.text(first)}</span>
          </span>
        </label>
      ))}
    </fieldset>
  );

  if (!auth) {
    return (
      <AuthShell title={`Join ${invite.clinicName}'s patient portal`} subtitle={subtitle}>
        {error && (
          <div className="mb-4">
            <ErrorBox error={error} />
          </div>
        )}
        <p className="mb-4 text-sm text-slate-700">
          First sign in, or create an account. If the clinic emailed you this link, use that email address: a new account confirms it with the
          6-digit code we email you.
        </p>
        <Segmented
          label="Do you have a portal account?"
          items={[
            { value: "in", label: "I have an account" },
            { value: "new", label: "I'm new here" },
          ]}
          value={mode}
          onChange={setMode}
          className="mb-5"
        />
        {mode === "in" ? <PatientSignInForm key="in" onSignedIn={() => here} /> : <PatientSignupForm key="new" onSignedIn={() => here} />}
        <p className="mt-4 text-xs text-muted">This link works once and runs out on {new Date(invite.expiresAt).toLocaleDateString("en-GB", { day: "numeric", month: "long", timeZone: "Africa/Lagos" })}.</p>
      </AuthShell>
    );
  }

  const otherEmail = error?.code === "invite_other_email";
  const staffRefused = error?.status === 403 && !isPortalUnavailable(error);
  return (
    <AuthShell title={`Join ${invite.clinicName}'s patient portal`} subtitle={subtitle}>
      <p className="mb-4 text-sm text-slate-700">
        You&apos;re signed in as <strong>{auth.user.email}</strong>.
      </p>
      {choose}
      {error && (
        <div className="mt-4 space-y-2">
          <ErrorBox error={error} />
          {otherEmail && (
            <p className="text-sm text-slate-700">
              If the clinic sent the invitation to this address, sign out and sign in with an emailed code: that confirms the address is yours.
            </p>
          )}
          {(otherEmail || staffRefused) && (
            <Button variant="secondary" className="w-full" onClick={signOut}>
              Sign out and use another account
            </Button>
          )}
        </div>
      )}
      <Button className="mt-4 w-full" loading={busy} disabled={!relationship} onClick={accept}>
        Link {first} to my account
      </Button>
    </AuthShell>
  );
}
