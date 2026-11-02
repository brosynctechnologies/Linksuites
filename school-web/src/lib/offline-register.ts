"use client";

// The offline-tolerant class register.
//
// A save goes straight to the API when it can. When the API can't be reached, the register is kept in IndexedDB as
// `{ classId, date, clientId, marks }` (plus the school, so a queued register is only ever sent to the school it was taken in)
// and sent again when the browser comes back online and every 30 seconds.
//
// Idempotency: each save gets a new client id (a UUID), and every resend of that save reuses it. The API treats a PUT whose
// client id and marks match what it already stored as a no-op. There is at most one queued register per school, class and
// date: a newer save replaces the older one, so the latest marks are what reaches the server.
//
// Stale saves: each save also carries the version of the register the marks were based on (`baseClientId`: the stored
// register's client id when it was loaded, or "" when it hadn't been taken), and the unsent save it replaces (`priorClientId`).
// If someone else saved the register since, the API answers 409 and the queued item is marked failed with STALE_REGISTER,
// never resent, so a phone reconnecting hours later can't silently overwrite newer marks.
//
// Each queued item belongs to the user who saved it and is only ever sent in that user's session. Signing out clears the
// queue and the cache (see clearOfflineData), after a warning when registers haven't sent yet.
//
// The last register loaded for each class and date is cached too, so a register opened before the connection dropped can
// still be shown and taken.

import { useEffect, useSyncExternalStore } from "react";
import { api, ApiError, toApiError } from "./api";
import { getAuth } from "./auth";
import type { RegisterDto, RegisterMarkInput, SaveRegisterRequest } from "./types/academic";

export const OFFLINE_BANNER = "Saved on this phone. It will send when you're back online.";
export const RETRY_MS = 30_000;
export const STALE_REGISTER = "Someone else updated this register. Open it to review your marks.";
export const UNSENT_SIGN_OUT = "You have registers that haven't sent yet. Sign out anyway?";

export interface QueuedRegister {
  /** `${tenantId}:${classId}:${date}`. */
  key: string;
  tenantId: string;
  /** The signed-in user who saved it. Never sent in anyone else's session. */
  userId: string;
  /** The stored register's client id when the marks were taken ("" when it hadn't been taken). */
  baseClientId: string;
  /** The unsent save on this phone that this one replaced, if any. */
  priorClientId: string | null;
  /** True when the server refused it because someone else saved the register since (409). */
  stale?: boolean;
  classId: string;
  className: string;
  date: string;
  clientId: string;
  marks: RegisterMarkInput[];
  queuedAt: string;
  /** Set when the server refused the register (not a connection problem). It is not resent until the teacher saves again. */
  failed: string | null;
}

type Cached = RegisterDto & { key: string; cachedAt: string };

const DB_NAME = "schoollink-offline";
const DB_VERSION = 1;
const QUEUE = "registers";
const CACHE = "register-cache";

// ---------- IndexedDB (with an in-memory fallback when it is blocked, e.g. a private window) ----------

const memory = { [QUEUE]: new Map<string, unknown>(), [CACHE]: new Map<string, unknown>() };
let dbPromise: Promise<IDBDatabase | null> | null = null;

