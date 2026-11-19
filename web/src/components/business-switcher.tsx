"use client";

import { useId, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth, useAuth } from "@/lib/auth";
import { hubUrl } from "@/lib/suite";
import { useApi } from "@/lib/use-api";
import type { AuthResponse, MyBusinessDto } from "@/lib/types";
import { ApiErrorView, Button, Input, Sheet, Select } from "./ui";

/** Hard navigation on purpose: every page and in-memory request must start fresh for the other business. */
function reopenDashboard() {
  // eslint-disable-next-line @next/next/no-location-assign-relative-destination
  window.location.assign("/dashboard");
}

/**
 * Wave 3: the user's OrderLink stores under one sign-in. Switches between them (shown with more than one), and lets an owner start
 * another store, which gets its own plan (free to start) and is opened at once. Hidden in a support view.
 */
export function BusinessSwitcher({ className }: { className?: string }) {
  const id = useId();
  const auth = useAuth();
  const support = !!auth?.support;
  const isOwner = auth?.tenant.role === "Owner";
  const { data } = useApi<MyBusinessDto[]>(support ? null : "/api/me/businesses");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ name: "", whatsappNumber: "" });
  const list = Array.isArray(data) ? data : [];
  if (support || (list.length <= 1 && !isOwner)) return null;
  const current = list.find((b) => b.current)?.tenantId ?? "";

  async function switchTo(tenantId: string) {
    if (!tenantId || tenantId === current) return;
    setBusy(true);
    setError(null);
    try {
      setAuth(await api<AuthResponse>("/api/auth/switch", { method: "POST", body: { tenantId } }));
      reopenDashboard();
    } catch (e) {
      setError(toApiError(e));
      setBusy(false);
    }
  }

  async function add(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setAuth(await api<AuthResponse>("/api/me/businesses", { method: "POST", body: { name: form.name.trim(), whatsappNumber: form.whatsappNumber.trim() } }));
      reopenDashboard();
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <div className={className}>
      {list.length > 1 && (
        <Select id={id} label="Switch store" value={current} disabled={busy} onChange={(e) => switchTo(e.target.value)}>
          {list.map((b) => (
            <option key={b.tenantId} value={b.tenantId}>
              {b.name} ({b.role})
            </option>
          ))}
        </Select>
      )}
      {isOwner && (
        <Button variant="secondary" className="mt-3 w-full" onClick={() => setAdding(true)}>
          Add another store
        </Button>
      )}
      {error && !adding && <div className="mt-2"><ApiErrorView error={error} /></div>}
      <Sheet open={adding} onClose={() => setAdding(false)} title="Add another store">
        <form onSubmit={add} className="space-y-4 p-5">
          <p className="text-sm text-muted">
            It uses this sign-in, has its own products, orders and plan, and starts on the free plan.
          </p>
          <Input label="Store name" required maxLength={80} value={form.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
          <Input
            label="WhatsApp number"
            type="tel"
            inputMode="tel"
            required
            value={form.whatsappNumber}
            onChange={(e) => setForm((f) => ({ ...f, whatsappNumber: e.target.value }))}
            hint="Orders for this store are sent to this number."
          />
          {error && <ApiErrorView error={error} />}
          <Button type="submit" loading={busy} className="w-full">
            Create store
          </Button>
          <p className="text-xs text-subtle">
            Running another kind of business?{" "}
            <a href={hubUrl} className="font-medium text-ink underline">
              Start it on another LinkSuite product
            </a>{" "}
            with the same email and password.
          </p>
        </form>
      </Sheet>
    </div>
  );
}
