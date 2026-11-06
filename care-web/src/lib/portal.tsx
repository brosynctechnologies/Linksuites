"use client";

// The patient portal's chosen patient (Task 20), the way school-web's parent pages choose a child. A portal account is linked to patients in
// any number of clinics; every page works on one of them. The choice travels in the address as `?patient={id}` (the API's notice links
// carry the same marker, and an id is not clinical) and the last one is remembered in this browser by id only.

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, type ReactNode } from "react";
import type { ApiError } from "./api";
import { useHydrated } from "./auth";
import { STORAGE_PREFIX } from "./config";
import { useApi } from "./use-api";
import type { PortalPatientRef } from "./types/portal";

/** The API's sentence for a suspended clinic (CarePortalGate). */
export const PORTAL_UNAVAILABLE = "This clinic's portal is unavailable.";
/** The API's sentence for a late portal cancel (AppointmentService.LateCancel). */
export const LATE_CANCEL = "This appointment starts soon. Call the clinic to change it.";

const PATIENT_KEY = `${STORAGE_PREFIX}portalPatient`;

function readStored(): string | null {
  try {
    return window.localStorage.getItem(PATIENT_KEY);
  } catch {
    return null;
  }
}

function writeStored(id: string) {
  try {
    window.localStorage.setItem(PATIENT_KEY, id);
  } catch {
    /* storage unavailable */
  }
}

/** Forget the remembered patient (on sign out, so the next account on this browser starts fresh). */
export function forgetPortalPatient() {
  try {
    window.localStorage.removeItem(PATIENT_KEY);
  } catch {
    /* ignore */
  }
}

interface PortalState {
  /** The linked patients (active links in open clinics). */
  list: PortalPatientRef[];
  /** The patient pages work on: from the list, or null while loading, when the list is empty, or when the asked-for one is blocked. */
  patient: PortalPatientRef | null;
  /** The id asked for in the address that the list doesn't hold (a suspended clinic, or no longer linked). */
  blockedId: string | null;
  /** Why `blockedId` can't be opened: the API's own answer (403 with the clinic's sentence, or 404). */
  blockedError?: ApiError;
  /** The asked-for id answered after all (linked since the list loaded): reload the list. */
  blockedOk: boolean;
  error?: ApiError;
  loading: boolean;
  select: (id: string) => void;
  reload: () => void;
  /**
   * Final fix pass (Task 20 carry): a patient route answered 404 mid-session (the link was revoked). Forgets the patient if it was the
   * remembered one and reloads the list, so the switcher drops it and the pages (keyed by patient) remount without its stale data.
   */
  lost: (patientId: string) => void;
}

const Ctx = createContext<PortalState | null>(null);

export function PortalPatientProvider({ children }: { children: ReactNode }) {
  const hydrated = useHydrated();
  const list = useApi<PortalPatientRef[]>("/api/care-portal/patients");
  const asked = useSearchParams().get("patient");
  const router = useRouter();
  const pathname = usePathname();

  const available = useMemo(() => list.data ?? [], [list.data]);
  const fromAddress = asked ? available.find((p) => p.id === asked) : undefined;
  // An id in the address that isn't in the list is asked about directly, so the clinic's sentence (suspended) or a 404 shows. The
  // notices route is the lightest patient route and reads nothing clinical.
  const blockedId = asked && list.data && !fromAddress ? asked : null;
  const probe = useApi<unknown>(blockedId ? `/api/care-portal/patients/${blockedId}/notifications` : null);
  const stored = hydrated ? readStored() : null;
  const patient = blockedId ? null : (fromAddress ?? available.find((p) => p.id === stored) ?? available[0] ?? null);

  const select = useCallback(
    (id: string) => {
      writeStored(id);
      router.replace(`${pathname}?patient=${encodeURIComponent(id)}`);
    },
    [router, pathname],
  );

  const reloadList = list.reload;
  const lost = useCallback(
    (patientId: string) => {
      if (readStored() === patientId) forgetPortalPatient();
      reloadList();
    },
    [reloadList],
  );

  const chosenId = patient?.id;
  useEffect(() => {
    if (chosenId) writeStored(chosenId);
  }, [chosenId]);

  const value = useMemo<PortalState>(
    () => ({
      list: available,
      patient,
      blockedId,
      blockedError: probe.error,
      blockedOk: probe.data !== undefined && !probe.error,
      error: list.error,
      loading: list.loading && !list.data,
      select,
      reload: list.reload,
      lost,
    }),
    [available, patient, blockedId, probe.error, probe.data, list.error, list.loading, list.data, select, list.reload, lost],
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/** The portal state, or null outside the portal's patient pages (the invite and sign-in pages). */
export function usePortalMaybe(): PortalState | null {
  return useContext(Ctx);
}

export function usePortal(): PortalState {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("usePortal must be used inside PortalPatientProvider");
  return ctx;
}

/** A portal page's address for the chosen patient: `/portal/bills?patient={id}`. */
export function portalHref(page: string, patientId: string | null | undefined): string {
  return patientId ? `${page}?patient=${encodeURIComponent(patientId)}` : page;
}

/** The API route for the chosen patient, or null until there is one. */
export function usePatientApi(suffix: string): string | null {
  const { patient } = usePortal();
  return patient ? `/api/care-portal/patients/${patient.id}${suffix}` : null;
}

export function isPortalUnavailable(e: ApiError | null | undefined): boolean {
  return !!e && e.status === 403 && e.message === PORTAL_UNAVAILABLE;
}

/** "Ada's record" for a parent or guardian; "Your record" for the patient themself. */
export function whose(p: PortalPatientRef | null, thing: string): string {
  if (!p || p.relationship === "Self") return `Your ${thing}`;
  return `${p.firstName}'s ${thing}`;
}

export function relationshipLabel(r: PortalPatientRef["relationship"]): string {
  return r === "Self" ? "You" : r === "Parent" ? "Your child" : "In your care";
}

const CHECKOUT_KEY = `${STORAGE_PREFIX}portalCheckout`;

/**
 * Remember which patient a Paystack checkout was for, so the return page can link back to that patient's bills and receipt. This tab
 * only (sessionStorage), and only the reference and the patient's id: nothing clinical.
 */
export function rememberCheckout(reference: string, patientId: string) {
  try {
    window.sessionStorage.setItem(CHECKOUT_KEY, JSON.stringify({ reference, patientId }));
  } catch {
    /* storage unavailable */
  }
}

/** The patient a returning checkout was for, when this tab started it. */
export function checkoutPatient(reference: string): string | null {
  try {
    const raw = window.sessionStorage.getItem(CHECKOUT_KEY);
    const saved = raw ? (JSON.parse(raw) as { reference?: string; patientId?: string }) : null;
    return saved?.reference === reference && saved.patientId ? saved.patientId : null;
  } catch {
    return null;
  }
}
