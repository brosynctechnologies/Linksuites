"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { COUNTRIES, PRODUCT } from "@/lib/config";
import type { AuthResponse, TutorSignupRequest } from "@/lib/types";
import { nextPath } from "./auth-shell";
import { ConfirmEmailStep, isSignupPending, isUnconfirmed, type SignupPending } from "./confirm-email";
import { Button, ErrorBox, Input, Notice, Select, linkClass } from "./ui";

/**
 * Runs a sign-in call, stores the session and moves on (`?next=` or `fallback`). Final fix pass B: a sign-up answers "confirm your email"
 * (`pending`, the address the code went to) and a password login for an unconfirmed sign-up is refused with the API's sentence
 * (`unconfirmed`); the form then shows {@link ConfirmEmailStep}, which ends in `finish`.
 */
function useAuthSubmit(fallback: string) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [unconfirmed, setUnconfirmed] = useState<string | null>(null);
  function finish(res: AuthResponse) {
    setAuth(res);
    router.replace(nextPath(fallback));
  }
  async function run(fn: () => Promise<AuthResponse | SignupPending | void>) {
    setBusy(true);
    setError(null);
    try {
      const res = await fn();
      if (isSignupPending(res)) setPending(res.email);
      else if (res) {
        finish(res);
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

// ---------- Learners ----------

export function LearnerLoginForm() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const { busy, error, run, finish, unconfirmed, setUnconfirmed } = useAuthSubmit("/learn");

  function submit(e: FormEvent) {
    e.preventDefault();
    run(() => api<AuthResponse>("/api/auth/learner/login", { method: "POST", body: { email: email.trim(), password }, auth: false }));
  }

  if (unconfirmed) {
    return (
      <ConfirmEmailStep
        email={email.trim()}
        password={password}
        confirmPath="/api/auth/learner/signup/confirm"
        notice={unconfirmed}
        onConfirmed={finish}
        onBack={() => setUnconfirmed(null)}
        backLabel="Back to log in"
      />
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      <Input label="Password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
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
  );
}

export function LearnerSignupForm() {
  const [form, setForm] = useState({ fullName: "", email: "", password: "" });
  const { busy, error, setError, run, finish, pending, setPending } = useAuthSubmit("/learn");
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.fullName.trim() || !form.email.trim()) {
      setError(new ApiError(400, { detail: "Enter your name and email." }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiError(400, { detail: "Password must be at least 8 characters." }));
      return;
    }
    run(() =>
      api<AuthResponse | SignupPending>("/api/auth/learner/signup", {
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
        confirmPath="/api/auth/learner/signup/confirm"
        onConfirmed={finish}
        onBack={() => setPending(null)}
        backLabel="Change my details"
      />
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <Input
        label="Full name"
        autoComplete="name"
        required
        value={form.fullName}
        onChange={set("fullName")}
        hint="As it should appear on your certificates."
        error={error?.field("fullName")}
      />
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
        Create my account
      </Button>
    </form>
  );
}

// ---------- Tutors ----------

type TutorMode = "password" | "otp-email" | "otp-code";

export function TutorLoginForm() {
  const [mode, setMode] = useState<TutorMode>("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const { busy, error, setError, run, finish, unconfirmed, setUnconfirmed } = useAuthSubmit("/studio");

  const switchMode = (m: TutorMode) => {
    setError(null);
    setMode(m);
  };

  if (unconfirmed) {
    return (
      <ConfirmEmailStep
        email={email.trim()}
        password={password}
        confirmPath="/api/auth/signup/confirm"
        product={PRODUCT}
        notice={unconfirmed}
        onConfirmed={finish}
        onBack={() => setUnconfirmed(null)}
        backLabel="Back to log in"
      />
    );
  }

  if (mode === "password") {
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
            Log in to the studio
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
      <form
        onSubmit={(e) => {
          e.preventDefault();
          run(async () => {
            await api("/api/auth/otp/request", { method: "POST", body: { email: email.trim() }, auth: false });
            setMode("otp-code");
          });
        }}
        className="space-y-4"
      >
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
    <form
      onSubmit={(e) => {
        e.preventDefault();
        run(() => api<AuthResponse>("/api/auth/otp/verify", { method: "POST", body: { email: email.trim(), code: code.trim(), product: PRODUCT }, auth: false }));
      }}
      className="space-y-4"
    >
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
        <button
          type="button"
          className="font-semibold text-lagoon-text underline-offset-2 hover:underline"
          disabled={busy}
          onClick={() => run(async () => void (await api("/api/auth/otp/request", { method: "POST", body: { email: email.trim() }, auth: false })))}
        >
          Resend code
        </button>
        <button type="button" className="text-slate-600 hover:text-ink" onClick={() => switchMode("password")}>
          Use password
        </button>
      </div>
    </form>
  );
}

export function TutorSignupForm({ defaultName, defaultEmail }: { defaultName?: string; defaultEmail?: string }) {
  const [form, setForm] = useState<TutorSignupRequest>({
    fullName: defaultName ?? "",
    email: defaultEmail ?? "",
    password: "",
    country: "NG",
    product: PRODUCT,
    businessName: "",
  });
  const { busy, error, setError, run, finish, pending, setPending } = useAuthSubmit("/studio");
  const set = (k: keyof TutorSignupRequest) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.fullName.trim() || !form.email.trim() || !form.businessName.trim()) {
      setError(new ApiError(400, { detail: "Enter your name, email and academy name." }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiError(400, { detail: "Password must be at least 8 characters." }));
      return;
    }
    run(() =>
      api<AuthResponse | SignupPending>("/api/auth/signup", {
        method: "POST",
        body: { ...form, fullName: form.fullName.trim(), email: form.email.trim(), businessName: form.businessName.trim() },
        auth: false,
      }),
    );
  }

  // A learner adding a studio with their password is signed in at once; a new address confirms the emailed code first.
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

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
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
        hint="At least 8 characters. Already learning on LearnLink? Use the same email and password."
        error={error?.field("password")}
      />
      <Input
        label="Academy name"
        autoComplete="organization"
        required
        value={form.businessName}
        onChange={set("businessName")}
        placeholder="e.g. Tolu's JAMB Physics Class"
        hint="Your academy page and certificates use this name. You can change it later."
        error={error?.field("businessName")}
      />
      <Select label="Country" value={form.country} onChange={set("country")}>
        {COUNTRIES.map((c) => (
          <option key={c.code} value={c.code}>
            {c.name}
          </option>
        ))}
      </Select>
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="w-full">
        Create my academy
      </Button>
    </form>
  );
}
