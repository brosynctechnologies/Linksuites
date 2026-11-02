import { API_URL } from "./config";
import { clearAuth, endSupportView, getAuth, setAuth } from "./auth";
import type { AuthResponse, ProblemDetails } from "./types";

/** Error thrown for any non-2xx response, shaped after RFC 7807 ProblemDetails. */
export class ApiError extends Error {
  status: number;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;

  constructor(status: number, problem: ProblemDetails = {}) {
    const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    super(problem.detail || firstFieldError || problem.title || defaultMessage(status));
    this.name = "ApiError";
    this.status = status;
    this.title = problem.title;
    this.detail = problem.detail;
    this.errors = problem.errors;
    this.code = problem.code;
  }

  get isPlanLimit() {
    return this.status === 402 || this.code === "plan_limit";
  }

  /** Flattened list of all field messages. */
  get fieldMessages(): string[] {
    return this.errors ? Object.values(this.errors).flat() : [];
  }
}

function defaultMessage(status: number) {
  if (status === 0) return "Could not reach the server. Check your connection and try again.";
  if (status === 401) return "Please log in again.";
  if (status === 402) return "Upgrade your plan to do this.";
  if (status === 403) return "You don't have access to this.";
  if (status === 404) return "Not found.";
  if (status === 409) return "This conflicts with existing data.";
  if (status >= 500) return "Something went wrong on our side. Please try again.";
  return "Request failed.";
}

export function toApiError(e: unknown): ApiError {
  if (e instanceof ApiError) return e;
  return new ApiError(0, { detail: e instanceof Error && e.message ? e.message : undefined });
}

export interface ApiOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  /** Send the bearer token (default true). */
  auth?: boolean;
  signal?: AbortSignal;
}

async function rawFetch(path: string, opts: ApiOptions): Promise<Response> {
  const headers: Record<string, string> = { Accept: "application/json" };
  let body: BodyInit | undefined;
  if (opts.body instanceof FormData) {
    body = opts.body;
  } else if (opts.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(opts.body);
  }
  if (opts.auth !== false) {
    const token = getAuth()?.accessToken;
    if (token) headers.Authorization = `Bearer ${token}`;
  }
  try {
    return await fetch(`${API_URL}${path}`, {
      method: opts.method ?? (opts.body !== undefined ? "POST" : "GET"),
      headers,
      body,
      signal: opts.signal,
      cache: "no-store",
    });
  } catch (e) {
    if (e instanceof DOMException && e.name === "AbortError") throw e;
    throw new ApiError(0);
  }
}

let refreshing: Promise<boolean> | null = null;

async function refreshTokens(): Promise<boolean> {
  const current = getAuth();
  // A support view has no refresh token and is never refreshed.
  if (!current?.refreshToken || current.support) return false;
  const refreshToken = current.refreshToken;
  try {
    const res = await fetch(`${API_URL}/api/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    if (!res.ok) return false;
    setAuth((await res.json()) as AuthResponse);
    return true;
  } catch {
    return false;
  }
}

function redirectToLogin() {
  if (typeof window === "undefined") return;
  if (window.location.pathname.startsWith("/login")) return;
  const next = encodeURIComponent(window.location.pathname + window.location.search);
  // Hard navigation on purpose: this runs outside React (any component may trigger it).
  // eslint-disable-next-line @next/next/no-location-assign-relative-destination
  window.location.href = `/login?next=${next}`;
}

async function parse<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  if (!text) return undefined as T;
  try {
    return JSON.parse(text) as T;
  } catch {
    return text as unknown as T;
  }
}

/**
 * Fetch wrapper for the API. JSON in/out, ProblemDetails errors as `ApiError`.
 * On a 401 for an authenticated call it refreshes once and retries; if that fails
 * the session is cleared and the browser is sent to /login.
 */
export async function api<T = unknown>(path: string, opts: ApiOptions = {}): Promise<T> {
  let res = await rawFetch(path, opts);

  if (res.status === 401 && opts.auth !== false && getAuth()?.support) {
    // The support token expired: the tab shows "Support view ended", never the login form.
    endSupportView();
    throw new ApiError(401, { detail: "Support view ended." });
  }

  if (res.status === 401 && opts.auth !== false && getAuth()) {
    refreshing ??= refreshTokens().finally(() => {
      refreshing = null;
    });
    const ok = await refreshing;
    if (ok) {
      res = await rawFetch(path, opts);
    }
    if (!ok || res.status === 401) {
      clearAuth();
      redirectToLogin();
      throw new ApiError(401);
    }
  }

  if (!res.ok) {
    const problem = await parse<ProblemDetails | string | undefined>(res).catch(() => undefined);
    throw new ApiError(res.status, typeof problem === "object" && problem ? problem : {});
  }
  return parse<T>(res);
}

/** Upload a single file as multipart `file`. */
export function upload<T>(path: string, file: File): Promise<T> {
  const fd = new FormData();
  fd.append("file", file);
  return api<T>(path, { method: "POST", body: fd });
}

export async function logout() {
  const refreshToken = getAuth()?.refreshToken;
  if (refreshToken) {
    await api("/api/auth/logout", { method: "POST", body: { refreshToken }, auth: false }).catch(() => {});
  }
  clearAuth();
}
