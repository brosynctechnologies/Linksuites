"use client";

import type { BillingDto } from "./types";
import { useApi } from "./use-api";

/**
 * Whether the current plan includes a feature limit key such as `expenses` or `reports` (0/1 in the
 * phase 2 contract). `null` while unknown (still loading, or billing isn't readable for this role):
 * callers should then rely on the API's 402 response instead.
 */
export function usePlanFeature(key: string): boolean | null {
  const { data } = useApi<BillingDto>("/api/billing");
  const limit = data?.currentPlan?.limits?.[key];
  if (typeof limit !== "number") return null;
  return limit !== 0;
}
