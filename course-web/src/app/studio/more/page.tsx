"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { ADMIN_URL } from "@/lib/config";
import { AcademySwitcher } from "@/components/academy-switcher";
import { ChevronRightIcon, ExternalIcon, LogoutIcon, ShieldIcon } from "@/components/icons";
import { elsewhereNav, navFor, primaryNav, secondaryNav } from "@/components/studio-nav";
import { PageHeader } from "@/components/ui";

export default function StudioMorePage() {
  const auth = useAuth();
  const router = useRouter();
  const role = auth?.tenant?.role;
  // Anything the bottom bar couldn't fit, plus the secondary pages.
  const items = [...navFor(primaryNav, role).slice(4), ...navFor(secondaryNav, role), ...elsewhereNav];

  return (
    <div>
      <PageHeader title="More" />
      {auth?.tenant && (
        <div className="mb-4 rounded-panel border border-line bg-surface p-4">
          <p className="font-semibold text-ink">{auth.user.fullName}</p>
          <p className="truncate text-sm text-muted">{auth.user.email}</p>
          <p className="mt-3 border-t border-line-soft pt-3 text-sm text-slate-700">
            {auth.tenant.name}
            {auth.tenant.role && <span className="block text-xs text-muted">Your role: {auth.tenant.role}</span>}
          </p>
          <AcademySwitcher className="mt-3" />
        </div>
      )}
      <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
        {items.map((item) => (
          <li key={item.href}>
            <Link href={item.href} className="flex items-center gap-3 px-4 py-3.5 text-ink hover:bg-slate-50">
              <item.icon className="text-muted" />
              <span className="flex-1 font-medium">{item.label}</span>
              <ChevronRightIcon className="text-faint" />
            </Link>
          </li>
        ))}
        {auth?.tenant && (
          <li>
            <a href={`/a/${auth.tenant.slug}`} target="_blank" rel="noopener noreferrer" className="flex items-center gap-3 px-4 py-3.5 text-ink hover:bg-slate-50">
              <ExternalIcon className="text-muted" />
              <span className="flex-1 font-medium">View academy</span>
              <ChevronRightIcon className="text-faint" />
            </a>
          </li>
        )}
        {auth?.user.isPlatformAdmin && (
          <li>
            <a href={ADMIN_URL} className="flex items-center gap-3 px-4 py-3.5 text-ink hover:bg-slate-50">
              <ShieldIcon className="text-muted" />
              <span className="flex-1 font-medium">Admin console</span>
              <ChevronRightIcon className="text-faint" />
            </a>
          </li>
        )}
        {!auth?.support && (
          <li>
            <button
              type="button"
              onClick={async () => {
                await logout();
                router.replace("/teach/login");
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
