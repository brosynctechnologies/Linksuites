"use client";

export default function StoreError({ reset }: { error: Error; reset: () => void }) {
  return (
    <main id="main" tabIndex={-1} className="mx-auto flex min-h-screen max-w-[560px] flex-col justify-center px-6 outline-none">
      <h1 className="font-display text-2xl font-bold text-ink">This store didn&apos;t load</h1>
      <p className="mt-2 max-w-prose text-sm text-muted">Check your internet connection, then try again.</p>
      <button
        type="button"
        onClick={reset}
        className="mt-6 inline-flex min-h-12 w-fit items-center rounded-control bg-ink px-6 font-semibold text-white hover:bg-ink-2"
      >
        Try again
      </button>
    </main>
  );
}
