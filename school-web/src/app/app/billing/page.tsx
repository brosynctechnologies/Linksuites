"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { CheckIcon } from "@/components/icons";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Notice, PageHeader, PageLoader, UsageMeter, cx } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatStampDay, naira } from "@/lib/format";
import { can } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { BillingDto, PlanDto } from "@/lib/types";

/** The API's usage labels, in the school's words. */
const USAGE_LABELS: Record<string, string> = { students: "Students", team_seats: "Staff logins" };

/** Back from Paystack: look again every 10 seconds for a minute, then leave it to the "Check again" button. */
const CHECK_EVERY_MS = 10_000;
const AUTO_CHECKS = 6;

function intervalWord(interval: string) {
  return interval.toLowerCase().replace(/ly$/, "");
}

/** What changes when Paystack's confirmation lands: the plan, or the end of the paid term. */
function planSignature(data: BillingDto) {
  return `${data.currentPlan.code}|${data.subscription?.currentPeriodEnd ?? ""}`;
}

export default function BillingPage() {
  const { data, error, loading, reload } = useApi<BillingDto>("/api/billing");
  const auth = useAuth();
  const isProprietor = can(auth?.tenant?.role, "billing.manage");
  // A support view can look at the plan but not buy one.
  const readOnly = !!auth?.support;
  const reference = useSearchParams().get("ref");
  const router = useRouter();
  const [busy, setBusy] = useState<string | null>(null);
  const [checkoutError, setCheckoutError] = useState<ApiError | null>(null);

  // The plan as the page first found it after Paystack sent the proprietor back.
  const [before, setBefore] = useState<string | null>(null);
  if (reference && data && before === null) setBefore(planSignature(data));
  const confirmed = !!reference && !!data && before !== null && planSignature(data) !== before;
  const [checks, setChecks] = useState(0);

  useEffect(() => {
    if (!reference || confirmed || checks >= AUTO_CHECKS) return;
    const timer = window.setTimeout(() => {
      setChecks((n) => n + 1);
      reload();
    }, CHECK_EVERY_MS);
    return () => window.clearTimeout(timer);
  }, [reference, confirmed, checks, reload]);

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

      {reference && (
        <Notice tone={confirmed ? "success" : "info"}>
          {confirmed ? (
            <p>Paystack has confirmed your payment. Your school is on {currentPlan.name}.</p>
          ) : (
            <>
              <p>Payment received. Your plan updates as soon as Paystack confirms it, usually within a minute.</p>
              {checks >= AUTO_CHECKS && (
                <p className="mt-1">
                  If your plan below hasn&apos;t changed yet, check again in a few minutes. Your payment reference is{" "}
                  <span className="tnum font-bold">{reference}</span>.
                </p>
              )}
            </>
          )}
          <div className="mt-2.5 flex flex-wrap gap-2">
            {!confirmed && (
              <Button variant="secondary" loading={loading} onClick={reload}>
                Check again
              </Button>
            )}
            {confirmed && (
              <Button variant="secondary" onClick={() => router.replace("/app/billing")}>
                Done
              </Button>
            )}
          </div>
        </Notice>
      )}

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

      {!data.paymentsEnabled && <Notice tone="warning">Upgrades are unavailable until online payments are set up. Try again later.</Notice>}
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
                  {current && canRenew && (
                    <Button
                      variant={nextUp ? "secondary" : "primary"}
                      className="w-full md:w-auto"
                      loading={busy === plan.code}
                      disabled={!data.paymentsEnabled || !!busy}
                      onClick={() => pay(plan)}
                    >
                      Pay for another term
                    </Button>
                  )}
                  {!current && canPay && isUpgrade(plan) && (
                    <Button
                      variant={plan.code === nextUp ? "primary" : "secondary"}
                      className="w-full md:w-auto"
                      loading={busy === plan.code}
                      disabled={!data.paymentsEnabled || !!busy}
                      onClick={() => pay(plan)}
                    >
                      Upgrade to {plan.name}
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
        it; moving to a higher plan starts a new four-month term that day. Payments go through Paystack.
      </p>
    </div>
  );
}
