import type { Metadata } from "next";
import type { ReactNode } from "react";
import { SubNav } from "@/components/sub-nav";
import { LAB_NAV } from "@/lib/lab-pharmacy";

export const metadata: Metadata = { title: "Lab" };

/** Wave 4: the lab's screens (worklist, catalogue, turnaround) under one set of section links. */
export default function LabLayout({ children }: { children: ReactNode }) {
  return (
    <div>
      <SubNav items={LAB_NAV} label="Lab" />
      {children}
    </div>
  );
}
