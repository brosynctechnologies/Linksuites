"use client";

import { usePathname } from "next/navigation";
import { useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { ApiError, toApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { APP_NAME } from "@/lib/config";
import {
  INSTALL_AFTER_VISITS,
  disablePush,
  dismissInstall,
  enablePush,
  installDismissed,
  isIos,
  isStandalone,
  readPushState,
  recordVisit,
  registerServiceWorker,
  rememberHome,
  serviceWorkerAllowed,
  visitCount,
  type PushState,
} from "@/lib/pwa";
import { Button, Card, ErrorBox, Notice, SectionTitle } from "./ui";

/*
 * Installable app (Wave 4, PWA): <PwaSetup /> in the root layout registers the service worker and, after a few visits, offers a
 * signed-in user to install the app. <PushNotifications /> is the opt-in on the account page.
 */

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

// Chrome's install event arrives once per page load, possibly before React renders, so it is kept outside React.
let deferredPrompt: BeforeInstallPromptEvent | null = null;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

if (typeof window !== "undefined") {
  window.addEventListener("beforeinstallprompt", (e) => {
    e.preventDefault();
    deferredPrompt = e as BeforeInstallPromptEvent;
    emit();
  });
  window.addEventListener("appinstalled", () => {
    deferredPrompt = null;
    dismissInstall();
    emit();
  });
}

type Offer = "none" | "prompt" | "ios";

function offerSnapshot(): Offer {
  if (!serviceWorkerAllowed() || isStandalone() || installDismissed() || visitCount() < INSTALL_AFTER_VISITS) return "none";
  if (deferredPrompt) return "prompt";
  return isIos() ? "ios" : "none";
}

/** Root-layout piece: the service worker, the visit count and the install prompt. */
export function PwaSetup() {
  const auth = useAuth();
  const pathname = usePathname();
  const signedIn = !!auth && !auth.support;
  const offer = useSyncExternalStore(subscribe, offerSnapshot, () => "none" as Offer);

  useEffect(() => {
    void registerServiceWorker();
  }, []);

  useEffect(() => {
    if (!signedIn) return;
    recordVisit();
    emit();
  }, [signedIn]);

  useEffect(() => {
    if (signedIn) rememberHome(pathname);
  }, [signedIn, pathname]);

  if (!signedIn || offer === "none") return null;

  function notNow() {
    dismissInstall();
    emit();
  }

  async function install() {
    const prompt = deferredPrompt;
    if (!prompt) return;
    deferredPrompt = null;
    await prompt.prompt().catch(() => {});
    const choice = await prompt.userChoice.catch(() => null);
    // Declined in the browser's own dialog: don't ask again.
    if (choice?.outcome !== "accepted") dismissInstall();
    emit();
  }

  return (
    <aside
      aria-label={`Install ${APP_NAME}`}
      className="fixed inset-x-3 bottom-[calc(5rem+env(safe-area-inset-bottom))] z-40 rounded-2xl border border-black/10 bg-surface p-4 shadow-lg lg:inset-x-auto lg:right-6 lg:bottom-6 lg:w-96 print:hidden"
    >
      <div className="flex items-start gap-3">
        <img src="/icon-192.png" alt="" width={40} height={40} className="h-10 w-10 shrink-0 rounded-xl" />
        <div className="min-w-0 flex-1">
          <p className="font-semibold">Install {APP_NAME}</p>
          {offer === "ios" ? (
            <p className="mt-0.5 text-sm text-muted">
              Tap <span className="font-semibold">Share</span>, then <span className="font-semibold">Add to Home Screen</span>. It opens like an app.
            </p>
          ) : (
            <p className="mt-0.5 text-sm text-muted">Open it from your home screen like an app, with notifications.</p>
          )}
        </div>
      </div>
      <div className="mt-3 flex justify-end gap-2">
        <Button variant="ghost" onClick={notNow}>
          {offer === "ios" ? "Got it" : "Not now"}
        </Button>
        {offer === "prompt" && <Button onClick={install}>Install</Button>}
      </div>
    </aside>
  );
}

const PUSH_TEXT: Record<Exclude<PushState, "off" | "on" | "unavailable">, string> = {
  unsupported: `This browser can't show ${APP_NAME} notifications. On Android, use Chrome; on iPhone, install the app first.`,
  "install-first": `On iPhone and iPad, add ${APP_NAME} to your Home Screen first (tap Share, then Add to Home Screen), then turn notifications on in the app.`,
  blocked: `Notifications are blocked for ${APP_NAME} in this browser. Allow them in the browser's site settings, then come back here.`,
};

/** The opt-in for push notifications on this device, for the account page. Hidden when the server has push switched off. */
export function PushNotifications({ description }: { description?: string }) {
  const [state, setState] = useState<PushState | "loading">("loading");
  const [publicKey, setPublicKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  useEffect(() => {
    let live = true;
    readPushState()
      .then((r) => {
        if (!live) return;
        setState(r.state);
        setPublicKey(r.publicKey);
      })
      .catch(() => live && setState("unavailable"));
    return () => {
      live = false;
    };
  }, []);

  if (state === "loading" || state === "unavailable") return null;

  async function toggle(on: boolean) {
    setBusy(true);
    setError(null);
    try {
      if (on && publicKey) setState(await enablePush(publicKey));
      else if (!on) {
        await disablePush();
        setState("off");
      }
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <PushFrame description={description ?? `Get a notification on this device when something needs you, even when ${APP_NAME} is closed.`}>
      {state === "on" || state === "off" ? (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm">{state === "on" ? "On for this device." : "Off for this device."}</p>
          <Button variant={state === "on" ? "secondary" : "primary"} loading={busy} onClick={() => toggle(state !== "on")}>
            {state === "on" ? "Turn off" : "Turn on notifications"}
          </Button>
        </div>
      ) : (
        <Notice tone={state === "blocked" ? "warning" : "info"}>{PUSH_TEXT[state]}</Notice>
      )}
      <ErrorBox error={error} />
    </PushFrame>
  );
}

function PushFrame({ description, children }: { description: string; children: ReactNode }) {
  return (
    <Card>
      <SectionTitle>Notifications on this device</SectionTitle>
      <p className="-mt-2 mb-4 text-sm text-muted">{description}</p>
      <div className="space-y-3">{children}</div>
    </Card>
  );
}

