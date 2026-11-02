"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { toApiError, type ApiError } from "@/lib/api";
import { hasImportableDraft, importDraft, loadDraft } from "@/lib/draft";
import { ApiErrorView, Button, Card, EmptyState, PageHeader, Spinner, buttonClass } from "@/components/ui";

/** Imports the free generator's draft into the signed-in account, then opens the new invoice. */
export default function ImportPage() {
  const router = useRouter();
  const [draft] = useState(() => loadDraft());
  const [step, setStep] = useState("Getting started…");
  const [error, setError] = useState<ApiError | null>(null);
  const [attempt, setAttempt] = useState(0);
  const started = useRef(-1);

  useEffect(() => {
    if (!hasImportableDraft(draft) || started.current === attempt) return;
    started.current = attempt;
    importDraft(draft, setStep).then(
      (inv) => router.replace(`/app/invoices/${inv.id}?imported=1`),
      (e) => setError(toApiError(e)),
    );
  }, [attempt, draft, router]);

  if (!hasImportableDraft(draft)) {
    return (
      <div>
        <PageHeader title="Save your invoice" />
        <EmptyState
          title="No invoice draft found"
          text="There is no invoice from the free generator in this browser. Start a new one here instead."
          action={
            <Link href="/app/invoices/new" className={buttonClass("primary")}>
              Create an invoice
            </Link>
          }
        />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-md">
      <PageHeader title="Saving your invoice" />
      <Card>
        {error ? (
          <div className="space-y-3">
            <ApiErrorView error={error} />
            <div className="flex gap-2">
              <Button
                onClick={() => {
                  setError(null);
                  setAttempt((a) => a + 1);
                }}
              >
                Try again
              </Button>
              <Link href="/app" className={buttonClass("secondary")}>
                Skip
              </Link>
            </div>
          </div>
        ) : (
          <div className="flex items-center gap-3 text-slate-700" role="status" aria-live="polite">
            <Spinner />
            <span>{step}</span>
          </div>
        )}
      </Card>
    </div>
  );
}
