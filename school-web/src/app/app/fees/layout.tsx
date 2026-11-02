"use client";

import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { SubNav } from "@/components/sub-nav";
import { FeesNoAccess, useFeeRole } from "./fees-shared";

/** Fees: Owner, Admin and the bursar. A single invoice has its own header, so the tabs show everywhere else. */
export default function FeesLayout({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const allowed = useFeeRole();
  const items = [
    { href: "/app/fees", label: "Overview", exact: true },
    { href: "/app/fees/invoices", label: "Invoices", exact: true },
    { href: "/app/fees/payments", label: "Payments" },
    { href: "/app/fees/debtors", label: "Debtors" },
    { href: "/app/fees/expenses", label: "Expenses" },
    { href: "/app/fees/items", label: "Fee items" },
    { href: "/app/fees/structure", label: "Fee structure" },
    { href: "/app/fees/settings", label: "Settings" },
  ];
  const isInvoice = pathname.startsWith("/app/fees/invoices/");
  if (!allowed) {
    return (
      <div>
        <h1 className="slab mb-3 text-2xl font-bold text-ink">Fees</h1>
        <FeesNoAccess />
      </div>
    );
  }
  return (
    <div>
      {!isInvoice && (
        <>
          <h1 className="slab mb-3 text-2xl font-bold text-ink">Fees</h1>
          <SubNav items={items} label="Fees" />
        </>
      )}
      {children}
    </div>
  );
}
