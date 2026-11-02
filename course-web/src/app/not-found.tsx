import Link from "next/link";
import { Wordmark } from "@/components/site-chrome";
import { buttonClass } from "@/components/styles";

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center bg-mist px-6 text-center">
      <Link href="/" className="mb-10 rounded-control">
        <Wordmark />
      </Link>
      {/* A path that stops short: the one recurring motif, used quietly. */}
      <div className="mb-6 flex items-center gap-2" aria-hidden="true">
        <span className="h-3 w-3 rounded-full bg-lime" />
        <span className="h-[2px] w-10 bg-lime" />
        <span className="h-3 w-3 rounded-full bg-lime" />
        <span className="h-[2px] w-10 bg-line" />
        <span className="h-3 w-3 rounded-full border-2 border-dashed border-slate-300" />
      </div>
      <h1 className="font-serif text-2xl font-semibold text-ink">This page isn&apos;t on the path</h1>
      <p className="mt-2 max-w-sm text-sm text-muted">The link may be old or mistyped. Find your course from the marketplace or My learning.</p>
      <div className="mt-6 flex flex-wrap justify-center gap-2">
        <Link href="/" className={buttonClass("primary", "px-6")}>
          Browse courses
        </Link>
        <Link href="/learn" className={buttonClass("secondary")}>
          My learning
        </Link>
      </div>
    </main>
  );
}
