import type { Metadata } from "next";
import type { ReactNode } from "react";
import { SubNav } from "@/components/sub-nav";
import { PHARMACY_NAV } from "@/lib/lab-pharmacy";

export const metadata: Metadata = { title: "Pharmacy" };

/** Wave 4: the pharmacy's screens (stock, dispensing, receipts, counts, alerts, reports) under one set of section links. */
export default function PharmacyLayout({ children }: { children: ReactNode }) {
  return (
    <div>
      <SubNav items={PHARMACY_NAV} label="Pharmacy" />
      {children}
    </div>
  );
}
