"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, useSyncExternalStore, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { rememberSchool, setAuth } from "@/lib/auth";
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
 * Staff verify at `/api/auth/otp/verify` with the product; parents at `/api/auth/parent/otp/verify`.
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

/** Staff sign in: password, or an emailed code. Goes to the school app. */
export function StaffLoginForm({ schoolSlug }: { schoolSlug?: string }) {
  const [mode, setMode] = useState<"password" | "code">("password");
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
        onBack={() => submit.setUnconfirmed(null)}
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
          {schoolSlug ? "Sign in as staff" : "Sign in"}
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

/** Proprietor signup: creates the school (product `school`) with its current session and three terms. */
export function SchoolSignupForm() {
  const [form, setForm] = useState({ businessName: "", fullName: "", email: "", password: "" });
  const { busy, error, setError, run, finish, pending, setPending } = useAuthSubmit("/app");
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.businessName.trim() || !form.fullName.trim() || !form.email.trim()) {
      setError(new ApiError(400, { detail: "Enter the school's name, your name and your email." }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiError(400, { errors: { password: ["Use at least 8 characters."] } }));
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
        onBack={() => setPending(null)}
        backLabel="Change my details"
      />
    );
  }

  const fieldError = error && (error.field("businessName") || error.field("fullName") || error.field("email") || error.field("password"));
  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <Input
        label="School name"
        autoComplete="organization"
        required
        value={form.businessName}
        onChange={set("businessName")}
        placeholder="e.g. Greenfield Academy, Ikeja"
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
        Create my school
      </Button>
      <p className="text-xs text-muted">You&apos;ll be the proprietor. You can invite your principal, bursar and teachers next.</p>
    </form>
  );
}

// ---------- Parents ----------

/** Parent sign in: an emailed code (the default, no password to remember) or a password. */
export function ParentSignInForm({ schoolSlug, onSignedIn }: { schoolSlug?: string; onSignedIn?: (res: AuthResponse) => Promise<string | void> | string | void }) {
  const [mode, setMode] = useState<"code" | "password">("code");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const submit = useAuthSubmit(
    "/parent",
    onSignedIn ??
      (schoolSlug
        ? () => {
            rememberSchool(schoolSlug);
            return "/parent";
          }
        : undefined),
  );
  const { busy, error, setError, run } = submit;

  if (submit.unconfirmed) {
    return (
      <ConfirmEmailStep
        email={email.trim()}
        password={password}
        confirmPath="/api/auth/parent/signup/confirm"
        notice={submit.unconfirmed}
        onConfirmed={submit.finish}
        onBack={() => submit.setUnconfirmed(null)}
        backLabel="Back to sign in"
      />
    );
  }

  if (mode === "code") {
    return (
      <div className="space-y-4">
        <EmailCode
          verifyPath="/api/auth/parent/otp/verify"
          email={email}
          setEmail={setEmail}
          onBack={() => setMode("password")}
          backLabel="Use a password instead"
          submit={submit}
        />
      </div>
    );
  }

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        run(() => api<AuthResponse>("/api/auth/parent/login", { method: "POST", body: { email: email.trim(), password }, auth: false }));
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
      <Button
        variant="ghost"
        className="w-full"
        onClick={() => {
          setError(null);
          setMode("code");
        }}
      >
        Email me a code instead
      </Button>
    </form>
  );
}

/**
 * A new parent account (used on the invite page when the parent has none yet). Final fix pass B: the emailed code comes next
 * (`/api/auth/parent/signup/confirm`); the account is usable once it is entered.
 */
export function ParentSignupForm({ onSignedIn }: { onSignedIn: (res: AuthResponse) => Promise<string | void> | string | void }) {
  const [form, setForm] = useState({ fullName: "", email: "", password: "" });
  const { busy, error, setError, run, finish, pending, setPending } = useAuthSubmit("/parent", onSignedIn);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.fullName.trim() || !form.email.trim()) {
      setError(new ApiError(400, { detail: "Enter your name and email." }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiError(400, { errors: { password: ["Use at least 8 characters."] } }));
      return;
    }
    run(() =>
      api<AuthResponse | SignupPending>("/api/auth/parent/signup", {
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
        confirmPath="/api/auth/parent/signup/confirm"
        onConfirmed={finish}
        onBack={() => setPending(null)}
        backLabel="Change my details"
      />
    );
  }

  const fieldError = error && (error.field("fullName") || error.field("email") || error.field("password"));
  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <Input label="Your full name" autoComplete="name" required value={form.fullName} onChange={set("fullName")} error={error?.field("fullName")} />
      <Input label="Email" type="email" autoComplete="email" required value={form.email} onChange={set("email")} error={error?.field("email")} />
      <Input
        label="Choose a password"
        type="password"
        autoComplete="new-password"
        required
        minLength={8}
        hint="At least 8 characters. Next time you can also sign in with an emailed code."
        value={form.password}
        onChange={set("password")}
        error={error?.field("password")}
      />
      <ErrorBox error={error && !fieldError ? error : null} />
      <Button type="submit" loading={busy} className="w-full">
        Create my parent account
      </Button>
    </form>
  );
}

// ---------- Students ----------

/** Students of a level with logins (Primary, JSS and SSS on the template): admission number and the password the school set. */
export function StudentSignInForm({ schoolSlug }: { schoolSlug: string }) {
  const [admissionNo, setAdmissionNo] = useState("");
  const [password, setPassword] = useState("");
  const { busy, error, run } = useAuthSubmit("/student", () => {
    rememberSchool(schoolSlug);
    return "/student";
  });

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        run(() =>
          api<AuthResponse>(`/api/school/${encodeURIComponent(schoolSlug)}/student-login`, {
            method: "POST",
            body: { admissionNo: admissionNo.trim(), password },
            auth: false,
          }),
        );
      }}
      className="space-y-4"
    >
      <Input
        label="Admission number"
        autoComplete="username"
        autoCapitalize="characters"
        spellCheck={false}
        required
        value={admissionNo}
        onChange={(e) => setAdmissionNo(e.target.value)}
        error={error?.field("admissionNo")}
      />
      <Input
        label="Password"
        type="password"
        autoComplete="current-password"
        required
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        error={error?.field("password")}
      />
      <ErrorBox error={error && !error.field("admissionNo") && !error.field("password") ? error : null} />
      <Button type="submit" loading={busy} className="w-full">
        Sign in
      </Button>
      <p className="text-xs text-muted">For primary and secondary students. Forgot your password? Ask your class teacher or the school office to reset it.</p>
    </form>
  );
}
