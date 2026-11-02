"use client";

import { useId, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { setAuth, useAuth } from "@/lib/auth";
import { useApi } from "@/lib/use-api";
import type { AuthResponse, MyBusinessDto } from "@/lib/types";

/**
 * Lets someone who belongs to several PropertyLink businesses switch between them.
 * Renders nothing unless GET /api/me/businesses returns more than one, or in a support view. Styled for the Asphalt rail.
 */
export function BusinessSwitcher({ className }: { className?: string }) {
  const id = useId();
  const support = !!useAuth()?.support;
  const { data } = useApi<MyBusinessDto[]>(support ? null : "/api/me/businesses");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const list = Array.isArray(data) ? data : [];
  if (support || list.length <= 1) return null;
  const current = list.find((b) => b.current)?.tenantId ?? "";

  async function switchTo(tenantId: string) {
    if (!tenantId || tenantId === current) return;
    setBusy(true);
    setError(null);
    try {
      const res = await api<AuthResponse>("/api/auth/switch", { method: "POST", body: { tenantId } });
      setAuth(res);
      // Hard navigation on purpose: every page and in-memory request must start fresh for the new business.
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination
      window.location.assign("/studio");
    } catch (e) {
      setError(toApiError(e));
      setBusy(false);
    }
  }

  return (
    <div className={className}>
      <label htmlFor={id} className="block text-xs font-medium text-on-asphalt">
        Switch business
      </label>
      <select
        id={id}
        className="mt-1 block min-h-11 w-full rounded-control border border-white/15 bg-asphalt px-2.5 text-sm text-white focus:border-adire-light disabled:opacity-60"
        value={current}
        disabled={busy}
        onChange={(e) => switchTo(e.target.value)}
      >
        {list.map((b) => (
          <option key={b.tenantId} value={b.tenantId}>
            {b.name} ({b.role})
          </option>
        ))}
      </select>
      {error && <p className="mt-1 text-xs text-[#ffb4ab]">{error.message}</p>}
    </div>
  );
}
