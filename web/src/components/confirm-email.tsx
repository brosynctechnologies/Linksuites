"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import type { AuthResponse } from "@/lib/types";
import { Button, ErrorBox, Input, Notice } from "./ui";

/**
 * A password sign-up's answer until the emailed 6-digit code is entered (final fix pass B): no session yet. The code goes, with the same
 * password, to the sign-up's `/confirm` route (`/api/auth/signup/confirm`, or `/api/auth/{patient|parent|learner|seeker}/signup/confirm`).
 */
export interface SignupPending {
  confirmationRequired: true;
  email: string;
}

export function isSignupPending(res: unknown): res is SignupPending {
  return typeof res === "object" && res !== null && (res as { confirmationRequired?: unknown }).confirmationRequired === true;
}

/** Password login's 403 `email_unconfirmed`: the sign-up's code was never entered. */
export function isUnconfirmed(err: unknown): err is ApiError {
  return err instanceof ApiError && err.code === "email_unconfirmed";
}

/**
 * The code step after a sign-up, or after a login that was told to confirm first (`notice` is then the API's sentence, and the code is
 * sent when asked). Mirrors the email-code sign-in: the 6-digit code, "Send a new code", the API's own error sentences. Whoever enters
 * the code sets `password`, the one typed on the form.
 */
export function ConfirmEmailStep({
  email,
  password,
  confirmPath,
  product,
  notice,
  onConfirmed,
  onBack,
  backLabel = "Back",
}: {
  email: string;
  password: string;
  confirmPath: string;
  product?: string;
  /** Set when no code was sent yet (the login page): shown above an "Email me a code" button. */
  notice?: string;
  onConfirmed: (res: AuthResponse) => void | Promise<void>;
  onBack?: () => void;
  backLabel?: string;
}) {
  const [sent, setSent] = useState(!notice);
  const [resent, setResent] = useState(false);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function run(fn: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await fn();
    } catch (err) {
      setError(toApiError(err));
    }
    setBusy(false);
  }

  const send = () =>
    run(async () => {
      await api("/api/auth/otp/request", { method: "POST", body: { email }, auth: false });
      setResent(sent);
      setSent(true);
      setCode("");
    });

  function verify(e: FormEvent) {
    e.preventDefault();
    run(async () => {
      const res = await api<AuthResponse>(confirmPath, {
        method: "POST",
        body: { email, code: code.trim(), password, ...(product ? { product } : {}) },
        auth: false,
      });
      await onConfirmed(res);
    });
  }

  if (!sent) {
    return (
      <div className="space-y-5">
        <Notice tone="warning">{notice}</Notice>
        <ErrorBox error={error} />
        <Button loading={busy} className="w-full" onClick={send}>
          Email me a code
        </Button>
        {onBack && (
          <Button variant="ghost" className="w-full" onClick={onBack}>
            {backLabel}
          </Button>
        )}
      </div>
    );
  }

  return (
    <form onSubmit={verify} className="space-y-5">
      <Notice tone="success">
        We emailed a 6-digit code to <strong>{email}</strong>. Enter it to confirm your email. It works for 10 minutes.
        {resent ? " A new code is on its way." : ""}
      </Notice>
      {/* This step only appears after a submit or a tap, which unmounted the control that had focus. */}
      <Input
        label="6-digit code"
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern="[0-9]*"
        maxLength={6}
        required
        autoFocus
        value={code}
        onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
        className="tracking-[0.4em]"
      />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="min-h-12 w-full text-base" disabled={code.length !== 6}>
        Confirm and sign in
      </Button>
      <div className="flex flex-wrap justify-between gap-2 text-sm">
        <button type="button" className="min-h-11 font-semibold text-ink underline underline-offset-2 disabled:opacity-50" disabled={busy} onClick={send}>
          Send a new code
        </button>
        {onBack && (
          <button type="button" className="min-h-11 text-ink-2 hover:text-ink" onClick={onBack}>
            {backLabel}
          </button>
        )}
      </div>
    </form>
  );
}
