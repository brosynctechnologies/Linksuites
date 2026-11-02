"use client";

import { useState } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { BillingDto, PlanDto } from "@/lib/types";
import { ApiErrorView, Badge, Button, ErrorBox, Notice, PageHeader, PageLoader, Panel, cx, formatDate } from "@/components/ui";
import { CheckIcon } from "@/components/icons";

export default function BillingPage() {
  const { data, error, reload } = useApi<BillingDto>("/api/billing");
  const [busy, setBusy] = useState<string | null>(null);
  const [checkoutError, setCheckoutError] = useState<ApiError | null>(null);
  // Support views can look at the plan but not buy one.
  const readOnly = !!useAuth()?.support;

  async function upgrade(plan: PlanDto) {
    setBusy(plan.code);
    setCheckoutError(null);
    try {
      const res = await api<{ authorizationUrl: string }>("/api/billing/checkout", { method: "POST", body: { planCode: plan.code } });
      window.location.assign(res.authorizationUrl);
    } catch (e) {
      setCheckoutError(toApiError(e));
      setBusy(null);
    }
  }

  if (!data) {
    return (
      <div>
        <PageHeader title="Plan & billing" back="/dashboard/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader rows={2} />}
      </div>
    );
  }

  const { usage, currentPlan } = data;
  const unlimited = usage.maxProducts === -1;
  const pct = unlimited || usage.maxProducts <= 0 ? 0 : Math.min(100, Math.round((usage.products / usage.maxProducts) * 100));

  return (
    <div className="space-y-6">
      <PageHeader title="Plan & billing" back="/dashboard/more" />

      <Panel>
        <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-4">
          <div>
            <p className="text-sm text-muted">Current plan</p>
            <p className="font-display text-3xl font-bold text-ink">{currentPlan.name}</p>
            {data.subscription?.currentPeriodEnd && (
              <p className="mt-1 flex flex-wrap gap-x-3 text-sm text-ink-2">
                <span>{data.subscription.complimentary ? "Complimentary" : data.subscription.status}</span>
                <span>{data.subscription.complimentary ? "Until" : "Ends"} {formatDate(data.subscription.currentPeriodEnd, false)}. Plans don&apos;t renew on their own.</span>
              </p>
            )}
          </div>
          <div className="w-full sm:max-w-xs">
            <div className="flex justify-between text-sm">
              <span className="text-ink-2">Products</span>
              <span className="font-semibold text-ink num">
                {usage.products} / {unlimited ? "∞" : usage.maxProducts}
              </span>
            </div>
            <div
              className="mt-1.5 h-2 overflow-hidden rounded-full bg-line"
              role="progressbar"
              aria-valuenow={usage.products}
              aria-valuemin={0}
              aria-valuemax={unlimited ? undefined : usage.maxProducts}
              aria-label="Product usage"
            >
              <div className={cx("h-full rounded-full", pct >= 90 ? "bg-hibiscus" : "bg-ink")} style={{ width: `${unlimited ? 4 : pct}%` }} />
            </div>
          </div>
        </div>
      </Panel>

      {!data.paymentsEnabled && <Notice tone="warning">Online payments aren&apos;t set up yet, so upgrades are unavailable for now.</Notice>}
      {checkoutError && <ErrorBox error={checkoutError} />}

      <div className="grid gap-4 md:grid-cols-2">
        {data.plans.map((plan) => {
          const current = plan.code === currentPlan.code;
          return (
            <section
              key={plan.code}
              className={cx(
                "flex flex-col rounded-panel border bg-surface p-5",
                current ? "border-ink ring-1 ring-ink" : "border-line",
              )}
            >
              <div className="flex items-center justify-between gap-3">
                <h2 className="font-display text-lg font-bold text-ink">{plan.name}</h2>
                {current && <Badge tone="ink">Current plan</Badge>}
              </div>
              <p className="mt-2 font-display text-3xl font-bold text-ink num">
                {plan.priceMinor === 0 ? "Free" : formatMoney(plan.priceMinor, plan.currency)}
                {plan.priceMinor > 0 && (
                  <span className="font-sans text-sm font-medium tracking-normal text-muted">/{plan.interval.toLowerCase().replace(/ly$/, "")}</span>
                )}
              </p>
              <ul className="mt-4 flex-1 space-y-2 text-sm text-ink-2">
                {plan.features.map((f) => (
                  <li key={f} className="flex gap-2">
                    <CheckIcon width={18} height={18} className="mt-0.5 shrink-0 text-ink" />
                    {f}
                  </li>
                ))}
              </ul>
              {!current && plan.priceMinor > 0 && (
                <Button
                  className="mt-5 w-full"
                  loading={busy === plan.code}
                  disabled={!data.paymentsEnabled || !!busy || readOnly}
                  onClick={() => upgrade(plan)}
                >
                  Upgrade to {plan.name}
                </Button>
              )}
            </section>
          );
        })}
      </div>
    </div>
  );
}
