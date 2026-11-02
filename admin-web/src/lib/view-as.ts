import { useSyncExternalStore } from "react";

/**
 * Support views this admin opened from this tab, kept in sessionStorage (`adminlink.views`) so "End view" still works after
 * moving around the console or reloading. Each entry belongs to the admin who opened it; expired ones are ignored by callers
 * and dropped on the next write.
 */
export interface SupportView {
  sessionId: string;
  adminId: string;
  businessId: string;
  url: string;
  expiresAt: string;
}

const KEY = "adminlink.views";
const listeners = new Set<() => void>();
let cachedRaw: string | null | undefined;
let cachedValue: SupportView[] = [];
const EMPTY: SupportView[] = [];

function readRaw(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return window.sessionStorage.getItem(KEY);
  } catch {
    return null;
  }
}

function getViews(): SupportView[] {
  const raw = readRaw();
  if (raw === cachedRaw) return cachedValue;
  cachedRaw = raw;
  try {
    const parsed = raw ? (JSON.parse(raw) as SupportView[]) : [];
    cachedValue = Array.isArray(parsed) ? parsed : [];
  } catch {
    cachedValue = [];
  }
  return cachedValue;
}

function save(views: SupportView[]) {
  const now = Date.now();
  const live = views.filter((v) => new Date(v.expiresAt).getTime() > now);
  try {
    if (live.length === 0) window.sessionStorage.removeItem(KEY);
    else window.sessionStorage.setItem(KEY, JSON.stringify(live));
  } catch {
    /* storage unavailable: the view still runs out on its own */
  }
  listeners.forEach((l) => l());
}

export function addSupportView(view: SupportView) {
  save([...getViews().filter((v) => v.sessionId !== view.sessionId), view]);
}

export function removeSupportView(sessionId: string) {
  save(getViews().filter((v) => v.sessionId !== sessionId));
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

/** Every stored support view (filter by admin, business and `expiresAt` at the call site). */
export function useSupportViews(): SupportView[] {
  return useSyncExternalStore(subscribe, getViews, () => EMPTY);
}
