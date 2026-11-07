import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { ManageBooking } from "./manage-booking";

export const metadata: Metadata = { title: "Your booking", robots: { index: false }, alternates: {} };

export default function ManageBookingRoute() {
  return (
    <Suspense fallback={<PageLoader />}>
      <ManageBooking />
    </Suspense>
  );
}
