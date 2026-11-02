"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { AuthFrame } from "@/components/sign-in";
import { Button, ErrorPanel, Input, linkClass } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";

/** Ask for a reset link. The answer is the same whether or not the email belongs to an admin. */
export function ForgotPasswordScreen() {
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [sentTo, setSentTo] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const value = email.trim();
    if (!value) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/auth/password/forgot", { method: "POST", body: { email: value, product: "admin" }, auth: false });
      setSentTo(value);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  if (sentTo) {
    return (
      <AuthFrame title="Check your email">
        <p className="mt-3 text-sm" role="status">
          If <span className="font-medium">{sentTo}</span> belongs to an admin, a link to choose a new password is on its way. It works once, for 10
          minutes.
        </p>
        <p className="mt-5 text-sm">
          <Link href="/login" className={linkClass}>
            Back to sign in
          </Link>
        </p>
      </AuthFrame>
    );
  }

  return (
    <AuthFrame title="Forgot password" subtitle="Enter your admin email. We'll send a link to choose a new password.">
      <form onSubmit={submit} className="mt-5 space-y-4" noValidate>
        <Input
          label="Email"
          type="email"
          autoComplete="username"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={error?.field("email")}
          required
          autoFocus
        />
        {error && !error.field("email") && <ErrorPanel error={error} />}
        <Button type="submit" className="w-full" loading={busy} disabled={!email.trim()}>
          Send reset link
        </Button>
      </form>
      <p className="mt-4 text-sm">
        <Link href="/login" className="inline-flex min-h-10 items-center text-muted underline underline-offset-2 hover:text-graphite">
          Back to sign in
        </Link>
      </p>
    </AuthFrame>
  );
}
