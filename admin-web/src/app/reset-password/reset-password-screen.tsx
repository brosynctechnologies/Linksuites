"use client";

import Link from "next/link";
import { useEffect, useState, useSyncExternalStore, type FormEvent } from "react";
import { NewPasswordFields, newPasswordProblems } from "@/components/new-password";
import { AuthFrame } from "@/components/sign-in";
import { Button, ErrorPanel, Loading, linkClass } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { endSession, PASSWORD_CHANGED_NOTE } from "@/lib/session";

interface ResetLink {
  email: string;
  token: string;
}

/**
 * The emailed link is `/reset-password#email=…&token=…`: the values live in the fragment so they never reach a server log.
 * Read them once, keep them in memory, and take them out of the address bar.
 */
let lastHash = "";
let captured: ResetLink | null = null;

function readLink(): ResetLink | null {
  const hash = window.location.hash.replace(/^#/, "");
  if (hash && hash !== lastHash) {
    lastHash = hash;
    const p = new URLSearchParams(hash);
    const email = p.get("email")?.trim();
    const token = p.get("token");
    captured = email && token ? { email, token } : null;
  }
  return captured;
}

const noopSubscribe = () => () => {};

export function ResetPasswordScreen() {
  // undefined on the server and during hydration; null when the address has no usable link.
  const link = useSyncExternalStore<ResetLink | null | undefined>(noopSubscribe, readLink, () => undefined);

  useEffect(() => {
    if (window.location.hash) window.history.replaceState(window.history.state, "", window.location.pathname + window.location.search);
  }, []);

  if (link === undefined) return <Loading label="Opening the link…" />;
  if (link === null) {
    return (
      <AuthFrame title="This link is incomplete">
        <p className="mt-3 text-sm">Open the link from the reset email again, exactly as it was sent. If it still doesn&apos;t work, ask for a new one.</p>
        <p className="mt-5 flex flex-wrap gap-x-4 gap-y-2 text-sm">
          <Link href="/forgot-password" className={linkClass}>
            Ask for a new link
          </Link>
          <Link href="/login" className={linkClass}>
            Back to sign in
          </Link>
        </p>
      </AuthFrame>
    );
  }
  return <ResetForm link={link} />;
}

function ResetForm({ link }: { link: ResetLink }) {
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [done, setDone] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setTouched(true);
    const local = newPasswordProblems(password, confirm);
    if (!password || !confirm || local.newPassword || local.confirm) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/auth/password/reset", { method: "POST", body: { email: link.email, token: link.token, newPassword: password }, auth: false });
      setPassword("");
      setConfirm("");
      // Every admin-panel session for this account has ended; a session in this tab would be refused anyway.
      endSession(PASSWORD_CHANGED_NOTE);
      setDone(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <AuthFrame title="Password changed">
        <p className="mt-3 text-sm" role="status">
          Your new password is set and every admin session for this account has ended. Sign in with the new password.
        </p>
        <Link href="/login" className="mt-5 inline-flex min-h-10 w-full items-center justify-center rounded-control bg-graphite px-3.5 text-sm font-medium text-white hover:bg-graphite-3">
          Sign in
        </Link>
      </AuthFrame>
    );
  }

  const tokenProblem = error?.field("token");
  const otherProblem = error && !tokenProblem && !error.field("newPassword");

  return (
    <AuthFrame title="Choose a new password" subtitle={<>For {link.email}.</>}>
      <form onSubmit={submit} className="mt-5 space-y-4" noValidate>
        <input type="email" autoComplete="username" value={link.email} readOnly hidden />
        <NewPasswordFields password={password} confirm={confirm} onPassword={setPassword} onConfirm={setConfirm} touched={touched} error={error} />
        {tokenProblem && (
          <div className="space-y-2">
            <ErrorPanel error={tokenProblem} />
            <p className="text-sm">
              <Link href="/forgot-password" className={linkClass}>
                Ask for a new link
              </Link>
            </p>
          </div>
        )}
        {otherProblem && <ErrorPanel error={error} />}
        <Button type="submit" className="w-full" loading={busy} disabled={!password || !confirm}>
          Set new password
        </Button>
      </form>
    </AuthFrame>
  );
}
