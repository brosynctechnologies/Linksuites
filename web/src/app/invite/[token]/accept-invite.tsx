"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { ROLE_DESCRIPTIONS } from "@/lib/roles";
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
        <p className="text-sm text-ink-2">
          {loadError.status === 404 ? "This invite link isn't valid. It may have been cancelled or already used." : loadError.message}
        </p>
        <Link href="/login" className={buttonClass("secondary", "mt-4 w-full")}>
          Go to log in
        </Link>
      </AuthShell>
    );
  }

  if (!invite) return <PageLoader label="Loading your invite…" />;

  if (invite.expired) {
    return (
      <AuthShell title="This invite has expired">
        <p className="text-sm text-ink-2">
          Invites last 7 days. Ask someone at <strong>{invite.businessName}</strong> to send you a new one.
        </p>
      </AuthShell>
    );
  }

  const description = invite.role === "Admin" || invite.role === "Staff" ? ROLE_DESCRIPTIONS[invite.role] : null;
  return (
    <AuthShell title={`Join ${invite.businessName}`} subtitle={`You've been invited as ${invite.role}.`}>
      {description && <p className="-mt-4 mb-6 text-sm text-muted">{description}.</p>}
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
      if (!fullName.trim()) return setError("Enter your full name.");
      if (password.length < 8) return setError("Your password needs at least 8 characters.");
    } else if (!password) {
      return setError("Enter your password.");
    }
    setBusy(true);
    setError(null);
    try {
      const body = invite.hasAccount ? { password } : { fullName: fullName.trim(), password };
      const res = await api<AuthResponse>(`/api/auth/invites/${encodeURIComponent(token)}/accept`, { method: "POST", body, auth: false });
      setAuth(res);
      router.replace("/dashboard");
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <ErrorBox error={error} />
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
      <Button type="submit" loading={busy} className="w-full">
        {invite.hasAccount ? "Log in and join" : "Create account and join"}
      </Button>
      {invite.hasAccount && (
        <p className="text-sm text-ink-2">
          <Link href="/forgot-password" className="-ml-1 inline-flex min-h-11 items-center px-1 font-semibold text-ink underline underline-offset-2">
            Forgot your password?
          </Link>
        </p>
      )}
    </form>
  );
}
