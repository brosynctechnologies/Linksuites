"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { storeLink } from "@/lib/config";
import { secondaryNav } from "@/components/dashboard-nav";
import { PageHeader, buttonClass, listClass } from "@/components/ui";
import { ChevronRightIcon, ExternalIcon, LogoutIcon } from "@/components/icons";

export default function MorePage() {
  const auth = useAuth();
  const router = useRouter();

  return (
    <div className="space-y-6">
      <PageHeader
        title="More"
        subtitle={
          auth ? (
            <span className="flex flex-col">
              <span className="font-medium text-ink-2">{auth.user.fullName}</span>
              <span className="break-all">{auth.user.email}</span>
            </span>
          ) : undefined
        }
      />
      {auth && (
        <a
          href={storeLink(auth.tenant.slug)}
          target="_blank"
          rel="noopener noreferrer"
          className={buttonClass("secondary", "w-full justify-between")}
        >
          <span className="truncate">View my store</span>
          <ExternalIcon width={18} height={18} />
          <span className="sr-only">(opens in a new tab)</span>
        </a>
      )}
      <ul className={listClass}>
        {secondaryNav.map((item) => (
          <li key={item.href}>
            <Link href={item.href} className="flex min-h-14 items-center gap-3 px-4 text-ink hover:bg-paper focus-visible:-outline-offset-3">
              <item.icon className="text-muted" />
              <span className="flex-1 font-medium">{item.label}</span>
              <ChevronRightIcon className="text-subtle" />
            </Link>
          </li>
        ))}
      </ul>
      {!auth?.support && (
        <ul className={listClass}>
          <li>
            <button
              type="button"
              onClick={async () => {
                await logout();
                router.replace("/login");
              }}
              className="flex min-h-14 w-full items-center gap-3 px-4 text-left text-hibiscus-deep hover:bg-hibiscus-soft focus-visible:-outline-offset-3"
            >
              <LogoutIcon />
              <span className="font-medium">Log out</span>
            </button>
          </li>
        </ul>
      )}
    </div>
  );
}
