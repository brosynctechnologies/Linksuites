"use client";

import { useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useAuth } from "@/lib/auth";
import { useCan } from "@/lib/permissions";
import type { BillingDto, PlanDto } from "@/lib/types";
import { ApiErrorView, Button, Card, ErrorBox, Notice, PageHeader, PageLoader, StatusBadge, UsageMeter, cx } from "@/components/ui";
import { CheckIcon } from "@/components/icons";
import { PlanPaymentNotice } from "@/components/plan-payment-notice";
import { usePlanPaymentReturn } from "@/lib/plan-payment";
import { PendingTransferNotice, PlanTransferPanel } from "@/components/plan-transfer";

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
  // Back from the secure checkout with ?ref=: confirm the payment, and show the new plan once it is active.
  const payment = usePlanPaymentReturn(reload);
  const [checkoutError, setCheckoutError] = useState<ApiError | null>(null);
  // While online payment is off, a plan is paid by bank transfer: the plan whose transfer details are open.
  const [transferPlan, setTransferPlan] = useState<PlanDto | null>(null);

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
        <PageHeader title="Plan & billing" back="/app/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const { currentPlan } = data;
  // Bank transfer is offered only while online payment is off (the API sends the details only then).
  const bank = data.paymentsEnabled ? null : (data.bankTransfer ?? null);
  // One transfer at a time: while one is being checked, no plan offers another (the notice above says what is pending).
  const offerTransfer = !(bank && data.pendingTransfer);
  const usage = Array.isArray(data.usage?.items) ? data.usage.items : [];
  // Free → Pro → Business, cheapest first, whatever order the API returns.
  const plans = [...(data.plans ?? [])].sort((a, b) => a.priceMinor - b.priceMinor);
  const currentRank = plans.findIndex((p) => p.code === currentPlan.code);
  const isUpgrade = (plan: PlanDto) => {
    const rank = plans.findIndex((p) => p.code === plan.code);
    return currentRank === -1 ? plan.priceMinor > currentPlan.priceMinor : rank > currentRank;
  };

  return (
    <div className="space-y-4">
      <PageHeader title="Plan & billing" back="/app/more" />
      <PlanPaymentNotice payment={payment} />

      <Card className="grid gap-5 md:grid-cols-[minmax(0,14rem)_minmax(0,1fr)] md:gap-8">
        <div>
          <p className="text-sm text-muted">Current plan</p>
          <p className="font-figure mt-0.5 text-[1.75rem] leading-tight text-graphite">{currentPlan.name}</p>
          {data.subscription?.currentPeriodEnd && (
            <p className="mt-1 text-sm text-slate-600">
              {data.subscription.complimentary
                ? `Complimentary until ${formatDay(data.subscription.currentPeriodEnd)}.`
                : `Ends ${formatDay(data.subscription.currentPeriodEnd)}. Plans don't renew on their own: pay again before then to keep ${currentPlan.name}.`}
            </p>
          )}
        </div>
        {usage.length > 0 && (
          <div className="space-y-3.5 md:border-l md:border-rule md:pl-8">
            {usage.map((u) => (
              <UsageMeter key={u.key} label={u.label} used={u.used} limit={u.limit} />
            ))}
          </div>
        )}
      </Card>

      {data.pendingTransfer && <PendingTransferNotice pending={data.pendingTransfer} canCancel={canManage && !readOnly} onCancelled={reload} />}
      {!data.paymentsEnabled && !bank && <Notice tone="warning">Upgrades are unavailable until online payments are set up. Try again later.</Notice>}
      {transferPlan && bank && offerTransfer && (
        <PlanTransferPanel
          plan={transferPlan}
          bank={bank}
          onClaimed={() => {
            setTransferPlan(null);
            reload();
          }}
          onClose={() => setTransferPlan(null)}
        />
      )}
      {checkoutError && <ErrorBox error={checkoutError} />}
      {!canManage && <Notice tone="info">Only the business owner can change the plan.</Notice>}

      <div className={cx("grid overflow-hidden rounded-panel border border-rule bg-paper", plans.length >= 3 ? "lg:grid-cols-3" : "md:grid-cols-2")}>
        {plans.map((plan, i) => {
          const current = plan.code === currentPlan.code;
          return (
            <section
              key={plan.code}
              aria-label={plan.name}
              className={cx(
                "relative flex flex-col p-5 sm:p-6",
                i > 0 && (plans.length >= 3 ? "border-t border-rule lg:border-t-0 lg:border-l" : "border-t border-rule md:border-t-0 md:border-l"),
                current && "bg-ink-wash/40",
              )}
            >
              {current && <span className="absolute inset-x-0 top-0 h-1 bg-ink" aria-hidden="true" />}
              <div className="flex items-center justify-between gap-3">
                <h2 className="text-lg font-semibold text-graphite">{plan.name}</h2>
                {current && <StatusBadge tone="ink">Your plan</StatusBadge>}
              </div>
              <p className="mt-3 flex items-baseline gap-1">
                <span className="font-figure amount text-[2.25rem] leading-none text-graphite">
                  {plan.priceMinor === 0 ? "Free" : formatMoney(plan.priceMinor, plan.currency)}
                </span>
                {plan.priceMinor > 0 && <span className="text-sm text-muted">/{intervalLabel(plan.interval)}</span>}
              </p>
              <ul className="mt-5 flex-1 space-y-2 text-sm text-slate-700">
                {plan.features.map((f) => (
                  <li key={f} className="flex gap-2.5">
                    <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-paid" />
                    {f}
                  </li>
                ))}
              </ul>
              {!current && canManage && isUpgrade(plan) && offerTransfer && (
                <Button
                  className="mt-6 w-full"
                  loading={busy === plan.code}
                  disabled={!(data.paymentsEnabled || bank) || !!busy || readOnly}
                  onClick={() => (bank ? setTransferPlan(plan) : upgrade(plan))}
                >
                  {bank ? "Pay by bank transfer" : `Upgrade to ${plan.name}`}
                </Button>
              )}
            </section>
          );
        })}
      </div>
    </div>
  );
}
