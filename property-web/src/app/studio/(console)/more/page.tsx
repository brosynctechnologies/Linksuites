"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { ADMIN_URL } from "@/lib/config";
import { navFor, secondaryNav } from "@/components/studio-nav";
import { BusinessSwitcher } from "@/components/business-switcher";
import { ChevronRightIcon, LogoutIcon, ShieldIcon } from "@/components/icons";
import { PageHeader } from "@/components/ui";

/** Phones: everything that doesn't fit in the bottom bar. */
export default function MorePage() {
  const auth = useAuth();
  const router = useRouter();
  const items = navFor(secondaryNav, auth?.tenant?.role);
  return (
    <div>
      <PageHeader title="More" subtitle={auth?.tenant?.name} />
      <div className="on-asphalt mb-4 rounded-panel bg-asphalt p-3 empty:hidden">
        <BusinessSwitcher />
      </div>
      <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
        {items.map((i) => (
          <li key={i.href}>
            <Link href={i.href} className="flex min-h-12 items-center gap-3 px-4 text-asphalt hover:bg-slate-50">
              <i.icon className="text-muted" />
              <span className="flex-1">{i.label}</span>
              <ChevronRightIcon className="h-4 w-4 text-faint" />
            </Link>
          </li>
        ))}
        {auth?.user.isPlatformAdmin && (
          <li>
            <a href={ADMIN_URL} className="flex min-h-12 items-center gap-3 px-4 text-asphalt hover:bg-slate-50">
              <ShieldIcon className="text-muted" />
              <span className="flex-1">Admin console</span>
            </a>
          </li>
        )}
        {!auth?.support && (
          <li>
            <button
              type="button"
              className="flex min-h-12 w-full items-center gap-3 px-4 text-left text-brick hover:bg-brick-wash"
              onClick={async () => {
                await logout();
                router.replace("/studio/login");
              }}
            >
              <LogoutIcon /> Log out
            </button>
          </li>
        )}
      </ul>
    </div>
  );
}
