"use client";

import Link from "next/link";
import { useRef, useState, type ClipboardEvent, type FormEvent, type KeyboardEvent, type ReactNode } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { maskEmail } from "@/lib/format";
import { setSession, useEndedNote } from "@/lib/session";
import type { AuthResponse } from "@/lib/types";
import { Button, ErrorPanel, Input, cx } from "./ui";

/** Email + password, then the 6-digit code emailed to the admin. */
export function SignIn({ onSignedIn }: { onSignedIn?: () => void }) {
  const ended = useEndedNote();
  const [step, setStep] = useState<"password" | "code">("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [challengeId, setChallengeId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [resent, setResent] = useState(false);

  async function requestCode() {
    const res = await api<{ challengeId: string }>("/api/platform/auth/login", { method: "POST", body: { email: email.trim(), password }, auth: false });
    setChallengeId(res.challengeId);
  }

  async function submitPassword(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await requestCode();
      setStep("code");
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function verify(code: string) {
    setBusy(true);
    setError(null);
    try {
      const res = await api<AuthResponse>("/api/platform/auth/verify", { method: "POST", body: { challengeId, code }, auth: false });
      if (!res.user?.platformRole) {
        setError(toApiError(new Error("This account is not a platform admin.")));
        return false;
      }
      setPassword("");
      setSession(res);
      onSignedIn?.();
      return true;
    } catch (err) {
      setError(toApiError(err));
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function resend() {
    setBusy(true);
    setError(null);
    setResent(false);
    try {
      await requestCode();
      setResent(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthFrame title="Sign in" subtitle={<>For the people who run the platform. Customer accounts can&apos;t sign in here.</>}>
      {ended && step === "password" && (
        <p className="mt-4 rounded-control border border-wait/30 bg-wait-wash px-3 py-2 text-sm font-medium text-wait" role="status">
          {ended}
        </p>
      )}

      {step === "password" ? (
        <form onSubmit={submitPassword} className="mt-5 space-y-4" noValidate>
          <Input
            label="Email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={error?.field("email")}
            required
          />
          <Input
            label="Password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={error?.field("password")}
            required
          />
          <p className="-mt-2 text-sm">
            <Link href="/forgot-password" className="inline-flex min-h-10 items-center text-muted underline underline-offset-2 hover:text-graphite">
              Forgot password?
            </Link>
          </p>
          {error && !error.field("email") && !error.field("password") && <ErrorPanel error={error} />}
          <Button type="submit" className="w-full" loading={busy} disabled={!email.trim() || !password}>
            Continue
          </Button>
        </form>
      ) : (
        <div className="mt-5 space-y-4">
          <p className="text-sm">
            Code sent to <span className="font-medium">{maskEmail(email.trim())}</span>. It works once.
          </p>
          <CodeInput disabled={busy} onComplete={verify} invalid={!!error} />
          {error && <ErrorPanel error={error} />}
          {resent && !error && (
            <p className="text-sm text-ok" role="status">
              A new code is on its way.
            </p>
          )}
          <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
            <button type="button" className="min-h-10 font-medium underline underline-offset-2" onClick={resend} disabled={busy}>
              Send a new code
            </button>
            <button
              type="button"
              className="min-h-10 text-muted underline underline-offset-2"
              onClick={() => {
                setStep("password");
                setError(null);
                setResent(false);
              }}
            >
              Use another account
            </button>
          </div>
        </div>
      )}
    </AuthFrame>
  );
}

/** The signed-out page: the console bar (without a name) and one centred card. */
export function AuthFrame({ title, subtitle, children }: { title: string; subtitle?: ReactNode; children: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col bg-concrete">
      <div className="flex h-9 items-center gap-3 bg-caution pr-4 text-sm font-semibold text-graphite">
        <span className="caution-stripe h-full w-10" aria-hidden="true" />
        Admin console
      </div>
      <main className="flex flex-1 items-start justify-center px-4 py-12 sm:items-center">
        <div className="w-full max-w-sm rounded-panel border border-line bg-surface p-6">
          <h1 className="text-xl font-semibold">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-muted">{subtitle}</p>}
          {children}
        </div>
      </main>
    </div>
  );
}

/** Six boxes; typing advances, backspace goes back, pasting fills all. Submits when the sixth digit lands. */
function CodeInput({ onComplete, disabled, invalid }: { onComplete: (code: string) => Promise<boolean>; disabled?: boolean; invalid?: boolean }) {
  const [digits, setDigits] = useState<string[]>(Array(6).fill(""));
  const refs = useRef<(HTMLInputElement | null)[]>([]);

  async function update(next: string[], focusIndex: number) {
    setDigits(next);
    refs.current[Math.min(5, focusIndex)]?.focus();
    if (next.every((d) => d !== "")) {
      const ok = await onComplete(next.join(""));
      if (!ok) {
        setDigits(Array(6).fill(""));
        refs.current[0]?.focus();
      }
    }
  }

  function onChange(i: number, value: string) {
    const clean = value.replace(/\D/g, "");
    if (!clean) {
      const next = [...digits];
      next[i] = "";
      setDigits(next);
      return;
    }
    if (clean.length > 1) {
      fill(i, clean);
      return;
    }
    const next = [...digits];
    next[i] = clean;
    void update(next, i + 1);
  }

  function fill(start: number, text: string) {
    const clean = text.replace(/\D/g, "").slice(0, 6 - start);
    if (!clean) return;
    const next = [...digits];
    for (let k = 0; k < clean.length; k++) next[start + k] = clean[k];
    void update(next, start + clean.length);
  }

  function onPaste(i: number, e: ClipboardEvent<HTMLInputElement>) {
    e.preventDefault();
    const text = e.clipboardData.getData("text");
    // A full code pasted anywhere fills every box from the first.
    fill(text.replace(/\D/g, "").length >= 6 ? 0 : i, text);
  }

  function onKeyDown(i: number, e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Backspace" && !digits[i] && i > 0) {
      e.preventDefault();
      const next = [...digits];
      next[i - 1] = "";
      setDigits(next);
      refs.current[i - 1]?.focus();
    } else if (e.key === "ArrowLeft" && i > 0) {
      refs.current[i - 1]?.focus();
    } else if (e.key === "ArrowRight" && i < 5) {
      refs.current[i + 1]?.focus();
    }
  }

  return (
    <fieldset>
      <legend className="mb-2 text-sm font-medium">6-digit code</legend>
      <div className="flex gap-2">
        {digits.map((d, i) => (
          <input
            key={i}
            ref={(el) => {
              refs.current[i] = el;
            }}
            value={d}
            onChange={(e) => onChange(i, e.target.value)}
            onPaste={(e) => onPaste(i, e)}
            onKeyDown={(e) => onKeyDown(i, e)}
            onFocus={(e) => e.target.select()}
            inputMode="numeric"
            autoComplete={i === 0 ? "one-time-code" : "off"}
            autoFocus={i === 0}
            disabled={disabled}
            aria-label={`Digit ${i + 1} of 6`}
            aria-invalid={invalid || undefined}
            className={cx(
              "tnum h-12 w-full min-w-0 rounded-control border bg-surface text-center font-mono text-xl",
              invalid ? "border-stop" : "border-line",
            )}
          />
        ))}
      </div>
    </fieldset>
  );
}
