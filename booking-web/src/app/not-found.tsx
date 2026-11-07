import type { Metadata } from "next";
import Link from "next/link";
import { buttonClass } from "@/components/styles";

export const metadata: Metadata = { title: "Page not found", robots: { index: false, follow: true }, alternates: {} };

export default function NotFound() {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-screen flex-col items-center justify-center bg-mist px-6 text-center outline-none">
      <div className="mb-6 flex h-24 w-[4.25rem] rotate-[-4deg] items-center justify-center rounded-card bg-surface shadow-card" aria-hidden="true">
        <span className="font-expanded text-2xl font-extrabold text-faint">404</span>
      </div>
      <h1 className="text-[1.75rem] font-bold text-aubergine">Page not found</h1>
      <p className="mt-2 max-w-sm text-sm text-muted">This address doesn&apos;t lead anywhere. Check the link, or start again from the home page.</p>
      <Link href="/" className={buttonClass("primary", "mt-6 px-6")}>
        Go to the home page
      </Link>
    </main>
  );
}
