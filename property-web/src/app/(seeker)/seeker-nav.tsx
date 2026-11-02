"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cx } from "@/components/ui";

const NAV = [
  { href: "/me", label: "Saved homes", exact: true },
  { href: "/me/searches", label: "Saved searches" },
  { href: "/me/enquiries", label: "Enquiries" },
  { href: "/me/viewings", label: "Viewings" },
  { href: "/trips", label: "Trips" },
  { href: "/me/account", label: "Account" },
];

export function SeekerNav() {
  const pathname = usePathname();
  return (
    <nav aria-label="My PropertyLink" className="no-scrollbar -mx-4 flex gap-1 overflow-x-auto border-b border-line px-4 sm:mx-0 sm:px-0">
      {NAV.map((n) => {
        const active = n.exact ? pathname === n.href : pathname === n.href || pathname.startsWith(n.href + "/");
        return (
          <Link
            key={n.href}
            href={n.href}
            aria-current={active ? "page" : undefined}
            className={cx("relative flex min-h-11 shrink-0 items-center px-3 text-sm font-medium whitespace-nowrap", active ? "text-asphalt" : "text-muted hover:text-asphalt")}
          >
            {n.label}
            <span aria-hidden="true" className={cx("absolute inset-x-2 -bottom-px h-[3px] rounded-full", active ? "bg-adire" : "bg-transparent")} />
          </Link>
        );
      })}
    </nav>
  );
}
