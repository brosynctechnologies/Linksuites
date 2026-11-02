"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, toApiError } from "./api";

interface State<T> {
  key: string | null;
  data?: T;
  error?: ApiError;
}

/**
 * Load JSON from a GET endpoint. Pass `null` to skip.
 * Keeps the previous data while a new key loads (filters and paging don't flash empty).
 */
export function useApi<T>(path: string | null) {
  const [nonce, setNonce] = useState(0);
  const [state, setState] = useState<State<T>>({ key: null });
  const key = path ? `${path}#${nonce}` : null;

  useEffect(() => {
    if (!path || !key) return;
    const ctrl = new AbortController();
    api<T>(path, { signal: ctrl.signal }).then(
      (data) => setState({ key, data }),
      (e) => {
        if (ctrl.signal.aborted) return;
        setState((s) => ({ key, data: s.data, error: toApiError(e) }));
      },
    );
    return () => ctrl.abort();
  }, [path, key]);

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

/** The value, updated only after it stops changing for `ms` milliseconds. */
export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}
