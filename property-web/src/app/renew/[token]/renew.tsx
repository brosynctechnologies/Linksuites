"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { LISTING_STATUS, statusOf } from "@/lib/status";
import type { RenewResultDto } from "@/lib/types";
import { AuthShell } from "@/components/auth-shell";
import { StatusBadge } from "@/components/badges";
import { Button, ErrorBox, PageLoader, buttonClass } from "@/components/ui";

/** A bad, used-up or expired token (400) or a listing that's gone (404 or 410). */
function isDeadLink(e: ApiError) {
  return e.status === 400 || e.status === 404 || e.status === 410;
}

/** Network and server errors are worth retrying; anything else is not. */
function isRetryable(e: ApiError) {
  return e.status === 0 || e.status >= 500;
}

/**
 * The renew link from the "still available?" email. Opening it only shows the listing; the visitor confirms with
 * "Keep it listed" (link scanners and previews must not renew on their own).
 */
export function Renew() {
  const { token } = useParams<{ token: string }>();
  const path = `/api/listings/renew/${encodeURIComponent(token)}`;
  const [info, setInfo] = useState<RenewResultDto | null>(null);
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [renewing, setRenewing] = useState(false);
  const [renewError, setRenewError] = useState<ApiError | null>(null);
  const [result, setResult] = useState<RenewResultDto | null>(null);
  const started = useRef<string | null>(null);

  useEffect(() => {
    const key = `${token}:${attempt}`;
    if (started.current === key) return;
    started.current = key;
    api<RenewResultDto>(path, { auth: false }).then(setInfo, (e) => setLoadError(toApiError(e)));
  }, [token, attempt, path]);

  async function renew() {
    setRenewing(true);
    setRenewError(null);
    try {
      setResult(await api<RenewResultDto>(path, { method: "POST", auth: false }));
    } catch (e) {
      setRenewError(toApiError(e));
    } finally {
      setRenewing(false);
    }
  }

  if ((loadError && isDeadLink(loadError)) || (renewError && isDeadLink(renewError))) {
    return (
      <AuthShell title="This renew link has expired">
        <p className="text-sm text-slate-700">Renew links only work for a short time. Sign in to your studio and renew the listing from your listings instead.</p>
        <Link href="/studio/listings" className={buttonClass("primary", "mt-5 w-full")}>
          Sign in to renew
        </Link>
      </AuthShell>
    );
  }

  if (loadError) {
    return (
      <AuthShell title="We couldn't load this listing">
        <ErrorBox error={isRetryable(loadError) ? loadError : "Something went wrong with this link. Renew the listing from your studio instead."} />
        <div className="mt-4 flex flex-wrap gap-2">
          {isRetryable(loadError) && (
            <Button
              onClick={() => {
                setLoadError(null);
                setAttempt((a) => a + 1);
              }}
            >
              Try again
            </Button>
          )}
          <Link href="/studio/listings" className={buttonClass("secondary")}>
            Open my listings
          </Link>
        </div>
      </AuthShell>
    );
  }

  if (result) {
    const st = statusOf(LISTING_STATUS, result.status);
    return (
      <AuthShell title={result.status === "Active" ? "Thanks. Your listing stays up." : "Listing updated"}>
        <p className="font-semibold text-asphalt">{result.title}</p>
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-slate-700">
          <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
          {result.expiresAt && <span>Now live until {formatDay(result.expiresAt)}.</span>}
        </div>
        {result.note && <p className="mt-3 text-sm text-slate-700">{result.note}</p>}
        <Link href="/studio/listings" className={buttonClass("primary", "mt-5 w-full")}>
          Open my listings
        </Link>
      </AuthShell>
    );
  }

  if (!info) return <PageLoader label="Loading your listing…" />;
  const st = statusOf(LISTING_STATUS, info.status);
  return (
    <AuthShell title="Is this home still available?" subtitle="Keep it listed and we'll extend how long it stays live.">
      <p className="font-semibold text-asphalt">{info.title}</p>
      <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-slate-700">
        <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
        {info.expiresAt && <span>Live until {formatDay(info.expiresAt)}.</span>}
      </div>
      {info.note && <p className="mt-3 text-sm text-slate-700">{info.note}</p>}
      {renewError && (
        <div className="mt-4">
          <ErrorBox error={renewError} />
        </div>
      )}
      <Button className="mt-5 w-full" loading={renewing} onClick={renew}>
        Keep it listed
      </Button>
      <Link href="/studio/listings" className={buttonClass("secondary", "mt-2 w-full")}>
        Open my listings
      </Link>
    </AuthShell>
  );
}
