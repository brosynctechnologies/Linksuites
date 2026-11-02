"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { careRoleLabel } from "@/lib/roles";
import type { AuthResponse, PublicInviteDto } from "@/lib/types";
import { AuthShell } from "@/components/auth-shell";
import { Button, ErrorBox, Input, Notice, PageLoader, buttonClass } from "@/components/ui";

type Loaded = { kind: "staff"; invite: PublicInviteDto } | { kind: "error"; error: ApiError };

/** A staff invitation from the clinic (`GET /api/public/invites/{token}`), accepted with a new or existing account. */
export function AcceptInvite() {
  const { token } = useParams<{ token: string }>();
  const [state, setState] = useState<Loaded | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    api<PublicInviteDto>(`/api/public/invites/${encodeURIComponent(token)}`, { auth: false, signal: ctrl.signal }).then(
      (invite) => setState({ kind: "staff", invite }),
      (e) => {
        if (!ctrl.signal.aborted) setState({ kind: "error", error: toApiError(e) });
      },
    );
    return () => ctrl.abort();
  }, [token]);

  if (!state) return <PageLoader />;
  if (state.kind === "error") {
    const e = state.error;
    return (
      <AuthShell title={e.status === 404 ? "This invitation link isn't valid" : "This invitation can't be used"}>
        <p className="text-sm text-slate-700">
          {e.status === 404 ? "It may have been replaced by a newer link. Ask the clinic to send you a new one." : e.message}
        </p>
        <Link href="/" className={buttonClass("secondary", "mt-5 w-full")}>
          Go to the home page
        </Link>
      </AuthShell>
    );
  }
  return <StaffInvite token={token} invite={state.invite} />;
}

/** The invite preview carries the platform role only, so a Staff invite reads "staff member" (the clinical role is set by the clinic). */
function inviteRoleLabel(role: PublicInviteDto["role"]): string {
  if (role === "Staff") return "a staff member";
  const label = careRoleLabel(role);
  return label === "Admin" ? "an admin" : `the ${label.toLowerCase()}`;
}

function StaffInvite({ token, invite }: { token: string; invite: PublicInviteDto }) {
  const router = useRouter();
  const [fullName, setFullName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  if (invite.expired) {
    return (
      <AuthShell title="This invitation has expired">
        <p className="text-sm text-slate-700">
          Ask someone at <strong>{invite.businessName}</strong> to send you a new one.
        </p>
      </AuthShell>
    );
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!invite.hasAccount) {
      if (!fullName.trim()) return setError("Enter your full name.");
      if (password.length < 8) return setError("Use at least 8 characters for your password.");
    }
    setBusy(true);
    setError(null);
    try {
      const body = invite.hasAccount ? { password } : { fullName: fullName.trim(), password };
      const res = await api<AuthResponse>(`/api/auth/invites/${encodeURIComponent(token)}/accept`, { method: "POST", body, auth: false });
      setAuth(res);
      router.replace("/app");
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <AuthShell
      title={`Join ${invite.businessName}`}
      subtitle={
        <>
          You&apos;ve been invited to join the clinic&apos;s team as <strong>{inviteRoleLabel(invite.role)}</strong>.
        </>
      }
    >
      <form onSubmit={submit} className="space-y-4">
        <Input label="Email" type="email" value={invite.email} readOnly disabled />
        {invite.hasAccount ? (
          <Notice>You already have an account. Enter your password to join.</Notice>
        ) : (
          <Input label="Full name" autoComplete="name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
        )}
        <Input
          label={invite.hasAccount ? "Password" : "Choose a password"}
          type="password"
          autoComplete={invite.hasAccount ? "current-password" : "new-password"}
          required
          minLength={invite.hasAccount ? undefined : 8}
          hint={invite.hasAccount ? undefined : "At least 8 characters."}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <ErrorBox error={error} />
        <Button type="submit" loading={busy} className="w-full">
          {invite.hasAccount ? "Sign in and join" : "Create account and join"}
        </Button>
      </form>
    </AuthShell>
  );
}
