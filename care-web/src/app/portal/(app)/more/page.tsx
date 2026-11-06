"use client";

import Link from "next/link";
import { AccountIcon, LogoutIcon, ShieldIcon } from "@/components/icons";
import { BellIcon } from "@/components/notification-bell";
import { PageHeader } from "@/components/ui";
import { portalHref, usePortal } from "@/lib/portal";
import { usePortalSignOut } from "../portal-shell";

/** The phone "More" page: the rest of the portal and sign out. */
export default function PortalMorePage() {
  const { patient } = usePortal();
  const signOut = usePortalSignOut();
  const items = [
    { href: "/portal/access-log", label: "Who opened the record", icon: ShieldIcon },
    { href: "/portal/contact", label: "Contact details", icon: AccountIcon },
    { href: "/portal/notifications", label: "Notifications", icon: BellIcon },
  ];
  return (
    <div>
      <PageHeader title="More" />
      <div className="case-sheet">
        <ul className="case-rows">
          {items.map((n) => (
            <li key={n.href}>
              <Link href={portalHref(n.href, patient?.id)} className="case-pad flex min-h-12 items-center gap-3 text-sm font-bold text-ink hover:bg-slate-50">
                <n.icon className="h-5 w-5 text-muted" />
                {n.label}
              </Link>
            </li>
          ))}
          <li>
            <button type="button" onClick={signOut} className="case-pad flex min-h-12 w-full items-center gap-3 text-left text-sm font-bold text-ink hover:bg-slate-50">
              <LogoutIcon className="h-5 w-5 text-muted" />
              Sign out
            </button>
          </li>
        </ul>
      </div>
    </div>
  );
}
