import { useSyncExternalStore } from "react";
import type { AdminUser, AuthResponse, PlatformMe } from "./types";

/**
 * The admin session lives in sessionStorage only (this tab), under `adminlink.session`.
 * The token lasts 8 hours and cannot be refreshed: on expiry or any 401 the session is cleared
 * and the sign-in screen shows "Your session has ended. Sign in again." The console also polls GET /api/platform/me, so a
 * session ended elsewhere (signed out, removed, password reset) or a lost admin role is noticed within a minute.
 */
export interface AdminSession {
  accessToken: string;
  expiresAt: string;
  user: AdminUser;
}

const KEY = "adminlink.session";
const ENDED_KEY = "adminlink.ended";
export const SESSION_ENDED_NOTE = "Your session has ended. Sign in again.";
export const ACCESS_REMOVED_NOTE = "You no longer have admin access.";
export const PASSWORD_CHANGED_NOTE = "Password changed. Sign in again.";

const listeners = new Set<() => void>();
let cachedRaw: string | null | undefined;
let cachedValue: AdminSession | null = null;
let cachedEnded: string | null = null;

function read(key: string): string | null {
  if (typeof window === "undefined") return null;
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) window.sessionStorage.removeItem(key);
    else window.sessionStorage.setItem(key, value);
  } catch {
    /* storage unavailable: the session lives only until reload */
  }
}

function emit() {
  listeners.forEach((l) => l());
}

export function getSession(): AdminSession | null {
  const raw = read(KEY);
  if (raw === cachedRaw) return cachedValue;
  cachedRaw = raw;
  try {
    const parsed = raw ? (JSON.parse(raw) as AdminSession) : null;
    cachedValue = parsed?.accessToken && parsed.user ? parsed : null;
  } catch {
    cachedValue = null;
  }
  return cachedValue;
}

export function setSession(res: AuthResponse) {
  const value: AdminSession = { accessToken: res.accessToken, expiresAt: res.expiresAt, user: res.user };
  write(KEY, JSON.stringify(value));
  write(ENDED_KEY, null);
  cachedEnded = null;
  emit();
}

/**
 * Refresh the stored identity from GET /api/platform/me (name, email, role and the server's session end).
 * Writes only when something changed, so polling doesn't re-render the console.
 */
export function applyMe(me: PlatformMe) {
  const s = getSession();
  if (!s || s.user.id !== me.id) return;
  const user: AdminUser = { ...s.user, fullName: me.fullName, email: me.email, platformRole: me.platformRole, isPlatformAdmin: true };
  const expiresAt = me.sessionExpiresAt ?? s.expiresAt;
  const same =
    user.fullName === s.user.fullName &&
    user.email === s.user.email &&
    user.platformRole === s.user.platformRole &&
    user.isPlatformAdmin === s.user.isPlatformAdmin &&
    new Date(expiresAt).getTime() === new Date(s.expiresAt).getTime();
  if (same) return;
  write(KEY, JSON.stringify({ ...s, user, expiresAt } satisfies AdminSession));
  emit();
}

/** Clear the session. With a note, the sign-in screen explains why (expiry, 401). */
export function endSession(note: string | null = null) {
  write(KEY, null);
  write(ENDED_KEY, note);
  cachedEnded = note;
  emit();
}

export function getEndedNote(): string | null {
  const raw = read(ENDED_KEY);
  if (raw !== cachedEnded) cachedEnded = raw;
  return cachedEnded;
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

/** Reactive session. `null` on the server and when signed out. */
export function useSession(): AdminSession | null {
  return useSyncExternalStore(subscribe, getSession, () => null);
}

export function useEndedNote(): string | null {
  return useSyncExternalStore(subscribe, getEndedNote, () => null);
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
