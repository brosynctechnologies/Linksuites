"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import type { AuthResponse } from "@/lib/types";
import { Button, ErrorBox, Input, Notice, linkClass } from "@/components/ui";
import { nextPath } from "@/components/auth-shell";
import { ConfirmEmailStep, isUnconfirmed } from "@/components/confirm-email";
import { PRODUCT } from "@/lib/config";

type Mode = "password" | "otp-email" | "otp-code" | "confirm";

export function LoginForm() {
  const router = useRouter();
  const [mode, setMode] = useState<Mode>("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  // Final fix pass B: a sign-up whose emailed code was never entered is told to confirm first (403 email_unconfirmed).
  const [notice, setNotice] = useState("");
  // The button that switched mode unmounts, so focus moves to the new step's first field (not on first load).
  const stepId = useId();
  const firstMode = useRef(true);
  useEffect(() => {
    if (firstMode.current) {
      firstMode.current = false;
      return;
    }
    document.getElementById(stepId)?.querySelector<HTMLElement>("input:not([disabled]), button:not([disabled])")?.focus();
  }, [mode, stepId]);

  function done(res: AuthResponse) {
    setAuth(res);
    router.replace(nextPath());
  }

  async function run(fn: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await fn();
    } catch (err) {
      if (isUnconfirmed(err)) {
        setNotice(err.message);
        setMode("confirm");
      } else setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  function submitPassword(e: FormEvent) {
    e.preventDefault();
    run(async () => done(await api<AuthResponse>("/api/auth/login", { method: "POST", body: { email, password, product: PRODUCT }, auth: false })));
  }

  function requestCode(e?: FormEvent) {
    e?.preventDefault();
    run(async () => {
      await api("/api/auth/otp/request", { method: "POST", body: { email }, auth: false });
      setMode("otp-code");
    });
  }

  function verifyCode(e: FormEvent) {
    e.preventDefault();
    run(async () =>
      done(await api<AuthResponse>("/api/auth/otp/verify", { method: "POST", body: { email, code: code.trim(), product: PRODUCT }, auth: false })),
    );
  }

  const switchMode = (m: Mode) => {
    setError(null);
    setMode(m);
  };

  if (mode === "confirm") {
    return (
      <div id={stepId}>
        <ConfirmEmailStep
          email={email.trim()}
          password={password}
          confirmPath="/api/auth/signup/confirm"
          product={PRODUCT}
          notice={notice}
          onConfirmed={done}
          onBack={() => switchMode("password")}
          backLabel="Back to log in"
        />
      </div>
    );
  }

  if (mode === "password") {
    return (
      <div id={stepId} className="space-y-4">
        <form onSubmit={submitPassword} className="space-y-4">
          <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Input
            label="Password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <p className="-mt-2 text-right text-sm">
            <Link href="/forgot-password" className={linkClass}>
              Forgot password?
            </Link>
          </p>
          <ErrorBox error={error} />
          <Button type="submit" loading={busy} className="w-full">
            Log in
          </Button>
        </form>
        <div className="flex items-center gap-3 text-xs text-muted">
          <span className="h-px flex-1 bg-line" /> or <span className="h-px flex-1 bg-line" />
        </div>
        <Button variant="secondary" className="w-full" onClick={() => switchMode("otp-email")}>
          Email me a code
        </Button>
      </div>
    );
  }

  if (mode === "otp-email") {
    return (
      <form id={stepId} onSubmit={requestCode} className="space-y-4">
        <p className="text-sm text-slate-600">We&apos;ll email you a 6-digit code to log in without a password.</p>
        <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <ErrorBox error={error} />
        <Button type="submit" loading={busy} className="w-full" disabled={!email}>
          Send code
        </Button>
        <Button variant="ghost" className="w-full" onClick={() => switchMode("password")}>
          Use password instead
        </Button>
      </form>
    );
  }

  return (
    <form id={stepId} onSubmit={verifyCode} className="space-y-4">
      <Notice tone="success">
        If an account exists for <strong>{email}</strong>, a code is on its way.
      </Notice>
      <Input
        label="6-digit code"
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern="[0-9]*"
        maxLength={6}
        required
        value={code}
        onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
        className="text-center text-lg tracking-[0.4em] tabular-nums"
      />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="w-full" disabled={code.length !== 6}>
        Verify and log in
      </Button>
      <div className="flex justify-between text-sm">
        <button type="button" className="font-semibold text-rose underline-offset-2 hover:underline" onClick={() => requestCode()} disabled={busy}>
          Resend code
        </button>
        <button type="button" className="text-slate-600 hover:text-aubergine" onClick={() => switchMode("password")}>
          Use password
        </button>
      </div>
    </form>
  );
}
