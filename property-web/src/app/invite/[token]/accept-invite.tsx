"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { ROLE_DESCRIPTIONS, toRole } from "@/lib/permissions";
import type { AuthResponse, PublicInviteDto } from "@/lib/types";
import { AuthShell } from "@/components/auth-shell";
import { Button, ErrorBox, Input, Notice, PageLoader, buttonClass } from "@/components/ui";

/** Public page behind the invite email link: shows the invite and lets the person join (no sign-in needed). */
export function AcceptInvite() {
  const { token } = useParams<{ token: string }>();
  const [invite, setInvite] = useState<PublicInviteDto | null>(null);
  const [loadError, setLoadError] = useState<ApiError | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    api<PublicInviteDto>(`/api/public/invites/${encodeURIComponent(token)}`, { auth: false, signal: ctrl.signal }).then(setInvite, (e) => {
      if (!ctrl.signal.aborted) setLoadError(toApiError(e));
    });
    return () => ctrl.abort();
  }, [token]);

  if (loadError) {
    return (
      <AuthShell title="Invite not found">
        <p className="text-sm text-slate-600">
          {loadError.status === 404 ? "This invite link isn't valid. It may have been cancelled." : loadError.message}
        </p>
        <Link href="/studio/login" className={buttonClass("secondary", "mt-4 w-full")}>
          Go to log in
        </Link>
      </AuthShell>
    );
  }

  if (!invite) return <PageLoader />;

  if (invite.expired) {
    return (
      <AuthShell title="This invite has expired">
        <p className="text-sm text-slate-600">
          Invites are valid for 7 days. Ask someone at <strong>{invite.businessName}</strong> to send you a new one.
        </p>
      </AuthShell>
    );
  }

  const role = toRole(invite.role);
  return (
    <AuthShell
      title={`Join ${invite.businessName}`}
      subtitle={
        <>
          You&apos;ve been invited as <strong>{invite.role}</strong>
          {role && <span className="block text-xs text-muted">{ROLE_DESCRIPTIONS[role]}</span>}
        </>
      }
    >
      <AcceptForm token={token} invite={invite} />
    </AuthShell>
  );
}

function AcceptForm({ token, invite }: { token: string; invite: PublicInviteDto }) {
  const router = useRouter();
  const [fullName, setFullName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!invite.hasAccount) {
      if (!fullName.trim()) return setError("Please enter your full name.");
      if (password.length < 8) return setError("Password must be at least 8 characters.");
    }
    setBusy(true);
    setError(null);
    try {
      const body = invite.hasAccount ? { password } : { fullName: fullName.trim(), password };
      const res = await api<AuthResponse>(`/api/auth/invites/${encodeURIComponent(token)}/accept`, { method: "POST", body, auth: false });
      setAuth(res);
      router.replace("/studio");
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Email" type="email" value={invite.email} readOnly disabled />
      {invite.hasAccount ? (
        <Notice>You already have an account. Enter your password to join.</Notice>
      ) : (
        <Input label="Full name" autoComplete="name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
      )}
      <Input
        label={invite.hasAccount ? "Password" : "Create a password"}
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
        {invite.hasAccount ? "Sign in to join" : "Create account and join"}
      </Button>
    </form>
  );
}
