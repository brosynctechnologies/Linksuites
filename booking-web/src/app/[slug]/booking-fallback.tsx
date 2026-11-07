"use client";

import Link from "next/link";
import { PublicFrame } from "@/components/public-parts";
import { Button, PageLoader, buttonClass } from "@/components/ui";
import type { PublicBookingPageDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { BookingPageView } from "./booking-view";

/** When the server couldn't reach the API, the browser loads the page itself, with a way to try again. */
export function ClientBookingPage({ slug, embed }: { slug: string; embed: boolean }) {
  const { data, error, reload } = useApi<PublicBookingPageDto>(`/api/public/book/${encodeURIComponent(slug)}`, { auth: false });

  if (error && !data) {
    return (
      <PublicFrame brand={null}>
        <main id="main" tabIndex={-1} className="px-4 py-16 text-center outline-none">
          <h1 className="font-expanded text-2xl font-extrabold">{error.status === 404 ? "No booking page here" : "This page didn't load"}</h1>
          <p className="mx-auto mt-2 max-w-sm text-sm text-muted">
            {error.status === 404
              ? "Check the link you were given. The business may have changed its booking address."
              : error.message}
          </p>
          {error.status === 404 ? (
            <Link href="/" className={buttonClass("secondary", "mt-6")}>
              Go to the home page
            </Link>
          ) : (
            <Button className="mt-6" onClick={reload}>
              Try again
            </Button>
          )}
        </main>
      </PublicFrame>
    );
  }
  if (!data) {
    return (
      <main id="main" tabIndex={-1} className="outline-none">
        <PageLoader />
      </main>
    );
  }

  return <BookingPageView page={data} slug={slug} embed={embed} />;
}
