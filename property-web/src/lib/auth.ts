import { useSyncExternalStore } from "react";
import { STORAGE_PREFIX } from "./config";
import type { SupportMarker } from "./support";
import type { AuthResponse, AuthTenant, AuthUser } from "./types";

/**
 * What we persist in localStorage after login/refresh.
 * A support view (`support` set) is kept in this tab's sessionStorage instead, never in localStorage.
 * A seeker session has `tenant: null`; an agency session carries the business (tenant) and role.
 * An agency session also works for every seeker endpoint. Stored under `propertylink.auth`.
 */
export interface StoredAuth {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
  tenant: AuthTenant | null;
  support?: SupportMarker;
}

const KEY = `${STORAGE_PREFIX}auth`;
const listeners = new Set<() => void>();
let cachedRaw: string | null = null;
let cachedValue: StoredAuth | null = null;

function readRaw(): string | null {
  if (typeof window === "undefined") return null;
  try {
    // A support view in this tab takes precedence over the browser's own session.
    return window.sessionStorage.getItem(KEY) ?? window.localStorage.getItem(KEY);
  } catch {
    return null;
  }
}

export function getAuth(): StoredAuth | null {
  const raw = readRaw();
  if (raw === cachedRaw) return cachedValue;
  cachedRaw = raw;
  try {
    const parsed = raw ? (JSON.parse(raw) as StoredAuth) : null;
    cachedValue = parsed && parsed.accessToken && parsed.user ? { ...parsed, tenant: parsed.tenant ?? null } : null;
  } catch {
    cachedValue = null;
  }
  return cachedValue;
}

function emit() {
  listeners.forEach((l) => l());
}

export function setAuth(res: AuthResponse | StoredAuth) {
  const value: StoredAuth = {
    accessToken: res.accessToken,
    refreshToken: res.refreshToken,
    expiresAt: res.expiresAt,
    user: res.user,
    tenant: res.tenant ?? null,
  };
  const support = "support" in res ? res.support : undefined;
  try {
    if (support) {
      window.sessionStorage.setItem(KEY, JSON.stringify({ ...value, support }));
    } else {
      window.sessionStorage.removeItem(KEY);
      window.localStorage.setItem(KEY, JSON.stringify(value));
    }
  } catch {
    /* storage unavailable */
  }
  emit();
}

/** Update user/tenant fields (e.g. after an agency rename) without touching tokens. */
export function patchAuth(patch: Partial<Pick<StoredAuth, "user" | "tenant">>) {
  const current = getAuth();
  if (!current) return;
  setAuth({ ...current, ...patch });
}

export function clearAuth() {
  try {
    // Clearing a support view leaves the browser's own session alone.
    if (window.sessionStorage.getItem(KEY) !== null) window.sessionStorage.removeItem(KEY);
    else window.localStorage.removeItem(KEY);
  } catch {
    /* ignore */
  }
  emit();
}

/** Mark this tab's support view as over (token expired or rejected). It stays read only and shows "Support view ended". */
export function endSupportView() {
  const current = getAuth();
  if (!current?.support || current.support.ended) return;
  setAuth({ ...current, support: { ...current.support, ended: true } });
}

/** Drop this tab's support view without notifying the UI; call right before closing the tab or navigating away. */
export function discardSupportView() {
  try {
    window.sessionStorage.removeItem(KEY);
  } catch {
    /* ignore */
  }
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  const onStorage = (e: StorageEvent) => {
    if (e.key === KEY) cb();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(cb);
    window.removeEventListener("storage", onStorage);
  };
}

/** Reactive access to the stored session. `null` on the server and when logged out. */
export function useAuth(): StoredAuth | null {
  return useSyncExternalStore(subscribe, getAuth, () => null);
}

const noopSubscribe = () => () => {};
/** True once running in the browser after hydration. */
export function useHydrated(): boolean {
  return useSyncExternalStore(
    noopSubscribe,
    () => true,
    () => false,
  );
}

/** First name for greetings: "Adaeze Okafor" → "Adaeze". */
export function firstName(fullName: string | null | undefined): string {
  return (fullName ?? "").trim().split(/\s+/)[0] || "there";
}
