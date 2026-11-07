"use client";

import { useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { COUNTRIES, PRODUCT } from "@/lib/config";
import type { AuthResponse, SignupRequest } from "@/lib/types";
import { Button, ErrorBox, Input, Select } from "@/components/ui";
import { nextPath } from "@/components/auth-shell";
import { ConfirmEmailStep, isSignupPending, type SignupPending } from "@/components/confirm-email";
import { useFocusFirstInvalid, useFocusOnStep } from "@/components/form-focus";

export function SignupForm() {
  const router = useRouter();
  const root = useRef<HTMLDivElement>(null);
  const [form, setForm] = useState<SignupRequest>({
    fullName: "",
    email: "",
    password: "",
    country: "NG",
    product: PRODUCT,
    businessName: "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  // Final fix pass B: the sign-up emails a 6-digit code; the account is usable once it is entered.
  const [pending, setPending] = useState<string | null>(null);
  useFocusFirstInvalid(root, error);
  useFocusOnStep(root, pending);
  const set = (k: keyof SignupRequest) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    const missing: Record<string, string[]> = {};
    if (!form.fullName.trim()) missing.fullName = ["Enter your full name."];
    if (!form.email.trim()) missing.email = ["Enter your email address."];
    if (form.password.length < 8) missing.password = ["Password must be at least 8 characters."];
    if (!form.businessName.trim()) missing.businessName = ["Enter your business name."];
    if (Object.keys(missing).length > 0) {
      setError(new ApiError(400, { detail: "Some details are missing:", errors: missing }));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const res = await api<AuthResponse | SignupPending>("/api/auth/signup", { method: "POST", body: form, auth: false });
      if (isSignupPending(res)) {
        setPending(res.email);
        setBusy(false);
        return;
      }
      setAuth(res);
      router.replace(nextPath());
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  if (pending) {
    return (
      <div ref={root}>
        <ConfirmEmailStep
          email={pending}
          password={form.password}
          confirmPath="/api/auth/signup/confirm"
          product={PRODUCT}
          onConfirmed={(res) => {
            setAuth(res);
            router.replace(nextPath());
          }}
          onBack={() => setPending(null)}
          backLabel="Change my details"
        />
      </div>
    );
  }

  return (
    <div ref={root}>
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
          hint="At least 8 characters. Already use another of our products? Use the same password."
          error={error?.field("password")}
        />
        <Input
          label="Business name"
          autoComplete="organization"
          required
          value={form.businessName}
          onChange={set("businessName")}
          placeholder="e.g. Ada Designs Ltd"
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
          Create my account
        </Button>
      </form>
    </div>
  );
}
