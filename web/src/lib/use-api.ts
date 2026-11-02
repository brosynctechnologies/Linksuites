"use client";

import { useCallback, useEffect, useState } from "react";
import { api, ApiError, toApiError } from "./api";

interface State<T> {
  key: string | null;
  data?: T;
  error?: ApiError;
}

/**
 * Load JSON from an authenticated GET endpoint. Pass `null` to skip.
 * Keeps the previous data while a new key loads (nice for filters/paging).
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
