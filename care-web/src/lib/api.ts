import { API_URL } from "./config";
import { clearAuth, endSupportView, getAuth, setAuth } from "./auth";
import type { AuthResponse, ProblemDetails } from "./types";
import type { ImportPreviewRow } from "./types/imports";

/** Error thrown for any non-2xx response, shaped after RFC 7807 ProblemDetails. */
export class ApiError extends Error {
  status: number;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
  code?: string;
  /** Import confirm (409 import_errors): the rows that still have errors. */
  rows?: ImportPreviewRow[];
  /** The whole problem body, for extension members such as `unsignedWardRounds` on a refused discharge. */
  problem: ProblemDetails & Record<string, unknown>;

  constructor(status: number, problem: ProblemDetails = {}) {
    const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    super(problem.detail || firstFieldError || problem.title || defaultMessage(status));
    this.name = "ApiError";
    this.status = status;
    this.title = problem.title;
    this.detail = problem.detail;
    this.errors = problem.errors;
    this.code = problem.code;
    this.rows = problem.rows;
    this.problem = problem as ProblemDetails & Record<string, unknown>;
  }

  get isPlanLimit() {
    return this.status === 402 || this.code === "plan_limit";
  }

  /** Flattened list of all field messages. */
  get fieldMessages(): string[] {
    return this.errors ? Object.values(this.errors).flat() : [];
  }

  /** First message for a field (case-insensitive). */
  field(name: string): string | undefined {
    if (!this.errors) return undefined;
    const lower = name.toLowerCase();
    const hit = Object.entries(this.errors).find(([k]) => k.toLowerCase() === lower);
    return hit?.[1]?.[0];
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
  /** Let the request outlive the page (a last autosave on leaving). */
  keepalive?: boolean;
  accept?: string;
  /** Extra request headers, such as `X-Care-Reason` for emergency access (a reason never goes in the address). */
  headers?: Record<string, string>;
  /**
   * On a 401 that a refresh can't fix, forget the session but stay on this page and throw the API's own sentence (the portal invite page
   * shows "Please sign in again." and its sign-in forms) instead of moving to the sign-in page.
   */
  stayOnPage?: boolean;
}

async function rawFetch(path: string, opts: ApiOptions): Promise<Response> {
  const headers: Record<string, string> = { ...opts.headers, Accept: opts.accept ?? "application/json" };
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
      keepalive: opts.keepalive,
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

/** One refresh shared by every concurrent caller. */
function sharedRefresh(): Promise<boolean> {
  refreshing ??= refreshTokens().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

/** Where a signed-out visitor should sign in: the patient portal (`/portal/...`) at its own page, staff at /login. */
export function loginPathFor(pathname: string): string {
  if (pathname === "/portal" || pathname.startsWith("/portal/")) return "/portal/sign-in";
  return "/login";
}

function redirectToLogin() {
  if (typeof window === "undefined") return;
  const path = window.location.pathname;
  if (path.startsWith("/login") || path.startsWith("/portal/sign-in")) return;
  const next = encodeURIComponent(path + window.location.search);
  // Hard navigation on purpose: this runs outside React (any component may trigger it).
  // eslint-disable-next-line @next/next/no-location-assign-relative-destination
  window.location.href = `${loginPathFor(path)}?next=${next}`;
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
 * Perform a request, refreshing the access token once on 401 (all concurrent
 * callers share a single refresh). Throws `ApiError` for non-2xx responses.
 */
async function request(path: string, opts: ApiOptions): Promise<Response> {
  let res = await rawFetch(path, opts);

  if (res.status === 401 && opts.auth !== false && getAuth()?.support) {
    // The support token expired: the tab shows "Support view ended", never the login form.
    endSupportView();
    throw new ApiError(401, { detail: "Support view ended." });
  }

  if (res.status === 401 && opts.auth !== false && getAuth()) {
    const first = opts.stayOnPage ? await parse<ProblemDetails | string | undefined>(res).catch(() => undefined) : undefined;
    const ok = await sharedRefresh();
    if (ok) {
      res = await rawFetch(path, opts);
    }
    if (!ok || res.status === 401) {
      clearAuth();
      if (opts.stayOnPage) {
        const again = ok ? await parse<ProblemDetails | string | undefined>(res).catch(() => undefined) : first;
        throw new ApiError(401, typeof again === "object" && again ? again : {});
      }
      redirectToLogin();
      throw new ApiError(401);
    }
  }

  if (!res.ok) {
    const problem = await parse<ProblemDetails | string | undefined>(res).catch(() => undefined);
    throw new ApiError(res.status, typeof problem === "object" && problem ? problem : {});
  }
  return res;
}

/** Fetch wrapper for the API. JSON in/out, ProblemDetails errors as `ApiError`. */
export async function api<T = unknown>(path: string, opts: ApiOptions = {}): Promise<T> {
  return parse<T>(await request(path, opts));
}

/** Fetch a binary response (PDFs, CSVs, templates) as a Blob, with the same auth/refresh handling. */
export async function apiBlob(path: string, opts: ApiOptions = {}): Promise<Blob> {
  const res = await request(path, { accept: "*/*", ...opts });
  return res.blob();
}

/** Upload a single file as multipart `file` (plus any extra text fields). */
export function upload<T>(path: string, file: File, fields: Record<string, string> = {}): Promise<T> {
  const fd = new FormData();
  fd.append("file", file);
  for (const [k, v] of Object.entries(fields)) fd.append(k, v);
  return api<T>(path, { method: "POST", body: fd });
}

/** POST a ready-made multipart form (several named files, e.g. verification documents). */
export function postForm<T>(path: string, fd: FormData): Promise<T> {
  return api<T>(path, { method: "POST", body: fd });
}

function xhrUpload(
  path: string,
  fd: FormData,
  onProgress: (fraction: number) => void,
  signal?: AbortSignal,
): Promise<{ status: number; text: string }> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", `${API_URL}${path}`);
    xhr.setRequestHeader("Accept", "application/json");
    const token = getAuth()?.accessToken;
    if (token) xhr.setRequestHeader("Authorization", `Bearer ${token}`);
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total);
    };
    xhr.onload = () => resolve({ status: xhr.status, text: xhr.responseText });
    xhr.onerror = () => reject(new ApiError(0));
    xhr.onabort = () => reject(new DOMException("Upload cancelled", "AbortError"));
    signal?.addEventListener("abort", () => xhr.abort());
    xhr.send(fd);
  });
}

