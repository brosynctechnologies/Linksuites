import Link from "next/link";
import { buttonClass } from "@/components/styles";

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center bg-desk px-6 text-center">
      <div className="mb-6 flex h-24 w-[4.25rem] rotate-[-4deg] items-center justify-center rounded-doc bg-paper shadow-paper" aria-hidden="true">
        <span className="font-display text-2xl text-faint">404</span>
      </div>
      <h1 className="font-display text-[1.75rem] font-[560] text-graphite">Page not found</h1>
      <p className="mt-2 max-w-sm text-sm text-muted">This address doesn&apos;t lead anywhere. Check the link, or start again from the home page.</p>
      <Link href="/" className={buttonClass("primary", "mt-6 px-6")}>
        Go to the home page
      </Link>
    </main>
  );
}
