"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { Button, ErrorBox, Input, Notice } from "@/components/ui";

export function ForgotPasswordForm() {
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [sent, setSent] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/api/auth/password/forgot", { method: "POST", body: { email: email.trim(), product: "whatsapp-orders" }, auth: false });
      setSent(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <div className="space-y-5">
        <Notice tone="success">
          If an account uses <strong>{email.trim()}</strong>, we&apos;ve sent a link to reset your password. The link works for 10 minutes.
        </Notice>
        <p className="text-sm text-ink-2">No email after a few minutes? Check your spam folder.</p>
        <Button variant="secondary" className="w-full" onClick={() => setSent(false)}>
          Use a different email
        </Button>
      </div>
    );
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <Input label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="min-h-12 w-full text-base">
        Send reset link
      </Button>
    </form>
  );
}
