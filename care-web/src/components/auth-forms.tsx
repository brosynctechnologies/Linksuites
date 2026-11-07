"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, useSyncExternalStore, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { PRODUCT } from "@/lib/config";
import type { AuthResponse } from "@/lib/types";
import { nextPath } from "./auth-shell";
import { ConfirmEmailStep, isSignupPending, isUnconfirmed, type SignupPending } from "./confirm-email";
import { Button, ErrorBox, Input, Notice } from "./ui";
import { cx, linkClass } from "./styles";

/**
 * Runs a sign-in call, stores the session and moves on: to the path `then` returns, else `?next=` or `fallback`. Final fix pass B: a
 * sign-up answers "confirm your email" (`pending`, the address the code went to) and a password login for an unconfirmed sign-up is
 * refused with the API's sentence (`unconfirmed`); the form then shows {@link ConfirmEmailStep}, which ends in `finish`.
 */
export function useAuthSubmit(fallback: string, then?: (res: AuthResponse) => Promise<string | void> | string | void) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [unconfirmed, setUnconfirmed] = useState<string | null>(null);
  async function finish(res: AuthResponse) {
    setAuth(res);
    const to = then ? await then(res) : undefined;
    router.replace(to ?? nextPath(fallback));
  }
  async function run(fn: () => Promise<AuthResponse | SignupPending | void>) {
    setBusy(true);
    setError(null);
    try {
      const res = await fn();
      if (isSignupPending(res)) setPending(res.email);
      else if (res) {
        await finish(res);
        return;
      }
    } catch (err) {
      if (isUnconfirmed(err)) setUnconfirmed(err.message);
      else setError(toApiError(err));
    }
    setBusy(false);
  }
  return { busy, error, setError, run, finish, pending, setPending, unconfirmed, setUnconfirmed };
}

/** After a failed submit, moves focus to the form's first invalid field (the error summary above the button is announced either way). */
function useFocusInvalid(error: ApiError | null) {
  const ref = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (!error) return;
    const frame = requestAnimationFrame(() => ref.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
    return () => cancelAnimationFrame(frame);
  }, [error]);
  return ref;
}

