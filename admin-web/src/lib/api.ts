import { endSession, getSession, SESSION_ENDED_NOTE } from "./session";
import type { ProblemDetails } from "./types";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

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

  /** First message for a field (case-insensitive, camelCase keys from the API). */
  field(name: string): string | undefined {
    if (!this.errors) return undefined;
    const lower = name.toLowerCase();
    const hit = Object.entries(this.errors).find(([k]) => k.toLowerCase() === lower);
    return hit?.[1]?.[0];
  }

  /** Field messages not claimed by any of the given field names (shown above the form). */
  otherFieldMessages(known: string[]): string[] {
    if (!this.errors) return [];
    const set = new Set(known.map((k) => k.toLowerCase()));
    return Object.entries(this.errors)
      .filter(([k]) => !set.has(k.toLowerCase()))
      .flatMap(([, v]) => v);
  }
}

function defaultMessage(status: number) {
  if (status === 0) return "Could not reach the API. Check that it is running and try again.";
  if (status === 401) return SESSION_ENDED_NOTE;
  if (status === 403) return "You do not have access to this.";
  if (status === 404) return "Not found.";
  if (status === 409) return "This conflicts with the current state. Reload and try again.";
  if (status === 429) return "Too many requests. Wait a moment and try again.";
  if (status >= 500) return "The API failed to handle this. Try again.";
  return "Request failed.";
}

export function toApiError(e: unknown): ApiError {
  if (e instanceof ApiError) return e;
  return new ApiError(0, { detail: e instanceof Error && e.message ? e.message : undefined });
}

export interface ApiOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  /** Send the bearer token (default true). Sign-in calls pass false. */
  auth?: boolean;
  signal?: AbortSignal;
  accept?: string;
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

/** One request. A 401 on an authenticated call ends the session (no refresh for admins). */
async function request(path: string, opts: ApiOptions): Promise<Response> {
  const headers: Record<string, string> = { Accept: opts.accept ?? "application/json" };
  let body: BodyInit | undefined;
  if (opts.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(opts.body);
  }
  const useAuth = opts.auth !== false;
  if (useAuth) {
    const token = getSession()?.accessToken;
    if (token) headers.Authorization = `Bearer ${token}`;
  }
  let res: Response;
  try {
    res = await fetch(`${API_URL}${path}`, {
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

  if (res.status === 401 && useAuth) {
    endSession(SESSION_ENDED_NOTE);
    throw new ApiError(401);
  }
  if (!res.ok) {
    const problem = await parse<ProblemDetails | string | undefined>(res).catch(() => undefined);
    throw new ApiError(res.status, typeof problem === "object" && problem ? problem : {});
  }
  return res;
}

/** JSON in/out, ProblemDetails errors as `ApiError`. */
export async function api<T = unknown>(path: string, opts: ApiOptions = {}): Promise<T> {
  return parse<T>(await request(path, opts));
}

/** Fetch a binary response (documents, CSV) as a Blob with the token. */
export async function apiBlob(path: string, opts: ApiOptions = {}): Promise<Blob> {
  const res = await request(path, { accept: "*/*", ...opts });
  return res.blob();
}

/** Save a Blob to the device. */
export function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Accept either a bare array or a `Paged<T>` from list endpoints. */
export function asList<T>(data: T[] | { items: T[] } | undefined | null): T[] {
  if (!data) return [];
  return Array.isArray(data) ? data : (data.items ?? []);
}
