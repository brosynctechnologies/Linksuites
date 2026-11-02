"use client";

import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";

/** The value, updated only after it stops changing for `ms` milliseconds. */
export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** A query-string value (needs a Suspense boundary above; the /app layout provides one). */
export function useQueryParam(name: string): string | null {
  return useSearchParams().get(name);
}
