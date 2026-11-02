"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { formatLength, plural } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { Channel, CheckoutResponse, CourseDetailDto, QuoteResponse } from "@/lib/types";
import { Button, ErrorBox, Spinner, buttonClass, cx, inputClass } from "@/components/ui";
import { AwardIcon, BookIcon, CheckIcon, ClockIcon, GlobeIcon } from "@/components/icons";

interface Props {
  courseId: string;
  academySlug: string;
  courseSlug: string;
  priceMinor: number;
  currency: string;
  isFree: boolean;
  lessonsCount: number;
  durationSec: number | null;
  language: string | null;
  channel: Channel;
}

/**
 * Sticky buy panel (desktop) and bottom bar (phones). Knows whether the visitor is enrolled by re-reading
 * the course with their token (the server render is anonymous, the session lives in this browser).
 */
export function BuyPanel(p: Props) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const router = useRouter();
  const detailPath = `/api/catalog/courses/${encodeURIComponent(p.academySlug)}/${encodeURIComponent(p.courseSlug)}`;
  const { data: mine, loading: checking } = useApi<CourseDetailDto>(auth ? detailPath : null);
  const enrolled = !!(auth && mine?.enrolled);

  const [coupon, setCoupon] = useState("");
  const [quote, setQuote] = useState<QuoteResponse | null>(null);
  const [quoting, setQuoting] = useState(false);
  const [buying, setBuying] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const free = p.isFree || p.priceMinor <= 0;
  const total = quote ? quote.totalMinor : p.priceMinor;
  const here = `/courses/${p.academySlug}/${p.courseSlug}${p.channel === "Academy" ? "?via=academy" : ""}`;
  const learnHref = `/learn/${p.courseId}`;

  async function applyCoupon(e: FormEvent) {
    e.preventDefault();
    if (!coupon.trim()) return;
    setQuoting(true);
    setError(null);
    try {
      setQuote(await api<QuoteResponse>("/api/learn/checkout/quote", { method: "POST", body: { courseId: p.courseId, couponCode: coupon.trim() } }));
    } catch (err) {
      setError(toApiError(err));
      setQuote(null);
    } finally {
      setQuoting(false);
    }
  }

  async function checkout() {
    setBuying(true);
    setError(null);
    try {
      const res = await api<CheckoutResponse>("/api/learn/checkout", {
        method: "POST",
        // Only a coupon the quote accepted goes with the order; an invalid one would make the server refuse the checkout.
        body: { courseId: p.courseId, couponCode: quote && quote.discountMinor > 0 && coupon.trim() ? coupon.trim() : null, channel: p.channel },
      });
      if (res.enrolled) {
        router.push(learnHref);
        return;
      }
      if (res.authorizationUrl) {
        window.location.assign(res.authorizationUrl);
        return;
      }
      setError(toApiError(new Error("Payment couldn't start. Try again in a moment.")));
      setBuying(false);
    } catch (err) {
      const apiErr = toApiError(err);
      if (apiErr.status === 409) {
        // Already enrolled (e.g. bought on another device).
        router.push(learnHref);
        return;
      }
      setError(apiErr);
      setBuying(false);
    }
  }

  const ctaLabel = free || total <= 0 ? "Enrol for free" : `Buy for ${formatMoney(total, p.currency)}`;

  function cta(className?: string) {
    if (!hydrated || (auth && checking && !mine)) {
      return (
        <span className={cx(buttonClass("secondary", "pointer-events-none"), className)} aria-hidden="true">
          <Spinner className="h-4 w-4" />
        </span>
      );
    }
    if (enrolled) {
      return (
        <Link href={learnHref} className={cx(buttonClass("primary"), className)}>
          Continue learning
        </Link>
      );
    }
    if (!auth) {
      return (
        <Link href={`/signup?next=${encodeURIComponent(here)}`} className={cx(buttonClass("primary"), className)}>
          {ctaLabel}
        </Link>
      );
    }
    return (
      <Button className={className} loading={buying} onClick={checkout}>
        {ctaLabel}
      </Button>
    );
  }

  const length = formatLength(p.durationSec);

  return (
    <>
      <aside className="rounded-panel border border-line bg-surface p-5" aria-label="Enrol">
        {enrolled ? (
          <p className="flex items-center gap-2 text-sm font-semibold text-lime-dark">
            <CheckIcon className="h-4 w-4" /> You&apos;re enrolled
          </p>
        ) : (
          <div className="flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
            {free ? (
              <span className="text-3xl font-bold text-lime-dark">Free</span>
            ) : (
              <>
                <span className="tnum text-3xl font-bold tracking-[-0.01em] text-ink">{formatMoney(total, p.currency)}</span>
                {quote && quote.discountMinor > 0 && (
                  <>
                    <span className="tnum text-base text-muted line-through">{formatMoney(quote.priceMinor, p.currency)}</span>
                    <span className="tnum text-sm font-semibold text-coral-dark">You save {formatMoney(quote.discountMinor, p.currency)}</span>
                  </>
                )}
              </>
            )}
          </div>
        )}

        {cta("mt-4 w-full")}

        {!auth && hydrated && !enrolled && (
          <p className="mt-2.5 text-center text-sm text-muted">
            Have an account?{" "}
            <Link href={`/login?next=${encodeURIComponent(here)}`} className="font-semibold text-lagoon-text hover:underline">
              Log in
            </Link>
          </p>
        )}

        {auth && !enrolled && !free && (
          <form onSubmit={applyCoupon} className="mt-4 flex gap-2">
            <label htmlFor="coupon" className="sr-only">
              Coupon code
            </label>
            <input
              id="coupon"
              className={cx(inputClass, "uppercase placeholder:normal-case")}
              placeholder="Coupon code"
              value={coupon}
              autoComplete="off"
              onChange={(e) => {
                setCoupon(e.target.value);
                setQuote(null);
              }}
            />
            <Button type="submit" variant="secondary" loading={quoting} disabled={!coupon.trim()}>
              Apply
            </Button>
          </form>
        )}
        {quote?.couponMessage && (
          <p className={cx("mt-2 text-sm", quote.discountMinor > 0 ? "text-lime-dark" : "text-coral-dark")} role="status">
            {quote.couponMessage}
          </p>
        )}
        {error && (
          <div className="mt-3">
            <ErrorBox error={error} />
          </div>
        )}

        <ul className="mt-5 space-y-2.5 border-t border-line-soft pt-4 text-sm text-slate-700">
          <li className="flex items-center gap-2.5">
            <BookIcon className="h-4 w-4 shrink-0 text-muted" />
            {plural(p.lessonsCount, "lesson")}
          </li>
          {length && (
            <li className="flex items-center gap-2.5">
              <ClockIcon className="h-4 w-4 shrink-0 text-muted" />
              {length} of video
            </li>
          )}
          {p.language && (
            <li className="flex items-center gap-2.5">
              <GlobeIcon className="h-4 w-4 shrink-0 text-muted" />
              Taught in {p.language}
            </li>
          )}
          <li className="flex items-center gap-2.5">
            <AwardIcon className="h-4 w-4 shrink-0 text-muted" />
            Progress saved on any device
          </li>
        </ul>
        {!free && !enrolled && <p className="mt-4 text-xs text-muted">Pay securely with card, bank transfer or USSD through Paystack.</p>}
      </aside>

      {/* Phones: price and the main action stay in reach. */}
      <div className="fixed inset-x-0 bottom-0 z-30 flex items-center gap-3 border-t border-line bg-surface/95 px-4 py-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] shadow-bar backdrop-blur lg:hidden">
        <div className="min-w-0 flex-1">
          {enrolled ? (
            <p className="text-sm font-semibold text-lime-dark">You&apos;re enrolled</p>
          ) : free ? (
            <p className="text-xl font-bold text-lime-dark">Free</p>
          ) : (
            <p className="tnum text-xl font-bold text-ink">{formatMoney(total, p.currency)}</p>
          )}
        </div>
        {cta("shrink-0 px-5")}
      </div>
    </>
  );
}
