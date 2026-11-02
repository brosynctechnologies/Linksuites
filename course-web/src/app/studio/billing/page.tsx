"use client";

import { useState } from "react";
import { useSearchParams } from "next/navigation";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useAuth } from "@/lib/auth";
import { useCan } from "@/lib/permissions";
import type { BillingDto, PlanDto } from "@/lib/types";
import { ApiErrorView, Button, Card, ErrorBox, Notice, PageHeader, PageLoader, StatusBadge, UsageMeter, cx } from "@/components/ui";
import { CheckIcon } from "@/components/icons";
import { MARKETPLACE_FEE_PCT } from "@/lib/config";

function intervalLabel(interval: string) {
  const i = interval.toLowerCase();
  if (i.startsWith("month")) return "month";
  if (i.startsWith("year") || i.startsWith("annual")) return "year";
  if (i.startsWith("week")) return "week";
  return i.replace(/ly$/, "");
}

export default function BillingPage() {
  const { data, error, reload } = useApi<BillingDto>("/api/billing");
  const canManage = useCan("billing.manage");
  // Support views can look at the plan but not buy one.
  const readOnly = !!useAuth()?.support;
  const [busy, setBusy] = useState<string | null>(null);
  const [checkoutError, setCheckoutError] = useState<ApiError | null>(null);
  // Paystack sends the payer back here with ?ref=; the plan itself changes when the payment webhook arrives.
  const returnedFromPayment = !!useSearchParams().get("ref");

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
        <PageHeader title="Plan & billing" back="/studio/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const { currentPlan } = data;
  const usage = Array.isArray(data.usage?.items) ? data.usage.items : [];
  // Free → Pro → Business, cheapest first, whatever order the API returns.
  const plans = [...(data.plans ?? [])].sort((a, b) => a.priceMinor - b.priceMinor);
  const currentRank = plans.findIndex((p) => p.code === currentPlan.code);
  const isUpgrade = (plan: PlanDto) => {
    const rank = plans.findIndex((p) => p.code === plan.code);
    return currentRank === -1 ? plan.priceMinor > currentPlan.priceMinor : rank > currentRank;
  };
  // Paying again for the plan you're on adds a period after the current one ends. A comp is never extended by a payment.
  const canExtend = currentPlan.priceMinor > 0 && !!data.subscription && !data.subscription.complimentary;

  return (
    <div className="space-y-4">
      <PageHeader title="Plan & billing" back="/studio/more" subtitle={`Plans change your course limit, video storage and the fee on academy sales. Marketplace sales carry a ${MARKETPLACE_FEE_PCT}% fee on every plan.`} />

      <Card className="grid gap-5 md:grid-cols-[minmax(0,14rem)_minmax(0,1fr)] md:gap-8">
        <div>
          <p className="text-sm text-muted">Current plan</p>
          <p className="mt-0.5 text-[1.75rem] leading-tight font-extrabold text-ink">{currentPlan.name}</p>
          {data.subscription?.currentPeriodEnd && (
            <p className="mt-1 text-sm text-slate-600">
              {data.subscription.complimentary
                ? `Complimentary until ${formatDay(data.subscription.currentPeriodEnd)}.`
                : `Ends ${formatDay(data.subscription.currentPeriodEnd)}. Plans don't renew on their own: extend before then to keep ${currentPlan.name}.`}
            </p>
          )}
        </div>
        {usage.length > 0 && (
          <div className="space-y-3.5 md:border-l md:border-line md:pl-8">
            {usage.map((u) => (
              <UsageMeter key={u.key} label={u.label} used={u.used} limit={u.limit} />
            ))}
          </div>
        )}
      </Card>

      {returnedFromPayment && (
        <Notice tone="info">Payment received. Your plan updates as soon as Paystack confirms it, usually within a minute. Refresh this page if it hasn&apos;t changed yet.</Notice>
      )}
      {!data.paymentsEnabled && <Notice tone="warning">Upgrades are unavailable until online payments are set up. Try again later.</Notice>}
      {checkoutError && <ErrorBox error={checkoutError} />}
      {!canManage && <Notice tone="info">Only the academy owner can change the plan.</Notice>}

      <div className={cx("grid overflow-hidden rounded-panel border border-line bg-surface", plans.length >= 3 ? "lg:grid-cols-3" : "md:grid-cols-2")}>
        {plans.map((plan, i) => {
          const current = plan.code === currentPlan.code;
          return (
            <section
              key={plan.code}
              aria-label={plan.name}
              className={cx(
                "relative flex flex-col p-5 sm:p-6",
                i > 0 && (plans.length >= 3 ? "border-t border-line lg:border-t-0 lg:border-l" : "border-t border-line md:border-t-0 md:border-l"),
                current && "bg-lagoon-wash",
              )}
            >
              {current && <span className="absolute inset-x-0 top-0 h-1 bg-lagoon" aria-hidden="true" />}
              <div className="flex items-center justify-between gap-3">
                <h2 className="text-lg font-semibold text-ink">{plan.name}</h2>
                {current && <StatusBadge tone="lagoon">Your plan</StatusBadge>}
              </div>
              <p className="mt-3 flex items-baseline gap-1">
                <span className="tnum text-[2.25rem] leading-none font-extrabold text-ink">
                  {plan.priceMinor === 0 ? "Free" : formatMoney(plan.priceMinor, plan.currency)}
                </span>
                {plan.priceMinor > 0 && <span className="text-sm text-muted">/{intervalLabel(plan.interval)}</span>}
              </p>
              <ul className="mt-5 flex-1 space-y-2 text-sm text-slate-700">
                {plan.features.map((f) => (
                  <li key={f} className="flex gap-2.5">
                    <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-lagoon-text" />
                    {f}
                  </li>
                ))}
              </ul>
              {canManage && ((!current && isUpgrade(plan)) || (current && canExtend)) && (
                <Button
                  className="mt-6 w-full"
                  loading={busy === plan.code}
                  disabled={!data.paymentsEnabled || !!busy || readOnly}
                  onClick={() => upgrade(plan)}
                >
                  {current ? `Extend ${plan.name}` : `Upgrade to ${plan.name}`}
                </Button>
              )}
            </section>
          );
        })}
      </div>
    </div>
  );
}
