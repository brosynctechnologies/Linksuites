import { api, ApiError } from "./api";
import { getAuth } from "./auth";
import { SITE_URL } from "./config";

/*
 * Installable app (Wave 4, PWA): the service worker, the install prompt's visit count and Web Push.
 * The worker itself is public/sw.js; the manifest is app/manifest.ts.
 */

/** The product code push subscriptions are saved under (the API's product codes). */
export const PUSH_PRODUCT = "whatsapp-orders";
const PREFIX = "orderlink.pwa.";
/** This app's signed-in areas. The last one used is remembered. */
const HOMES: readonly string[] = ["/dashboard"];

/** Set at build time (next.config.ts), so each deploy registers a new worker, which drops the old caches. */
const SW_VERSION = process.env.NEXT_PUBLIC_SW_VERSION || "dev";
/** Browser sessions a signed-in user has before the install prompt shows. */
export const INSTALL_AFTER_VISITS = 3;

function read(key: string): string | null {
  try {
    return window.localStorage.getItem(PREFIX + key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    window.localStorage.setItem(PREFIX + key, value);
  } catch {
    /* storage unavailable */
  }
}

/** The worker runs only in production builds, over HTTPS, on this app's own address (never on a custom domain). */
export function serviceWorkerAllowed(): boolean {
  if (process.env.NODE_ENV !== "production" || typeof window === "undefined" || !("serviceWorker" in navigator)) return false;
  if (window.location.protocol !== "https:") return false;
  try {
    return window.location.hostname === new URL(SITE_URL).hostname;
  } catch {
    return false;
  }
}

export async function registerServiceWorker(): Promise<ServiceWorkerRegistration | null> {
  if (!serviceWorkerAllowed()) return null;
  try {
    return await navigator.serviceWorker.register(`/sw.js?v=${encodeURIComponent(SW_VERSION)}`, { scope: "/", updateViaCache: "none" });
  } catch {
    return null;
  }
}

async function registration(): Promise<ServiceWorkerRegistration | null> {
  if (!serviceWorkerAllowed()) return null;
  return (await navigator.serviceWorker.getRegistration("/")) ?? (await registerServiceWorker());
}

/** Counts one visit per browser session (tab session) of a signed-in user. */
export function recordVisit() {
  try {
    if (window.sessionStorage.getItem(PREFIX + "visit")) return;
    window.sessionStorage.setItem(PREFIX + "visit", "1");
  } catch {
    return;
  }
  write("visits", String(visitCount() + 1));
}

export function visitCount(): number {
  return Number(read("visits")) || 0;
}

export function installDismissed(): boolean {
  return read("install-dismissed") !== null;
}

/** "Not now" is remembered on this browser: the prompt does not come back. */
export function dismissInstall() {
  write("install-dismissed", new Date().toISOString());
}

export function isStandalone(): boolean {
  if (typeof window === "undefined") return false;
  return window.matchMedia("(display-mode: standalone)").matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;
}

export function isIos(): boolean {
  if (typeof navigator === "undefined") return false;
  const ua = navigator.userAgent;
  return /iPad|iPhone|iPod/.test(ua) || (ua.includes("Macintosh") && navigator.maxTouchPoints > 1);
}

export function rememberHome(pathname: string) {
  const home = HOMES.find((h) => pathname === h || pathname.startsWith(`${h}/`));
  if (home && read("home") !== home) write("home", home);
}

// ---------- Push notifications ----------

/**
 * unsupported: no Web Push here (an old browser, a development build or plain http). install-first: iPhone and iPad only get
 * notifications from the installed app. unavailable: the API has no VAPID keys set. blocked: the user denied notifications.
 */
export type PushState = "unsupported" | "install-first" | "unavailable" | "blocked" | "off" | "on";

interface PushStatusDto {
  enabled: boolean;
  publicKey: string | null;
  subscriptions: { id: string; product: string; endpoint: string }[];
}

function keyBytes(base64Url: string): Uint8Array<ArrayBuffer> {
  const base64 = (base64Url + "=".repeat((4 - (base64Url.length % 4)) % 4)).replace(/-/g, "+").replace(/_/g, "/");
  const raw = window.atob(base64);
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

function sameKey(current: ArrayBuffer | null, publicKey: string): boolean {
  if (!current) return false;
  const a = new Uint8Array(current);
  const b = keyBytes(publicKey);
  return a.length === b.length && a.every((v, i) => v === b[i]);
}

export async function readPushState(): Promise<{ state: PushState; publicKey: string | null }> {
  if (isIos() && !isStandalone()) return { state: "install-first", publicKey: null };
  if (!serviceWorkerAllowed() || !("PushManager" in window) || !("Notification" in window)) return { state: "unsupported", publicKey: null };
  const status = await api<PushStatusDto>("/api/me/push");
  if (!status.enabled || !status.publicKey) return { state: "unavailable", publicKey: null };
  if (Notification.permission === "denied") return { state: "blocked", publicKey: status.publicKey };
  const reg = await registration();
  const sub = await reg?.pushManager.getSubscription();
  const mine = !!sub && Notification.permission === "granted" && status.subscriptions.some((s) => s.endpoint === sub.endpoint && s.product === PUSH_PRODUCT);
  return { state: mine ? "on" : "off", publicKey: status.publicKey };
}

/** Asks for permission, subscribes this browser and saves the subscription for the signed-in user. */
export async function enablePush(publicKey: string): Promise<PushState> {
  const permission = await Notification.requestPermission();
  if (permission !== "granted") return permission === "denied" ? "blocked" : "off";
  const reg = await registration();
  if (!reg) return "unsupported";
  await navigator.serviceWorker.ready;
  let sub = await reg.pushManager.getSubscription();
  // A subscription made with an older key can't receive from the new one.
  if (sub && !sameKey(sub.options.applicationServerKey, publicKey)) {
    await sub.unsubscribe();
    sub = null;
  }
  sub ??= await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(publicKey) });
  const json = sub.toJSON();
  await api("/api/me/push/subscriptions", { method: "POST", body: { product: PUSH_PRODUCT, endpoint: json.endpoint, keys: json.keys } });
  return "on";
}

export async function disablePush(): Promise<void> {
  const reg = await registration();
  const sub = await reg?.pushManager.getSubscription();
  if (!sub) return;
  await api("/api/me/push/subscriptions/remove", { method: "POST", body: { endpoint: sub.endpoint } }).catch((e: unknown) => {
    if (!(e instanceof ApiError && e.status === 404)) throw e;
  });
  await sub.unsubscribe();
}

/** On sign-out: this browser stops getting the account's notifications. Never throws. */
export async function forgetPushOnThisDevice(): Promise<void> {
  try {
    const auth = getAuth();
    if (!auth || auth.support || !serviceWorkerAllowed()) return;
    const reg = await navigator.serviceWorker.getRegistration("/");
    const sub = await reg?.pushManager.getSubscription();
    if (!sub) return;
    await api("/api/me/push/subscriptions/remove", { method: "POST", body: { endpoint: sub.endpoint } }).catch(() => {});
    await sub.unsubscribe();
  } catch {
    /* best effort */
  }
}
