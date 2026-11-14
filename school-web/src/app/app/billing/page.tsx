"use client";

import { useState } from "react";
import { CheckIcon } from "@/components/icons";
import { PlanPaymentNotice } from "@/components/plan-payment-notice";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Notice, PageHeader, PageLoader, UsageMeter, cx } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatStampDay, naira } from "@/lib/format";
import { can } from "@/lib/roles";
import { usePlanPaymentReturn } from "@/lib/plan-payment";
import { PendingTransferNotice, PlanTransferPanel } from "@/components/plan-transfer";
import { useApi } from "@/lib/use-api";
import type { BillingDto, PlanDto } from "@/lib/types";

/** The API's usage labels, in the school's words. */
const USAGE_LABELS: Record<string, string> = { students: "Students", team_seats: "Staff logins" };

function intervalWord(interval: string) {
  return interval.toLowerCase().replace(/ly$/, "");
}

export default function BillingPage() {
  const { data, error, reload } = useApi<BillingDto>("/api/billing");
  const auth = useAuth();
  const isProprietor = can(auth?.tenant?.role, "billing.manage");
  // A support view can look at the plan but not buy one.
  const readOnly = !!auth?.support;
  const [busy, setBusy] = useState<string | null>(null);
  const [checkoutError, setCheckoutError] = useState<ApiError | null>(null);
  // While online payment is off, a plan is paid by bank transfer: the plan whose transfer details are open.
  const [transferPlan, setTransferPlan] = useState<PlanDto | null>(null);

  // Back from the secure checkout with ?ref=: confirm the payment with the provider, and show the new plan once it is active.
  const payment = usePlanPaymentReturn(reload);

  async function pay(plan: PlanDto) {
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
        <PageHeader title="Plan and billing" back="/app/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const { currentPlan, subscription } = data;
  // Bank transfer is offered only while online payment is off (the API sends the details only then).
  const bank = data.paymentsEnabled ? null : (data.bankTransfer ?? null);
  // One transfer at a time: while one is being checked, no plan offers another (the notice above says what is pending).
  const offerTransfer = !(bank && data.pendingTransfer);
  const usage = Array.isArray(data.usage?.items) ? data.usage.items : [];
  // Free, Standard, Premium: cheapest first, whatever order the API returns.
  const plans = [...(data.plans ?? [])].sort((a, b) => a.priceMinor - b.priceMinor);
  const currentRank = plans.findIndex((p) => p.code === currentPlan.code);
  const isUpgrade = (plan: PlanDto) => {
    const rank = plans.findIndex((p) => p.code === plan.code);
    return currentRank === -1 ? plan.priceMinor > currentPlan.priceMinor : rank > currentRank;
  };
  // One primary button on the page: the next plan up (or renewing, on the top plan). The rest get a quieter button.
  const nextUp = plans.find(isUpgrade)?.code;
  const canPay = isProprietor && !readOnly;
  // "Pay again before then to keep it" needs a way to pay again. A comp is never extended by a payment, so it has none.
  const canRenew = canPay && currentPlan.priceMinor > 0 && !!subscription && !subscription.complimentary;

  return (
    <div className="space-y-5">
      <PageHeader title="Plan and billing" back="/app/more" subtitle="Your school's plan, what it holds and what each plan costs a term." />

      <PlanPaymentNotice payment={payment} />

      <Card className="grid gap-5 md:grid-cols-[minmax(0,15rem)_minmax(0,1fr)] md:gap-8">
        <div>
          <p className="text-sm text-muted">Current plan</p>
          <p className="slab mt-0.5 text-3xl leading-tight font-bold text-ink">{currentPlan.name}</p>
          <p className="mt-1.5 text-sm text-slate-700">
            {subscription?.currentPeriodEnd
              ? subscription.complimentary
                ? `Complimentary until ${formatStampDay(subscription.currentPeriodEnd)}.`
                : `Ends ${formatStampDay(subscription.currentPeriodEnd)}. Plans don't renew on their own: pay again before then to keep ${currentPlan.name}.`
              : currentPlan.priceMinor === 0
                ? "The free plan has no end date."
                : null}
          </p>
        </div>
        {usage.length > 0 && (
          <div className="space-y-3.5 md:border-l md:border-rule md:pl-8">
            {usage.map((u) => (
              <UsageMeter key={u.key} label={USAGE_LABELS[u.key] ?? u.label} used={u.used} limit={u.limit} />
            ))}
          </div>
        )}
      </Card>

      {data.pendingTransfer && <PendingTransferNotice pending={data.pendingTransfer} canCancel={canPay} onCancelled={reload} />}
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
      {!isProprietor && <Notice tone="info">Only the proprietor can change the plan.</Notice>}
      {checkoutError && <ErrorBox error={checkoutError} />}

      <div className="ruled-sheet">
        <ul className="ruled-rows" aria-label="Plans">
          {plans.map((plan) => {
            const current = plan.code === currentPlan.code;
            return (
              <li
                key={plan.code}
                aria-current={current ? "true" : undefined}
                className={cx(
                  "ruled-pad grid gap-3 py-4 md:grid-cols-[minmax(0,13rem)_minmax(0,1fr)_minmax(0,12rem)] md:items-start md:gap-6 md:py-5",
                  current && "bg-blazer-wash/50",
                )}
              >
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="slab text-xl font-bold text-ink">{plan.name}</h2>
                    {current && <Badge tone="blazer">Your plan</Badge>}
                  </div>
                  <p className="mt-1 flex items-baseline gap-1">
                    <span className="slab tnum text-2xl font-bold text-ink">{naira(plan.priceMinor)}</span>
                    {plan.priceMinor > 0 && <span className="text-sm text-muted">/{intervalWord(plan.interval)}</span>}
                  </p>
                </div>
                <ul className="space-y-1.5 text-sm text-slate-700">
                  {plan.features.map((f) => (
                    <li key={f} className="flex gap-2">
                      <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-pass" />
                      {f}
                    </li>
                  ))}
                </ul>
                <div className="md:self-center md:justify-self-end">
                  {current && canRenew && offerTransfer && (
                    <Button
                      variant={nextUp ? "secondary" : "primary"}
                      className="w-full md:w-auto"
                      loading={busy === plan.code}
                      disabled={!(data.paymentsEnabled || bank) || !!busy}
                      onClick={() => (bank ? setTransferPlan(plan) : pay(plan))}
                    >
                      {bank ? "Pay by bank transfer" : "Pay for another term"}
                    </Button>
                  )}
                  {!current && canPay && isUpgrade(plan) && offerTransfer && (
                    <Button
                      variant={plan.code === nextUp ? "primary" : "secondary"}
                      className="w-full md:w-auto"
                      loading={busy === plan.code}
                      disabled={!(data.paymentsEnabled || bank) || !!busy}
                      onClick={() => (bank ? setTransferPlan(plan) : pay(plan))}
                    >
                      {bank ? "Pay by bank transfer" : `Upgrade to ${plan.name}`}
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      </div>

      <p className="max-w-prose text-sm text-muted">
        A paid term runs four months from the day you pay. Paying for the same plan again while a term is running adds four months to
        it; moving to a higher plan starts a new four-month term that day.
      </p>
    </div>
  );
}