/** One message per failed check, shaped like the API's field errors, so each field is marked and the summary lists them all. */
function fieldErrors(checks: [field: string, failed: boolean, message: string][]): ApiError | null {
  const errors: Record<string, string[]> = {};
  for (const [field, failed, message] of checks) if (failed) errors[field] = [message];
  return Object.keys(errors).length ? new ApiError(400, { errors }) : null;
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
  focusFirst,
}: {
  verifyPath: string;
  product?: string;
  email: string;
  setEmail: (v: string) => void;
  onBack: () => void;
  backLabel: string;
  submit: ReturnType<typeof useAuthSubmit>;
  /** Set when the visitor switched to this step, so focus follows them; not on the page's first view. */
  focusFirst?: boolean;
}) {
  const [sent, setSent] = useState(false);
  const [changed, setChanged] = useState(false);
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
        <Input
          label="Email"
          type="email"
          autoComplete="email"
          required
          autoFocus={focusFirst || changed}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
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
        autoFocus
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
            setChanged(true);
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
  // After any switch between steps, the new step's first field takes focus.
  const [switched, setSwitched] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const submit = useAuthSubmit("/app");
  const { busy, error, setError, run } = submit;

  if (submit.unconfirmed) {
    return (
      <ConfirmEmailStep
        email={email.trim()}
        password={password}
        confirmPath="/api/auth/signup/confirm"
        product={PRODUCT}
        notice={submit.unconfirmed}
        onConfirmed={submit.finish}
        onBack={() => {
          setSwitched(true);
          submit.setUnconfirmed(null);
        }}
        backLabel="Back to sign in"
      />
    );
  }

  if (mode === "code") {
    return (
      <EmailCode
        verifyPath="/api/auth/otp/verify"
        product={PRODUCT}
        email={email}
        setEmail={setEmail}
        onBack={() => {
          setSwitched(true);
          setMode("password");
        }}
        backLabel="Use my password instead"
        submit={submit}
        focusFirst={switched}
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
        <Input label="Email" type="email" autoComplete="email" required autoFocus={switched} value={email} onChange={(e) => setEmail(e.target.value)} />
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
          setSwitched(true);
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
  const [returned, setReturned] = useState(false);
  const { busy, error, setError, run, finish, pending, setPending } = useAuthSubmit("/app");
  const formRef = useFocusInvalid(error);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    const invalid = fieldErrors([
      ["businessName", !form.businessName.trim(), "Enter the clinic's name."],
      ["fullName", !form.fullName.trim(), "Enter your full name."],
      ["email", !form.email.trim(), "Enter your email."],
      ["password", form.password.length < 8, "Use at least 8 characters for your password."],
    ]);
    if (invalid) {
      setError(invalid);
      return;
    }
    run(() =>
      api<AuthResponse | SignupPending>("/api/auth/signup", {
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

  if (pending) {
    return (
      <ConfirmEmailStep
        email={pending}
        password={form.password}
        confirmPath="/api/auth/signup/confirm"
        product={PRODUCT}
        onConfirmed={finish}
        onBack={() => {
          setReturned(true);
          setPending(null);
        }}
        backLabel="Change my details"
      />
    );
  }

  return (
    <form ref={formRef} onSubmit={submit} className="space-y-4" noValidate>
      <Input
        label="Clinic name"
        autoComplete="organization"
        required
        autoFocus={returned}
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
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="w-full">
        Create my clinic
      </Button>
      <p className="text-xs text-muted">You&apos;ll be the medical director. You can invite your doctors, nurses, front desk and cashier next.</p>
    </form>
  );
}

// ---------- Patient portal (Task 20) ----------

/**
 * Patient portal sign in: an emailed code (the default: it also confirms the email, which an emailed invite needs) or a password. Both
 * go to `/api/auth/patient`, the shared customer accounts; a staff login here is refused by the portal routes, not by sign in.
 */
export function PatientSignInForm({ onSignedIn }: { onSignedIn?: (res: AuthResponse) => Promise<string | void> | string | void }) {
  const [mode, setMode] = useState<"code" | "password">("code");
  // After any switch between steps, the new step's first field takes focus.
  const [switched, setSwitched] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const submit = useAuthSubmit("/portal", onSignedIn);
  const { busy, error, setError, run } = submit;

  if (submit.unconfirmed) {
    return (
      <ConfirmEmailStep
        email={email.trim()}
        password={password}
        confirmPath="/api/auth/patient/signup/confirm"
        notice={submit.unconfirmed}
        onConfirmed={submit.finish}
        onBack={() => {
          setSwitched(true);
          submit.setUnconfirmed(null);
        }}
        backLabel="Back to sign in"
      />
    );
  }

  if (mode === "code") {
    return (
      <EmailCode
        verifyPath="/api/auth/patient/otp/verify"
        email={email}
        setEmail={setEmail}
        onBack={() => {
          setSwitched(true);
          setMode("password");
        }}
        backLabel="Use a password instead"
        submit={submit}
        focusFirst={switched}
      />
    );
  }

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        run(() => api<AuthResponse>("/api/auth/patient/login", { method: "POST", body: { email: email.trim(), password }, auth: false }));
      }}
      className="space-y-4"
    >
      <Input label="Email" type="email" autoComplete="email" required autoFocus={switched} value={email} onChange={(e) => setEmail(e.target.value)} />
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
      <Button
        variant="ghost"
        className="w-full"
        onClick={() => {
          setError(null);
          setSwitched(true);
          setMode("code");
        }}
      >
        Email me a code instead
      </Button>
    </form>
  );
}

/**
 * A new patient portal account (`POST /api/auth/patient/signup`). It sees nothing until the clinic's invite is accepted. Final fix pass B:
 * the emailed code comes next (`/api/auth/patient/signup/confirm`), which confirms the email, so an emailed invite accepts straight after
 * and the chosen password keeps working.
 */
export function PatientSignupForm({ onSignedIn }: { onSignedIn?: (res: AuthResponse) => Promise<string | void> | string | void }) {
  const [form, setForm] = useState({ fullName: "", email: "", password: "" });
  const [returned, setReturned] = useState(false);
  const { busy, error, setError, run, finish, pending, setPending } = useAuthSubmit("/portal", onSignedIn);
  const formRef = useFocusInvalid(error);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    const invalid = fieldErrors([
      ["fullName", !form.fullName.trim(), "Enter your full name."],
      ["email", !form.email.trim(), "Enter your email."],
      ["password", form.password.length < 8, "Use at least 8 characters for your password."],
    ]);
    if (invalid) {
      setError(invalid);
      return;
    }
    run(() =>
      api<AuthResponse | SignupPending>("/api/auth/patient/signup", {
        method: "POST",
        body: { fullName: form.fullName.trim(), email: form.email.trim(), password: form.password },
        auth: false,
      }),
    );
  }

  if (pending) {
    return (
      <ConfirmEmailStep
        email={pending}
        password={form.password}
        confirmPath="/api/auth/patient/signup/confirm"
        onConfirmed={finish}
        onBack={() => {
          setReturned(true);
          setPending(null);
        }}
        backLabel="Change my details"
      />
    );
  }

  return (
    <form ref={formRef} onSubmit={submit} className="space-y-4" noValidate>
      <Input
        label="Your full name"
        autoComplete="name"
        required
        autoFocus={returned}
        value={form.fullName}
        onChange={set("fullName")}
        error={error?.field("fullName")}
      />
      <Input label="Email" type="email" autoComplete="email" required value={form.email} onChange={set("email")} error={error?.field("email")} />
      <Input
        label="Choose a password"
        type="password"
        autoComplete="new-password"
        required
        minLength={8}
        hint="At least 8 characters. You can also sign in with an emailed code."
        value={form.password}
        onChange={set("password")}
        error={error?.field("password")}
      />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="w-full">
        Create my account
      </Button>
    </form>
  );
}
