"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useCallback } from "react";

/**
 * Filters held in the URL query (so a filtered list can be shared or reloaded).
 * `set` merges a patch; any change other than `page` resets to page 1.
 */
export function useUrlFilters<K extends string>(keys: readonly K[]) {
  const sp = useSearchParams();
  const pathname = usePathname();
  const router = useRouter();
  const values = Object.fromEntries(keys.map((k) => [k, sp.get(k) ?? ""])) as Record<K, string>;
  const page = Math.max(1, Number(sp.get("page")) || 1);

  const set = useCallback(
    (patch: Partial<Record<K | "page", string | number | null>>) => {
      const next = new URLSearchParams(sp.toString());
      for (const [k, v] of Object.entries(patch) as [string, string | number | null | undefined][]) {
        if (v === null || v === undefined || v === "") next.delete(k);
        else next.set(k, String(v));
      }
      if (!("page" in patch)) next.delete("page");
      if (next.get("page") === "1") next.delete("page");
      const q = next.toString();
      router.replace(q ? `${pathname}?${q}` : pathname, { scroll: false });
    },
    [sp, pathname, router],
  );

  const clear = useCallback(() => router.replace(pathname, { scroll: false }), [pathname, router]);
  const active = keys.some((k) => values[k] !== "");

  return { values, page, set, clear, active };
}
