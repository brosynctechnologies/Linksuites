"use client";

import { useSyncExternalStore } from "react";

/** The current time, ticking every `intervalMs` (rounded down to it, so snapshots stay stable). 0 on the server. */
export function useNow(intervalMs = 30_000): number {
  return useSyncExternalStore(
    (cb) => {
      const t = setInterval(cb, Math.min(intervalMs, 15_000));
      return () => clearInterval(t);
    },
    () => Math.floor(Date.now() / intervalMs) * intervalMs,
    () => 0,
  );
}
