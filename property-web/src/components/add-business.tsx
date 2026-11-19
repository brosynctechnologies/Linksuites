"use client";

import { useId, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth, useAuth } from "@/lib/auth";
import { hubUrl } from "@/lib/suite";
import type { AuthResponse } from "@/lib/types";
import { buttonClass, cx, inputClass, labelClass } from "./styles";

/**
 * Wave 3: an owner starts another business under the same sign-in (POST /api/me/businesses). It gets its own data and plan (free to
 * start) and opens at once. Shared verbatim by the InvoiceLink, BookLink, LearnLink and PropertyLink apps; `home` is the app's dashboard.
 */
export function AddBusiness({ home, className }: { home: string; className?: string }) {
  const id = useId();
  const auth = useAuth();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  if (!auth || auth.support || auth.tenant?.role !== "Owner") return null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setAuth(await api<AuthResponse>("/api/me/businesses", { method: "POST", body: { name: name.trim() } }));
      // Hard navigation on purpose: every page and in-memory request must start fresh for the new business.
      window.location.assign(home);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  if (!open)
    return (
      <div className={className}>
        <button type="button" className={buttonClass("secondary", "w-full")} onClick={() => setOpen(true)}>
          Add another business
        </button>
      </div>
    );

  return (
    <form onSubmit={submit} className={cx("space-y-3", className)}>
      <div>
        <label htmlFor={id} className={labelClass}>
          New business name
        </label>
        <input id={id} className={cx(inputClass, "mt-1")} required maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
        <p className="mt-1 text-xs text-muted">Same sign-in, its own data and plan. It starts on the free plan.</p>
      </div>
      {error && (
        <p role="alert" className="text-sm text-red-700">
          {error.message}
        </p>
      )}
      <div className="flex gap-2">
        <button type="submit" className={buttonClass("primary")} disabled={busy || !name.trim()}>
          {busy ? "Creating…" : "Create business"}
        </button>
        <button type="button" className={buttonClass("ghost")} onClick={() => setOpen(false)} disabled={busy}>
          Cancel
        </button>
      </div>
      <p className="text-xs text-muted">
        Another kind of business?{" "}
        <a href={hubUrl} className="font-semibold underline">
          Start it on another LinkSuite product
        </a>{" "}
        with the same email and password.
      </p>
    </form>
  );
}
