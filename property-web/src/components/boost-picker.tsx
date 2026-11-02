"use client";

import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { BoostOptionDto } from "@/lib/types";
import { ApiErrorView, Button, PageLoader, cx } from "./ui";

/** Pick a boost and pay on Paystack (returns to /studio/boosts/{id}). */
export function BoostPicker({ listingId }: { listingId: string }) {
  const { data, error, reload } = useApi<BoostOptionDto[]>("/api/property/boost-options");
  const [code, setCode] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [payError, setPayError] = useState<ApiError | null>(null);
  // Support views can see the options but not pay for a boost.
  const readOnly = !!useAuth()?.support;
  const options = asList(data);

  async function pay() {
    if (!code) return;
    setBusy(true);
    setPayError(null);
    try {
      const res = await api<{ boostId: string; authorizationUrl: string; amountMinor: number }>(`/api/property/listings/${listingId}/boosts`, {
        method: "POST",
        body: { code },
      });
      window.location.assign(res.authorizationUrl);
    } catch (e) {
      setPayError(toApiError(e));
      setBusy(false);
    }
  }

  if (!data) return error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />;
  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-700">Featured listings carry an Adire tag and appear first on the home page, from every city (taking turns when there are many). Top listings sit above others in search.</p>
      <fieldset className="space-y-2">
        <legend className="sr-only">Boost</legend>
        {options.map((o) => (
          <label
            key={o.code}
            className={cx("flex min-h-11 cursor-pointer items-center justify-between gap-3 rounded-control border p-3", code === o.code ? "border-adire bg-adire-wash" : "border-line")}
          >
            <span className="flex items-center gap-3">
              <input type="radio" name="boost" checked={code === o.code} onChange={() => setCode(o.code)} className="accent-[#33309a]" />
              <span className="text-sm font-semibold text-asphalt">{o.name}</span>
            </span>
            <span className="price text-asphalt">{formatMoney(o.priceMinor, o.currency)}</span>
          </label>
        ))}
      </fieldset>
      {payError && <ApiErrorView error={payError} />}
      <Button className="w-full" disabled={!code || readOnly} loading={busy} onClick={pay}>
        Pay with Paystack
      </Button>
    </div>
  );
}