/**
 * Upload a large file with progress (XMLHttpRequest reports upload progress; fetch does not).
 * `onProgress` receives 0..1. On 401 it refreshes the token once and uploads again.
 */
export async function uploadWithProgress<T>(
  path: string,
  file: File,
  onProgress: (fraction: number) => void,
  opts: { fields?: Record<string, string>; signal?: AbortSignal } = {},
): Promise<T> {
  const build = () => {
    const fd = new FormData();
    fd.append("file", file);
    for (const [k, v] of Object.entries(opts.fields ?? {})) fd.append(k, v);
    return fd;
  };
  let res = await xhrUpload(path, build(), onProgress, opts.signal);
  if (res.status === 401 && getAuth()?.support) {
    endSupportView();
    throw new ApiError(401, { detail: "Support view ended." });
  }
  if (res.status === 401 && getAuth()) {
    if (await sharedRefresh()) {
      onProgress(0);
      res = await xhrUpload(path, build(), onProgress, opts.signal);
    }
    if (res.status === 401) {
      clearAuth();
      redirectToLogin();
      throw new ApiError(401);
    }
  }
  let body: unknown = undefined;
  try {
    body = res.text ? JSON.parse(res.text) : undefined;
  } catch {
    body = undefined;
  }
  if (res.status < 200 || res.status >= 300) {
    throw new ApiError(res.status, typeof body === "object" && body ? (body as ProblemDetails) : {});
  }
  return body as T;
}

/** Save a Blob to the user's device. */
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

/** Download an authenticated file (PDF/CSV) and save it. */
export async function downloadFile(path: string, filename: string, opts: ApiOptions = {}) {
  saveBlob(await apiBlob(path, opts), filename);
}

/** Absolute URL of an API resource; absolute URLs from the API pass through unchanged. */
export function apiUrl(path: string) {
  if (/^https?:\/\//i.test(path)) return path;
  return `${API_URL}${path.startsWith("/") ? "" : "/"}${path}`;
}

/** Accept either a bare array or a `Paged<T>` from list endpoints. */
export function asList<T>(data: T[] | { items: T[] } | undefined | null): T[] {
  if (!data) return [];
  return Array.isArray(data) ? data : (data.items ?? []);
}

/** Signs out: revokes the refresh token, then forgets this browser's session (a shared reception PC must not keep it). */
export async function logout() {
  const refreshToken = getAuth()?.refreshToken;
  if (refreshToken) {
    await api("/api/auth/logout", { method: "POST", body: { refreshToken }, auth: false }).catch(() => {});
  }
  clearAuth();
}
