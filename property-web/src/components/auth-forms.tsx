"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, useSyncExternalStore, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { PRODUCT } from "@/lib/config";
import type { AgencyKind, AuthResponse } from "@/lib/types";
import { nextPath } from "./auth-shell";
import { Button, ErrorBox, Input, Notice } from "./ui";
import { cx, linkClass } from "./styles";

function useAuthSubmit(fallback: string) {
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
        router.replace(nextPath(fallback));
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

// ---------- Seekers ----------

export function SeekerLoginForm() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const { busy, error, run } = useAuthSubmit("/me");

  function submit(e: FormEvent) {
    e.preventDefault();
    run(() => api<AuthResponse>("/api/auth/seeker/login", { method: "POST", body: { email: email.trim(), password }, auth: false }));
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} error={error?.field("email")} />
      <Input
        label="Password"
        type="password"
        autoComplete="current-password"
        required
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        error={error?.field("password")}
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
  );
}

export function SeekerSignupForm() {
  const [form, setForm] = useState({ fullName: "", email: "", password: "" });
  const { busy, error, setError, run } = useAuthSubmit("/me");
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
      api<AuthResponse>("/api/auth/seeker/signup", {
        method: "POST",
        body: { fullName: form.fullName.trim(), email: form.email.trim(), password: form.password },
        auth: false,
      }),
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <Input label="Full name" autoComplete="name" required value={form.fullName} onChange={set("fullName")} error={error?.field("fullName")} />
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
      <ErrorBox error={error && !error.field("fullName") && !error.field("email") && !error.field("password") ? error : null} />
      <Button type="submit" loading={busy} className="w-full">
        Create my account
      </Button>
    </form>
  );
}

// ---------- Agencies, agents and landlords ----------

type LoginMode = "password" | "otp-email" | "otp-code";

export function AgencyLoginForm() {
  const [mode, setMode] = useState<LoginMode>("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const { busy, error, setError, run } = useAuthSubmit("/studio");

  const switchMode = (m: LoginMode) => {
    setError(null);
    setMode(m);
  };

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
          className={cx(linkClass, "min-h-11")}
          disabled={busy}
          onClick={() => run(async () => void (await api("/api/auth/otp/request", { method: "POST", body: { email: email.trim() }, auth: false })))}
        >
          Resend code
        </button>
        <button type="button" className="min-h-11 text-slate-600 hover:text-asphalt" onClick={() => switchMode("password")}>
          Use password
        </button>
      </div>
    </form>
  );
}

const KINDS: { value: AgencyKind; label: string; hint: string; nameLabel: string; placeholder: string }[] = [
  { value: "Agency", label: "An agency", hint: "A firm with several agents.", nameLabel: "Agency name", placeholder: "e.g. Adeyemi Realty" },
  { value: "Agent", label: "A solo agent", hint: "You work on your own.", nameLabel: "Business name", placeholder: "e.g. Tunde Bakare Properties" },
  { value: "Landlord", label: "A landlord", hint: "You let or sell your own property.", nameLabel: "Name shown on listings", placeholder: "e.g. Mrs Okafor's Flats" },
];

export function AgencySignupForm() {
  const [kind, setKind] = useState<AgencyKind>("Agency");
  const [form, setForm] = useState({ fullName: "", email: "", password: "", businessName: "" });
  const { busy, error, setError, run } = useAuthSubmit("/studio");
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));
  const current = KINDS.find((k) => k.value === kind)!;

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.fullName.trim() || !form.email.trim() || !form.businessName.trim()) {
      setError(new ApiError(400, { detail: `Enter your name, email and ${current.nameLabel.toLowerCase()}.` }));
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
          accountKind: kind,
        },
        auth: false,
      }),
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <fieldset>
        <legend className="text-sm font-medium text-slate-700">I am</legend>
        <div className="mt-1.5 grid gap-2">
          {KINDS.map((k) => (
            <label
              key={k.value}
              className={cx(
                "flex min-h-11 cursor-pointer items-start gap-3 rounded-control border p-3",
                kind === k.value ? "border-adire bg-adire-wash" : "border-line hover:bg-slate-50",
              )}
            >
              <input type="radio" name="kind" value={k.value} checked={kind === k.value} onChange={() => setKind(k.value)} className="mt-1 accent-[#33309a]" />
              <span>
                <span className="block text-sm font-semibold text-asphalt">{k.label}</span>
                <span className="block text-xs text-slate-600">{k.hint}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      <Input
        label={current.nameLabel}
        autoComplete="organization"
        required
        value={form.businessName}
        onChange={set("businessName")}
        placeholder={current.placeholder}
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
        hint="At least 8 characters. Already searching on PropertyLink? Use the same email and password."
        error={error?.field("password")}
      />
      <ErrorBox error={error && !error.field("businessName") && !error.field("fullName") && !error.field("email") && !error.field("password") ? error : null} />
      <Button type="submit" loading={busy} className="w-full">
        Create my studio
      </Button>
    </form>
  );
}
