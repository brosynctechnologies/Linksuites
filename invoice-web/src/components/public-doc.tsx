import Link from "next/link";
import type { ReactNode } from "react";
import { APP_NAME } from "@/lib/config";
import { normalizeColor } from "@/lib/templates";
import type { PublicBusiness } from "@/lib/types";

/** Mobile-friendly frame for the public invoice / quote pages: the sender's name on top, the document on the desk. */
export function PublicDocShell({
  business,
  showBranding,
  children,
}: {
  business: PublicBusiness | null;
  showBranding?: boolean;
  children: ReactNode;
}) {
  const accent = normalizeColor(business?.brandColor);
  return (
    <div className="min-h-screen bg-desk">
      <header className="border-b border-rule bg-paper" style={{ borderTop: `4px solid ${accent}` }}>
        <div className="mx-auto flex max-w-3xl items-center gap-3 px-4 py-3.5">
          {business?.logoUrl && <img src={business.logoUrl} alt="" className="h-10 w-10 rounded-[6px] object-contain" />}
          <div className="min-w-0">
            <p className="truncate font-display text-lg font-semibold text-graphite">{business?.name ?? ""}</p>
            {business?.email && <p className="truncate text-xs text-muted">{business.email}</p>}
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-3xl space-y-5 px-3 py-5 sm:px-4 sm:py-8">{children}</main>
      {showBranding && (
        <footer className="px-4 pb-10 text-center text-xs text-muted">
          Invoice made with{" "}
          <Link href="/" className="font-semibold text-ink underline-offset-2 hover:underline">
            {APP_NAME}
          </Link>{" "}
          — create yours free in 60 seconds.
        </footer>
      )}
    </div>
  );
}
