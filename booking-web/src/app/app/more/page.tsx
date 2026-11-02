"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { navFor, secondaryNav } from "@/components/app-nav";
import { BusinessSwitcher } from "@/components/business-switcher";
import { PageHeader } from "@/components/ui";
import { ChevronRightIcon, LogoutIcon } from "@/components/icons";

export default function MorePage() {
  const auth = useAuth();
  const router = useRouter();

  return (
    <div>
      <PageHeader title="More" />
      {auth && (
        <div className="mb-4 rounded-panel border border-line bg-surface p-4">
          <p className="font-semibold text-aubergine">{auth.user.fullName}</p>
          <p className="truncate text-sm text-muted">{auth.user.email}</p>
          <p className="mt-3 border-t border-line-soft pt-3 text-sm text-slate-700">
            {auth.tenant.name}
            {auth.tenant.role && <span className="block text-xs text-muted">Your role: {auth.tenant.role}</span>}
          </p>
          <BusinessSwitcher className="mt-3" />
        </div>
      )}
      <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
        {navFor(secondaryNav, auth?.tenant.role).map((item) => (
          <li key={item.href}>
            <Link href={item.href} className="flex items-center gap-3 px-4 py-3.5 text-aubergine hover:bg-slate-50">
              <item.icon className="text-muted" />
              <span className="flex-1 font-medium">{item.label}</span>
              <ChevronRightIcon className="text-faint" />
            </Link>
          </li>
        ))}
        {!auth?.support && (
          <li>
            <button
              type="button"
              onClick={async () => {
                await logout();
                router.replace("/login");
              }}
              className="flex w-full items-center gap-3 px-4 py-3.5 text-left text-brick hover:bg-brick-wash"
            >
              <LogoutIcon />
              <span className="font-medium">Log out</span>
            </button>
          </li>
        )}
      </ul>
    </div>
  );
}
