import Link from "next/link";

export default function NotFound() {
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-xl flex-col justify-center px-6">
      <p className="font-display text-4xl font-extrabold text-line-strong" aria-hidden="true">
        404
      </p>
      <h1 className="mt-2 font-display text-2xl font-bold text-ink">Page not found</h1>
      <p className="mt-2 text-sm text-muted">The page you&apos;re looking for doesn&apos;t exist. Check the link, or start from the home page.</p>
      <Link href="/" className="mt-6 inline-flex min-h-12 w-fit items-center rounded-control bg-marigold px-6 font-semibold text-ink hover:bg-marigold-hover">
        Go home
      </Link>
    </main>
  );
}
