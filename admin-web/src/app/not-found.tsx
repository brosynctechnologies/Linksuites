import Link from "next/link";

export default function NotFound() {
  return (
    <div className="flex min-h-dvh flex-col">
      <div className="flex h-9 items-center gap-3 bg-caution pr-4 text-sm font-semibold text-graphite">
        <span className="caution-stripe h-full w-10" aria-hidden="true" />
        Admin console
      </div>
      <main className="flex flex-1 flex-col items-center justify-center px-4 text-center">
        <p className="font-mono text-sm text-muted">404</p>
        <h1 className="mt-1 text-xl font-semibold">There is no page at this address</h1>
        <p className="mt-1 max-w-sm text-sm text-muted">Check the link, or start from the overview.</p>
        <Link href="/" className="mt-5 inline-flex min-h-10 items-center rounded-control bg-graphite px-4 text-sm font-medium text-white hover:bg-graphite-3">
          Go to the overview
        </Link>
      </main>
    </div>
  );
}
