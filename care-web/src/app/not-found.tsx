import Link from "next/link";
import { Wordmark } from "@/components/site-chrome";
import { buttonClass } from "@/components/styles";

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center bg-gauze px-6 text-center">
      <Link href="/" className="mb-10 inline-flex min-h-11 items-center rounded-control">
        <Wordmark />
      </Link>
      <h1 className="display text-2xl font-bold text-ink">We can&apos;t find this page</h1>
      <p className="mt-2 max-w-sm text-sm text-muted">The link may be old or mistyped. Staff can sign in and find the page from the menu.</p>
      <div className="mt-6 flex flex-wrap justify-center gap-2">
        <Link href="/login" className={buttonClass("primary", "px-6")}>
          Staff sign in
        </Link>
        <Link href="/" className={buttonClass("secondary")}>
          Home page
        </Link>
      </div>
    </main>
  );
}
