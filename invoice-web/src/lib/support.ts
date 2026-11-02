import { setAuth } from "./auth";
import { API_URL } from "./config";
import type { AuthTenant, AuthUser } from "./types";

/**
 * Support view: a platform admin opens `/support#token=…` and sees the owner's dashboard read only.
 * The session lives in this tab's sessionStorage, has no refresh token and ends when the token expires.
 */
export interface SupportMarker {
  expiresAt: string;
  readOnly: true;
  /** Set once the token has expired or been rejected; the tab then shows "Support view ended". */
  ended?: boolean;
}

/** Expiry from a JWT's `exp` claim, or null when the token can't be read. */
export function tokenExpiry(token: string): Date | null {
  try {
    const part = token.split(".")[1];
    const base64 = part.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(part.length / 4) * 4, "=");
    const exp = (JSON.parse(atob(base64)) as { exp?: unknown }).exp;
    return typeof exp === "number" ? new Date(exp * 1000) : null;
  } catch {
    return null;
  }
}

/** "14:32", in Lagos time. */
export function lagosTime(iso: string): string {
  return new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "Africa/Lagos" }).format(
    new Date(iso),
  );
}

/** Check the token with `GET /api/me` and store it as this tab's support session. Rejects when it is missing, expired or invalid. */
export async function startSupportView(token: string | null): Promise<void> {
  const expires = token ? tokenExpiry(token) : null;
  if (!token || !expires || expires.getTime() <= Date.now()) throw new Error("expired");
  const res = await fetch(`${API_URL}/api/me`, {
    headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
    cache: "no-store",
  });
  if (!res.ok) throw new Error("expired");
  const me = (await res.json()) as { user: AuthUser; tenant: AuthTenant | null };
  if (!me.tenant) throw new Error("expired");
  const expiresAt = expires.toISOString();
  setAuth({
    accessToken: token,
    refreshToken: "",
    expiresAt,
    user: me.user,
    tenant: me.tenant,
    support: { expiresAt, readOnly: true },
  });
}

/** Tell the API this support view is over (`POST /api/support-view/end`). Errors are ignored: the view may have ended already. */
export async function endSupportSession(token: string | undefined): Promise<void> {
  if (!token) return;
  try {
    await fetch(`${API_URL}/api/support-view/end`, {
      method: "POST",
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      cache: "no-store",
      keepalive: true,
    });
  } catch {
    /* ignore: closing the view must still work */
  }
}
