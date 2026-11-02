import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { BookingPage } from "./booking-page";

export const metadata: Metadata = { title: "Book an appointment" };

/** Public booking page: fully client-rendered from the public API, so it streams behind Suspense. */
export default function PublicBookingRoute() {
  return (
    <Suspense fallback={<PageLoader />}>
      <BookingPage />
    </Suspense>
  );
}
