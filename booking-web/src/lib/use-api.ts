"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, toApiError } from "./api";

interface State<T> {
  key: string | null;
  data?: T;
  error?: ApiError;
}

export interface UseApiOptions {
  /** Send the bearer token (default true). Public pages pass false. */
  auth?: boolean;
  /** Change this to force a refetch from outside (e.g. after a 409). */
  nonce?: number | string;
}

/**
 * Load JSON from a GET endpoint. Pass `null` to skip.
 * Keeps the previous data while a new key loads (nice for filters, paging and slot lists).
 */
export function useApi<T>(path: string | null, opts: UseApiOptions = {}) {
  const auth = opts.auth !== false;
  const [nonce, setNonce] = useState(0);
  const [state, setState] = useState<State<T>>({ key: null });
  const key = path ? `${path}#${nonce}#${opts.nonce ?? ""}` : null;

  useEffect(() => {
    if (!path || !key) return;
    const ctrl = new AbortController();
    api<T>(path, { signal: ctrl.signal, auth }).then(
      (data) => setState({ key, data }),
      (e) => {
        if (ctrl.signal.aborted) return;
        setState((s) => ({ key, data: s.data, error: toApiError(e) }));
      },
    );
    return () => ctrl.abort();
  }, [path, key, auth]);

  const reload = useCallback(() => setNonce((n) => n + 1), []);
  const setData = useCallback((updater: T | ((prev: T | undefined) => T)) => {
    setState((s) => ({
      ...s,
      data: typeof updater === "function" ? (updater as (p: T | undefined) => T)(s.data) : updater,
    }));
  }, []);

  return {
    data: state.data,
    error: state.key === key ? state.error : undefined,
    loading: key !== null && state.key !== key,
    reload,
    setData,
  };
}

/** Build a query string from defined, non-empty values: qs({ a: 1, b: "" }) → "?a=1". */
export function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === null || v === undefined || v === "") continue;
    sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : "";
}
