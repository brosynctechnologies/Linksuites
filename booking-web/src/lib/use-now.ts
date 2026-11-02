"use client";

import { useSyncExternalStore } from "react";

// One shared 1-second clock for countdowns and "now" lines. 0 on the server.
let now = 0;
let timer: ReturnType<typeof setInterval> | undefined;
const subs = new Set<() => void>();

function subscribe(cb: () => void) {
  subs.add(cb);
  if (!timer) {
    now = Date.now();
    timer = setInterval(() => {
      now = Date.now();
      subs.forEach((f) => f());
    }, 1000);
  }
  return () => {
    subs.delete(cb);
    if (!subs.size && timer) {
      clearInterval(timer);
      timer = undefined;
    }
  };
}

function snapshot() {
  if (!now) now = Date.now();
  return now;
}

/** Milliseconds since epoch, ticking every second while mounted (0 during server render). */
export function useNow(): number {
  return useSyncExternalStore(subscribe, snapshot, () => 0);
}
