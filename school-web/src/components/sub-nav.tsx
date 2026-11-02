"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cx } from "./styles";

/** Underlined section links (setup pages, student tools). Scrolls sideways on phones. */
export function SubNav({ items, label }: { items: { href: string; label: string; exact?: boolean }[]; label: string }) {
  const pathname = usePathname();
  return (
    <nav className="no-scrollbar -mx-4 mb-5 flex gap-1 overflow-x-auto border-b border-rule px-4 sm:mx-0 sm:px-0" aria-label={label}>
      {items.map((t) => {
        const active = t.exact ? pathname === t.href : pathname === t.href || pathname.startsWith(t.href + "/");
        return (
          <Link
            key={t.href}
            href={t.href}
            aria-current={active ? "page" : undefined}
            className={cx(
              "relative flex min-h-11 shrink-0 items-center px-3 pt-2 pb-2.5 text-sm font-bold whitespace-nowrap",
              active ? "text-ink" : "text-muted hover:text-ink",
            )}
          >
            {t.label}
            <span aria-hidden="true" className={cx("absolute inset-x-2 -bottom-px h-[3px] rounded-full", active ? "bg-blazer" : "bg-transparent")} />
          </Link>
        );
      })}
    </nav>
  );
}