function openDb(): Promise<IDBDatabase | null> {
  if (typeof window === "undefined") return Promise.resolve(null);
  dbPromise ??= new Promise<IDBDatabase | null>((resolve) => {
    try {
      if (!("indexedDB" in window)) return resolve(null);
      const req = window.indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(QUEUE)) db.createObjectStore(QUEUE, { keyPath: "key" });
        if (!db.objectStoreNames.contains(CACHE)) db.createObjectStore(CACHE, { keyPath: "key" });
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(null);
      req.onblocked = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
  return dbPromise;
}

async function tx<T>(store: typeof QUEUE | typeof CACHE, mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest | null): Promise<T | undefined> {
  const db = await openDb();
  if (!db) return undefined;
  return new Promise<T | undefined>((resolve) => {
    try {
      const t = db.transaction(store, mode);
      const req = fn(t.objectStore(store));
      t.oncomplete = () => resolve(req ? (req.result as T) : undefined);
      t.onerror = () => resolve(undefined);
      t.onabort = () => resolve(undefined);
    } catch {
      resolve(undefined);
    }
  });
}

async function put<T extends { key: string }>(store: typeof QUEUE | typeof CACHE, value: T) {
  memory[store].set(value.key, value);
  await tx(store, "readwrite", (s) => s.put(value));
}

async function remove(store: typeof QUEUE | typeof CACHE, key: string) {
  memory[store].delete(key);
  await tx(store, "readwrite", (s) => s.delete(key));
}

async function get<T>(store: typeof QUEUE | typeof CACHE, key: string): Promise<T | undefined> {
  const hit = await tx<T>(store, "readonly", (s) => s.get(key));
  return hit ?? (memory[store].get(key) as T | undefined);
}

async function all<T>(store: typeof QUEUE | typeof CACHE): Promise<T[]> {
  const rows = await tx<T[]>(store, "readonly", (s) => s.getAll());
  return rows ?? ([...memory[store].values()] as T[]);
}

// ---------- The queue, as a small external store for React ----------

let queue: QueuedRegister[] = [];
let loaded = false;
const listeners = new Set<() => void>();
const sentListeners = new Set<(key: string, register: RegisterDto) => void>();

function emit() {
  listeners.forEach((l) => l());
}

/** Only the signed-in user's registers are shown or sent; anything else on the phone is left alone (and cleared at sign-out). */
function isMine(q: QueuedRegister, tenantId: string | null, userId: string | null) {
  return !!tenantId && !!userId && q.tenantId === tenantId && q.userId === userId;
}

async function refresh() {
  const tenantId = currentTenantId();
  const userId = currentUserId();
  queue = (await all<QueuedRegister>(QUEUE)).filter((q) => isMine(q, tenantId, userId)).sort((a, b) => a.queuedAt.localeCompare(b.queuedAt));
  loaded = true;
  emit();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  if (!loaded) void refresh();
  return () => listeners.delete(listener);
}

const EMPTY: QueuedRegister[] = [];

/** Every register waiting to be sent for the signed-in school. */
export function useQueuedRegisters(): QueuedRegister[] {
  return useSyncExternalStore(
    subscribe,
    () => queue,
    () => EMPTY,
  );
}

/** Called after a queued register reaches the server, so an open register page can show the stored copy. */
export function onRegisterSent(listener: (key: string, register: RegisterDto) => void): () => void {
  sentListeners.add(listener);
  return () => sentListeners.delete(listener);
}

export function registerKey(tenantId: string, classId: string, date: string) {
  return `${tenantId}:${classId}:${date}`;
}

function currentTenantId(): string | null {
  return getAuth()?.tenant?.id ?? null;
}

function currentUserId(): string | null {
  return getAuth()?.user?.id ?? null;
}

/** A fresh id for one save. Every resend of that save reuses it. */
export function newClientId(): string {
  try {
    if (typeof crypto !== "undefined" && "randomUUID" in crypto) return crypto.randomUUID();
  } catch {
    // fall through
  }
  return `r-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

function path(classId: string, date: string) {
  return `/api/school/attendance/${classId}/${date}`;
}

/** A connection problem (no network, the API is down, or a gateway error): worth keeping and sending again later. */
export function isConnectionError(e: ApiError) {
  return e.status === 0 || e.status === 502 || e.status === 503 || e.status === 504;
}

// ---------- Cache of loaded registers ----------

export async function cacheRegister(tenantId: string, register: RegisterDto) {
  const value: Cached = { ...register, messageParents: [], key: registerKey(tenantId, register.classId, register.date), cachedAt: new Date().toISOString() };
  await put(CACHE, value);
}

export async function cachedRegister(tenantId: string, classId: string, date: string): Promise<RegisterDto | null> {
  const hit = await get<Cached>(CACHE, registerKey(tenantId, classId, date));
  if (!hit) return null;
  return { classId: hit.classId, className: hit.className, date: hit.date, taken: hit.taken, clientId: hit.clientId, marks: hit.marks, messageParents: [] };
}

// ---------- Saving ----------

export type SaveOutcome = { kind: "sent"; register: RegisterDto } | { kind: "queued" } | { kind: "error"; error: ApiError };

/** The version a save is based on: the stored register's client id, or "" for a register nobody has taken yet. */
export function registerBase(register: Pick<RegisterDto, "taken" | "clientId">): string {
  return register.taken ? (register.clientId ?? "") : "";
}

/**
 * Save a register: straight to the API, or into the queue when the API can't be reached. A server refusal (400, 403, 409) is
 * returned as an error and nothing is queued, so the teacher sees it at once. `baseClientId` is the version the teacher's marks
 * were taken from (see registerBase).
 */
export async function saveRegister(input: {
  classId: string;
  className: string;
  date: string;
  marks: RegisterMarkInput[];
  baseClientId: string;
}): Promise<SaveOutcome> {
  const tenantId = currentTenantId();
  const userId = currentUserId();
  if (!tenantId || !userId) return { kind: "error", error: new ApiError(401) };
  const key = registerKey(tenantId, input.classId, input.date);
  // A newer save replacing one still waiting on this phone keeps that one's base: its marks were taken from the same copy, and
  // the older save may already have reached the server unheard (priorClientId lets the server accept that).
  const previous = await get<QueuedRegister>(QUEUE, key);
  const waiting = previous && isMine(previous, tenantId, userId) && !previous.failed ? previous : null;
  const item: QueuedRegister = {
    key,
    tenantId,
    userId,
    baseClientId: waiting ? waiting.baseClientId : input.baseClientId,
    priorClientId: waiting ? waiting.clientId : null,
    classId: input.classId,
    className: input.className,
    date: input.date,
    clientId: newClientId(),
    marks: input.marks,
    queuedAt: new Date().toISOString(),
    failed: null,
  };
  // Stored first, so a tab closed mid-request still sends it later. The send below removes it again.
  await put(QUEUE, item);
  await refresh();

  try {
    const register = await send(item);
    return { kind: "sent", register };
  } catch (e) {
    const err = toApiError(e);
    if (isConnectionError(err)) return { kind: "queued" };
    await removeIfSame(key, item.clientId);
    return { kind: "error", error: err };
  }
}

async function send(item: QueuedRegister): Promise<RegisterDto> {
  const body: SaveRegisterRequest = {
    clientId: item.clientId,
    marks: item.marks,
    baseClientId: item.baseClientId ?? "",
    priorClientId: item.priorClientId ?? null,
  };
  const register = await api<RegisterDto>(path(item.classId, item.date), { method: "PUT", body });
  await removeIfSame(item.key, item.clientId);
  await cacheRegister(item.tenantId, register);
  return register;
}

/** Removes the queued register only if it is still the same save (a newer save for the same class and date stays queued). */
async function removeIfSame(key: string, clientId: string) {
  const current = await get<QueuedRegister>(QUEUE, key);
  if (current && current.clientId === clientId) await remove(QUEUE, key);
  await refresh();
}

/** The register saved on this phone for a class and date and not sent yet, if any. */
export async function queuedRegister(tenantId: string, classId: string, date: string): Promise<QueuedRegister | null> {
  const hit = await get<QueuedRegister>(QUEUE, registerKey(tenantId, classId, date));
  return hit && isMine(hit, tenantId, currentUserId()) ? hit : null;
}

// ---------- Signing out ----------

/** How many registers saved on this phone by the signed-in user haven't reached the server (waiting or refused), for one class when given. */
export async function unsentRegisterCount(classId?: string): Promise<number> {
  const tenantId = currentTenantId();
  const userId = currentUserId();
  return (await all<QueuedRegister>(QUEUE)).filter((q) => isMine(q, tenantId, userId) && (!classId || q.classId === classId)).length;
}

/** Asks before signing out when registers haven't sent yet. True when sign-out should go ahead. */
export async function confirmSignOutWithUnsent(): Promise<boolean> {
  if (typeof window === "undefined") return true;
  const unsent = await unsentRegisterCount().catch(() => 0);
  return unsent === 0 || window.confirm(UNSENT_SIGN_OUT);
}

/**
 * Removes every queued register and every cached register (class rosters with children's names) from this phone. Called at
 * sign-out, so the next person on a shared phone neither sees them nor sends them under their own account.
 */
export async function clearOfflineData(): Promise<void> {
  memory[QUEUE].clear();
  memory[CACHE].clear();
  await tx(QUEUE, "readwrite", (s) => s.clear());
  await tx(CACHE, "readwrite", (s) => s.clear());
  queue = [];
  emit();
}

/** Throw away a queued register (after the server refused it, or when the teacher no longer wants it). */
export async function discardQueued(key: string) {
  await remove(QUEUE, key);
  await refresh();
}

// ---------- Retrying ----------

let flushing: Promise<void> | null = null;

/** Sends every queued register of the signed-in school, oldest first. Stops at the first connection problem. */
export function flushQueue(): Promise<void> {
  flushing ??= (async () => {
    try {
      const tenantId = currentTenantId();
      const userId = currentUserId();
      if (!tenantId || !userId) return;
      // Only this user's registers: one queued by someone else on a shared phone is never sent under this account.
      const pending = (await all<QueuedRegister>(QUEUE))
        .filter((q) => isMine(q, tenantId, userId) && !q.failed)
        .sort((a, b) => a.queuedAt.localeCompare(b.queuedAt));
      for (const item of pending) {
        // Signed out (or switched account) while flushing: stop.
        if (currentTenantId() !== tenantId || currentUserId() !== userId) break;
        try {
          const register = await send(item);
          sentListeners.forEach((l) => l(item.key, register));
        } catch (e) {
          const err = toApiError(e);
          if (isConnectionError(err) || err.status === 401) break;
          // The server refused it: someone else saved the register since (409), or for example the date is now outside the edit
          // window. Keep it, marked, for the teacher to see; it is not sent again until they save again.
          const stale = err.status === 409;
          const current = await get<QueuedRegister>(QUEUE, item.key);
          if (current && current.clientId === item.clientId) await put(QUEUE, { ...current, failed: stale ? STALE_REGISTER : err.message, stale });
        }
      }
    } finally {
      await refresh();
      flushing = null;
    }
  })();
  return flushing;
}

let users = 0;
let timer: ReturnType<typeof setInterval> | null = null;
const onOnline = () => void flushQueue();

/** Starts retrying: on the `online` event, every 30 seconds, and once now. Returns the stop function. Safe to call from several places. */
export function startRegisterSync(): () => void {
  if (typeof window === "undefined") return () => {};
  users++;
  if (users === 1) {
    window.addEventListener("online", onOnline);
    timer = setInterval(() => {
      if (queue.some((q) => !q.failed)) void flushQueue();
    }, RETRY_MS);
    void refresh().then(() => flushQueue());
  }
  return () => {
    users--;
    if (users === 0) {
      window.removeEventListener("online", onOnline);
      if (timer) clearInterval(timer);
      timer = null;
    }
  };
}

/** Mounted once in the staff layout, so queued registers send from any page, not only the register. */
export function RegisterSync() {
  useEffect(() => startRegisterSync(), []);
  return null;
}
