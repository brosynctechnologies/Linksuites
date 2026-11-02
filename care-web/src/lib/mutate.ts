"use client";

import { useState } from "react";
import { toApiError, type ApiError } from "./api";

export type Outcome<T> = { ok: true; data: T } | { ok: false; error: ApiError };

/** Busy and error state around one async action. `run` never throws: it resolves to the outcome and keeps the error for display. */
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  async function run<T>(fn: () => Promise<T>): Promise<Outcome<T>> {
    setBusy(true);
    setError(null);
    try {
      return { ok: true, data: await fn() };
    } catch (e) {
      const err = toApiError(e);
      setError(err);
      return { ok: false, error: err };
    } finally {
      setBusy(false);
    }
  }
  return { busy, error, setError, run };
}
