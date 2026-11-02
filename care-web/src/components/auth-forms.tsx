"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, useSyncExternalStore, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { PRODUCT } from "@/lib/config";
import type { AuthResponse } from "@/lib/types";
import { nextPath } from "./auth-shell";
import { Button, ErrorBox, Input, Notice } from "./ui";
import { cx, linkClass } from "./styles";

/** Runs a sign-in call, stores the session and moves on: to the path `then` returns, else `?next=` or `fallback`. */
export function useAuthSubmit(fallback: string, then?: (res: AuthResponse) => Promise<string | void> | string | void) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  async function run(fn: () => Promise<AuthResponse | void>) {
    setBusy(true);
    setError(null);
    try {
      const res = await fn();
      if (res) {
        setAuth(res);
        const to = then ? await then(res) : undefined;
        router.replace(to ?? nextPath(fallback));
        return;
      }
    } catch (err) {
      setError(toApiError(err));
    }
    setBusy(false);
  }
  return { busy, error, setError, run };
}

const noop = () => () => {};
/** A link to the other auth page that keeps `?next=`. */
export function AuthSwitchLink({ href, children }: { href: string; children: ReactNode }) {
  const search = useSyncExternalStore(
    noop,
    () => window.location.search,
    () => "",
  );
  const next = new URLSearchParams(search).get("next");
  return (
    <Link href={next ? `${href}?${new URLSearchParams({ next })}` : href} className={linkClass}>
      {children}
    </Link>
  );
}

function OrRule() {
  return (
    <div className="flex items-center gap-3 text-xs text-muted">
      <span className="h-px flex-1 bg-rule" /> or <span className="h-px flex-1 bg-rule" />
    </div>
  );
}

/**
 * Email-code sign-in: ask for the email, send a 6-digit code (`POST /api/auth/otp/request`), then verify it at `verifyPath`.
 * Staff verify at `/api/auth/otp/verify` with the product; the patient portal (Task 20) at `/api/auth/patient/otp/verify`.
 */
function EmailCode({
  verifyPath,
  product,
  email,
  setEmail,
  onBack,
  backLabel,
  submit,
}: {
  verifyPath: string;
  product?: string;
  email: string;
  setEmail: (v: string) => void;
  onBack: () => void;
  backLabel: string;
  submit: ReturnType<typeof useAuthSubmit>;
}) {
  const [sent, setSent] = useState(false);
  const [code, setCode] = useState("");
  const { busy, error, run, setError } = submit;
  const request = () => run(async () => void (await api("/api/auth/otp/request", { method: "POST", body: { email: email.trim() }, auth: false })));

  if (!sent) {
    return (
      <form
        onSubmit={(e) => {
          e.preventDefault();
          run(async () => {
            await api("/api/auth/otp/request", { method: "POST", body: { email: email.trim() }, auth: false });
            setSent(true);
          });
        }}
        className="space-y-4"
      >
        <p className="text-sm text-slate-600">We&apos;ll email you a 6-digit code. No password needed.</p>
        <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <ErrorBox error={error} />
        <Button type="submit" loading={busy} className="w-full" disabled={!email.trim()}>
          Send code
        </Button>
        <Button
          variant="ghost"
          className="w-full"
          onClick={() => {
            setError(null);
            onBack();
          }}
        >
          {backLabel}
        </Button>
      </form>
    );
  }

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        run(() =>
          api<AuthResponse>(verifyPath, { method: "POST", body: { email: email.trim(), code: code.trim(), ...(product ? { product } : {}) }, auth: false }),
        );
      }}
      className="space-y-4"
    >
      <Notice tone="success">
        If an account exists for <strong>{email.trim()}</strong>, a code is on its way. It works for 10 minutes.
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
        className="tnum text-center text-lg tracking-[0.4em]"
      />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="w-full" disabled={code.length !== 6}>
        Sign in
      </Button>
      <div className="flex justify-between text-sm">
        <button type="button" className={cx(linkClass, "min-h-11")} disabled={busy} onClick={request}>
          Send a new code
        </button>
        <button
          type="button"
          className="min-h-11 text-slate-600 hover:text-ink"
          onClick={() => {
            setSent(false);
            setCode("");
            setError(null);
          }}
        >
          Change email
        </button>
      </div>
    </form>
  );
}

// ---------- Staff ----------

/** Staff sign in: password, or an emailed code. Goes to the clinic app. */
export function StaffLoginForm() {
  const [mode, setMode] = useState<"password" | "code">("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const submit = useAuthSubmit("/app");
  const { busy, error, setError, run } = submit;

  if (mode === "code") {
    return (
      <EmailCode
        verifyPath="/api/auth/otp/verify"
        product={PRODUCT}
        email={email}
        setEmail={setEmail}
        onBack={() => setMode("password")}
        backLabel="Use my password instead"
        submit={submit}
      />
    );
  }

  return (
    <div className="space-y-4">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          run(() => api<AuthResponse>("/api/auth/login", { method: "POST", body: { email: email.trim(), password, product: PRODUCT }, auth: false }));
        }}
        className="space-y-4"
      >
        <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <Input label="Password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        <p className="-mt-2 text-right text-sm">
          <Link href="/forgot-password" className={linkClass}>
            Forgot password?
          </Link>
        </p>
        <ErrorBox error={error} />
        <Button type="submit" loading={busy} className="w-full">
          Sign in
        </Button>
      </form>
      <OrRule />
      <Button
        variant="secondary"
        className="w-full"
        onClick={() => {
          setError(null);
          setMode("code");
        }}
      >
        Email me a code
      </Button>
    </div>
  );
}

/** Medical director signup: creates the clinic (product `care`) on the Free plan with its setup checklist. */
export function ClinicSignupForm() {
  const [form, setForm] = useState({ businessName: "", fullName: "", email: "", password: "" });
  const { busy, error, setError, run } = useAuthSubmit("/app");
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.businessName.trim() || !form.fullName.trim() || !form.email.trim()) {
      setError(new ApiError(400, { detail: "Enter the clinic's name, your name and your email." }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiError(400, { errors: { password: ["Use at least 8 characters."] } }));
      return;
    }
    run(() =>
      api<AuthResponse>("/api/auth/signup", {
        method: "POST",
        body: {
          fullName: form.fullName.trim(),
          email: form.email.trim(),
          password: form.password,
          country: "NG",
          product: PRODUCT,
          businessName: form.businessName.trim(),
        },
        auth: false,
      }),
    );
  }

  const fieldError = error && (error.field("businessName") || error.field("fullName") || error.field("email") || error.field("password"));
  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <Input
        label="Clinic name"
        autoComplete="organization"
        required
        value={form.businessName}
        onChange={set("businessName")}
        placeholder="e.g. Harbour Clinic, Yaba"
        error={error?.field("businessName")}
      />
      <Input label="Your full name" autoComplete="name" required value={form.fullName} onChange={set("fullName")} error={error?.field("fullName")} />
      <Input label="Email" type="email" autoComplete="email" required value={form.email} onChange={set("email")} error={error?.field("email")} />
      <Input
        label="Password"
        type="password"
        autoComplete="new-password"
        required
        minLength={8}
        value={form.password}
        onChange={set("password")}
        hint="At least 8 characters."
        error={error?.field("password")}
      />
      <ErrorBox error={error && !fieldError ? error : null} />
      <Button type="submit" loading={busy} className="w-full">
        Create my clinic
      </Button>
      <p className="text-xs text-muted">You&apos;ll be the medical director. You can invite your doctors, nurses, front desk and cashier next.</p>
    </form>
  );
}
