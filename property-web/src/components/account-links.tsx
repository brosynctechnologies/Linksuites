"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { logout } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { ADMIN_URL } from "@/lib/config";
import { initials } from "@/lib/format";
import { HeartIcon } from "./icons";
import { lsButton } from "./suite/ui";
import { cx } from "./styles";

/** Header account area: Log in / Sign up when signed out; saved homes and a menu when signed in. */
export function AccountLinks() {
  const auth = useAuth();
  const hydrated = useHydrated();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  if (!hydrated) return <span className="block h-11 w-28" aria-hidden="true" />;

  if (!auth) {
    return (
      <div className="flex items-center gap-1">
        <Link href="/login" className={lsButton("quiet", "md", "px-2.5 sm:px-3.5")}>
          Log in
        </Link>
        <Link href="/signup" className={lsButton("app", "md", "max-[399px]:hidden")}>
          Sign up
        </Link>
      </div>
    );
  }

  const items = [
    { href: "/me", label: "Saved homes" },
    { href: "/me/searches", label: "Saved searches" },
    { href: "/me/enquiries", label: "Enquiries" },
    { href: "/me/viewings", label: "Viewings" },
    { href: "/trips", label: "Trips" },
    { href: "/me/account", label: "Account" },
    auth.tenant ? { href: "/studio", label: `Studio: ${auth.tenant.name}` } : { href: "/studio/signup", label: "List a property" },
    ...(auth.user.isPlatformAdmin ? [{ href: ADMIN_URL, label: "Admin console" }] : []),
  ];

  return (
    <div className="flex items-center gap-1">
      <Link href="/me" className="flex h-11 w-11 items-center justify-center rounded-control text-asphalt hover:bg-asphalt/[0.06]" aria-label="Saved homes">
        <HeartIcon />
      </Link>
      <div className="relative" ref={ref}>
        <button
          type="button"
          aria-haspopup="menu"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
          className="flex h-11 w-11 items-center justify-center rounded-full"
        >
          <span className="sr-only">Account menu for {auth.user.fullName}</span>
          <span aria-hidden="true" className="flex h-9 w-9 items-center justify-center rounded-full bg-adire text-sm font-bold text-white">
            {initials(auth.user.fullName)}
          </span>
        </button>
        {open && (
          <div role="menu" className="absolute right-0 z-40 mt-1 w-64 overflow-hidden rounded-panel border border-line bg-surface py-1 shadow-float">
            <div className="border-b border-line-soft px-4 py-2.5">
              <p className="truncate text-sm font-semibold text-asphalt">{auth.user.fullName}</p>
              <p className="truncate text-xs text-muted">{auth.user.email}</p>
            </div>
            {items.map((it) => (
              <Link
                key={it.href}
                role="menuitem"
                href={it.href}
                onClick={() => setOpen(false)}
                className="flex min-h-11 items-center truncate px-4 text-sm text-asphalt hover:bg-slate-50"
              >
                {it.label}
              </Link>
            ))}
            {!auth.support && (
              <button
                type="button"
                role="menuitem"
                className={cx("flex min-h-11 w-full items-center border-t border-line-soft px-4 text-left text-sm text-brick hover:bg-brick-wash")}
                onClick={async () => {
                  setOpen(false);
                  await logout();
                  router.replace("/");
                }}
              >
                Log out
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
