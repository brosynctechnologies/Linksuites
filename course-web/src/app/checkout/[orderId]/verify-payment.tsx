"use client";

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { SITE_URL } from "@/lib/config";
import { AwardIcon } from "@/components/icons";
import { Button, ErrorBox, PageLoader, buttonClass } from "@/components/ui";

/** A same-site absolute URL becomes a path, so the link stays in the app. */
function localHref(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url, SITE_URL);
    if (typeof window !== "undefined" && u.origin === window.location.origin) return u.pathname + u.search;
    return u.toString();
  } catch {
    return null;
  }
}

export function VerifyPayment() {
  const { orderId } = useParams<{ orderId: string }>();
  const sp = useSearchParams();
  const reference = sp.get("reference") ?? sp.get("trxref");
  const auth = useAuth();
  const hydrated = useHydrated();
  const [result, setResult] = useState<{ enrolled: boolean; courseUrl: string | null } | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [attempt, setAttempt] = useState(0);
  const started = useRef<string | null>(null);

  useEffect(() => {
    if (!hydrated || !auth || !reference) return;
    const key = `${orderId}:${reference}:${attempt}`;
    if (started.current === key) return;
    started.current = key;
    api<{ enrolled: boolean; courseUrl: string | null }>(`/api/learn/orders/${encodeURIComponent(orderId)}/verify`, {
      method: "POST",
      body: { reference },
    }).then(setResult, (e) => setError(toApiError(e)));
  }, [hydrated, auth, orderId, reference, attempt]);

  if (!hydrated) return <PageLoader label="Confirming your payment…" />;

  if (!reference) {
    return (
      <Panel title="Payment not confirmed">
        <p className="text-slate-700">This page needs the payment reference from Paystack. If you paid, open My learning: your course appears there once the payment clears.</p>
        <Link href="/learn" className={buttonClass("primary", "mt-5")}>
          Go to My learning
        </Link>
      </Panel>
    );
  }

  if (!auth) {
    const here = typeof window !== "undefined" ? window.location.pathname + window.location.search : `/checkout/${orderId}`;
    return (
      <Panel title="Log in to finish">
        <p className="text-slate-700">Log in with the account you paid from and we&apos;ll confirm the payment.</p>
        <Link href={`/login?next=${encodeURIComponent(here)}`} className={buttonClass("primary", "mt-5")}>
          Log in
        </Link>
      </Panel>
    );
  }

  if (error) {
    return (
      <Panel title="We couldn't confirm the payment yet">
        <ErrorBox error={error} />
        <p className="mt-3 text-sm text-slate-700">
          If money left your account, don&apos;t pay again. Payments sometimes take a minute to clear; try again, or check My learning shortly.
        </p>
        <div className="mt-5 flex flex-wrap gap-2">
          <Button
            onClick={() => {
              setError(null);
              setAttempt((a) => a + 1);
            }}
          >
            Try again
          </Button>
          <Link href="/learn" className={buttonClass("secondary")}>
            My learning
          </Link>
        </div>
      </Panel>
    );
  }

  if (!result) return <PageLoader label="Confirming your payment…" />;

  if (!result.enrolled) {
    return (
      <Panel title="Payment not completed">
        <p className="text-slate-700">Paystack hasn&apos;t confirmed this payment. If you cancelled, you can go back to the course and try again.</p>
        <div className="mt-5 flex flex-wrap gap-2">
          <Button onClick={() => setAttempt((a) => a + 1)}>Check again</Button>
          <Link href="/learn" className={buttonClass("secondary")}>
            My learning
          </Link>
        </div>
      </Panel>
    );
  }

  const courseHref = localHref(result.courseUrl);
  return (
    <div className="animate-cert-in text-center">
      <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-lime text-white" aria-hidden="true">
        <AwardIcon className="h-7 w-7" />
      </span>
      <h1 className="mt-5 font-serif text-2xl font-semibold text-ink">You&apos;re in. Payment confirmed.</h1>
      <p className="mt-2 text-slate-700">The course is in My learning on every device you sign in on.</p>
      <div className="mt-6 flex flex-wrap justify-center gap-2">
        <Link href="/learn" className={buttonClass("primary", "px-6")}>
          Start learning
        </Link>
        {courseHref && (
          <a href={courseHref} className={buttonClass("secondary")}>
            Open first lesson
          </a>
        )}
      </div>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-panel border border-line bg-surface p-6">
      <h1 className="text-xl font-semibold text-ink">{title}</h1>
      <div className="mt-2">{children}</div>
    </div>
  );
}
