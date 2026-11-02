"use client";

import Link from "next/link";
import type { CareChecklistItemDto } from "@/lib/types";
import { CheckIcon } from "@/components/icons";
import { cx } from "@/components/ui";

/** Where each checklist item is done, and a line on what it means. Wards and beds arrive with the inpatient pages (Task 19). */
const WHERE: Record<string, { href?: string; text: string }> = {
  profile: { href: "/app/settings", text: "Address and phone, the patient number prefix, and what registration and consultation cost." },
  "price-list": { href: "/app/price-list", text: "Consultations, tests, drugs, procedures and bed days. Bills are built from it." },
  wards: { text: "Needed only when you admit patients. Wards and beds are set up with the inpatient pages." },
  staff: { href: "/app/staff", text: "Invite your doctors, nurses, front desk and cashier. Each signs in with their own email." },
};

/** The setup checklist from `GET /api/care/setup`: a sequence, so each item is numbered, with a tick when it is done. */
export function SetupChecklist({ items }: { items: CareChecklistItemDto[] }) {
  return (
    <ol className="case-sheet case-rows">
      {items.map((item, i) => {
        const where = WHERE[item.key];
        const body = (
          <>
            <span
              aria-hidden="true"
              className={cx(
                "display tnum flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-bold",
                item.done ? "bg-scrub text-white" : "border-2 border-scrub-line text-scrub",
              )}
            >
              {item.done ? <CheckIcon className="h-4 w-4" /> : i + 1}
            </span>
            <span className="min-w-0 flex-1">
              <span className={cx("block font-bold", item.done ? "text-muted" : "text-ink")}>
                {item.label}
                <span className="sr-only">{item.done ? " (done)" : " (to do)"}</span>
              </span>
              {where && <span className="block text-sm text-muted">{where.text}</span>}
            </span>
            {where?.href && (
              <span aria-hidden="true" className="text-faint">
                ›
              </span>
            )}
          </>
        );
        return (
          <li key={item.key}>
            {where?.href ? (
              <Link href={where.href} className="case-pad flex min-h-16 items-center gap-4 py-3 hover:bg-slate-50">
                {body}
              </Link>
            ) : (
              <div className="case-pad flex min-h-16 items-center gap-4 py-3">{body}</div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
