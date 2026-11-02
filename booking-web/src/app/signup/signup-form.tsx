"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { COUNTRIES, PRODUCT } from "@/lib/config";
import type { AuthResponse, SignupRequest } from "@/lib/types";
import { Button, ErrorBox, Input, Select } from "@/components/ui";
import { nextPath } from "@/components/auth-shell";

export function SignupForm() {
  const router = useRouter();
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
  const set = (k: keyof SignupRequest) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.fullName.trim() || !form.email.trim() || !form.businessName.trim()) {
      setError(new ApiError(400, { detail: "Please fill in your name, email and business name." }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiError(400, { detail: "Password must be at least 8 characters." }));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const res = await api<AuthResponse>("/api/auth/signup", { method: "POST", body: form, auth: false });
      setAuth(res);
      router.replace(nextPath());
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
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
        hint="At least 8 characters. Already use another of our products? Use the same password."
        error={error?.field("password")}
      />
      <Input
        label="Business name"
        autoComplete="organization"
        required
        value={form.businessName}
        onChange={set("businessName")}
        placeholder="e.g. Kemi’s Hair Studio"
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
  );
}
