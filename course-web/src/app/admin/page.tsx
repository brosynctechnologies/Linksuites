import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { buttonClass } from "@/components/styles";
import { ADMIN_URL } from "@/lib/config";

export const metadata: Metadata = { title: "Admin", robots: { index: false } };

/** Course reviews, payouts and categories now live in the platform admin console. */
export default function AdminMovedPage() {
  return (
    <AuthShell title="Admin has moved">
      <p className="text-sm text-slate-700">Course moderation and payouts moved to the admin console.</p>
      <a href={ADMIN_URL} className={buttonClass("primary", "mt-4 w-full")}>
        Open the admin console
      </a>
    </AuthShell>
  );
}
