import Link from "next/link";
import { Wordmark } from "@/components/site-chrome";
import { buttonClass } from "@/components/styles";

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center bg-plaster px-6 text-center">
      <Link href="/" className="mb-10 inline-flex min-h-11 items-center rounded-control">
        <Wordmark />
      </Link>
      <h1 className="display-wide text-2xl font-bold text-asphalt">This page isn&apos;t on the market</h1>
      <p className="mt-2 max-w-sm text-sm text-muted">
        The link may be old, or the home may have been rented, sold or taken down. Search for others like it.
      </p>
      <div className="mt-6 flex flex-wrap justify-center gap-2">
        <Link href="/rent" className={buttonClass("primary", "px-6")}>
          Homes for rent
        </Link>
        <Link href="/" className={buttonClass("secondary")}>
          Home page
        </Link>
      </div>
    </main>
  );
}
