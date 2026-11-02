"use client";

import { asList } from "./api";
import type { BookingBusinessDto, ServiceDto, StaffDto } from "./types";
import { useApi } from "./use-api";

/** Fallback zone while the business is loading (or unreadable for this role). */
export const FALLBACK_TZ = "Africa/Lagos";

/** The business settings plus its zone and currency with safe fallbacks. */
export function useBusiness() {
  const res = useApi<BookingBusinessDto>("/api/booking-business");
  return {
    ...res,
    business: res.data,
    tz: res.data?.timezone || FALLBACK_TZ,
    currency: res.data?.currency || "NGN",
  };
}

/** Services in their display order. */
export function useServices(nonce?: number) {
  const res = useApi<ServiceDto[]>("/api/services", { nonce });
  const list = asList(res.data).slice().sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));
  return { ...res, services: list };
}

/** Staff in their display order. */
export function useStaff(nonce?: number) {
  const res = useApi<StaffDto[]>("/api/staff", { nonce });
  const list = asList(res.data).slice().sort((a, b) => a.position - b.position || a.displayName.localeCompare(b.displayName));
  return { ...res, staff: list };
}

/** Staff members who can do a service (either side of the link counts). */
export function staffForService(staff: StaffDto[], service: ServiceDto | null | undefined): StaffDto[] {
  if (!service) return [];
  return staff.filter((s) => s.isBookable && (service.staffIds.includes(s.id) || s.serviceIds.includes(service.id)));
}

/**
 * Why customers aren't asked for deposits, from the server's own rule (Paystack connected and a plan that includes
 * deposits). Null when deposits are taken.
 */
export function depositsOffReason(b: Pick<BookingBusinessDto, "depositsEnabled" | "depositsInPlan" | "paystackConnected">): string | null {
  if (b.depositsEnabled) return null;
  if (!b.depositsInPlan && !b.paystackConnected) return "Deposits need a paid plan and a connected Paystack account.";
  if (!b.depositsInPlan) return "Your plan doesn't include deposits. Upgrade in Billing to take them.";
  return "Deposits are off until you connect Paystack in Settings.";
}
