"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { clearAuth, getAuth, useHydrated } from "@/lib/auth";
import { AuthShell } from "@/components/auth-shell";
import { Button, ErrorBox, Input, Notice, Spinner, buttonClass, linkClass } from "@/components/ui";

interface ResetLink {
  email: string;
  token: string;
}

/** The emailed link is `/reset-password#email=…&token=…`: the fragment never reaches a server. */
function readLink(): ResetLink | null {
  const params = new URLSearchParams(window.location.hash.slice(1));
  const email = params.get("email");
  const token = params.get("token");
  return email && token ? { email, token } : null;
}

/** Reads the reset link from the URL fragment once in the browser, then shows the form. */
export function ResetPasswordView() {
  const hydrated = useHydrated();
  if (!hydrated) {
    return (
      <AuthShell title="Choose a new password">
        <p role="status" className="flex items-center gap-3 text-sm text-slate-600">
          <Spinner />
          Checking the reset link…
        </p>
      </AuthShell>
    );
  }
  return <ResetPassword />;
}

function ResetPassword() {
  const [link] = useState(readLink);
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [mismatch, setMismatch] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [done, setDone] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);

  useEffect(() => {
    // Keep the token out of the address bar, history and referrers.
    if (window.location.hash) window.history.replaceState(null, "", window.location.pathname + window.location.search);
  }, []);

  if (!link) {
    return (
      <AuthShell title="This reset link is incomplete">
        <p className="text-sm text-slate-700">
          Open the link from the email again, or{" "}
          <Link href="/forgot-password" className={linkClass}>
            ask for a new one
          </Link>
          .
        </p>
      </AuthShell>
    );
  }

  if (done) {
    return (
      <AuthShell title="Password changed">
        <div className="space-y-4">
          <Notice tone="success">Your password is changed and you&apos;ve been signed out everywhere.</Notice>
          <Link href="/login" className={buttonClass("primary", "w-full")}>
            Log in
          </Link>
        </div>
      </AuthShell>
    );
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!link) return;
    setError(null);
    if (password !== confirm) {
      setMismatch(true);
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
      return;
    }
    setMismatch(false);
    setBusy(true);
    try {
      await api("/api/auth/password/reset", { method: "POST", body: { email: link.email, token: link.token, newPassword: password }, auth: false });
      // This browser's own session for the account was signed out too.
      const auth = getAuth();
      if (auth && !auth.support && auth.user.email.toLowerCase() === link.email.toLowerCase()) clearAuth();
      setDone(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  const badLink = !!error?.field("token");

  return (
    <AuthShell title="Choose a new password" subtitle={`For ${link.email}`}>
      <form ref={formRef} onSubmit={submit} className="space-y-4">
        <Input
          label="New password"
          type="password"
          autoComplete="new-password"
          required
          minLength={8}
          hint="At least 8 characters."
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <Input
          label="Confirm new password"
          type="password"
          autoComplete="new-password"
          required
          error={mismatch ? "The passwords don't match." : undefined}
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
        />
        <ErrorBox error={error ?? (mismatch ? "The passwords don't match." : null)} />
        {badLink && (
          <p className="text-sm">
            <Link href="/forgot-password" className={linkClass}>
              Get a new reset link
            </Link>
          </p>
        )}
        <Button type="submit" loading={busy} className="w-full">
          Change password
        </Button>
      </form>
    </AuthShell>
  );
}
