"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { getAuth, useAuth } from "@/lib/auth";
import { Button, Card, ErrorBox, Input, Notice, SectionTitle } from "./ui";

/** Change password (`POST /api/auth/password/change`). Other devices are signed out; this one stays signed in. */
export function ChangePassword() {
  const support = !!useAuth()?.support;
  const [current, setCurrent] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [mismatch, setMismatch] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [changed, setChanged] = useState(false);

  // A support view is read only and never changes the owner's password.
  if (support) return null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setChanged(false);
    if (password !== confirm) {
      setMismatch(true);
      return;
    }
    setMismatch(false);
    setBusy(true);
    try {
      await api("/api/auth/password/change", {
        method: "POST",
        // Sending this device's refresh token keeps it signed in.
        body: { currentPassword: current, newPassword: password, refreshToken: getAuth()?.refreshToken || null },
      });
      setCurrent("");
      setPassword("");
      setConfirm("");
      setChanged(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  const currentError = error?.field("currentPassword");
  const newError = error?.field("newPassword");

  return (
    <Card>
      <SectionTitle>Password</SectionTitle>
      <p className="-mt-2 mb-4 text-sm text-muted">Changing your password signs you out on your other devices.</p>
      <form onSubmit={submit} className="max-w-sm space-y-4">
        <Input
          label="Current password"
          type="password"
          autoComplete="current-password"
          required
          error={currentError}
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
        />
        <Input
          label="New password"
          type="password"
          autoComplete="new-password"
          required
          minLength={8}
          hint="At least 8 characters."
          error={newError}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <Input
          label="Confirm new password"
          type="password"
          autoComplete="new-password"
          required
          error={mismatch ? "The passwords don't match." : undefined}
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
        />
        {error && !currentError && !newError && <ErrorBox error={error} />}
        {changed && <Notice tone="success">Password changed. You&apos;ve been signed out on your other devices.</Notice>}
        <Button type="submit" variant="secondary" loading={busy}>
          Change password
        </Button>
      </form>
    </Card>
  );
}
