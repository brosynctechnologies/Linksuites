"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth, useAuth } from "@/lib/auth";
import { INVITE_ROLES, roleLabel } from "@/lib/roles";
import type { AuthResponse, ChildRef, GuardianInvitePreview, PublicInviteDto } from "@/lib/types";
import { ParentSignInForm, ParentSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { Button, ErrorBox, Input, Notice, PageLoader, Segmented, buttonClass } from "@/components/ui";

type Loaded = { kind: "guardian"; invite: GuardianInvitePreview } | { kind: "staff"; invite: PublicInviteDto } | { kind: "error"; error: ApiError };

/** Guardian invites first (`/api/school-invites/{token}`); a token that isn't one is tried as a staff invite. */
async function load(token: string, signal: AbortSignal): Promise<Loaded> {
  const t = encodeURIComponent(token);
  try {
    return { kind: "guardian", invite: await api<GuardianInvitePreview>(`/api/school-invites/${t}`, { auth: false, signal }) };
  } catch (e) {
    const err = toApiError(e);
    if (err.status !== 404) return { kind: "error", error: err };
  }
  try {
    return { kind: "staff", invite: await api<PublicInviteDto>(`/api/public/invites/${t}`, { auth: false, signal }) };
  } catch (e) {
    return { kind: "error", error: toApiError(e) };
  }
}

function names(list: string[]) {
  if (list.length <= 1) return list[0] ?? "";
  return `${list.slice(0, -1).join(", ")} and ${list[list.length - 1]}`;
}

export function AcceptInvite() {
  const { token } = useParams<{ token: string }>();
  const [state, setState] = useState<Loaded | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    load(token, ctrl.signal).then((s) => {
      if (!ctrl.signal.aborted) setState(s);
    });
    return () => ctrl.abort();
  }, [token]);

  if (!state) return <PageLoader />;
  if (state.kind === "error") {
    const e = state.error;
    return (
      <AuthShell title={e.status === 404 ? "This invitation link isn't valid" : "This invitation can't be used"}>
        <p className="text-sm text-slate-700">
          {e.status === 404 ? "It may have been replaced by a newer link. Ask the school office to send you a new one." : e.message}
        </p>
        <Link href="/" className={buttonClass("secondary", "mt-5 w-full")}>
          Go to the home page
        </Link>
      </AuthShell>
    );
  }
  if (state.kind === "staff") return <StaffInvite token={token} invite={state.invite} />;
  return <GuardianInvite token={token} invite={state.invite} />;
}

// ---------- Parents ----------

function GuardianInvite({ token, invite }: { token: string; invite: GuardianInvitePreview }) {
  const auth = useAuth();
  const router = useRouter();
  const [mode, setMode] = useState<"new" | "existing">("new");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  /** Links the guardian record to the signed-in parent account, then opens the parent page. */
  async function accept(): Promise<string> {
    await api<ChildRef[]>(`/api/school-invites/${encodeURIComponent(token)}/accept`, { method: "POST" });
    return "/parent";
  }

  const children = names(invite.children);
  const subtitle = (
    <>
      {invite.schoolName} uses SchoolLink to share {children ? <strong>{children}</strong> : "your child"}&apos;s results, attendance and fees with you.
    </>
  );

  // Already signed in to a parent account: one tap.
  if (auth && !auth.tenant && !auth.support) {
    return (
      <AuthShell title={`Hello, ${invite.guardianName}`} subtitle={subtitle}>
        <p className="mb-4 text-sm text-slate-700">
          You&apos;re signed in as <strong>{auth.user.email}</strong>. Add {children || "your child"} to this account?
        </p>
        <ErrorBox error={error} />
        <Button
          className="mt-4 w-full"
          loading={busy}
          onClick={async () => {
            setBusy(true);
            setError(null);
            try {
              router.replace(await accept());
            } catch (e) {
              setError(toApiError(e));
              setBusy(false);
            }
          }}
        >
          Add to my account
        </Button>
      </AuthShell>
    );
  }

  return (
    <AuthShell title={`Hello, ${invite.guardianName}`} subtitle={subtitle}>
      <Segmented
        label="Do you have a SchoolLink parent account?"
        items={[
          { value: "new", label: "I'm new here" },
          { value: "existing", label: "I have an account" },
        ]}
        value={mode}
        onChange={setMode}
        className="mb-5"
      />
      {mode === "new" ? <ParentSignupForm key="new" onSignedIn={accept} /> : <ParentSignInForm key="existing" onSignedIn={accept} />}
      <p className="mt-4 text-xs text-muted">This link is for {invite.guardianName} and works once. If it isn&apos;t you, close this page.</p>
    </AuthShell>
  );
}

// ---------- Staff ----------

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

  const role = INVITE_ROLES.find((r) => r.role === invite.role);
  return (
    <AuthShell
      title={`Join ${invite.businessName}`}
      subtitle={
        <>
          You&apos;ve been invited as <strong>{roleLabel(invite.role)}</strong>
          {role && <span className="block text-xs text-muted">{role.text}</span>}
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
