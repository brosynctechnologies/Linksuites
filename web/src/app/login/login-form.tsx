"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import type { AuthResponse } from "@/lib/types";
import { Button, ErrorBox, Input, Notice } from "@/components/ui";
import { nextPath } from "@/components/auth-shell";
import { ConfirmEmailStep, isUnconfirmed } from "@/components/confirm-email";

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

  // Switching between password and code unmounts the button that had focus, so move focus to the new step's first control.
  const box = useRef<HTMLDivElement>(null);
  const shownMode = useRef(mode);
  useEffect(() => {
    if (shownMode.current === mode) return;
    shownMode.current = mode;
    box.current?.querySelector<HTMLElement>("input, button")?.focus();
  }, [mode]);

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
    run(async () => done(await api<AuthResponse>("/api/auth/login", { method: "POST", body: { email, password, product: "whatsapp-orders" }, auth: false })));
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
      done(await api<AuthResponse>("/api/auth/otp/verify", { method: "POST", body: { email, code: code.trim(), product: "whatsapp-orders" }, auth: false })),
    );
  }

  const switchMode = (m: Mode) => {
    setError(null);
    setMode(m);
  };

  const step = (node: ReactNode) => <div ref={box}>{node}</div>;

  if (mode === "confirm") {
    return step(
      <ConfirmEmailStep
        email={email.trim()}
        password={password}
        confirmPath="/api/auth/signup/confirm"
        product={"whatsapp-orders"}
        notice={notice}
        onConfirmed={done}
        onBack={() => switchMode("password")}
        backLabel="Back to log in"
      />
    );
  }

  if (mode === "password") {
    return step(
      <div className="space-y-5">
        <form onSubmit={submitPassword} className="space-y-5">
          <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Input
            label="Password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <p className="-mt-3 text-right text-sm">
            <Link href="/forgot-password" className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
              Forgot password?
            </Link>
          </p>
          <ErrorBox error={error} />
          <Button type="submit" loading={busy} className="min-h-12 w-full text-base">
            Log in
          </Button>
        </form>
        <div className="flex items-center gap-3 text-xs text-subtle">
          <span className="h-px flex-1 bg-line" /> or <span className="h-px flex-1 bg-line" />
        </div>
        <Button variant="secondary" className="w-full" onClick={() => switchMode("otp-email")}>
          Email me a code
        </Button>
      </div>
    );
  }

  if (mode === "otp-email") {
    return step(
      <form onSubmit={requestCode} className="space-y-5">
        <p className="text-sm text-ink-2">We&apos;ll email you a 6-digit code to log in without a password.</p>
        <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <ErrorBox error={error} />
        <Button type="submit" loading={busy} className="min-h-12 w-full text-base" disabled={!email}>
          Send code
        </Button>
        <Button variant="ghost" className="w-full" onClick={() => switchMode("password")}>
          Use password instead
        </Button>
      </form>
    );
  }

  return step(
    <form onSubmit={verifyCode} className="space-y-5">
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
        className="tracking-[0.4em]"
      />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="min-h-12 w-full text-base" disabled={code.length !== 6}>
        Verify and log in
      </Button>
      <div className="flex flex-wrap justify-between gap-2 text-sm">
        <button type="button" className="min-h-11 font-semibold text-ink underline underline-offset-2 disabled:opacity-50" onClick={() => requestCode()} disabled={busy}>
          Resend code
        </button>
        <button type="button" className="min-h-11 text-ink-2 hover:text-ink" onClick={() => switchMode("password")}>
          Use password
        </button>
      </div>
    </form>
  );
}
