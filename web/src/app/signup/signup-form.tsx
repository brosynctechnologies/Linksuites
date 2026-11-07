"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { setAuth } from "@/lib/auth";
import { COUNTRIES } from "@/lib/config";
import type { AuthResponse, SignupRequest } from "@/lib/types";
import { Button, ErrorBox, Input, Select } from "@/components/ui";
import { ConfirmEmailStep, isSignupPending, type SignupPending } from "@/components/confirm-email";

function fieldError(err: ApiError | null, name: string) {
  if (!err?.errors) return undefined;
  const hit = Object.entries(err.errors).find(([k]) => k.toLowerCase() === name.toLowerCase());
  return hit?.[1]?.[0];
}

export function SignupForm() {
  const router = useRouter();
  const [form, setForm] = useState<SignupRequest>({
    fullName: "",
    email: "",
    password: "",
    storeName: "",
    whatsappNumber: "",
    country: "NG",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  // Final fix pass B: the sign-up emails a 6-digit code; the account is usable once it is entered.
  const [pending, setPending] = useState<string | null>(null);
  const set = (k: keyof SignupRequest) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  // After a failed submit the error summary is announced; focus moves to the first field that needs fixing.
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (error) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [error]);

  // Back from the code step ("Change my details"): the button that had focus is gone, so start at the first field again.
  const wasPending = useRef(false);
  useEffect(() => {
    if (wasPending.current && !pending) formRef.current?.querySelector<HTMLElement>("input")?.focus();
    wasPending.current = !!pending;
  }, [pending]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (form.password.length < 8) {
      setError(new ApiError(400, { errors: { password: ["Password must be at least 8 characters."] } }));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const res = await api<AuthResponse | SignupPending>("/api/auth/signup", { method: "POST", body: { ...form, product: "whatsapp-orders" }, auth: false });
      if (isSignupPending(res)) {
        setPending(res.email);
        setBusy(false);
        return;
      }
      done(res);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  function done(res: AuthResponse) {
    setAuth(res);
    router.replace("/dashboard");
  }

  if (pending) {
    return (
      <ConfirmEmailStep
        email={pending}
        password={form.password}
        confirmPath="/api/auth/signup/confirm"
        product="whatsapp-orders"
        onConfirmed={done}
        onBack={() => setPending(null)}
        backLabel="Change my details"
      />
    );
  }

  return (
    <form ref={formRef} onSubmit={submit} className="space-y-5" noValidate>
      <Input label="Your full name" autoComplete="name" required value={form.fullName} onChange={set("fullName")} error={fieldError(error, "fullName")} />
      <Input label="Email" type="email" autoComplete="email" required value={form.email} onChange={set("email")} error={fieldError(error, "email")} />
      <Input
        label="Password"
        type="password"
        autoComplete="new-password"
        required
        minLength={8}
        value={form.password}
        onChange={set("password")}
        hint="At least 8 characters."
        error={fieldError(error, "password")}
      />
      <Input label="Store name" required value={form.storeName} onChange={set("storeName")} placeholder="e.g. Ada's Closet" error={fieldError(error, "storeName")} />
      <div className="grid grid-cols-[7.5rem_minmax(0,1fr)] gap-3">
        <Select label="Country" value={form.country} onChange={set("country")}>
          {COUNTRIES.map((c) => (
            <option key={c.code} value={c.code}>
              {c.code} – {c.name}
            </option>
          ))}
        </Select>
        <Input
          label="WhatsApp number"
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          required
          value={form.whatsappNumber}
          onChange={set("whatsappNumber")}
          placeholder="0803 123 4567"
          error={fieldError(error, "whatsappNumber")}
        />
      </div>
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="min-h-12 w-full text-base">
        Create my store
      </Button>
    </form>
  );
}
