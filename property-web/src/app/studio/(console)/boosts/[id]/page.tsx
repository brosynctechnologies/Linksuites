"use client";

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatDay, formatStamp } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { BOOST_STATUS, statusOf } from "@/lib/status";
import type { BoostDto } from "@/lib/types";
import { ApiErrorView, Button, Card, Notice, PageHeader, PageLoader, StatusBadge, buttonClass } from "@/components/ui";

/** A boost, and the Paystack return page: with `?reference=` it verifies the payment first. */
export default function BoostPage() {
  const { id } = useParams<{ id: string }>();
  const sp = useSearchParams();
  const reference = sp.get("reference") ?? sp.get("trxref");
  const [boost, setBoost] = useState<BoostDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [verifyError, setVerifyError] = useState<ApiError | null>(null);
  const [attempt, setAttempt] = useState(0);
  const started = useRef<string | null>(null);

  useEffect(() => {
    const key = `${id}:${reference}:${attempt}`;
    if (started.current === key) return;
    started.current = key;
    const load = async () => {
      if (reference) {
        try {
          setBoost(await api<BoostDto>(`/api/property/boosts/${encodeURIComponent(id)}/verify`, { method: "POST", body: { reference } }));
          setVerifyError(null);
          return;
        } catch (e) {
          setVerifyError(toApiError(e));
        }
      }
      try {
        const hit = asList(await api<BoostDto[]>("/api/property/boosts")).find((b) => b.id === id);
        if (hit) setBoost(hit);
        else setError(toApiError(new Error("We couldn't find this boost.")));
      } catch (e) {
        setError(toApiError(e));
      }
    };
    void load();
  }, [id, reference, attempt]);

  if (!boost && error) {
    return (
      <div>
        <PageHeader title="Boost" back="/studio/boosts" />
        <ApiErrorView error={error} onRetry={() => setAttempt((a) => a + 1)} />
      </div>
    );
  }
  if (!boost) return <PageLoader label={reference ? "Confirming your payment…" : "Loading…"} />;
  const st = statusOf(BOOST_STATUS, boost.status);

  return (
    <div className="space-y-4">
      <PageHeader title={boost.listingTitle} back="/studio/boosts" subtitle={boost.kind === "Top" ? "Top of search" : "Featured"} />
      {reference && boost.status === "Active" && !verifyError && <Notice tone="success">Payment confirmed. Your boost is live.</Notice>}
      {verifyError && (
        <Notice tone="warning">
          We couldn&apos;t confirm the payment yet ({verifyError.message}). If money left your account, don&apos;t pay again.{" "}
          <button type="button" className="font-semibold underline" onClick={() => setAttempt((a) => a + 1)}>
            Check again
          </button>
        </Notice>
      )}
      <Card>
        <dl className="grid gap-3 sm:grid-cols-2">
          <div>
            <dt className="text-sm text-muted">Status</dt>
            <dd className="mt-0.5">
              <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
            </dd>
          </div>
          <div>
            <dt className="text-sm text-muted">Amount</dt>
            <dd className="price text-asphalt">{formatMoney(boost.amountMinor)}</dd>
          </div>
          <div>
            <dt className="text-sm text-muted">Length</dt>
            <dd className="font-semibold text-asphalt">{boost.days} days</dd>
          </div>
          <div>
            <dt className="text-sm text-muted">Runs until</dt>
            <dd className="font-semibold text-asphalt">{boost.endsAt ? formatDay(boost.endsAt) : "Starts once paid"}</dd>
          </div>
          {boost.paidAt && (
            <div>
              <dt className="text-sm text-muted">Paid</dt>
              <dd className="font-semibold text-asphalt">{formatStamp(boost.paidAt)}</dd>
            </div>
          )}
        </dl>
        <div className="mt-4 flex flex-wrap gap-2">
          <Link href={`/studio/listings/${boost.listingId}`} className={buttonClass("secondary")}>
            Open listing
          </Link>
          {boost.status === "Pending" && !reference && (
            <Button variant="ghost" onClick={() => setAttempt((a) => a + 1)}>
              Refresh
            </Button>
          )}
        </div>
      </Card>
    </div>
  );
}
